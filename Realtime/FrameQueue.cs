using System.Threading.Channels;
using MdfTracker.Api.Models;

namespace MdfTracker.Api.Realtime;

/// <summary>
/// The write side of the system: sockets never touch the database directly, they drop
/// frames/events into this in-memory queue and a background writer batches them out.
/// </summary>
public class FrameQueue
{
    private const int Capacity = 50_000;

    private readonly Channel<object> _channel = Channel.CreateBounded<object>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

    private int _dropped;

    /// <summary>Frames discarded because the writer could not keep up. Exposed on /api/health.</summary>
    public int Dropped => _dropped;

    public void EnqueueFrame(SessionFrame frame) => Write(frame);

    public void EnqueueEvent(SessionEvent sessionEvent) => Write(sessionEvent);

    private void Write(object item)
    {
        if (!_channel.Writer.TryWrite(item))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken) =>
        _channel.Reader.WaitToReadAsync(cancellationToken);

    public bool TryRead(out object? item) => _channel.Reader.TryRead(out item);
}
