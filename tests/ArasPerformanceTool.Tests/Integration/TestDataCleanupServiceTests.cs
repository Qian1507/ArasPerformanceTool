
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

    
    [Fact]
    public async Task CleanupAsync_ShouldContinue_WhenOneItemFails()
    {
        // Arrange: shared Aras connection.
        var manifest = new TestDataManifest(
            environmentId: ArasFixture.EnvironmentId);

        var cleanupService =
            new TestDataCleanupService(_aras.Client);

        string? createdPartId = null;

        try
        {
            // 1. Register an invalid ItemType.
            // This should cause a real Aras error, not "no items found".
            var invalidId = Guid.NewGuid()
                .ToString("N")
                .ToUpperInvariant();

            manifest.Track(
                "LIA_NONEXISTENT_ITEMTYPE_999",
                invalidId);

            // 2. Create a real test Part.
            var itemNumber =
                $"LIA-FAILTEST-{Guid.NewGuid():N}"[..24];

            var createResponse = await _aras.CreateItemAsync(
                "Part",
                new Dictionary<string, string>
                {
                    ["item_number"] = itemNumber,
                    ["name"] = "Cleanup Failure Test"
                });

            var xml = XDocument.Parse(createResponse);

            var createdItem = xml.Descendants()
                .FirstOrDefault(x =>
                    x.Name.LocalName == "Item" &&
                    (string?)x.Attribute("type") == "Part");

            Assert.NotNull(createdItem);

            createdPartId =
                (string?)createdItem.Attribute("id");

            Assert.False(
                string.IsNullOrWhiteSpace(createdPartId));

            manifest.Track("Part", createdPartId!);

            // Act: cleanup both items in one run.
            var result = await cleanupService.CleanupAsync(
                manifest,
                ArasFixture.EnvironmentId,
                ArasFixture.ServerUrl,
                _aras.AccessToken);

            // Assert: the invalid item failed.
            Assert.Equal(2, result.Found);
            Assert.Equal(1, result.Failed);
            Assert.Equal(1, result.Deleted);
            Assert.Equal(0, result.AlreadyMissing);
            Assert.Equal(1, result.Remaining);

            var failedItem = Assert.Single(
                manifest.Items,
                x => x.ItemType == "LIA_NONEXISTENT_ITEMTYPE_999");

            Assert.Equal(
                TrackedItemStatus.CleanupFailed,
                failedItem.Status);

            Assert.False(
                string.IsNullOrWhiteSpace(failedItem.LastError));

            // Assert: the real Part was deleted despite the first failure.
          var deletedItem = Assert.Single(
                manifest.Items,
                x => x.ItemType == "Part");

            Assert.Equal(createdPartId, deletedItem.Id);

            Assert.Equal(
                TrackedItemStatus.Deleted,
                deletedItem.Status);

            // Only the failed item remains unresolved.
            var unresolved = Assert.Single(
                manifest.UnresolvedItems);

            Assert.Equal(invalidId, unresolved.Id);
        }
        finally
        {
            // Safety fallback for the real test Part.
            // Never attempt to delete the invalid/fake item here.
            if (!string.IsNullOrWhiteSpace(createdPartId) &&
                manifest.UnresolvedItems.Any(x =>
                    x.ItemType == "Part" &&
                    x.Id == createdPartId))
            {
                try
                {
                    await _aras.DeleteItemAsync(
                        "Part",
                        createdPartId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Test Part cleanup requires attention: " +
                        $"{createdPartId}. {ex.Message}");
                }
            }
        }
    }

}
