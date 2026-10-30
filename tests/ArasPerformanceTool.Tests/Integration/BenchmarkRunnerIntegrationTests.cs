
using System.Xml.Linq;
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Benchmarking;
using ArasPerformanceTool.Engine.Benchmarking.Workloads;
using Xunit;

namespace ArasPerformanceTool.Tests.Integration;

public class BenchmarkRunnerIntegrationTests
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
    public async Task RunAsync_ShouldMeasureCreateWorkload_AndProduceValidReport()
    {
        // Arrange: Read credentials from environment variables
        var username = Environment.GetEnvironmentVariable("ARAS_USERNAME");
        var password = Environment.GetEnvironmentVariable("ARAS_PASSWORD");

        Assert.False(string.IsNullOrWhiteSpace(username));
        Assert.False(string.IsNullOrWhiteSpace(password));

        using var httpClient = new HttpClient();

        var arasClient = new ArasClient(
            httpClient,
            OAuthUrl,
            Database);

        var token = await arasClient.GetAccessTokenAsync(
            username!,
            password!);

        var dataManager = new TestDataManager(arasClient);
        var runner = new BenchmarkRunner();
        var createWorkload = new CreateWorkload(arasClient);

        const string itemType = "Part";
        const int iterations = 5;
        const int concurrency = 1;

        var config = new BenchmarkConfiguration(
            itemType,
            iterations,
            concurrency);

        try
        {
            // Act: Execute real AML Create operations
            var report = await runner.RunAsync(
                "CreatePartBenchmark",
                config,
                async index =>
                {
                    // Generate a unique Part number within 32 characters
                    var itemNumber =
                        $"BENCH-{Guid.NewGuid():N}"[..24];

                    var properties = new Dictionary<string, string>
                    {
                        ["item_number"] = itemNumber,
                        ["name"] = $"Benchmark Part {index + 1}"
                    };

                    // Execute Create AML against Aras
                    var responseXml = await createWorkload.ExecuteAsync(
                        ServerUrl,
                        token,
                        itemType,
                        properties);

                    // Extract the created Item ID
                    var id = ExtractItemId(responseXml, itemType);

                    if (string.IsNullOrWhiteSpace(id))
                    {
                        throw new InvalidOperationException(
                            $"Could not extract created Part ID at iteration {index}.");
                    }

                    // Track the record for safe cleanup
                    dataManager.Manifest.Track(itemType, id);
                });

            // Assert: Verify report information
            Assert.Equal("CreatePartBenchmark", report.WorkloadName);
            Assert.Equal(itemType, report.Configuration.ItemType);

            // Assert: Verify request counts
            Assert.Equal(iterations, report.Statistics.TotalRequests);
            Assert.Equal(iterations, report.Statistics.SuccessCount);
            Assert.Equal(0, report.Statistics.FailureCount);

            // Assert: Verify latency measurements
            Assert.True(report.Statistics.AverageMs > 0);
            Assert.True(report.Statistics.MinMs > 0);
            Assert.True(
                report.Statistics.MinMs <= report.Statistics.AverageMs);
            Assert.True(
                report.Statistics.AverageMs <= report.Statistics.MaxMs);

            // Assert: Verify successful throughput
            Assert.True(
                report.Statistics.ThroughputRequestsPerSecond > 0);

            // Assert: Verify raw measurements
            Assert.Equal(iterations, report.RawMeasurements.Count);
            Assert.All(
                report.RawMeasurements,
                measurement => Assert.True(measurement.IsSuccess));

            // Assert: All created records are tracked
            Assert.Equal(iterations, dataManager.Manifest.TotalCount);

            // Output results to the test runner
            var stats = report.Statistics;

            Console.WriteLine(
                $"ItemType: {itemType}, Iterations: {iterations}");

            Console.WriteLine(
                $"Average: {stats.AverageMs} ms");

            Console.WriteLine(
                $"Min: {stats.MinMs} ms, Max: {stats.MaxMs} ms");

            Console.WriteLine(
                $"Throughput: {stats.ThroughputRequestsPerSecond:F2} req/s");
        }
        finally
        {
            // Always attempt cleanup, even if assertions fail
            var cleanupResult = await dataManager.CleanupAsync(
                ServerUrl,
                token,
                itemType);

            // Verify that cleanup did not leave tracked records
            Assert.Empty(cleanupResult.FailedIds);
            Assert.Equal(0, dataManager.Manifest.TotalCount);
        }
    }

    
    [Fact]
    public async Task RunAsync_ShouldCompareResults_AcrossItemTypesAndDataVolumes()
    {
        // Arrange: Retrieve credentials from environment variables
        var username = Environment.GetEnvironmentVariable("ARAS_USERNAME");
        var password = Environment.GetEnvironmentVariable("ARAS_PASSWORD");

        Assert.False(string.IsNullOrWhiteSpace(username));
        Assert.False(string.IsNullOrWhiteSpace(password));

        using var httpClient = new HttpClient();

        var arasClient = new ArasClient(
            httpClient,
            OAuthUrl,
            Database);

        var token = await arasClient.GetAccessTokenAsync(
            username!,
            password!);

        var dataManager = new TestDataManager(arasClient);
        var runner = new BenchmarkRunner();
        var createWorkload = new CreateWorkload(arasClient);

        // Three configurations for controlled comparisons
        var configPartSmall = new BenchmarkConfiguration(
            "Part",
            Iterations: 2,
            Concurrency: 1);

        var configDocument = new BenchmarkConfiguration(
            "Document",
            Iterations: 2,
            Concurrency: 1);

        var configPartLarge = new BenchmarkConfiguration(
            "Part",
            Iterations: 4,
            Concurrency: 1);

        // Execute one Create operation and track the created record
        async Task CreateAndTrackAsync(string itemType, int index)
        {
            var itemNumber = $"BENCH-{Guid.NewGuid():N}"[..24];

            var properties = new Dictionary<string, string>
            {
                ["item_number"] = itemNumber,
                ["name"] = $"Benchmark {itemType} {index + 1}"
            };

            var responseXml = await createWorkload.ExecuteAsync(
                ServerUrl,
                token,
                itemType,
                properties);

            var id = ExtractItemId(responseXml, itemType);

            // Never silently ignore a created item without an ID
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException(
                    $"Failed to extract created {itemType} ID at iteration {index}.");
            }

            // Track the record so cleanup can remove it
            dataManager.Manifest.Track(itemType, id);
        }

        try
        {
            // Scenario A: Part with 2 iterations
            var reportPartSmall = await runner.RunAsync(
                "CreatePartSmall",
                configPartSmall,
                index => CreateAndTrackAsync("Part", index));

            // Scenario B: Document with 2 iterations
            var reportDocument = await runner.RunAsync(
                "CreateDocument",
                configDocument,
                index => CreateAndTrackAsync("Document", index));

            // Scenario C: Part with 4 iterations
            var reportPartLarge = await runner.RunAsync(
                "CreatePartLarge",
                configPartLarge,
                index => CreateAndTrackAsync("Part", index));

            // Assert: All requests completed successfully
            Assert.Equal(2, reportPartSmall.Statistics.TotalRequests);
            Assert.Equal(2, reportDocument.Statistics.TotalRequests);
            Assert.Equal(4, reportPartLarge.Statistics.TotalRequests);

            Assert.Equal(2, reportPartSmall.Statistics.SuccessCount);
            Assert.Equal(2, reportDocument.Statistics.SuccessCount);
            Assert.Equal(4, reportPartLarge.Statistics.SuccessCount);

            Assert.Equal(0, reportPartSmall.Statistics.FailureCount);
            Assert.Equal(0, reportDocument.Statistics.FailureCount);
            Assert.Equal(0, reportPartLarge.Statistics.FailureCount);

            // Assert: Report configurations are preserved
            Assert.Equal("Part", reportPartSmall.Configuration.ItemType);
            Assert.Equal("Document", reportDocument.Configuration.ItemType);
            Assert.Equal("Part", reportPartLarge.Configuration.ItemType);

            // Assert: Each run produced valid latency metrics
            var partSmallStats = reportPartSmall.Statistics;
            var documentStats = reportDocument.Statistics;
            var partLargeStats = reportPartLarge.Statistics;

            Assert.True(partSmallStats.AverageMs > 0);
            Assert.True(documentStats.AverageMs > 0);
            Assert.True(partLargeStats.AverageMs > 0);

            Assert.True(partSmallStats.MinMs <= partSmallStats.MaxMs);
            Assert.True(documentStats.MinMs <= documentStats.MaxMs);
            Assert.True(partLargeStats.MinMs <= partLargeStats.MaxMs);

            // Assert: Each run produced valid throughput
            Assert.True(partSmallStats.ThroughputRequestsPerSecond > 0);
            Assert.True(documentStats.ThroughputRequestsPerSecond > 0);
            Assert.True(partLargeStats.ThroughputRequestsPerSecond > 0);

            // Assert: All created records are tracked for cleanup
            Assert.Equal(6, dataManager.Manifest.GetIds("Part").Count);
            Assert.Equal(2, dataManager.Manifest.GetIds("Document").Count);
            Assert.Equal(8, dataManager.Manifest.TotalCount);

            // Output: Compare ItemTypes with the same request count
            Console.WriteLine("--- ItemType Comparison (2 iterations) ---");

            Console.WriteLine(
                $"Part: Avg={partSmallStats.AverageMs:F2} ms, " +
                $"Throughput={partSmallStats.ThroughputRequestsPerSecond:F2} req/s");

            Console.WriteLine(
                $"Document: Avg={documentStats.AverageMs:F2} ms, " +
                $"Throughput={documentStats.ThroughputRequestsPerSecond:F2} req/s");

            // Output: Compare request counts for the same ItemType
            Console.WriteLine("--- Part Iteration Comparison ---");

            Console.WriteLine(
                $"2 iterations: Avg={partSmallStats.AverageMs:F2} ms, " +
                $"Throughput={partSmallStats.ThroughputRequestsPerSecond:F2} req/s");

            Console.WriteLine(
                $"4 iterations: Avg={partLargeStats.AverageMs:F2} ms, " +
                $"Throughput={partLargeStats.ThroughputRequestsPerSecond:F2} req/s");
        }
        finally
        {
            // Cleanup all tracked Part and Document records
            var cleanupResult = await dataManager.CleanupAsync(
                ServerUrl,
                token);

            // Cleanup failures must not be silently ignored
            Assert.Empty(cleanupResult.FailedIds);
            Assert.Equal(0, dataManager.Manifest.TotalCount);
        }
    }



    // Extract the ID of the Item returned by Aras
    private static string? ExtractItemId(
        string xmlPayload,
        string itemType)
    {
        var xml = XDocument.Parse(xmlPayload);

        var itemElement = xml.Descendants()
            .FirstOrDefault(e =>
                e.Name.LocalName == "Item" &&
                string.Equals(
                    (string?)e.Attribute("type"),
                    itemType,
                    StringComparison.OrdinalIgnoreCase));

        return (string?)itemElement?.Attribute("id");
    }
}
