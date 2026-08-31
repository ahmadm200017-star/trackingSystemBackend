# MdfTracker.Api

Backend for the MDF object-tracking system. A Flutter app tracks a user-selected object
through the phone camera with OpenCV and streams the bounding box here; this API records
the run, describes the tracked object with a vision model, and fans the live stream out to
one or more browser dashboards.

- Mobile client: [trackingSystemMobile](https://github.com/ahmadm200017-star/trackingSystemMobile)
- Dashboard: [trackingSystemDashboard](https://github.com/ahmadm200017-star/trackingSystemDashboard)

## How it fits together

```
  phone                          this API                        dashboards
  -----                          --------                        ----------
  POST /api/sessions   ──────►   session row created   ──────►   SessionStarted (all)
  WS   /ws/track       ──────►   FrameQueue ──► writer ──► DB
                                     └────────────────────►      Frame / Status (per session)
  POST /{id}/description ────►   Groq vision model      ──────►  SessionDescribed (all)
  POST /{id}/end       ──────►   summary stored        ──────►   SessionEnded (all)
```

The device pushes over a **plain WebSocket**, because Dart has no first-party SignalR
client. Dashboards consume the relayed stream over **SignalR**. The server is the only
thing that talks to both.

Writes and reads are deliberately split on the ingest path: a socket never touches the
database. It drops frames into an in-memory `FrameQueue` (bounded at 50,000; when full,
`DropWrite` discards the incoming frame rather than blocking the socket) and a single
background `FrameWriterService` persists them in batches of 500.
Frames lost to a full queue are counted and reported on `/api/health` and `/api/stats/live`,
so a gap in stored history is visible rather than silent.

## Stack

- .NET 10 (`net10.0`), ASP.NET Core
- EF Core 10 + SQL Server
- SignalR for dashboard fan-out
- Groq (`qwen/qwen3.8-27b`) for object descriptions

## Running locally

```bash
dotnet run
# http://localhost:5201
```

`GET /` returns an endpoint index. The database is created on first run — see
[Schema management](#schema-management) for the caveat.

Override configuration with environment variables (double underscore for nesting):

```bash
ConnectionStrings__Default="Server=(localdb)\MSSQLLocalDB;Database=mdf_tracker;Trusted_Connection=True;TrustServerCertificate=True" \
Groq__ApiKey="gsk_..." \
dotnet run
```

## Configuration

| Key | Default | Notes |
| --- | --- | --- |
| `ConnectionStrings:Default` | LocalDB `mdf_tracker` | SQL Server. |
| `Cors:Origins` | `[]` | Empty reflects the caller's origin, which is the dev default. SignalR's browser client sends credentials on negotiate, and `AllowAnyOrigin()` is illegal with `AllowCredentials()`, so there is no wildcard. |
| `Groq:ApiKey` | *empty* | **Server-side only.** Empty disables descriptions; the endpoint then answers `503` and tracking is unaffected. |
| `Groq:Model` | `qwen/qwen3.8-27b` | Must be a vision-capable Groq model. One that Groq no longer serves fails with a `404`, not a fallback. |
| `Groq:Temperature` / `TopP` / `MaxCompletionTokens` / `ReasoningEffort` / `Stop` / `Stream` | `0.6` / `0.95` / `2048` / `default` / null / `true` | Mirrors Groq's published example for this model. Blank or null values are omitted from the request. |
| `Groq:TimeoutSeconds` | `30` | Must stay below the mobile client's own deadline for the description call. |

`appsettings.json` is **gitignored** — it holds the connection string and the Groq key.

## API

JSON is camelCase; enums travel as lowercase strings (`csrt`, `back`, `lost`). Validation
errors are keyed by the camelCase name the client sent.

### Sessions

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/api/sessions` | Start a session. Returns the row, including its `sessionNumber`. |
| `POST` | `/api/sessions/{id}/description` | Upload the first-frame crop; returns the session with `objectDescription` filled in. |
| `POST` | `/api/sessions/{id}/end` | Close with the summary: `endTime`, `averageFps`, `isSuccessful`. |
| `GET` | `/api/sessions` | History, newest first. Filters: `status`, `trackerAlgorithm`, `cameraType`, `isSuccessful`, `search`. Paging `page`, `perPage` (max 100). |
| `GET` | `/api/sessions/{id}` | One session. |
| `GET` | `/api/sessions/{id}/frames` | Paged frames. `sort=asc` for chronological; `perPage` max 5000. |
| `GET` | `/api/sessions/{id}/events` | Lost / reacquired events, newest first. |
| `GET` | `/api/sessions/{id}/analytics` | Movement path, X/Y over time, and the timeline's drop zones. `maxPoints` default 5000, clamped 100–50,000. |
| `DELETE` | `/api/sessions/{id}` | Deletes the session with its frames and events (cascade). |

### Stats and health

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/stats/overview` | KPI cards plus a success/failure and FPS breakdown per tracker. `successRate` is already a percentage over *completed* sessions — do not recompute. |
| `GET` | `/api/stats/live` | Who is connected right now, and the process-wide dropped-frame counter. |
| `GET` | `/api/health` | `status`, dropped frames, connection counts. |
| `GET` | `/` | Endpoint index. |

### Enums

| Field | Values |
| --- | --- |
| `cameraType` | `front`, `back` |
| `trackerAlgorithm` | `csrt`, `kcf`, `mil` |
| `status` | `active`, `completed` |
| `eventType` / `state` | `lost`, `reacquired` |
| Analytics `drops[].type` | `lost` (reported by the device), `stationary` (inferred here) |

### Object description

`POST /api/sessions/{id}/description` takes the padded colour crop of the frame that
seeded the tracker:

```json
{ "imageBase64": "<raw base64, or a full data: URL>", "mimeType": "image/jpeg" }
```

The server asks a Groq vision model what the object is, stores it on the session, and
pushes `SessionDescribed` to every dashboard. **The Groq key never leaves the server**,
which is the whole reason this is an upload rather than a direct call from the device.

The payload is verified before anything is spent: base64 decoded once, capped at 3 MB
decoded, and identified by magic bytes (JPEG, PNG, WebP). The real format wins over the
declared `mimeType`, so a mislabelled upload still works. Called at most once per session.

| Response | Meaning |
| --- | --- |
| `200` | Description generated and stored. |
| `400` | Not valid base64, not an image, or over the size cap. No Groq call was made. |
| `409` | This session already has a description. |
| `502` | Groq failed — its own message is passed through, including the rate-limit "try again in Ns". |
| `503` | `Groq:ApiKey` is not configured. Descriptions are off; tracking is unaffected. |

## Realtime

### Mobile ingest — `WS /ws/track?sessionId={guid}`

Plain WebSocket. The handshake is refused with `404` unless the session exists and is
still `active`, and with `400` without a `sessionId`. On connect the server sends
`{"type":"connected","session":{...}}`.

Client sends:

```json
{"type":"frame",  "frameTimestamp":"...", "x":100, "y":200, "width":96, "height":96, "fps":29.7}
{"type":"status", "state":"lost",         "occurredAt":"..."}
{"type":"ping"}
```

Server replies `{"type":"pong"}` to a ping, and `{"type":"error","message":"..."}` to
anything malformed or out of range. A rejected frame is **not** stored — see
[Validation](#validation).

`role=dashboard` on this endpoint returns `410 Gone`; dashboards moved to the hub.

### Dashboards — `SignalR /hubs/tracking`

Client calls `SubscribeToSession(guid)` / `UnsubscribeFromSession(guid)` /
`GetActiveSessions()`.

Server calls: `ActiveSessions` (once, on connect), `Frame` and `Status`
(**group-scoped** — only the subscribed session, since frames are high volume),
`SessionStarted`, `SessionEnded`, `SessionDescribed` (**all clients**, so session lists
stay current without polling).

Group membership does not survive a SignalR reconnect. Re-subscribe after `onreconnected`
and resync the active list, or the dashboard goes quiet.

## Validation

Bounds live in one place, `Validation/TrackingLimits.cs`, so REST and the socket cannot
drift apart. The socket path needs it most: it does not go through model binding, so
without explicit checks anything it received reached the database.

| Rule | Limit |
| --- | --- |
| Frame `x` / `y` | Within one frame of slack either side of the session's reported size (`-width … 2×width`). Falls back to ±100,000 when the device reported no size. |
| Frame `width` / `height` | `0` to twice the frame dimension. Zero is legal — a lost tracker reports an empty box. |
| `fps` | 0–1000, and never NaN or infinity. |
| Frame / event timestamps | Between the session start and now, ±5 minutes of clock skew. |
| `startTime` | Within 2 days of server time. |
| `endTime` | Not before `startTime`, not in the future, not implying a run over 24 hours. |
| `processingScale` | 0.05–1.0. |
| `page` | Clamped 1–100,000. `Skip()` takes an int, so an unclamped page overflows `(page - 1) * perPage`. |
| Device strings | Sanitised, not rejected: control characters stripped, whitespace collapsed, truncated to the column width. |

Coordinates are bounded by the session's own frame size rather than an absolute cap
because an absolute cap is useless here — `x = -5000` sits comfortably inside ±100,000
yet is 5,000 px off a 720-wide frame, and plots as a spike that flattens the whole chart.

## Data model

Tables are snake_case; the API is camelCase. Mapping happens in `AppDbContext` and the
DTOs, nowhere else.

**`tracking_sessions`** — `id`, `session_number`, `start_time`, `end_time`, `camera_type`,
`tracker_algorithm`, `average_fps`, `status`, `is_successful`, `object_description`,
`device_model`, `os_version`, `app_version`, `processing_scale`, `screen_width`,
`screen_height`

**`session_frames`** — `id`, `session_id`, `frame_timestamp`, `x_coordinate`,
`y_coordinate`, `width`, `height`

**`session_events`** — `id`, `session_id`, `event_type`, `occurred_at`

Session numbers are `TS-YYYYMMDD-NNNN`, restarting daily. The sequence continues from the
highest number issued that day rather than counting rows, so deleting a session cannot
make the next number collide with a live one.

`screen_width` / `screen_height` are the **camera image** dimensions, not the phone's
display. The dashboard maps coordinates against them, and the ingest validator derives its
coordinate bounds from them.

### Schema management

`Program.cs` calls `EnsureCreated()`, which only ever creates a *missing* database — it
will not add a column to one that already exists. Columns added after the first deploy are
therefore patched in by a guarded `ALTER TABLE` block at startup, which is idempotent and
additive.

**This is the main thing to know before changing the model.** A new property needs a line
in that block, or it will exist locally and be missing in production. Moving to EF
migrations is the right fix once the schema settles.

## Analytics

`GET /api/sessions/{id}/analytics` returns `points` (chronological, downsampled at an even
stride) and `drops`, the red zones on the dashboard timeline. Two kinds:

- **`lost`** — reported by the device as a `lost` status event.
- **`stationary`** — *inferred here*: the box moved ≤ 4 px for ≥ 2 seconds. A judgement
  call, not a measurement, which is why the dashboard labels it as inferred.

Because long sessions are downsampled, two short adjacent stationary periods can appear
merged. `frameCount` on the session is the true total; `points.length` is what was plotted.

## Deployment

Deployed to site4now shared hosting (IIS, out-of-process, self-contained win-x86).

```powershell
dotnet publish -c Release -r win-x86 --self-contained true -p:PublishReadyToRun=false -o publish-selfcontained
.\deploy-ftp.ps1 -Password '<ftp password>' -RemoteRoot '/tracking'
```

The dashboard is a static build in a subfolder of the same site:

```powershell
# built with VITE_BASE=/dashboard/ — the API owns the site root
.\deploy-ftp.ps1 -Password '<ftp password>' -RemoteRoot '/tracking/dashboard' -LocalDir '..\..\frontend\dist'
```

`deploy-ftp.ps1` uploads `app_offline.htm` first so IIS releases its lock on the exe, then
removes it at the end. Pass `-List` to inspect the remote layout without deploying.

Files already on the server at the same byte size are skipped so an interrupted run can
resume — **except** anything matching `-AlwaysUpload` (config and text files, and the app's
own `MdfTracker.Api.*` output). Size is a weak identity check and those are exactly where
it lies: a Vite `index.html` is a constant size across builds, because the asset hash it
points at is always the same length, and a recompiled assembly can land on the same byte
count.

`web.config` is kept in the project root on purpose — the SDK merges it during publish
rather than generating one, so the `<security>` block survives. Without it the host answers
`401` with a Basic-auth prompt, because it disables anonymous authentication server-wide
and expects each site to re-enable it.

## Tools

`../tools/` holds smoke and load scripts, run against a base URL:

| Script | Covers |
| --- | --- |
| `lifecycle-test.ps1` | REST session lifecycle plus the socket's refusal paths. |
| `ws-smoke.ps1` | Mobile ingest: frames, status events, ping, error replies. Needs `-SessionId`. |
| `stationary-test.ps1` | Lays down a stationary stretch and checks `/analytics` reports the zone. |
| `load-test.ps1` | Bulk frame ingest; defaults to 3000 frames. |
| `hub-smoke.ps1`, `signalr-smoke.mjs` | SignalR negotiate and hub fan-out. |

Note: `ws-smoke.ps1` errors on its final `CloseAsync`. Its receive helper cancels
`ReceiveAsync` via a timeout token, and in .NET that *aborts* the socket; it waits for more
replies than the server sends. Cosmetic — the frames it sent are still persisted.

## Known constraints

- **Groq free tier is 8,000 tokens/minute**, and one vision call costs roughly 1,360 tokens
  — almost all of it the image. That is about 5–6 descriptions per minute. Since every
  session start triggers one, rapid session cycling will hit `429`, surfaced as a `502`
  carrying Groq's own retry hint. Lowering `Groq:MaxCompletionTokens` is the cheapest lever.
- **`droppedFrames` is process-wide**, not per session. A non-zero value means stored
  history has gaps somewhere, but not which session lost them.
- **`isSuccessful` records how the run ended**, not whether tracking was clean: it is true
  when the user stopped with the target still locked. A session with reacquired drops can
  still be successful.
