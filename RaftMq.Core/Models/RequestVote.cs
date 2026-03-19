namespace RaftMq.Core.Models;

public class RequestVoteRequest
{
    public long Term { get; init; }
    public string CandidateId { get; init; } = string.Empty;
    public long LastLogIndex { get; init; }
    public long LastLogTerm { get; init; }
}

public class RequestVoteResponse
{
    public long Term { get; init; }
    public bool VoteGranted { get; init; }
}
