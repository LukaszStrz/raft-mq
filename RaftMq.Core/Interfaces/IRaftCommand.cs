namespace RaftMq.Core.Interfaces;

/// <summary>
/// Marker interface for all commands that can be applied to the state machine.
/// Commands must be JSON serializable.
/// </summary>
public interface IRaftCommand
{
}
