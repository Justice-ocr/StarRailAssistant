using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace SRAFrontend.Server.Services;

public sealed class SraLogStreamService
{
    private readonly ConcurrentQueue<string> _recentLogs = new();
    private readonly ConcurrentDictionary<Guid, Channel<string>> _subscribers = new();
    private const int MaxRecentLogs = 500;

    public void Push(string line)
    {
        _recentLogs.Enqueue(line);
        while (_recentLogs.Count > MaxRecentLogs && _recentLogs.TryDequeue(out _)) { }

        foreach (var subscriber in _subscribers.Values)
            subscriber.Writer.TryWrite(line);
    }

    public List<string> GetRecentLogs(int count)
    {
        return _recentLogs.TakeLast(Math.Max(0, count)).ToList();
    }

    public async IAsyncEnumerable<string> Subscribe([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });
        _subscribers[id] = channel;

        try
        {
            await foreach (var line in channel.Reader.ReadAllAsync(cancellationToken))
                yield return line;
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
        }
    }
}
