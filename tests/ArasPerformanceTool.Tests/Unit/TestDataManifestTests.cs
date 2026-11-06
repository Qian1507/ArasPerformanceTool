
using ArasPerformanceTool.Engine.Benchmarking;
using Xunit;

namespace ArasPerformanceTool.Tests.Unit;

public sealed class TestDataManifestTests
{
    private const string EnvironmentId = "test-environment";

    [Fact]
    public void Track_ShouldAddItemToUnresolvedItems()
    {
        var manifest = new TestDataManifest();

        manifest.Track("Part", "PART001");

        var item = Assert.Single(manifest.UnresolvedItems);

        Assert.Equal("Part", item.ItemType);
        Assert.Equal("PART001", item.Id);
        Assert.Equal(TrackedItemStatus.Active, item.Status);
        Assert.Equal(1, manifest.TotalCount);
    }

    [Fact]
    public void MarkDeleted_ShouldKeepHistoryButRemoveFromUnresolved()
    {
        var manifest = new TestDataManifest();

        manifest.Track("Part", "PART001");

        Assert.True(manifest.MarkPending("Part", "PART001"));
        Assert.True(manifest.MarkDeleted("Part", "PART001"));

        Assert.Empty(manifest.UnresolvedItems);
        Assert.Empty(manifest.ActiveItems);

        var item = Assert.Single(manifest.Items);

        Assert.Equal(TrackedItemStatus.Deleted, item.Status);
        Assert.Equal(0, manifest.TotalCount);
    }

    [Fact]
    public void MarkAlreadyMissing_ShouldResolveItem()
    {
        var manifest = new TestDataManifest();

        manifest.Track("Part", "PART001");

        Assert.True(
            manifest.MarkAlreadyMissing("Part", "PART001"));

        Assert.Empty(manifest.UnresolvedItems);

        var item = Assert.Single(manifest.Items);

        Assert.Equal(
            TrackedItemStatus.AlreadyMissing,
            item.Status);

        Assert.Equal(0, manifest.TotalCount);
    }

    [Fact]
    public void MarkFailed_ShouldKeepItemUnresolved()
    {
        var manifest = new TestDataManifest();

        manifest.Track("Part", "PART001");

        Assert.True(
            manifest.MarkFailed(
                "Part",
                "PART001",
                "Test cleanup failure"));

        var item = Assert.Single(manifest.UnresolvedItems);

        Assert.Equal(
            TrackedItemStatus.CleanupFailed,
            item.Status);

        Assert.Equal(
            "Test cleanup failure",
            item.LastError);
    }

    [Fact]
    public void Load_ShouldRestorePersistedManifest()
    {
        var directory = CreateTempDirectory();

        try
        {
            var filePath = Path.Combine(
                directory,
                "manifest.json");

            var original = new TestDataManifest(
                EnvironmentId,
                filePath);

            original.Track("Part", "PART001");
            original.MarkPending("Part", "PART001");

            var restored = TestDataManifest.Load(
                filePath,
                EnvironmentId);

            Assert.Equal(original.RunId, restored.RunId);
            Assert.Equal(EnvironmentId, restored.EnvironmentId);

            var item = Assert.Single(restored.UnresolvedItems);

            Assert.Equal("PART001", item.Id);
            Assert.Equal(
                TrackedItemStatus.DeletePending,
                item.Status);

            restored.MarkDeleted("Part", "PART001");

            var reloaded = TestDataManifest.Load(
                filePath,
                EnvironmentId);

            Assert.Empty(reloaded.UnresolvedItems);
            Assert.Equal(
                TrackedItemStatus.Deleted,
                Assert.Single(reloaded.Items).Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_ShouldRejectDifferentEnvironment()
    {
        var directory = CreateTempDirectory();

        try
        {
            var filePath = Path.Combine(
                directory,
                "manifest.json");

            var manifest = new TestDataManifest(
                EnvironmentId,
                filePath);

            manifest.Track("Part", "PART001");

            Assert.Throws<InvalidOperationException>(() =>
                TestDataManifest.Load(
                    filePath,
                    "different-environment"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "ArasPerformanceTool.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        return directory;
    }
}
