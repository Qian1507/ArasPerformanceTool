
using System.Text;
using System.Text.Json;
using ArasPerformanceTool.Engine.Benchmarking;
using Xunit;

namespace ArasPerformanceTool.Tests.Unit;

public sealed class TestDataManifestStoreTests : IDisposable
{
    private const string EnvironmentId =
        "http://localhost/InnovatorServer/Server/InnovatorServer.aspx|InnovatorSolutions";

    private const string OtherEnvironmentId =
        "http://localhost/OtherServer|OtherDatabase";

    private readonly string _directory;

    public TestDataManifestStoreTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "ArasPerformanceToolTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Scan_WhenDirectoryDoesNotExist_ReturnsEmptyResults()
    {
        var store = new TestDataManifestStore(
            Path.Combine(_directory, "NotExisting"));

        var result = store.Scan(EnvironmentId);

        Assert.Empty(result.PendingRuns);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Scan_WhenAllItemsAreDeleted_ReturnsNoPendingRuns()
    {
        var path = WriteManifest(
            statuses: [TrackedItemStatus.Deleted, TrackedItemStatus.Deleted]);

        var result = new TestDataManifestStore(_directory)
            .Scan(EnvironmentId);

        Assert.Empty(result.PendingRuns);
        Assert.Empty(result.Errors);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Scan_WhenItemsAreUnresolved_ReturnsCorrectRun()
    {
        var runId = Guid.NewGuid().ToString("N");

        WriteManifest(
            statuses:
            [
                TrackedItemStatus.Active,
                TrackedItemStatus.Active,
                TrackedItemStatus.Deleted
            ],
            runId: runId);

        var result = new TestDataManifestStore(_directory)
            .Scan(EnvironmentId);

        var pending = Assert.Single(result.PendingRuns);

        Assert.Equal(runId, pending.RunId);
        Assert.Equal(EnvironmentId, pending.EnvironmentId);
        Assert.Equal(2, pending.RemainingCount);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Scan_WhenItemsArePendingOrFailed_CountsThemAsUnresolved()
    {
        WriteManifest(
            statuses:
            [
                TrackedItemStatus.Active,
                TrackedItemStatus.DeletePending,
                TrackedItemStatus.CleanupFailed,
                TrackedItemStatus.Deleted,
                TrackedItemStatus.AlreadyMissing
            ]);

        var result = new TestDataManifestStore(_directory)
            .Scan(EnvironmentId);

        var pending = Assert.Single(result.PendingRuns);

        Assert.Equal(3, pending.RemainingCount);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Scan_WhenItemsAreDeletedOrMissing_ReturnsNoPendingRuns()
    {
        var path = WriteManifest(
            statuses:
            [
                TrackedItemStatus.Deleted,
                TrackedItemStatus.AlreadyMissing
            ]);

        var result = new TestDataManifestStore(_directory)
            .Scan(EnvironmentId);

        Assert.Empty(result.PendingRuns);
        Assert.Empty(result.Errors);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Scan_WhenOneFileIsCorrupted_ContinuesScanning()
    {
        WriteManifest(
            statuses: [TrackedItemStatus.Active]);

        var corruptedPath = Path.Combine(
            _directory,
            "corrupted.json");

        File.WriteAllText(corruptedPath, "{ invalid json");

        var result = new TestDataManifestStore(_directory)
            .Scan(EnvironmentId);

        Assert.Single(result.PendingRuns);

        var error = Assert.Single(result.Errors);
        Assert.Equal(corruptedPath, error.ManifestPath);
    }

    [Fact]
    public void Scan_WhenSchemaVersionIsUnsupported_RecordsError()
    {
        var path = WriteManifest(
            statuses: [TrackedItemStatus.Active],
            schemaVersion: 999);

        var result = new TestDataManifestStore(_directory)
            .Scan(EnvironmentId);

        Assert.Empty(result.PendingRuns);

        var error = Assert.Single(result.Errors);
        Assert.Equal(path, error.ManifestPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Scan_WhenEnvironmentIdIsBlank_RecordsError(
        string environmentId)
    {
        var path = WriteManifest(
            statuses: [TrackedItemStatus.Active],
            environmentId: environmentId);

        var result = new TestDataManifestStore(_directory)
            .Scan(EnvironmentId);

        Assert.Empty(result.PendingRuns);

        var error = Assert.Single(result.Errors);
        Assert.Equal(path, error.ManifestPath);
    }

    [Fact]
    public void Scan_WhenEnvironmentDoesNotMatch_SkipsManifest()
    {
        WriteManifest(
            statuses: [TrackedItemStatus.Active],
            environmentId: OtherEnvironmentId);

        var result = new TestDataManifestStore(_directory)
            .Scan(EnvironmentId);

        Assert.Empty(result.PendingRuns);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Scan_WhenCurrentAndOtherEnvironmentsExist_ReturnsOnlyCurrent()
    {
        var currentRunId = Guid.NewGuid().ToString("N");

        WriteManifest(
            statuses: [TrackedItemStatus.Active],
            runId: currentRunId,
            environmentId: EnvironmentId);

        WriteManifest(
            statuses: [TrackedItemStatus.Active],
            environmentId: OtherEnvironmentId);

        var result = new TestDataManifestStore(_directory)
            .Scan(EnvironmentId);

        Assert.True(
            result.PendingRuns.Count > 0,
            $"PendingRuns={result.PendingRuns.Count}, " +
            $"Errors={result.Errors.Count}, " +
            $"Details={string.Join("; ", result.Errors.Select(e => e.ErrorMessage))}");

        var pending = Assert.Single(result.PendingRuns);

        Assert.Equal(currentRunId, pending.RunId);
        Assert.Equal(1, pending.RemainingCount);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Scan_DoesNotModifyManifestFile()
    {
        var path = WriteManifest(
            statuses:
            [
                TrackedItemStatus.Active,
                TrackedItemStatus.Deleted
            ]);

        var before = File.ReadAllBytes(path);

        var result = new TestDataManifestStore(_directory)
            .Scan(EnvironmentId);

        var after = File.ReadAllBytes(path);

        Assert.Single(result.PendingRuns);
        Assert.Empty(result.Errors);
        Assert.Equal(before, after);
    }

    private string WriteManifest(
        TrackedItemStatus[] statuses,
        string? runId = null,
        string? environmentId = null,
        int schemaVersion = 1)
    {
        runId ??= Guid.NewGuid().ToString("N");
        environmentId ??= EnvironmentId;

        var createdAt = DateTime.UtcNow;

        var manifest = new
        {
            SchemaVersion = schemaVersion,
            RunId = runId,
            EnvironmentId = environmentId,
            CreatedAtUtc = createdAt,

            Items = statuses.Select(status => new
            {
                ItemType = "Part",
                Id = Guid.NewGuid()
                    .ToString("N")
                    .ToUpperInvariant(),
                CreatedAtUtc = createdAt,
                Status = (int)status,
                Source = 0,
                ConfigId = (string?)null,
                ParentItemId = (string?)null,
                LastError = (string?)null
            }).ToArray()
        };

        var path = Path.Combine(
            _directory,
            $"{runId}.json");

        var json = JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        File.WriteAllText(path, json, Encoding.UTF8);

        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
