using System;
using RaftMq.Core.Interfaces;

namespace RaftMq.Core.Models;

public class AppendEntriesRequest<T> where T : IRaftCommand
{
    public long Term { get; init; }
    public string LeaderId { get; init; } = string.Empty;
    public long PrevLogIndex { get; init; }
    public long PrevLogTerm { get; init; }
    public LogEntry<T>[] Entries { get; init; } = Array.Empty<LogEntry<T>>();
    public long LeaderCommit { get; init; }
}

public class AppendEntriesResponse
{
    public long Term { get; init; }
    public bool Success { get; init; }
}
