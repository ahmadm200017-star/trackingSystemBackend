using MdfTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace MdfTracker.Api.Services;

/// <summary>Human friendly session numbers: TS-20260827-0007, restarting each day.</summary>
public class SessionNumberGenerator
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly AppDbContext _db;

    public SessionNumberGenerator(AppDbContext db) => _db = db;

    public async Task<string> NextAsync(DateTimeOffset startTime, CancellationToken cancellationToken = default)
    {
        var prefix = $"TS-{startTime.UtcDateTime:yyyyMMdd}-";

        await Gate.WaitAsync(cancellationToken);
        try
        {
            // Continue from the highest number issued today rather than counting rows:
            // a deleted session must not make the next number collide with a live one.
            var latest = await _db.TrackingSessions
                .Where(s => s.SessionNumber.StartsWith(prefix))
                .OrderByDescending(s => s.SessionNumber)
                .Select(s => s.SessionNumber)
                .FirstOrDefaultAsync(cancellationToken);

            var next = 1;
            if (latest is not null && int.TryParse(latest[prefix.Length..], out var sequence))
            {
                next = sequence + 1;
            }

            return $"{prefix}{next:D4}";
        }
        finally
        {
            Gate.Release();
        }
    }
}
