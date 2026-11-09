using System.Collections.Concurrent;

namespace ArasPerformanceTool.Api.Services;

public sealed class RunExecutionCoordinator
{
    private readonly ConcurrentDictionary<Guid, byte> _activeRuns = new();

    public bool TryAcquire(Guid runId)
    {
        return _activeRuns.TryAdd(runId, 0);
    }

    public void Release(Guid runId)
    {
        _activeRuns.TryRemove(runId, out _);
    }

    public bool IsActive(Guid runId)
    {
        return _activeRuns.ContainsKey(runId);
    }
}