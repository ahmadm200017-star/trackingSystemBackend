using System.Text.Json;
using MdfTracker.Api;
using MdfTracker.Api.Data;
using MdfTracker.Api.Realtime;
using MdfTracker.Api.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.EntityFrameworkCore;

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

builder.Services.AddProblemDetails();

// Write path: sockets enqueue, one background writer persists in batches.
builder.Services.AddSingleton<FrameQueue>();
builder.Services.AddSingleton<TrackingBroadcaster>();
builder.Services.AddHostedService<FrameWriterService>();

builder.Services.AddScoped<SessionNumberGenerator>();

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
}

app.Run();
