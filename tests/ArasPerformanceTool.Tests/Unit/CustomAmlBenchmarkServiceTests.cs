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

}