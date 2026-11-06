
using System.Xml.Linq;
using ArasPerformanceTool.Engine.Benchmarking;
using ArasPerformanceTool.Engine.Benchmarking.Workloads;
using Xunit;

namespace ArasPerformanceTool.Tests.Integration;

[Collection("Aras Integration")]
public sealed class BenchmarkRunnerIntegrationTests
{
    private readonly ArasFixture _aras;

    public BenchmarkRunnerIntegrationTests(ArasFixture aras)
    {
        _aras = aras;
    }

    [Fact]
    public async Task RunAsync_ShouldMeasureCreateWorkload_AndProduceValidReport()
    {
        // Arrange: bind the manifest to the test environment.
        var manifest = new TestDataManifest(
            environmentId: ArasFixture.EnvironmentId);

        var dataManager = new TestDataManager(
            _aras.Client,
            manifest);

        var runner = new BenchmarkRunner();
        var createWorkload = new CreateWorkload(_aras.Client);

        const string itemType = "Part";
        const int iterations = 5;
        const int concurrency = 1;

        var config = new BenchmarkConfiguration(
            itemType,
            iterations,
            concurrency);

        try
        {
            var report = await runner.RunAsync(
                "CreatePartBenchmark",
                config,
                async index =>
                {
                    var itemNumber =
                        $"BENCH-{Guid.NewGuid():N}"[..24];

                    var properties = new Dictionary<string, string>
                    {
                        ["item_number"] = itemNumber,
                        ["name"] = $"Benchmark Part {index + 1}"
                    };

                    var responseXml = await createWorkload.ExecuteAsync(
                        ArasFixture.ServerUrl,
                        _aras.AccessToken,
                        itemType,
                        properties);

                    var id = ExtractItemId(
                        responseXml,
                        itemType);

                    if (string.IsNullOrWhiteSpace(id))
                    {
                        throw new InvalidOperationException(
                            $"Could not extract created Part ID at iteration {index}.");
                    }

                    dataManager.Manifest.Track(itemType, id);
                });

            // Verify report information.
            Assert.Equal(
                "CreatePartBenchmark",
                report.WorkloadName);

            Assert.Equal(
                itemType,
                report.Configuration.ItemType);

            // Verify request counts.
            Assert.Equal(
                iterations,
                report.Statistics.TotalRequests);

            Assert.Equal(
                iterations,
                report.Statistics.SuccessCount);

            Assert.Equal(
                0,
                report.Statistics.FailureCount);

            // Verify latency.
            Assert.True(report.Statistics.AverageMs > 0);
            Assert.True(report.Statistics.MinMs > 0);

            Assert.True(
                report.Statistics.MinMs <=
                report.Statistics.AverageMs);

            Assert.True(
                report.Statistics.AverageMs <=
                report.Statistics.MaxMs);

            // Verify throughput.
            Assert.True(
                report.Statistics.ThroughputRequestsPerSecond > 0);

            // Verify raw measurements.
            Assert.Equal(
                iterations,
                report.RawMeasurements.Count);

            Assert.All(
                report.RawMeasurements,
                measurement => Assert.True(measurement.IsSuccess));

            // Verify tracked items.
            Assert.Equal(
                iterations,
                dataManager.Manifest.TotalCount);

            // Output benchmark results.
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
            // Cleanup using the current environment identity.
            var cleanupResult = await dataManager.CleanupAsync(
                ArasFixture.ServerUrl,
                _aras.AccessToken,
                itemType,
                expectedEnvironmentId: ArasFixture.EnvironmentId);

            Assert.Empty(cleanupResult.FailedIds);
            Assert.Equal(0, dataManager.Manifest.TotalCount);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldCompareResults_AcrossItemTypesAndDataVolumes()
    {
        // Arrange: bind the manifest to the test environment.
        var manifest = new TestDataManifest(
            environmentId: ArasFixture.EnvironmentId);

        var dataManager = new TestDataManager(
            _aras.Client,
            manifest);

        var runner = new BenchmarkRunner();
        var createWorkload = new CreateWorkload(_aras.Client);

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

        async Task CreateAndTrackAsync(string itemType, int index)
        {
            var itemNumber =
                $"BENCH-{Guid.NewGuid():N}"[..24];

            var properties = new Dictionary<string, string>
            {
                ["item_number"] = itemNumber,
                ["name"] = $"Benchmark {itemType} {index + 1}"
            };

            var responseXml = await createWorkload.ExecuteAsync(
                ArasFixture.ServerUrl,
                _aras.AccessToken,
                itemType,
                properties);

            var id = ExtractItemId(responseXml, itemType);

            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException(
                    $"Failed to extract created {itemType} ID at iteration {index}.");
            }

            dataManager.Manifest.Track(itemType, id);
        }

        try
        {
            // Scenario A: Part, 2 iterations.
            var reportPartSmall = await runner.RunAsync(
                "CreatePartSmall",
                configPartSmall,
                index => CreateAndTrackAsync("Part", index));

            // Scenario B: Document, 2 iterations.
            var reportDocument = await runner.RunAsync(
                "CreateDocument",
                configDocument,
                index => CreateAndTrackAsync("Document", index));

            // Scenario C: Part, 4 iterations.
            var reportPartLarge = await runner.RunAsync(
                "CreatePartLarge",
                configPartLarge,
                index => CreateAndTrackAsync("Part", index));

            // Verify request counts.
            Assert.Equal(
                2,
                reportPartSmall.Statistics.TotalRequests);

            Assert.Equal(
                2,
                reportDocument.Statistics.TotalRequests);

            Assert.Equal(
                4,
                reportPartLarge.Statistics.TotalRequests);

            // Verify successful requests.
            Assert.Equal(
                2,
                reportPartSmall.Statistics.SuccessCount);

            Assert.Equal(
                2,
                reportDocument.Statistics.SuccessCount);

            Assert.Equal(
                4,
                reportPartLarge.Statistics.SuccessCount);

            Assert.Equal(
                0,
                reportPartSmall.Statistics.FailureCount);

            Assert.Equal(
                0,
                reportDocument.Statistics.FailureCount);

            Assert.Equal(
                0,
                reportPartLarge.Statistics.FailureCount);

            // Verify ItemType configurations.
            Assert.Equal(
                "Part",
                reportPartSmall.Configuration.ItemType);

            Assert.Equal(
                "Document",
                reportDocument.Configuration.ItemType);

            Assert.Equal(
                "Part",
                reportPartLarge.Configuration.ItemType);

            // Verify latency metrics.
            var partSmallStats = reportPartSmall.Statistics;
            var documentStats = reportDocument.Statistics;
            var partLargeStats = reportPartLarge.Statistics;

            Assert.True(partSmallStats.AverageMs > 0);
            Assert.True(documentStats.AverageMs > 0);
            Assert.True(partLargeStats.AverageMs > 0);

            Assert.True(
                partSmallStats.MinMs <= partSmallStats.MaxMs);

            Assert.True(
                documentStats.MinMs <= documentStats.MaxMs);

            Assert.True(
                partLargeStats.MinMs <= partLargeStats.MaxMs);

            // Verify throughput.
            Assert.True(
                partSmallStats.ThroughputRequestsPerSecond > 0);

            Assert.True(
                documentStats.ThroughputRequestsPerSecond > 0);

            Assert.True(
                partLargeStats.ThroughputRequestsPerSecond > 0);

            // Verify tracked data.
            Assert.Equal(
                6,
                dataManager.Manifest.GetIds("Part").Count);

            Assert.Equal(
                2,
                dataManager.Manifest.GetIds("Document").Count);

            Assert.Equal(
                8,
                dataManager.Manifest.TotalCount);

            // Output comparisons.
            Console.WriteLine(
                "--- ItemType Comparison (2 iterations) ---");

            Console.WriteLine(
                $"Part: Avg={partSmallStats.AverageMs:F2} ms, " +
                $"Throughput={partSmallStats.ThroughputRequestsPerSecond:F2} req/s");

            Console.WriteLine(
                $"Document: Avg={documentStats.AverageMs:F2} ms, " +
                $"Throughput={documentStats.ThroughputRequestsPerSecond:F2} req/s");

            Console.WriteLine(
                "--- Part Iteration Comparison ---");

            Console.WriteLine(
                $"2 iterations: Avg={partSmallStats.AverageMs:F2} ms, " +
                $"Throughput={partSmallStats.ThroughputRequestsPerSecond:F2} req/s");

            Console.WriteLine(
                $"4 iterations: Avg={partLargeStats.AverageMs:F2} ms, " +
                $"Throughput={partLargeStats.ThroughputRequestsPerSecond:F2} req/s");
        }
        finally
        {
            // Cleanup both Part and Document test records.
            var cleanupResult = await dataManager.CleanupAsync(
                ArasFixture.ServerUrl,
                _aras.AccessToken,
                expectedEnvironmentId: ArasFixture.EnvironmentId);

            Assert.Empty(cleanupResult.FailedIds);
            Assert.Equal(0, dataManager.Manifest.TotalCount);
        }
    }

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
