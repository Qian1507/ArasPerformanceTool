
using System.Xml.Linq;
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Benchmarking;
using Xunit;

namespace ArasPerformanceTool.Tests.Integration;

[Collection("Aras Integration")]
public sealed class TestDataCleanupServiceTests
{
    private readonly ArasFixture _aras;

    public TestDataCleanupServiceTests(ArasFixture aras)
    {
        _aras = aras;
    }

    [Fact]
    public async Task CleanupAsync_ShouldDeleteTrackedPart_AndUpdateManifest()
    {
        // Arrange: create a manifest for this test run.
        var manifest = new TestDataManifest(
            environmentId: ArasFixture.EnvironmentId);

        var cleanupService =
            new TestDataCleanupService(_aras.Client);

        // Create a uniquely named test Part.
        var itemNumber =
            $"LIA-CLEANUP-{Guid.NewGuid():N}"[..24];

        var createAml = AmlBuilder.Create(
            "Part",
            new Dictionary<string, string>
            {
                ["item_number"] = itemNumber,
                ["name"] = "Cleanup Integration Test"
            });

        var createResponse = await _aras.Client.ExecuteAmlAsync(
            ArasFixture.ServerUrl,
            _aras.AccessToken,
            createAml);

        var createXml = XDocument.Parse(createResponse);

        Assert.DoesNotContain(
            createXml.Descendants(),
            x => x.Name.LocalName == "Fault");

        var createdPart = createXml.Descendants()
            .FirstOrDefault(x =>
                x.Name.LocalName == "Item" &&
                (string?)x.Attribute("type") == "Part");

        Assert.NotNull(createdPart);

        var itemId = (string?)createdPart.Attribute("id");

        Assert.False(string.IsNullOrWhiteSpace(itemId));

        // Track the Part so CleanupService knows it is test data.
        manifest.Track("Part", itemId!);

        // Act: delete the tracked Part and verify the result.
        var result = await cleanupService.CleanupAsync(
            manifest,
            ArasFixture.EnvironmentId,
            ArasFixture.ServerUrl,
            _aras.AccessToken);

        // Assert: cleanup metrics.
        Assert.Equal(1, result.Found);
        Assert.Equal(1, result.Deleted);
        Assert.Equal(0, result.AlreadyMissing);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Remaining);

        // Assert: persisted tracking state in memory.
        var trackedItem = Assert.Single(manifest.Items);

        Assert.Equal(
            TrackedItemStatus.Deleted,
            trackedItem.Status);

        Assert.Empty(manifest.UnresolvedItems);
    }

    [Fact]
    public async Task CleanupAsync_ShouldMarkAlreadyMissing_WhenPartDoesNotExist()
    {
        // Arrange: create a manifest with a nonexistent Part ID.
        var manifest = new TestDataManifest(
            environmentId: ArasFixture.EnvironmentId);

        var cleanupService =
            new TestDataCleanupService(_aras.Client);

        var missingPartId = Guid.NewGuid()
            .ToString("N")
            .ToUpperInvariant();

        manifest.Track("Part", missingPartId);

        // Act: cleanup an item that does not exist in Aras.
        var result = await cleanupService.CleanupAsync(
            manifest,
            ArasFixture.EnvironmentId,
            ArasFixture.ServerUrl,
            _aras.AccessToken);

        // Assert: missing item is treated as already resolved.
        Assert.Equal(1, result.Found);
        Assert.Equal(0, result.Deleted);
        Assert.Equal(1, result.AlreadyMissing);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Remaining);

        var trackedItem = Assert.Single(manifest.Items);

        Assert.Equal(
            TrackedItemStatus.AlreadyMissing,
            trackedItem.Status);

        Assert.Empty(manifest.UnresolvedItems);
    }
}
