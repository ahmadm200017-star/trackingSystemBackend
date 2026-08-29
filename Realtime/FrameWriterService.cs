using MdfTracker.Api.Data;
using MdfTracker.Api.Models;

namespace MdfTracker.Api.Realtime;

/// <summary>
/// Drains <see cref="FrameQueue"/> and persists frames/events in batches, so a fast
/// streaming device never blocks on a database round trip per frame.
/// </summary>
public class FrameWriterService : BackgroundService
{
    private const int BatchSize = 500;

    private readonly FrameQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FrameWriterService> _logger;

    public FrameWriterService(FrameQueue queue, IServiceScopeFactory scopeFactory, ILogger<FrameWriterService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var frames = new List<SessionFrame>(BatchSize);
        var events = new List<SessionEvent>(32);

        try
        {
            // Wake up as soon as anything is queued, then take everything that piled up
            // while the previous batch was being written.
            while (await _queue.WaitToReadAsync(stoppingToken))
            {
                while (_queue.TryRead(out var item))
                {
                    switch (item)
                    {
                        case SessionFrame frame:
                            frames.Add(frame);
                            break;
                        case SessionEvent sessionEvent:
                            events.Add(sessionEvent);
                            break;
                    }

                    if (frames.Count >= BatchSize)
                    {
                        await FlushAsync(frames, events, stoppingToken);
                    }
                }

                await FlushAsync(frames, events, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }

        await FlushAsync(frames, events, CancellationToken.None);
    }

    private async Task FlushAsync(List<SessionFrame> frames, List<SessionEvent> events, CancellationToken cancellationToken)
    {
        if (frames.Count == 0 && events.Count == 0)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (frames.Count > 0)
            {
                db.SessionFrames.AddRange(frames);
            }

            if (events.Count > 0)
            {
                db.SessionEvents.AddRange(events);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist {FrameCount} frames and {EventCount} events", frames.Count, events.Count);
        }
        finally
        {
            frames.Clear();
            events.Clear();
        }
    }
}
