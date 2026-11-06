
using ArasPerformanceTool.Engine.Benchmarking;
using Xunit;

namespace ArasPerformanceTool.Tests.Integration;

[Collection("Aras Integration")]
public sealed class TestDataManagerTests
{
    private readonly ArasFixture _aras;

    public TestDataManagerTests(ArasFixture aras)
    {
        _aras = aras;
    }

    [Fact]
    public async Task PrepareAndCleanup_ShouldManageTestRecordsSafely()
    {
        // Arrange: create a manifest bound to the test environment.
        var manifest = new TestDataManifest(
            environmentId: ArasFixture.EnvironmentId);

        var dataManager = new TestDataManager(
            _aras.Client,
            manifest);

        const string itemType = "Part";
        const int recordVolume = 3;

        try
        {
            // Prepare three test Parts.
            var createdIds = await dataManager.PrepareAsync(
                ArasFixture.ServerUrl,
                _aras.AccessToken,
                itemType,
                recordVolume);

            // Verify preparation and ID tracking.
            Assert.Equal(recordVolume, createdIds.Count);

            Assert.Equal(
                recordVolume,
                dataManager.Manifest.TotalCount);

            Assert.Equal(
                recordVolume,
                createdIds.Distinct().Count());

            // Cleanup records created by this test.
            var cleanupResult = await dataManager.CleanupAsync(
                ArasFixture.ServerUrl,
                _aras.AccessToken,
                itemType,
                expectedEnvironmentId: ArasFixture.EnvironmentId);

            // Verify cleanup statistics.
            Assert.Equal(
                recordVolume,
                cleanupResult.DeletedCount);

            Assert.Equal(
                0,
                cleanupResult.AlreadyMissingCount);

            Assert.Empty(cleanupResult.FailedIds);

            // Successfully cleaned records are no longer unresolved.
            Assert.Equal(0, dataManager.Manifest.TotalCount);

            // Verify a second cleanup has nothing left to process.
            var secondCleanup = await dataManager.CleanupAsync(
                ArasFixture.ServerUrl,
                _aras.AccessToken,
                itemType,
                expectedEnvironmentId: ArasFixture.EnvironmentId);

            Assert.Equal(0, secondCleanup.DeletedCount);
            Assert.Equal(0, secondCleanup.AlreadyMissingCount);
            Assert.Empty(secondCleanup.FailedIds);
        }
        finally
        {
            // Attempt cleanup if any tracked records remain.
            if (dataManager.Manifest.UnresolvedItems.Count > 0)
            {
                var finalCleanup = await dataManager.CleanupAsync(
                    ArasFixture.ServerUrl,
                    _aras.AccessToken,
                    itemType,
                    expectedEnvironmentId: ArasFixture.EnvironmentId);

                Assert.Empty(finalCleanup.FailedIds);
            }
        }
    }
}
