using System.Net;
using System.Xml.Linq;
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Benchmarking;
using Moq;
using Xunit;

namespace ArasPerformanceTool.Tests.Unit;

public sealed class TestDataCleanupAuthenticationTests
{
    [Fact]
    public async Task CleanupAsync_WhenFirstReadReturnsUnauthorized_StopsAndPreservesManifest()
    {
        // Arrange
        const string serverUrl =
            "http://localhost/InnovatorServer/Server/InnovatorServer.aspx";

        const string accessToken = "expired-test-token";

        var environmentId =
            $"{serverUrl}|InnovatorSolutions";

        var directory = Path.Combine(
            Path.GetTempPath(),
            "ArasPerformanceToolCleanupAuthTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        try
        {
            var runId = Guid.NewGuid().ToString("N");

            var manifestPath = Path.Combine(
                directory,
                $"{runId}.json");

            var manifest = new TestDataManifest(
                environmentId: environmentId,
                filePath: manifestPath,
                runId: runId);

            manifest.Track(
                "Part",
                Guid.NewGuid().ToString("N").ToUpperInvariant());

            manifest.Track(
                "Part",
                Guid.NewGuid().ToString("N").ToUpperInvariant());

            var before =
                await File.ReadAllBytesAsync(manifestPath);

            var authenticationFailure = new HttpRequestException(
                "Aras rejected the access token.",
                inner: null,
                statusCode: HttpStatusCode.Unauthorized);

            var arasClientMock =
                new Mock<IArasClient>(MockBehavior.Strict);

            arasClientMock
                .Setup(x => x.ExecuteAmlAsync(
                    serverUrl,
                    accessToken,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(authenticationFailure);

            var cleanupService =
                new TestDataCleanupService(arasClientMock.Object);

            // Act
            var exception =
                await Assert.ThrowsAsync<HttpRequestException>(
                    () => cleanupService.CleanupAsync(
                        manifest,
                        environmentId,
                        serverUrl,
                        accessToken));

            // Assert: Preserve the original authentication failure.
            Assert.Same(authenticationFailure, exception);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                exception.StatusCode);

            // Only the first existence-check request was sent.
            arasClientMock.Verify(
                x => x.ExecuteAmlAsync(
                    serverUrl,
                    accessToken,
                    It.Is<string>(aml => aml.Contains("action=\"get\"")),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            // No Delete, retry, next-item request or OAuth request.
            arasClientMock.VerifyNoOtherCalls();

            Assert.True(File.Exists(manifestPath));

            var after =
                await File.ReadAllBytesAsync(manifestPath);

            Assert.Equal(before, after);

            Assert.Equal(
                2,
                manifest.UnresolvedItems.Count());
                // Verify that authentication failure did not change item states.
            Assert.All(
                manifest.UnresolvedItems,
                item => Assert.Equal(
                    TrackedItemStatus.Active,
                    item.Status));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CleanupAsync_WhenUnauthorizedAfterOneDeletion_PreservesProgressAndStops()
    {
        // Arrange
        const string serverUrl =
            "http://localhost/InnovatorServer/Server/InnovatorServer.aspx";

        const string accessToken = "expired-test-token";

        var environmentId =
            $"{serverUrl}|InnovatorSolutions";

        var directory = Path.Combine(
            Path.GetTempPath(),
            "ArasPerformanceToolCleanupAuthTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        try
        {
            var runId = Guid.NewGuid().ToString("N");

            var manifestPath = Path.Combine(
                directory,
                $"{runId}.json");

            var manifest = new TestDataManifest(
                environmentId: environmentId,
                filePath: manifestPath,
                runId: runId);

            for (var i = 0; i < 3; i++)
            {
                manifest.Track(
                    "Part",
                    Guid.NewGuid().ToString("N").ToUpperInvariant());
            }

            // Match the service's actual target ordering.
            // Do not rely on Track insertion order or timestamp delays.
            var targets = manifest.UnresolvedItems
                .OrderByDescending(item => item.CreatedAtUtc)
                .ToArray();

            var firstId = targets[0].Id;
            var secondId = targets[1].Id;
            var thirdId = targets[2].Id;

            var existingFirstItemXml =
                $"<Result><Item type=\"Part\" id=\"{firstId}\" /></Result>";

            var deleteFirstItemXml =
                $"<Result><Item type=\"Part\" id=\"{firstId}\" /></Result>";

            const string missingItemXml =
                "<Result><Fault>" +
                "<faultcode>0</faultcode>" +
                "<faultstring>No items found.</faultstring>" +
                "</Fault></Result>";

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
                // First item: exists, deleted, then verified missing.
                .ReturnsAsync(existingFirstItemXml)
                .ReturnsAsync(deleteFirstItemXml)
                .ReturnsAsync(missingItemXml)
                // Second item: existence check receives HTTP 401.
                .ThrowsAsync(authenticationFailure);

            var cleanupService =
                new TestDataCleanupService(arasClientMock.Object);

            // Act
            var exception =
                await Assert.ThrowsAsync<HttpRequestException>(
                    () => cleanupService.CleanupAsync(
                        manifest,
                        environmentId,
                        serverUrl,
                        accessToken));

            // Assert: Authentication failure is preserved.
            Assert.Same(authenticationFailure, exception);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                exception.StatusCode);

            // Verify the exact sequence of actual AML requests.
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

            Assert.Equal(4, requests.Length);

            Assert.Equal("get", requests[0].Action);
            Assert.Equal(firstId, requests[0].Id);

            Assert.Equal("delete", requests[1].Action);
            Assert.Equal(firstId, requests[1].Id);

            Assert.Equal("get", requests[2].Action);
            Assert.Equal(firstId, requests[2].Id);

            Assert.Equal("get", requests[3].Action);
            Assert.Equal(secondId, requests[3].Id);

            arasClientMock.Verify(
                x => x.ExecuteAmlAsync(
                    serverUrl,
                    accessToken,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(4));

            
            arasClientMock.VerifyNoOtherCalls();

            // Reload from disk to verify persisted recovery state.
            Assert.True(File.Exists(manifestPath));

            var recoveredManifest = TestDataManifest.Load(
                manifestPath,
                environmentId);

            // All three tracked records remain in the manifest.
            Assert.Equal(3, recoveredManifest.Items.Count);

            // Only two items still require cleanup.
            Assert.Equal(2, recoveredManifest.TotalCount);

            // The first item was successfully deleted.
            Assert.Equal(
                TrackedItemStatus.Deleted,
                recoveredManifest.Items.Single(
                    item => item.Id == firstId).Status);

            var unresolved = recoveredManifest.UnresolvedItems
                .ToDictionary(item => item.Id);

            // The deleted item must not appear among unresolved items.
            Assert.False(unresolved.ContainsKey(firstId));

            // The rejected item and untouched item remain recoverable.
            Assert.Equal(2, unresolved.Count);

            Assert.Equal(
                TrackedItemStatus.Active,
                unresolved[secondId].Status);

            Assert.Equal(
                TrackedItemStatus.Active,
                unresolved[thirdId].Status);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }

    }

    [Fact]
    public async Task CleanupAsync_WhenVerificationReturnsUnauthorized_PreservesDeletePendingAndStops()
    {
        // Arrange
        const string serverUrl =
            "http://localhost/InnovatorServer/Server/InnovatorServer.aspx";

        const string accessToken = "expired-test-token";

        var environmentId =
            $"{serverUrl}|InnovatorSolutions";

        var directory = Path.Combine(
            Path.GetTempPath(),
            "ArasPerformanceToolCleanupAuthTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        try
        {
            var runId = Guid.NewGuid().ToString("N");

            var manifestPath = Path.Combine(
                directory,
                $"{runId}.json");

            var manifest = new TestDataManifest(
                environmentId: environmentId,
                filePath: manifestPath,
                runId: runId);

            for (var i = 0; i < 2; i++)
            {
                manifest.Track(
                    "Part",
                    Guid.NewGuid().ToString("N").ToUpperInvariant());
            }

            var targets = manifest.UnresolvedItems
                .OrderByDescending(item => item.CreatedAtUtc)
                .ToArray();

            var firstId = targets[0].Id;
            var secondId = targets[1].Id;

            var existingItemXml =
                $"<Result><Item type=\"Part\" id=\"{firstId}\" /></Result>";

            var deleteResponseXml =
                $"<Result><Item type=\"Part\" id=\"{firstId}\" /></Result>";

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
                // Item exists before deletion.
                .ReturnsAsync(existingItemXml)
                // Delete returns successfully.
                .ReturnsAsync(deleteResponseXml)
                // Verification cannot complete because authentication fails.
                .ThrowsAsync(authenticationFailure);

            var cleanupService =
                new TestDataCleanupService(arasClientMock.Object);

            // Act
            var exception =
                await Assert.ThrowsAsync<HttpRequestException>(
                    () => cleanupService.CleanupAsync(
                        manifest,
                        environmentId,
                        serverUrl,
                        accessToken));

            // Assert
            Assert.Same(authenticationFailure, exception);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                exception.StatusCode);

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

            // Read, Delete, verification Read — all for the first item.
            Assert.Equal(3, requests.Length);

            Assert.Equal("get", requests[0].Action);
            Assert.Equal(firstId, requests[0].Id);

            Assert.Equal("delete", requests[1].Action);
            Assert.Equal(firstId, requests[1].Id);

            Assert.Equal("get", requests[2].Action);
            Assert.Equal(firstId, requests[2].Id);

            arasClientMock.Verify(
                x => x.ExecuteAmlAsync(
                    serverUrl,
                    accessToken,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(3));

            arasClientMock.VerifyNoOtherCalls();

            // Verify the state actually persisted to disk.
            var recoveredManifest = TestDataManifest.Load(
                manifestPath,
                environmentId);

            var recoveredItems = recoveredManifest.Items
                .ToDictionary(item => item.Id);

            Assert.Equal(2, recoveredItems.Count);
            Assert.Equal(2, recoveredManifest.TotalCount);

            // Delete was attempted, but absence was not verified.
            Assert.Equal(
                TrackedItemStatus.DeletePending,
                recoveredItems[firstId].Status);

            // The next item must not have been processed.
            Assert.Equal(
                TrackedItemStatus.Active,
                recoveredItems[secondId].Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

}