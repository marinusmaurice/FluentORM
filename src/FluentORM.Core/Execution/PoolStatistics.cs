namespace FluentORM.Core.Execution;

public sealed class PoolStatistics
{
    public int Active { get; init; }
    public int Idle { get; init; }
    public int WaitCount { get; init; }
    public int TotalCreated { get; init; }
}
