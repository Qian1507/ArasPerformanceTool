
using System.Text.Json;

namespace ArasPerformanceTool.Engine.Benchmarking;

public enum TrackedItemStatus
{
    Active,
    DeletePending,
    Deleted,
    AlreadyMissing,
    CleanupFailed
}

public enum TestItemSource
{
    Standard,
    CustomAml
}

public sealed record TrackedItem(
    string ItemType,
    string Id,
    DateTime CreatedAtUtc)
{
    public TrackedItemStatus Status { get; init; }
        = TrackedItemStatus.Active;

    public TestItemSource Source { get; init; }
        = TestItemSource.Standard;

    public string? ConfigId { get; init; }
    public string? ParentItemId { get; init; }
    public string? LastError { get; init; }
}

public sealed class TestDataManifest
{
    private readonly object _sync = new();

    private readonly Dictionary<string, TrackedItem> _items =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string? _filePath;

    public string RunId { get; private set; }
    public string? EnvironmentId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

 public TestDataManifest(
    string? environmentId = null,
    string? filePath = null,
    string? runId = null)
{
    if (filePath != null &&
        string.IsNullOrWhiteSpace(environmentId))
    {
        throw new ArgumentException(
            "EnvironmentId is required for persistence.");
    }

    EnvironmentId = environmentId;
    _filePath = filePath;

    RunId = string.IsNullOrWhiteSpace(runId)
        ? Guid.NewGuid().ToString("N")
        : runId;

    CreatedAtUtc = DateTime.UtcNow;

    lock (_sync)
    {
        PersistUnderLock();
    }
}

    private TestDataManifest(
        string filePath,
        ManifestSnapshot snapshot)
    {
        _filePath = filePath;
        RunId = snapshot.RunId;
        EnvironmentId = snapshot.EnvironmentId;
        CreatedAtUtc = snapshot.CreatedAtUtc;

        foreach (var item in snapshot.Items)
        {
            _items[GetKey(item.ItemType, item.Id)] = item;
        }
    }

    public static TestDataManifest Load(
        string filePath,
        string expectedEnvironmentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            expectedEnvironmentId);

        var json = File.ReadAllText(filePath);

        var snapshot =
            JsonSerializer.Deserialize<ManifestSnapshot>(json)
            ?? throw new InvalidDataException(
                "Manifest is empty.");

