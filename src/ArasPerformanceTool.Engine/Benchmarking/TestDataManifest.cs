
using System.Collections.Concurrent;

namespace ArasPerformanceTool.Engine.Benchmarking;

public class TestDataManifest
{
    // Track test records using a unique ItemType + ItemId key
    private readonly ConcurrentDictionary<string, TrackedItem> _items = new();

    // Unique identifier for the current benchmark run
    public string RunId { get; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;

    // Return a snapshot of all currently tracked records
    public IReadOnlyList<TrackedItem> Items => _items.Values.ToList();

    public int TotalCount => _items.Count;

    // Register a record created by the benchmark
    public void Track(string itemType, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemType);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var key = GetKey(itemType, id);

        _items.TryAdd(
            key,
            new TrackedItem(itemType, id, DateTime.UtcNow));
    }

    // Get tracked IDs belonging to a specific ItemType
    public IReadOnlyList<string> GetIds(string itemType)
    {
        return _items.Values
            .Where(x => string.Equals(
                x.ItemType,
                itemType,
                StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Id)
            .ToList();
    }

    // Remove a record only after cleanup is confirmed
    public bool Remove(string itemType, string id)
    {
        return _items.TryRemove(
            GetKey(itemType, id),
            out _);
    }

    // Clear all tracked records (not used for normal cleanup)
    public void Clear()
    {
        _items.Clear();
    }

    private static string GetKey(string itemType, string id)
    {
        return $"{itemType.ToUpperInvariant()}:{id.ToUpperInvariant()}";
    }
}

// Represents a record created by a benchmark run
public sealed record TrackedItem(
    string ItemType,
    string Id,
    DateTime CreatedAtUtc);
