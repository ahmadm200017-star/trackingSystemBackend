using System.Text.Json;
using MdfTracker.Api;
using MdfTracker.Api.Data;
using MdfTracker.Api.Middleware;
using MdfTracker.Api.Realtime;
using MdfTracker.Api.Services;
using MdfTracker.Api.Services.Vision;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ---------- services ----------

var connectionString = builder.Configuration.GetConnectionString("Default")
                       ?? "Server=(localdb)\\MSSQLLocalDB;Database=mdf_tracker;Trusted_Connection=True;TrustServerCertificate=True";

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

builder.Services
    .AddControllers(options =>
        // Without this, validation errors are keyed by the C# property name ("CameraType")
        // while the rest of the payload is camelCase.
        options.ModelMetadataDetailsProviders.Add(
            new SystemTextJsonValidationMetadataProvider(JsonNamingPolicy.CamelCase)))
    .AddJsonOptions(options => JsonConfig.Apply(options.JsonSerializerOptions));

// The socket endpoint writes its handshake/error bodies with WriteAsJsonAsync,
// which reads these options rather than the MVC ones.
builder.Services.ConfigureHttpJsonOptions(options => JsonConfig.Apply(options.SerializerOptions));

// Same JSON contract over the hub as over REST: camelCase, lowercase string enums.
builder.Services.AddSignalR().AddJsonProtocol(options => JsonConfig.Apply(options.PayloadSerializerOptions));

// A database outage answers 503 with a Retry-After rather than a bare 500, so a device
// can tell "hold the data and retry" apart from "your request was wrong".
builder.Services.AddExceptionHandler<DatabaseFailureHandler>();
builder.Services.AddProblemDetails();

// Write path: sockets enqueue, one background writer persists in batches.
builder.Services.AddSingleton<FrameQueue>();
builder.Services.AddSingleton<TrackingBroadcaster>();
builder.Services.AddHostedService<FrameWriterService>();

// Sessions whose device stops talking are closed by the server, so the live room and the
// session list cannot show a run that ended when the app was killed as still active.
builder.Services.Configure<SessionReaperOptions>(
    builder.Configuration.GetSection(SessionReaperOptions.SectionName));
builder.Services.AddHostedService<StaleSessionReaper>();

builder.Services.AddScoped<SessionNumberGenerator>();

// Object description: the Groq key lives here, never on the device. Without it the
// description endpoint answers 503 and tracking carries on unaffected.
builder.Services.Configure<GroqOptions>(builder.Configuration.GetSection(GroqOptions.SectionName));

builder.Services.AddHttpClient<GroqVisionClient>((provider, client) =>
{
    var options = provider.GetRequiredService<IOptions<GroqOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    // SignalR's browser client sends credentials on the negotiate request, and
    // AllowAnyOrigin() is illegal together with AllowCredentials() — so the dev default
    // reflects the caller's origin instead of using a wildcard.
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins);
    }
    else
    {
        policy.SetIsOriginAllowed(_ => true);
    }

    policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

var app = builder.Build();

// ---------- pipeline ----------

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20)
});

app.MapControllers();
app.MapTrackingSocket();
app.MapHub<TrackingHub>("/hubs/tracking");

// Simple project, simple schema management: create the database on first run.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // EnsureCreated only ever creates a missing database - it will not add a column to one
    // that already exists. object_description arrived after the first deploy, so it is
    // patched in by hand here. Drop this once the project moves to EF migrations.
    db.Database.ExecuteSqlRaw(
        @"IF COL_LENGTH('tracking_sessions', 'object_description') IS NULL
              ALTER TABLE tracking_sessions ADD object_description NVARCHAR(500) NULL;
          IF COL_LENGTH('tracking_sessions', 'device_model') IS NULL
              ALTER TABLE tracking_sessions ADD device_model NVARCHAR(120) NULL;
          IF COL_LENGTH('tracking_sessions', 'os_version') IS NULL
              ALTER TABLE tracking_sessions ADD os_version NVARCHAR(60) NULL;
          IF COL_LENGTH('tracking_sessions', 'app_version') IS NULL
              ALTER TABLE tracking_sessions ADD app_version NVARCHAR(30) NULL;
          IF COL_LENGTH('tracking_sessions', 'processing_scale') IS NULL
              ALTER TABLE tracking_sessions ADD processing_scale DECIMAL(3,2) NULL;
          IF COL_LENGTH('tracking_sessions', 'latitude') IS NULL
              ALTER TABLE tracking_sessions ADD latitude DECIMAL(9,6) NULL;
          IF COL_LENGTH('tracking_sessions', 'longitude') IS NULL
              ALTER TABLE tracking_sessions ADD longitude DECIMAL(9,6) NULL;
          IF COL_LENGTH('tracking_sessions', 'location_accuracy_m') IS NULL
              ALTER TABLE tracking_sessions ADD location_accuracy_m DECIMAL(8,2) NULL;
          IF COL_LENGTH('tracking_sessions', 'imu_enabled') IS NULL
              ALTER TABLE tracking_sessions ADD imu_enabled BIT NOT NULL CONSTRAINT df_tracking_sessions_imu_enabled DEFAULT 0;
          IF COL_LENGTH('session_frames', 'fps') IS NULL
              ALTER TABLE session_frames ADD fps DECIMAL(6,2) NULL;
          IF COL_LENGTH('tracking_sessions', 'auto_closed') IS NULL
              ALTER TABLE tracking_sessions ADD auto_closed BIT NOT NULL CONSTRAINT df_tracking_sessions_auto_closed DEFAULT 0;
          IF COL_LENGTH('session_frames', 'target_latitude') IS NULL
              ALTER TABLE session_frames ADD target_latitude DECIMAL(9,6) NULL;
          IF COL_LENGTH('session_frames', 'target_longitude') IS NULL
              ALTER TABLE session_frames ADD target_longitude DECIMAL(9,6) NULL;");
}

app.Run();