        if (snapshot.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                "Unsupported manifest version.");
        }

        if (!string.Equals(
            snapshot.EnvironmentId,
            expectedEnvironmentId,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Aras environment mismatch.");
        }

        if (!Guid.TryParseExact(snapshot.RunId, "N", out _)
            || snapshot.Items == null)
        {
            throw new InvalidDataException(
                "Manifest is missing required data or has an invalid RunId.");
        }

        // Add this validation.
        var fileNameRunId = Path.GetFileNameWithoutExtension(filePath);

        if (!string.Equals(
            fileNameRunId,
            snapshot.RunId,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Manifest filename does not match its RunId.");
        }

        // Validate all tracked items before loading them.
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in snapshot.Items)
        {
            if (item == null ||
                string.IsNullOrWhiteSpace(item.ItemType) ||
                string.IsNullOrWhiteSpace(item.Id))
            {
                throw new InvalidDataException(
                    "Manifest contains an invalid tracked item.");
            }

            if (!Enum.IsDefined(item.Status) ||
                !Enum.IsDefined(item.Source))
            {
                throw new InvalidDataException(
                    "Manifest contains an invalid item status or source.");
            }

            var key = GetKey(item.ItemType, item.Id);

            if (!keys.Add(key))
            {
                throw new InvalidDataException(
                    "Manifest contains duplicate tracked items.");
            }
        }

        return new TestDataManifest(filePath, snapshot);
    }

    public IReadOnlyList<TrackedItem> Items
    {
        get
        {
            lock (_sync)
            {
                return _items.Values.ToArray();
            }
        }
    }

    public IReadOnlyList<TrackedItem> ActiveItems
    {
        get
        {
            lock (_sync)
            {
                return _items.Values
                    .Where(x =>
                        x.Status == TrackedItemStatus.Active)
                    .ToArray();
            }
        }
    }

    public IReadOnlyList<TrackedItem> UnresolvedItems
    {
        get
        {
            lock (_sync)
            {
                return _items.Values
                    .Where(x =>
                        x.Status != TrackedItemStatus.Deleted &&
                        x.Status != TrackedItemStatus.AlreadyMissing)
                    .ToArray();
            }
        }
    }

    // Compatible with existing controller checks.
    public int TotalCount
    {
        get
        {
            lock (_sync)
            {
                return _items.Values.Count(x =>
                    x.Status != TrackedItemStatus.Deleted &&
                    x.Status != TrackedItemStatus.AlreadyMissing);
            }
        }
    }
    
    public void Track(string itemType, string id)
    {
        Track(itemType, id, TestItemSource.Standard);
    }

    public void Track(
        string itemType,
        string id,
        TestItemSource source,
        string? configId = null,
        string? parentItemId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemType);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        lock (_sync)
        {
            var key = GetKey(itemType, id);

            if (_items.ContainsKey(key))
                return;

            _items[key] = new TrackedItem(
                itemType, id, DateTime.UtcNow)
            {
                Source = source,
                ConfigId = configId,
                ParentItemId = parentItemId
            };

            PersistUnderLock();
        }
    }

    public IReadOnlyList<string> GetIds(string itemType)
    {
        lock (_sync)
        {
            return _items.Values
                .Where(x =>
                    x.Status != TrackedItemStatus.Deleted &&
                    x.Status != TrackedItemStatus.AlreadyMissing &&
                    string.Equals(
                        x.ItemType,
                        itemType,
                        StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Id)
                .ToArray();
        }
    }

    public bool MarkPending(string itemType, string id)
    {
        return UpdateStatus(
            itemType, id,
            TrackedItemStatus.DeletePending);
    }

    public bool MarkDeleted(string itemType, string id)
    {
        return UpdateStatus(
            itemType, id,
            TrackedItemStatus.Deleted);
    }

    public bool MarkAlreadyMissing(string itemType, string id)
    {
        return UpdateStatus(
            itemType,
            id,
            TrackedItemStatus.AlreadyMissing);
    }
    
    public bool MarkFailed(
        string itemType,
        string id,
        string? error = null)
    {
        return UpdateStatus(
            itemType, id,
            TrackedItemStatus.CleanupFailed,
            error);
    }

    // Backward compatibility with existing CleanupAsync.
    public bool Remove(string itemType, string id)
    {
        return MarkDeleted(itemType, id);
    }

    // Explicit reset only, not normal cleanup.
    public void Clear()
    {
        lock (_sync)
        {
            _items.Clear();
            PersistUnderLock();
        }
    }

    private bool UpdateStatus(
        string itemType,
        string id,
        TrackedItemStatus status,
        string? error = null)
    {
        lock (_sync)
        {
            var key = GetKey(itemType, id);

            if (!_items.TryGetValue(key, out var item))
                return false;

            _items[key] = item with
            {
                Status = status,
                LastError = error
            };

            PersistUnderLock();
            return true;
        }
    }

    private void PersistUnderLock()
    {
        if (_filePath == null)
            return;

        var fullPath = Path.GetFullPath(_filePath);
        var directory = Path.GetDirectoryName(fullPath)!;

        Directory.CreateDirectory(directory);

        var tempPath = fullPath + "." +
            Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            var snapshot = new ManifestSnapshot(
                1,
                RunId,
                EnvironmentId!,
                CreatedAtUtc,
                _items.Values.ToArray());

            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                snapshot,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            // Temp file and manifest are in the same directory.
            // Overwrite the existing manifest with the new snapshot.
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static string GetKey(
        string itemType,
        string id)
    {
        return $"{itemType.ToUpperInvariant()}:" +
               $"{id.ToUpperInvariant()}";
    }

    private sealed record ManifestSnapshot(
        int SchemaVersion,
        string RunId,
        string EnvironmentId,
        DateTime CreatedAtUtc,
        IReadOnlyList<TrackedItem> Items);
}
