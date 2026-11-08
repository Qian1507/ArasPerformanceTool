using System.Text.Json;
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Benchmarking;
using ArasPerformanceTool.Engine.Services;
using Moq;
using Xunit;

namespace ArasPerformanceTool.Tests.Unit;

public sealed class CustomAmlBenchmarkServiceTests
{
    private const string ServerUrl =
        "http://localhost/InnovatorServer/Server/InnovatorServer.aspx";

    private const string EnvironmentId =
        "http://localhost/InnovatorServer/Server/InnovatorServer.aspx|InnovatorSolutions";

    private const string AccessToken = "test-token";

    private const string AddAml = """
        <Item type="Part" action="add">
            <item_number>TEST_PART</item_number>
            <name>Test Part</name>
        </Item>
        """;

    [Fact]
    public async Task RunAsync_WhenAddHasNoManifestDirectory_ThrowsBeforeCallingAras()
    {
        // Arrange

        var runId = Guid.NewGuid().ToString("N");

        var arasClient = new Mock<IArasClient>();

        var service = new CustomAmlBenchmarkService(
            arasClient.Object);

        var config = new BenchmarkConfiguration(
            "Part",
            1,
            1);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RunAsync(
                AddAml,
                "aml-add",
                config,
                ServerUrl,
                AccessToken,
                EnvironmentId,
                runId,
                manifestDirectory: null));

        // Assert
        Assert.Equal(
            "Custom AML add requires a persistent manifest directory.",
            exception.Message);

