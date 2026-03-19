using RaftMq.Core.Interfaces;

namespace RaftMq.Core.Models;

public class LogEntry<T> where T : IRaftCommand
{
    public long Term { get; init; }
    public long Index { get; init; }
    public T Command { get; init; } = default!;
}
