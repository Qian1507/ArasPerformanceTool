
using System.Xml.Linq;
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Benchmarking;
using ArasPerformanceTool.Engine.Benchmarking.Workloads;
using Xunit;

namespace ArasPerformanceTool.Tests.Integration;

public class TestDataManagerTests
{
    private static string OAuthUrl =>
        Environment.GetEnvironmentVariable("ARAS_OAUTH_URL")
        ?? "http://localhost/InnovatorServer/OAuthServer/connect/token";

    private static string ServerUrl =>
        Environment.GetEnvironmentVariable("ARAS_SERVER_URL")
        ?? "http://localhost/InnovatorServer/Server/InnovatorServer.aspx";

    private static string Database =>
        Environment.GetEnvironmentVariable("ARAS_DATABASE")
        ?? "InnovatorSolutions";

    [Fact]
    public async Task PrepareAndCleanup_ShouldManageTestRecordsSafely()
    {
        // Arrange: Read credentials from environment variables
        var username = Environment.GetEnvironmentVariable("ARAS_USERNAME");
        var password = Environment.GetEnvironmentVariable("ARAS_PASSWORD");

        Assert.False(string.IsNullOrWhiteSpace(username));
        Assert.False(string.IsNullOrWhiteSpace(password));

        using var httpClient = new HttpClient();
        var arasClient = new ArasClient(httpClient, OAuthUrl, Database);

        var token = await arasClient.GetAccessTokenAsync(
            username!,
            password!);

        var dataManager = new TestDataManager(arasClient);

        const string itemType = "Part";
        const int recordVolume = 3;

        // Prepare three test Parts
        var createdIds = await dataManager.PrepareAsync(
            ServerUrl,
            token,
            itemType,
            recordVolume);

        // Verify preparation and ID tracking
        Assert.Equal(recordVolume, createdIds.Count);
        Assert.Equal(recordVolume, dataManager.Manifest.TotalCount);
        Assert.Equal(recordVolume, createdIds.Distinct().Count());

        // Cleanup records created by this test
        var cleanupResult = await dataManager.CleanupAsync(
            ServerUrl,
            token,
            itemType);

        // Verify cleanup statistics
        Assert.Equal(recordVolume, cleanupResult.DeletedCount);
        Assert.Equal(0, cleanupResult.AlreadyMissingCount);
        Assert.Empty(cleanupResult.FailedIds);

        // Successfully cleaned records must leave the manifest
        Assert.Equal(0, dataManager.Manifest.TotalCount);

        // Verify a second cleanup has nothing left to process
        var secondCleanup = await dataManager.CleanupAsync(
            ServerUrl,
            token,
            itemType);

        Assert.Equal(0, secondCleanup.DeletedCount);
        Assert.Equal(0, secondCleanup.AlreadyMissingCount);
        Assert.Empty(secondCleanup.FailedIds);
    }
}