        arasClient.Verify(
            client => client.ExecuteAmlAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenAddSucceeds_UsesProvidedRunIdForManifest()
    {
        // Arrange

        var runId = Guid.NewGuid().ToString("N");
        
        var directory = Path.Combine(
            Path.GetTempPath(),
            "ArasPerformanceToolTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        try
        {
            var arasClient = new Mock<IArasClient>();

            arasClient
                .Setup(client => client.ExecuteAmlAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("""
                    <SOAP-ENV:Envelope xmlns:SOAP-ENV="http://schemas.xmlsoap.org/soap/envelope/">
                      <SOAP-ENV:Body>
                        <Item type="Part" id="11111111111111111111111111111111" action="add"/>
                      </SOAP-ENV:Body>
                    </SOAP-ENV:Envelope>
                    """);

            var service = new CustomAmlBenchmarkService(
                arasClient.Object);

            var config = new BenchmarkConfiguration(
                "Part",
                1,
                1);

            // Act
            var result = await service.RunAsync(
                AddAml,
                "aml-add",
                config,
                ServerUrl,
                AccessToken,
                EnvironmentId,
                runId,
                directory);

            // Assert
            var manifestPath = Path.Combine(
                directory,
                $"{runId}.json");

            Assert.True(File.Exists(manifestPath));

            var json = File.ReadAllText(manifestPath);
            using var document = JsonDocument.Parse(json);

            Assert.Equal(
                runId,
                document.RootElement
                    .GetProperty("RunId")
                    .GetString());

            Assert.Equal(
                EnvironmentId,
                document.RootElement
                    .GetProperty("EnvironmentId")
                    .GetString());

        
        var items = document.RootElement.GetProperty("Items");

        var trackedItem = Assert.Single(
            items.EnumerateArray());

        Assert.Equal(
            "Part",
            trackedItem.GetProperty("ItemType").GetString());

        Assert.Equal(
            (int)TestItemSource.CustomAml,
            trackedItem.GetProperty("Source").GetInt32());
                }


        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunAsync_WhenRunIdIsInvalid_ThrowsBeforeCallingAras()
    {
        var arasClient = new Mock<IArasClient>();
        var service = new CustomAmlBenchmarkService(arasClient.Object);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RunAsync(
                AddAml,
                "aml-add",
                new BenchmarkConfiguration("Part", 1, 1),
                ServerUrl,
                AccessToken,
                EnvironmentId,
                "../invalid-run-id",
                manifestDirectory: Path.GetTempPath()));

        arasClient.Verify(
            client => client.ExecuteAmlAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenManifestExists_DoesNotOverwrite()
    {
        var runId = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(
            Path.GetTempPath(),
            "ArasPerformanceToolTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        try
        {
            var manifestPath = Path.Combine(
                directory,
                $"{runId}.json");

            const string originalContent = "existing manifest";

            File.WriteAllText(manifestPath, originalContent);

            var arasClient = new Mock<IArasClient>();
            var service = new CustomAmlBenchmarkService(arasClient.Object);

            await Assert.ThrowsAsync<IOException>(
                () => service.RunAsync(
                    AddAml,
                    "aml-add",
                    new BenchmarkConfiguration("Part", 1, 1),
                    ServerUrl,
                    AccessToken,
                    EnvironmentId,
                    runId,
                    directory));

            Assert.Equal(
                originalContent,
                File.ReadAllText(manifestPath));

            arasClient.Verify(
                client => client.ExecuteAmlAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    
    [Fact]
    public async Task RunAsync_WhenCleanupFails_PersistsRunForRecovery()
    {
        // Arrange
        var runId = Guid.NewGuid().ToString("N");

        var directory = Path.Combine(
            Path.GetTempPath(),
            "ArasPerformanceToolTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        try
        {
            const string createdId =
                "11111111111111111111111111111111";

            var arasClient = new Mock<IArasClient>();

            arasClient
                .Setup(client => client.ExecuteAmlAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((
                    string server,
                    string token,
                    string aml,
                    CancellationToken ct) =>
                {
                    if (aml.Contains("action=\"add\""))
                    {
                        return $"""
                            <AML>
                            <Item type="Part" id="{createdId}" action="add" />
                            </AML>
                            """;
                    }

                    throw new InvalidOperationException(
                        "Simulated cleanup failure.");
                });

            var service = new CustomAmlBenchmarkService(
                arasClient.Object);

            var config = new BenchmarkConfiguration(
                "Part",
                1,
                1);

            // Act
            var result = await service.RunAsync(
                AddAml,
                "aml-add-cleanup-failure",
                config,
                ServerUrl,
                AccessToken,
                EnvironmentId,
                runId,
                directory);

            // Assert: The manifest must remain on disk.
            var manifestPath = Path.Combine(
                directory,
                $"{runId}.json");

            Assert.True(File.Exists(manifestPath));

            // Reopen from disk, as if the application restarted.
            var reloaded = TestDataManifest.Load(
                manifestPath,
                EnvironmentId);

            Assert.Equal(runId, reloaded.RunId);

            var unresolvedItem = Assert.Single(
                reloaded.UnresolvedItems);

            Assert.Equal("Part", unresolvedItem.ItemType);
            Assert.Equal(createdId, unresolvedItem.Id);
            Assert.Equal(
                TrackedItemStatus.CleanupFailed,
                unresolvedItem.Status);

            Assert.Equal(
                TestItemSource.CustomAml,
                unresolvedItem.Source);

            Assert.Contains(
                "Simulated cleanup failure.",
                unresolvedItem.LastError ?? string.Empty);

            // Recovery scanning must discover this run.
            var store = new TestDataManifestStore(directory);

            var scan = store.Scan(EnvironmentId);

            var pending = Assert.Single(scan.PendingRuns);

            Assert.Equal(runId, pending.RunId);
            Assert.Equal(1, pending.RemainingCount);
            Assert.Empty(scan.Errors);

            // Preserve the benchmark report and report cleanup failure.
            Assert.Equal(runId, result.RunId);
            Assert.NotNull(result.Report);
            Assert.NotNull(result.CleanupSummary);

            Assert.Contains(
                createdId,
                result.CleanupSummary!.FailedIds);

            Assert.False(
                string.IsNullOrWhiteSpace(result.CleanupError));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }


}