namespace Domain.Abstractions;

public sealed class PartitionChangedEventArgs(string fromState, string toState, DateTime atUtc) : EventArgs
{
    public string FromState { get; } = fromState;
    public string ToState { get; } = toState;
    public DateTime AtUtc { get; } = atUtc;
}
