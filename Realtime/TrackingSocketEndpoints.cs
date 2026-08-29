using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MdfTracker.Api.Data;
using MdfTracker.Api.Responses;
using MdfTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MdfTracker.Api.Realtime;

/// <summary>
/// Ingest socket for the mobile app: /ws/track?sessionId={guid}
///
/// Plain WebSockets, because Flutter streams with <c>web_socket_channel</c> and there is
/// no first-party SignalR client for Dart. Dashboards consume the relayed stream from the
/// SignalR hub at /hubs/tracking instead — see <see cref="TrackingHub"/>.
/// </summary>
public static class TrackingSocketEndpoints
{
    private const int ReceiveBufferSize = 4 * 1024;
    private const int MaxMessageBytes = 64 * 1024;

    public static void MapTrackingSocket(this IEndpointRouteBuilder app)
    {
        app.Map("/ws/track", HandleAsync);
    }

    private static async Task HandleAsync(HttpContext context)
    {
        var role = (context.Request.Query["role"].ToString() is { Length: > 0 } value ? value : "mobile").ToLowerInvariant();

        // Checked before the handshake so an old dashboard client gets the migration
        // message whether it upgrades or just GETs the URL.
        if (role == "dashboard")
        {
            context.Response.StatusCode = StatusCodes.Status410Gone;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Dashboards moved to the SignalR hub at /hubs/tracking. " +
                          "Connect with @microsoft/signalr and call SubscribeToSession(sessionId)."
            });
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = "This endpoint expects a WebSocket handshake." });
            return;
        }

        await HandleMobileAsync(context);
    }

    // ---------- mobile: pushes frames ----------

    private static async Task HandleMobileAsync(HttpContext context)
    {
        var broadcaster = context.RequestServices.GetRequiredService<TrackingBroadcaster>();
        var queue = context.RequestServices.GetRequiredService<FrameQueue>();
        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TrackingSocket");

        if (!Guid.TryParse(context.Request.Query["sessionId"], out var sessionId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = "sessionId query parameter is required for role=mobile." });
            return;
        }

        var session = await db.TrackingSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null || session.Status != SessionStatus.Active)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { message = "No active session with that id." });
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        broadcaster.AddMobile(session.Id, session.SessionNumber);
        logger.LogInformation("Mobile stream connected for session {SessionNumber}", session.SessionNumber);

        await broadcaster.SendAsync(socket, new
        {
            type = "connected",
            session = SessionResponse.From(session)
        }, context.RequestAborted);

        try
        {
            await foreach (var payload in ReadMessagesAsync(socket, context.RequestAborted))
            {
                IncomingWsMessage? message;
                try
                {
                    message = JsonSerializer.Deserialize<IncomingWsMessage>(payload, JsonConfig.Options);
                }
                catch (JsonException)
                {
                    await broadcaster.SendAsync(socket, new { type = "error", message = "Malformed JSON." }, context.RequestAborted);
                    continue;
                }

                switch (message?.Type?.ToLowerInvariant())
                {
                    case "frame":
                        await HandleFrameAsync(message, session, queue, broadcaster, context.RequestAborted);
                        break;

                    case "status":
                        await HandleStatusAsync(message, session, queue, broadcaster, socket, context.RequestAborted);
                        break;

                    case "ping":
                        await broadcaster.SendAsync(socket, new { type = "pong" }, context.RequestAborted);
                        break;

                    default:
                        await broadcaster.SendAsync(
                            socket,
                            new { type = "error", message = "Unknown message type. Expected 'frame', 'status' or 'ping'." },
                            context.RequestAborted);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client went away.
        }
        catch (WebSocketException ex)
        {
            logger.LogDebug(ex, "Mobile socket closed abruptly for session {SessionNumber}", session.SessionNumber);
        }
        finally
        {
            broadcaster.RemoveMobile(session.Id);
            logger.LogInformation("Mobile stream disconnected for session {SessionNumber}", session.SessionNumber);
        }
    }

    private static async Task HandleFrameAsync(
        IncomingWsMessage message,
        TrackingSession session,
        FrameQueue queue,
        TrackingBroadcaster broadcaster,
        CancellationToken cancellationToken)
    {
        if (message.X is null || message.Y is null)
        {
            return;
        }

        var timestamp = message.FrameTimestamp ?? DateTimeOffset.UtcNow;

        queue.EnqueueFrame(new SessionFrame
        {
            SessionId = session.Id,
            FrameTimestamp = timestamp,
            XCoordinate = message.X.Value,
            YCoordinate = message.Y.Value,
            Width = message.Width ?? 0,
            Height = message.Height ?? 0
        });

        broadcaster.ReportFps(session.Id, message.Fps);

        await broadcaster.BroadcastFrameAsync(new LiveFrameMessage
        {
            SessionId = session.Id,
            SessionNumber = session.SessionNumber,
            FrameTimestamp = timestamp,
            X = message.X.Value,
            Y = message.Y.Value,
            Width = message.Width ?? 0,
            Height = message.Height ?? 0,
            Fps = message.Fps
        });
    }

    private static async Task HandleStatusAsync(
        IncomingWsMessage message,
        TrackingSession session,
        FrameQueue queue,
        TrackingBroadcaster broadcaster,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<SessionEventType>(message.State, ignoreCase: true, out var state))
        {
            await broadcaster.SendAsync(
                socket,
                new { type = "error", message = "status.state must be 'lost' or 'reacquired'." },
                cancellationToken);
            return;
        }

        var occurredAt = message.OccurredAt ?? DateTimeOffset.UtcNow;

        queue.EnqueueEvent(new SessionEvent
        {
            SessionId = session.Id,
            EventType = state,
            OccurredAt = occurredAt
        });

        await broadcaster.BroadcastStatusAsync(new LiveStatusMessage
        {
            SessionId = session.Id,
            SessionNumber = session.SessionNumber,
            State = state,
            OccurredAt = occurredAt
        });
    }

    // ---------- framing ----------

    private static async IAsyncEnumerable<string> ReadMessagesAsync(
        WebSocket socket,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferSize];

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var payload = new MemoryStream();
            WebSocketReceiveResult result;

            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", CancellationToken.None);
                    yield break;
                }

                if (payload.Length + result.Count > MaxMessageBytes)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too large", CancellationToken.None);
                    yield break;
                }

                payload.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text || payload.Length == 0)
            {
                continue;
            }

            yield return Encoding.UTF8.GetString(payload.ToArray());
        }
    }
}
