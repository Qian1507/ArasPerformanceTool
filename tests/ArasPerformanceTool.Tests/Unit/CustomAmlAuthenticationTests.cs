using System.Net;
using System.Xml.Linq;
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Benchmarking;
using ArasPerformanceTool.Engine.Exceptions;
using ArasPerformanceTool.Engine.Services;
using Moq;
using Xunit;

namespace ArasPerformanceTool.Tests.Unit;

public sealed class CustomAmlAuthenticationTests
{
    [Fact]
    public async Task RunAsync_WhenBenchmarkReturnsUnauthorized_SkipsAutomaticCleanupAndPreservesPartialReport()
    {
        // Arrange
        const string serverUrl =
            "http://localhost/InnovatorServer/Server/InnovatorServer.aspx";

        const string accessToken = "expired-test-token";

        const string rawAml =
            "<AML>" +
            "<Item type=\"Part\" action=\"add\">" +
            "<item_number>TEST-PART</item_number>" +
            "<name>Authentication test</name>" +
            "</Item>" +
            "</AML>";

        var environmentId =
            $"{serverUrl}|InnovatorSolutions";

        var runId = Guid.NewGuid().ToString("N");
        var createdId = Guid.NewGuid()
            .ToString("N")
            .ToUpperInvariant();

        var directory = Path.Combine(
            Path.GetTempPath(),
            "ArasPerformanceToolCustomAmlAuthTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        try
        {
            var createResponse =
                $"<Result><Item type=\"Part\" id=\"{createdId}\" /></Result>";

            var authenticationFailure = new HttpRequestException(
                "Aras rejected the access token.",
                inner: null,
                statusCode: HttpStatusCode.Unauthorized);

            var arasClientMock =
                new Mock<IArasClient>(MockBehavior.Strict);

            arasClientMock
                .SetupSequence(x => x.ExecuteAmlAsync(
                    serverUrl,
                    accessToken,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(createResponse)
                .ThrowsAsync(authenticationFailure);

            var service = new CustomAmlBenchmarkService(
                arasClientMock.Object);

            var config = new BenchmarkConfiguration(
                "Part",
                3,
                1);

            // Act
            var exception =
                await Assert.ThrowsAsync<BenchmarkAuthenticationException>(
                    () => service.RunAsync(
                        rawAml,
                        "CustomAmlAuthenticationTest",
                        config,
                        serverUrl,
                        accessToken,
                        environmentId,
                        runId,
                        directory));

            // Assert: Preserve authentication failure and partial results.
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                exception.StatusCode);

            Assert.Same(
                authenticationFailure,
                exception.InnerException);

            var report = exception.PartialReport;

            Assert.Equal(3, report.Configuration.Iterations);
            Assert.Equal(2, report.RawMeasurements.Count);
            Assert.Equal(2, report.Statistics.TotalRequests);
            Assert.Equal(1, report.Statistics.SuccessCount);
            Assert.Equal(1, report.Statistics.FailureCount);

            // Only two Add requests may have been sent.
            var actions = arasClientMock.Invocations
                .Where(invocation =>
                    invocation.Method.Name ==
                    nameof(IArasClient.ExecuteAmlAsync))
                .Select(invocation =>
                {
                    var aml = Assert.IsType<string>(
                        invocation.Arguments[2]);

                    var item = XDocument.Parse(aml)
                        .Descendants()
                        .First(element =>
                            element.Name.LocalName == "Item");

                    return (string?)item.Attribute("action");
                })
                .ToArray();

            Assert.Equal(
                new[] { "add", "add" },
                actions);

            arasClientMock.Verify(
                x => x.ExecuteAmlAsync(
                    serverUrl,
                    accessToken,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));

            arasClientMock.VerifyNoOtherCalls();

            // The successful Add remains persisted for later cleanup.
            var manifestPath = Path.Combine(
                directory,
                $"{runId}.json");

            Assert.True(File.Exists(manifestPath));

            var recoveredManifest = TestDataManifest.Load(
                manifestPath,
                environmentId);

            var trackedItem = Assert.Single(
                recoveredManifest.Items);

            Assert.Equal(createdId, trackedItem.Id);
            Assert.Equal("Part", trackedItem.ItemType);

            Assert.Equal(
                TestItemSource.CustomAml,
                trackedItem.Source);

            Assert.Equal(
                TrackedItemStatus.Active,
                trackedItem.Status);

            Assert.Equal(1, recoveredManifest.TotalCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
    [Fact]
    public async Task RunAsync_WhenAutomaticCleanupReturnsUnauthorized_PreservesCompletedBenchmarkReport()
    {
        // Arrange
        const string serverUrl =
            "http://localhost/InnovatorServer/Server/InnovatorServer.aspx";

        const string accessToken = "expired-test-token";

        const string rawAml =
            "<AML>" +
            "<Item type=\"Part\" action=\"add\">" +
            "<item_number>TEST-PART</item_number>" +
            "<name>Cleanup authentication test</name>" +
            "</Item>" +
            "</AML>";

        var environmentId =
            $"{serverUrl}|InnovatorSolutions";

        var runId = Guid.NewGuid().ToString("N");

        var firstCreatedId = Guid.NewGuid()
            .ToString("N")
            .ToUpperInvariant();

        var secondCreatedId = Guid.NewGuid()
            .ToString("N")
            .ToUpperInvariant();

        var directory = Path.Combine(
            Path.GetTempPath(),
            "ArasPerformanceToolCustomAmlAuthTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        try
        {
            var firstCreateResponse =
                $"<Result><Item type=\"Part\" id=\"{firstCreatedId}\" /></Result>";

            var secondCreateResponse =
                $"<Result><Item type=\"Part\" id=\"{secondCreatedId}\" /></Result>";

            var authenticationFailure = new HttpRequestException(
                "Aras rejected the access token during cleanup.",
                inner: null,
                statusCode: HttpStatusCode.Unauthorized);

            var arasClientMock =
                new Mock<IArasClient>(MockBehavior.Strict);

            arasClientMock
                .SetupSequence(x => x.ExecuteAmlAsync(
                    serverUrl,
                    accessToken,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                // Both benchmark iterations succeed.
                .ReturnsAsync(firstCreateResponse)
                .ReturnsAsync(secondCreateResponse)
                // Automatic cleanup's first existence check fails.
                .ThrowsAsync(authenticationFailure);

            var service = new CustomAmlBenchmarkService(
                arasClientMock.Object);

            var config = new BenchmarkConfiguration(
                "Part",
                2,
                1);

            // Act
            var exception =
                await Assert.ThrowsAsync<BenchmarkAuthenticationException>(
                    () => service.RunAsync(
                        rawAml,
                        "CustomAmlCleanupAuthenticationTest",
                        config,
                        serverUrl,
                        accessToken,
                        environmentId,
                        runId,
                        directory));

            // Assert: Cleanup authentication failure is propagated.
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                exception.StatusCode);

            Assert.Same(
                authenticationFailure,
                exception.InnerException);

            // The completed benchmark report must not be lost.
            var report = exception.PartialReport;

            Assert.Equal(
                "CustomAmlCleanupAuthenticationTest",
                report.WorkloadName);

            Assert.Equal(2, report.Configuration.Iterations);
            Assert.Equal(2, report.RawMeasurements.Count);

            Assert.Equal(2, report.Statistics.TotalRequests);
            Assert.Equal(2, report.Statistics.SuccessCount);
            Assert.Equal(0, report.Statistics.FailureCount);

            Assert.All(
                report.RawMeasurements,
                measurement => Assert.True(measurement.IsSuccess));

            // Cleanup's failed Read must not become a benchmark measurement.
            var requests = arasClientMock.Invocations
                .Where(invocation =>
                    invocation.Method.Name ==
                    nameof(IArasClient.ExecuteAmlAsync))
                .Select(invocation =>
                {
                    var aml = Assert.IsType<string>(
                        invocation.Arguments[2]);

                    var item = XDocument.Parse(aml)
                        .Descendants()
                        .First(element =>
                            element.Name.LocalName == "Item");

                    return (
                        Action: (string?)item.Attribute("action"),
                        Id: (string?)item.Attribute("id"));
                })
                .ToArray();

            Assert.Equal(3, requests.Length);

            Assert.Equal(
                new[] { "add", "add", "get" },
                requests.Select(request => request.Action).ToArray());

            Assert.Contains(
                requests[2].Id,
                new[] { firstCreatedId, secondCreatedId });

            arasClientMock.Verify(
                x => x.ExecuteAmlAsync(
                    serverUrl,
                    accessToken,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(3));

            arasClientMock.VerifyNoOtherCalls();

            // Both created items remain persisted and unresolved.
            var manifestPath = Path.Combine(
                directory,
                $"{runId}.json");

            Assert.True(File.Exists(manifestPath));

            var recoveredManifest = TestDataManifest.Load(
                manifestPath,
                environmentId);

            Assert.Equal(2, recoveredManifest.Items.Count);
            Assert.Equal(2, recoveredManifest.TotalCount);

            var expectedIds = new[]
            {
                firstCreatedId,
                secondCreatedId
            }.OrderBy(id => id).ToArray();

            var actualIds = recoveredManifest.Items
                .Select(item => item.Id)
                .OrderBy(id => id)
                .ToArray();

            Assert.Equal(expectedIds, actualIds);

            Assert.All(
                recoveredManifest.Items,
                item =>
                {
                    Assert.Equal(
                        TrackedItemStatus.Active,
                        item.Status);

                    Assert.Equal(
                        TestItemSource.CustomAml,
                        item.Source);
                });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}