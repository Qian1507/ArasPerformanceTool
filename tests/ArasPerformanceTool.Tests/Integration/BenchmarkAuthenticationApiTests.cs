
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ArasPerformanceTool.Api.Services;
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Benchmarking;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Xunit;

namespace ArasPerformanceTool.Tests.Integration;

public sealed class BenchmarkAuthenticationApiTests
{
    [Fact]
    public async Task StandardCreate_WhenArasReturns401_PreservesReportAndSkipsCleanup()
    {
        // Arrange
        using var factory = new TestFactory();
        using var client = factory.CreateClient();

        const string accessToken = "expired-test-token";

        var createdId = Guid.NewGuid()
            .ToString("N")
            .ToUpperInvariant();

        var createResponse =
            $"<Result><Item type=\"Part\" id=\"{createdId}\" /></Result>";

        var unauthorized = new HttpRequestException(
            "Aras access token expired.",
            inner: null,
            statusCode: HttpStatusCode.Unauthorized);

        factory.ArasClient
            .Setup(x => x.GetAccessTokenAsync(
                "admin",
                "test-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessToken);

        factory.ArasClient
            .SetupSequence(x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                accessToken,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createResponse)
            .ThrowsAsync(unauthorized);

        var csrf = await GetCsrfAsync(client);

        using var loginResponse = await PostAsync(
            client,
            "/api/auth/login",
            new
            {
                username = "admin",
                password = "test-password",
                database = "InnovatorSolutions"
            },
            csrf);

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // Act
        using var response = await PostAsync(
            client,
            "/api/benchmark",
            new
            {
                itemType = "Part",
                action = "Create",
                iterations = 3,
                concurrency = 1,
                workloadName = "Standard401Test"
            },
            csrf);

        var responseText =
            await response.Content.ReadAsStringAsync();

        // Assert: Explicit reauthentication response.
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected HTTP 401. Actual response: {responseText}");

        using var json = JsonDocument.Parse(responseText);
        var result = json.RootElement;

        Assert.True(
            result.GetProperty("requiresLogin").GetBoolean());

        Assert.Equal(
            3,
            result.GetProperty("iterations").GetInt32());

        var jobId = result.GetProperty("jobId").GetGuid();

        Assert.NotEqual(Guid.Empty, jobId);

        // Only two iterations actually executed.
        var stats = result.GetProperty("statistics");

        Assert.Equal(
            2,
            stats.GetProperty("totalRequests").GetInt32());

        Assert.Equal(
            1,
            stats.GetProperty("successCount").GetInt32());

        Assert.Equal(
            1,
            stats.GetProperty("failureCount").GetInt32());

        // No additional AML requests for verification or cleanup.
        var amlRequests = factory.ArasClient.Invocations
            .Where(invocation =>
                invocation.Method.Name ==
                nameof(IArasClient.ExecuteAmlAsync))
            .ToArray();

        Assert.Equal(2, amlRequests.Length);

        Assert.All(
            amlRequests,
            invocation =>
            {
                var aml = Assert.IsType<string>(
                    invocation.Arguments[2]);

                Assert.Contains(
                    "action=\"add\"",
                    aml,
                    StringComparison.OrdinalIgnoreCase);
            });

        // Both report formats must be persisted.
        var jsonPath = Path.Combine(
            factory.StorageDirectory,
            "reports",
            $"{jobId:D}.json");

        var csvPath = Path.Combine(
            factory.StorageDirectory,
            "reports",
            $"{jobId:D}.csv");

        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(csvPath));

        // Reports remain accessible after authentication expiry.
        using var reportResponse = await client.GetAsync(
            $"/api/report/{jobId:D}/json");

        Assert.Equal(
            HttpStatusCode.OK,
            reportResponse.StatusCode);

        var savedReport = await reportResponse.Content
            .ReadFromJsonAsync<BenchmarkReport>();

        Assert.NotNull(savedReport);

        Assert.Equal(2, savedReport.RawMeasurements.Count);
        Assert.Equal(2, savedReport.Statistics.TotalRequests);
        Assert.Equal(1, savedReport.Statistics.SuccessCount);
        Assert.Equal(1, savedReport.Statistics.FailureCount);   

        // The created item must remain tracked for later cleanup.
        var manifestPath = Path.Combine(
            factory.StorageDirectory,
            "manifests",
            $"{jobId:N}.json");

        Assert.True(File.Exists(manifestPath));

        var environmentId =
            $"{TestFactory.ServerUrl}|InnovatorSolutions";

        var manifest = TestDataManifest.Load(
            manifestPath,
            environmentId);

        var tracked = Assert.Single(manifest.UnresolvedItems);

        Assert.Equal(createdId, tracked.Id);
        Assert.Equal(
            TrackedItemStatus.Active,
            tracked.Status);

        // The old session token is now considered expired.
        var session = await client.GetFromJsonAsync<JsonElement>(
            "/api/auth/session");

        Assert.False(
            session.GetProperty("authenticated").GetBoolean());

        // No Run lock may remain held after the failure.
        var coordinator = factory.Services
            .GetRequiredService<RunExecutionCoordinator>();

        Assert.False(coordinator.IsActive(jobId));

        // Further benchmarks using the same expired token are rejected
        // before any new AML execution.
        using var retryResponse = await PostAsync(
            client,
            "/api/benchmark",
            new
            {
                itemType = "Part",
                action = "Create",
                iterations = 1,
                concurrency = 1
            },
            csrf);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            retryResponse.StatusCode);

        factory.ArasClient.Verify(
            x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                accessToken,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        
        factory.ArasClient.Verify(
            x => x.GetAccessTokenAsync(
                "admin",
                "test-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

        

    [Fact]
    public async Task StandardRead_WhenPreparationReturns401_DoesNotCreateReportOrRunCleanup()
    {
        // Arrange
        using var factory = new TestFactory();
        using var client = factory.CreateClient();

        const string accessToken = "expired-preparation-token";

        factory.ArasClient
            .Setup(x => x.GetAccessTokenAsync(
                "admin",
                "test-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessToken);

        // The first AML call is PrepareAsync's Create.
        // Authentication expires before any measurement begins.
        factory.ArasClient
            .Setup(x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                accessToken,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(
                "Aras authentication expired during preparation.",
                inner: null,
                statusCode: HttpStatusCode.Unauthorized));

        var csrf = await GetCsrfAsync(client);

        using var loginResponse = await PostAsync(
            client,
            "/api/auth/login",
            new
            {
                username = "admin",
                password = "test-password",
                database = "InnovatorSolutions"
            },
            csrf);

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // Act
        using var response = await PostAsync(
            client,
            "/api/benchmark",
            new
            {
                itemType = "Part",
                action = "Read",
                iterations = 3,
                concurrency = 1,
                workloadName = "Preparation401Test"
            },
            csrf);

        var responseText =
            await response.Content.ReadAsStringAsync();

        // Assert: HTTP 401.
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        using var json = JsonDocument.Parse(responseText);
        var result = json.RootElement;

        Assert.True(
            result.GetProperty("requiresLogin").GetBoolean());

        var jobId = result.GetProperty("jobId").GetGuid();

        Assert.NotEqual(Guid.Empty, jobId);
        Assert.Equal(
            JsonValueKind.Null,
            result.GetProperty("statistics").ValueKind);

        using var reportResponse = await client.GetAsync(
            $"/api/report/{jobId:D}/json");

        Assert.Equal(
            HttpStatusCode.NotFound,
            reportResponse.StatusCode);

        Assert.False(File.Exists(Path.Combine(
            factory.StorageDirectory,
            "reports",
            $"{jobId:D}.json")));

        Assert.False(File.Exists(Path.Combine(
            factory.StorageDirectory,
            "reports",
            $"{jobId:D}.csv")));

        factory.ArasClient.Verify(
            x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                accessToken,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var manifest = TestDataManifest.Load(
            Path.Combine(
                factory.StorageDirectory,
                "manifests",
                $"{jobId:N}.json"),
            $"{TestFactory.ServerUrl}|InnovatorSolutions");

        Assert.Empty(manifest.Items);

        var session = await client.GetFromJsonAsync<JsonElement>(
            "/api/auth/session");

        Assert.False(
            session.GetProperty("authenticated").GetBoolean());

        var coordinator = factory.Services
            .GetRequiredService<RunExecutionCoordinator>();

        Assert.False(coordinator.IsActive(jobId));
    }

        
    [Fact]
    public async Task StandardCreate_WhenVerificationReturns401_PreservesCompletedReport()
    {
        // Arrange
        using var factory = new TestFactory();
        using var client = factory.CreateClient();

        const string accessToken = "expired-verification-token";

        var createdId = Guid.NewGuid()
            .ToString("N")
            .ToUpperInvariant();

        var createResponse =
            $"<Result><Item type=\"Part\" id=\"{createdId}\" /></Result>";

        var unauthorized = new HttpRequestException(
            "Aras authentication expired during verification.",
            inner: null,
            statusCode: HttpStatusCode.Unauthorized);

        factory.ArasClient
            .Setup(x => x.GetAccessTokenAsync(
                "admin",
                "test-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessToken);

        factory.ArasClient
            .SetupSequence(x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                accessToken,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            // Measured Create succeeds.
            .ReturnsAsync(createResponse)
            // Additional verification Read fails.
            .ThrowsAsync(unauthorized);

        var csrf = await GetCsrfAsync(client);

        using var loginResponse = await PostAsync(
            client,
            "/api/auth/login",
            new
            {
                username = "admin",
                password = "test-password",
                database = "InnovatorSolutions"
            },
            csrf);

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        csrf = await GetCsrfAsync(client);

        // Act
        using var response = await PostAsync(
            client,
            "/api/benchmark",
            new
            {
                itemType = "Part",
                action = "Create",
                iterations = 1,
                concurrency = 1,
                workloadName = "Verification401Test"
            },
            csrf);

        var responseText =
            await response.Content.ReadAsStringAsync();

        // Assert: Authentication expired.
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected HTTP 401. Actual response: {responseText}");

        using var json = JsonDocument.Parse(responseText);
        var result = json.RootElement;

        Assert.True(
            result.GetProperty("requiresLogin").GetBoolean());

        var jobId = result.GetProperty("jobId").GetGuid();

        Assert.NotEqual(Guid.Empty, jobId);

        // Verification failure must not affect benchmark measurements.
        var statistics = result.GetProperty("statistics");

        Assert.Equal(1, statistics.GetProperty("totalRequests").GetInt32());
        Assert.Equal(1, statistics.GetProperty("successCount").GetInt32());
        Assert.Equal(0, statistics.GetProperty("failureCount").GetInt32());

        // Exactly two AML requests:
        // 1. Measured Create
        // 2. Verification Read returning 401
        // No automatic Cleanup.
        var requests = factory.ArasClient.Invocations
            .Where(invocation =>
                invocation.Method.Name ==
                nameof(IArasClient.ExecuteAmlAsync))
            .Select(invocation =>
            {
                var aml = Assert.IsType<string>(
                    invocation.Arguments[2]);

                var item = System.Xml.Linq.XDocument.Parse(aml)
                    .Descendants()
                    .First(element =>
                        element.Name.LocalName == "Item");

                return (
                    Action: (string?)item.Attribute("action"),
                    Id: (string?)item.Attribute("id"));
            })
            .ToArray();

        Assert.Equal(
            new[] { "add", "get" },
            requests.Select(request => request.Action).ToArray());

        Assert.Equal(createdId, requests[1].Id);

        // The completed report must be saved.
        Assert.True(File.Exists(Path.Combine(
            factory.StorageDirectory,
            "reports",
            $"{jobId:D}.json")));

        Assert.True(File.Exists(Path.Combine(
            factory.StorageDirectory,
            "reports",
            $"{jobId:D}.csv")));

        using var reportResponse = await client.GetAsync(
            $"/api/report/{jobId:D}/json");

        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);

        var savedReport = await reportResponse.Content
            .ReadFromJsonAsync<BenchmarkReport>();

        Assert.NotNull(savedReport);
        Assert.Single(savedReport.RawMeasurements);

        Assert.True(savedReport.RawMeasurements[0].IsSuccess);
        Assert.Equal(1, savedReport.Statistics.TotalRequests);
        Assert.Equal(1, savedReport.Statistics.SuccessCount);
        Assert.Equal(0, savedReport.Statistics.FailureCount);

        // Verification failed before Cleanup was allowed to start.
        var manifest = TestDataManifest.Load(
            Path.Combine(
                factory.StorageDirectory,
                "manifests",
                $"{jobId:N}.json"),
            $"{TestFactory.ServerUrl}|InnovatorSolutions");

        var tracked = Assert.Single(manifest.Items);

        Assert.Equal(createdId, tracked.Id);
        Assert.Equal(TrackedItemStatus.Active, tracked.Status);
        Assert.Equal(1, manifest.TotalCount);

        // Session must be considered expired.
        var session = await client.GetFromJsonAsync<JsonElement>(
            "/api/auth/session");

        Assert.False(
            session.GetProperty("authenticated").GetBoolean());

        // Run lock must always be released.
        var coordinator = factory.Services
            .GetRequiredService<RunExecutionCoordinator>();

        Assert.False(coordinator.IsActive(jobId));

        factory.ArasClient.Verify(
            x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                accessToken,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        factory.ArasClient.Verify(
            x => x.GetAccessTokenAsync(
                "admin",
                "test-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }



    [Fact]
    public async Task StandardCreate_WhenCleanupReturns401_PreservesCompletedReport()
    {
        // Arrange
        using var factory = new TestFactory();
        using var client = factory.CreateClient();

        const string accessToken = "expired-test-token";

        var createdId = Guid.NewGuid()
            .ToString("N")
            .ToUpperInvariant();

        var createResponse =
            $"<Result><Item type=\"Part\" id=\"{createdId}\" /></Result>";

        var verificationResponse =
            $"<Result><Item type=\"Part\" id=\"{createdId}\">" +
            "<item_number>TEST-PART</item_number>" +
            "<name>API Benchmark Part 1</name>" +
            "</Item></Result>";

        var unauthorized = new HttpRequestException(
            "Aras rejected the token during cleanup.",
            inner: null,
            statusCode: HttpStatusCode.Unauthorized);

        factory.ArasClient
            .Setup(x => x.GetAccessTokenAsync(
                "admin",
                "test-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessToken);

        factory.ArasClient
            .SetupSequence(x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                accessToken,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            // Measured Create succeeds.
            .ReturnsAsync(createResponse)
            // Additional sample verification succeeds.
            .ReturnsAsync(verificationResponse)
            // Automatic Cleanup's first Read receives 401.
            .ThrowsAsync(unauthorized);

        var csrf = await GetCsrfAsync(client);

        using var loginResponse = await PostAsync(
            client,
            "/api/auth/login",
            new
            {
                username = "admin",
                password = "test-password",
                database = "InnovatorSolutions"
            },
            csrf);

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        csrf = await GetCsrfAsync(client);

        // Act
        using var response = await PostAsync(
            client,
            "/api/benchmark",
            new
            {
                itemType = "Part",
                action = "Create",
                iterations = 1,
                concurrency = 1,
                workloadName = "StandardCleanup401Test"
            },
            csrf);

        var responseText =
            await response.Content.ReadAsStringAsync();

        // Assert: The API operation requires reauthentication.
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected HTTP 401. Actual response: {responseText}");

        using var json = JsonDocument.Parse(responseText);
        var result = json.RootElement;

        Assert.True(
            result.GetProperty("requiresLogin").GetBoolean());

        var jobId = result.GetProperty("jobId").GetGuid();

        Assert.NotEqual(Guid.Empty, jobId);

        // Cleanup failure must not alter measured benchmark statistics.
        var statistics = result.GetProperty("statistics");

        Assert.Equal(
            1,
            statistics.GetProperty("totalRequests").GetInt32());

        Assert.Equal(
            1,
            statistics.GetProperty("successCount").GetInt32());

        Assert.Equal(
            0,
            statistics.GetProperty("failureCount").GetInt32());

        // Verify actual AML sequence: Add, sample Read, Cleanup Read.
        var requests = factory.ArasClient.Invocations
            .Where(invocation =>
                invocation.Method.Name ==
                nameof(IArasClient.ExecuteAmlAsync))
            .Select(invocation =>
            {
                var aml = Assert.IsType<string>(
                    invocation.Arguments[2]);

                var item = System.Xml.Linq.XDocument.Parse(aml)
                    .Descendants()
                    .First(element =>
                        element.Name.LocalName == "Item");

                return (
                    Action: (string?)item.Attribute("action"),
                    Id: (string?)item.Attribute("id"));
            })
            .ToArray();

        Assert.Equal(
            new[] { "add", "get", "get" },
            requests.Select(request => request.Action).ToArray());

        Assert.Equal(createdId, requests[1].Id);
        Assert.Equal(createdId, requests[2].Id);

        // Both stored report formats must remain present.
        Assert.True(File.Exists(Path.Combine(
            factory.StorageDirectory,
            "reports",
            $"{jobId:D}.json")));

        Assert.True(File.Exists(Path.Combine(
            factory.StorageDirectory,
            "reports",
            $"{jobId:D}.csv")));

        using var reportResponse = await client.GetAsync(
            $"/api/report/{jobId:D}/json");

        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);

        var savedReport = await reportResponse.Content
            .ReadFromJsonAsync<BenchmarkReport>();

        Assert.NotNull(savedReport);
        Assert.Single(savedReport.RawMeasurements);

        Assert.True(savedReport.RawMeasurements[0].IsSuccess);
        Assert.Equal(1, savedReport.Statistics.TotalRequests);
        Assert.Equal(1, savedReport.Statistics.SuccessCount);
        Assert.Equal(0, savedReport.Statistics.FailureCount);

        var verification = result.GetProperty("verification");

        Assert.True(
            verification.GetProperty("isVerified").GetBoolean());

        // Cleanup failed before deletion intent was recorded.
        var manifest = TestDataManifest.Load(
            Path.Combine(
                factory.StorageDirectory,
                "manifests",
                $"{jobId:N}.json"),
            $"{TestFactory.ServerUrl}|InnovatorSolutions");

        var tracked = Assert.Single(manifest.Items);

        Assert.Equal(createdId, tracked.Id);
        Assert.Equal(TrackedItemStatus.Active, tracked.Status);
        Assert.Equal(1, manifest.TotalCount);

        var session = await client.GetFromJsonAsync<JsonElement>(
            "/api/auth/session");

        Assert.False(
            session.GetProperty("authenticated").GetBoolean());

        var coordinator = factory.Services
            .GetRequiredService<RunExecutionCoordinator>();

        Assert.False(coordinator.IsActive(jobId));

        factory.ArasClient.Verify(
            x => x.GetAccessTokenAsync(
                "admin",
                "test-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()),
            Times.Once);

        factory.ArasClient.Verify(
            x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                accessToken,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        
    }


    [Fact]
    public async Task CustomAml_WhenBenchmarkReturns401_PersistsPartialReport()
    {
        // Arrange
        using var factory = new TestFactory();
        using var client = factory.CreateClient();

        const string accessToken = "expired-custom-aml-token";

        const string rawAml =
            "<AML>" +
            "<Item type=\"Part\" action=\"add\">" +
            "<item_number>TEST-PART</item_number>" +
            "<name>Custom AML authentication test</name>" +
            "</Item>" +
            "</AML>";

        var createdId = Guid.NewGuid()
            .ToString("N")
            .ToUpperInvariant();

        var createResponse =
            $"<Result><Item type=\"Part\" id=\"{createdId}\" /></Result>";

        factory.ArasClient
            .Setup(x => x.GetAccessTokenAsync(
                "admin",
                "test-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessToken);

        factory.ArasClient
            .SetupSequence(x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                accessToken,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(createResponse)
            .ThrowsAsync(new HttpRequestException(
                "Aras token expired during Custom AML.",
                inner: null,
                statusCode: HttpStatusCode.Unauthorized));

        var csrf = await GetCsrfAsync(client);

        using var loginResponse = await PostAsync(
            client,
            "/api/auth/login",
            new
            {
                username = "admin",
                password = "test-password",
                database = "InnovatorSolutions"
            },
            csrf);

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        csrf = await GetCsrfAsync(client);

        // Act
        using var response = await PostAsync(
            client,
            "/api/benchmark",
            new
            {
                aml = rawAml,
                iterations = 3,
                concurrency = 1,
                workloadName = "CustomAmlController401Test"
            },
            csrf);

        var responseText =
            await response.Content.ReadAsStringAsync();

        // Assert: Controller returns explicit authentication failure.
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected HTTP 401. Actual response: {responseText}");

        using var json = JsonDocument.Parse(responseText);
        var result = json.RootElement;

        Assert.True(
            result.GetProperty("requiresLogin").GetBoolean());

        var jobId = result.GetProperty("jobId").GetGuid();

        Assert.NotEqual(Guid.Empty, jobId);

        // Partial measurements must be returned by the Controller.
        var statistics = result.GetProperty("statistics");

        Assert.Equal(2, statistics.GetProperty("totalRequests").GetInt32());
        Assert.Equal(1, statistics.GetProperty("successCount").GetInt32());
        Assert.Equal(1, statistics.GetProperty("failureCount").GetInt32());

        // The Controller must persist both report formats.
        var jsonPath = Path.Combine(
            factory.StorageDirectory,
            "reports",
            $"{jobId:D}.json");

        var csvPath = Path.Combine(
            factory.StorageDirectory,
            "reports",
            $"{jobId:D}.csv");

        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(csvPath));

        // Read the report through the actual API, not from the exception.
        using var reportResponse = await client.GetAsync(
            $"/api/report/{jobId:D}/json");

        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);

        var savedReport = await reportResponse.Content
            .ReadFromJsonAsync<BenchmarkReport>();

        Assert.NotNull(savedReport);

        Assert.Equal(3, savedReport.Configuration.Iterations);
        Assert.Equal(2, savedReport.RawMeasurements.Count);

        Assert.Equal(2, savedReport.Statistics.TotalRequests);
        Assert.Equal(1, savedReport.Statistics.SuccessCount);
        Assert.Equal(1, savedReport.Statistics.FailureCount);

        Assert.Equal(
            "CustomAmlController401Test",
            savedReport.WorkloadName);

        // Custom AML must not continue after the authentication failure.
        var requests = factory.ArasClient.Invocations
            .Where(invocation =>
                invocation.Method.Name ==
                nameof(IArasClient.ExecuteAmlAsync))
            .Select(invocation =>
            {
                var aml = Assert.IsType<string>(
                    invocation.Arguments[2]);

                var item = System.Xml.Linq.XDocument.Parse(aml)
                    .Descendants()
                    .First(element =>
                        element.Name.LocalName == "Item");

                return (string?)item.Attribute("action");
            })
            .ToArray();

        Assert.Equal(new[] { "add", "add" }, requests);

        // The successfully created item remains tracked.
        var manifest = TestDataManifest.Load(
            Path.Combine(
                factory.StorageDirectory,
                "manifests",
                $"{jobId:N}.json"),
            $"{TestFactory.ServerUrl}|InnovatorSolutions");

        var tracked = Assert.Single(manifest.Items);

        Assert.Equal(createdId, tracked.Id);
        Assert.Equal(TrackedItemStatus.Active, tracked.Status);
        Assert.Equal(TestItemSource.CustomAml, tracked.Source);

        // Session is marked as expired.
        var session = await client.GetFromJsonAsync<JsonElement>(
            "/api/auth/session");

        Assert.False(
            session.GetProperty("authenticated").GetBoolean());

        // The Controller must release the Run lock.
        var coordinator = factory.Services
            .GetRequiredService<RunExecutionCoordinator>();

        Assert.False(coordinator.IsActive(jobId));

        factory.ArasClient.Verify(
            x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                accessToken,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        factory.ArasClient.Verify(
            x => x.GetAccessTokenAsync(
                "admin",
                "test-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    

    [Fact]
    public async Task ManualCleanup_WhenArasReturns401_PreservesManifestAndReleasesLock()
    {
        // Arrange
        using var factory = new TestFactory();
        using var client = factory.CreateClient();

        const string accessToken = "expired-manual-cleanup-token";

        var runId = Guid.NewGuid();

        var itemId = Guid.NewGuid()
            .ToString("N")
            .ToUpperInvariant();

        var environmentId =
            $"{TestFactory.ServerUrl}|InnovatorSolutions";

        var manifestPath = Path.Combine(
            factory.StorageDirectory,
            "manifests",
            $"{runId:N}.json");

        // Create a persisted pending-cleanup manifest.
        var manifest = new TestDataManifest(
            environmentId: environmentId,
            filePath: manifestPath,
            runId: runId.ToString("N"));

        manifest.Track("Part", itemId);


        var before = await File.ReadAllBytesAsync(manifestPath);

        factory.ArasClient
            .Setup(x => x.GetAccessTokenAsync(
                "admin",
                "test-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessToken);

        factory.ArasClient
            .Setup(x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                accessToken,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(
                "Aras authentication expired during manual cleanup.",
                inner: null,
                statusCode: HttpStatusCode.Unauthorized));

        var csrf = await GetCsrfAsync(client);

        using var loginResponse = await PostAsync(
            client,
            "/api/auth/login",
            new
            {
                username = "admin",
                password = "test-password",
                database = "InnovatorSolutions"
            },
            csrf);

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        csrf = await GetCsrfAsync(client);

        // Act
        using var response = await PostAsync(
            client,
            $"/api/Benchmark/pending-cleanup/{runId:N}/cleanup",
            new { },
            csrf);

        var responseText =
            await response.Content.ReadAsStringAsync();

        // Assert: HTTP 401, not 502.
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected HTTP 401. Actual response: {responseText}");

        using var json = JsonDocument.Parse(responseText);
        var result = json.RootElement;

        Assert.True(
            result.GetProperty("requiresLogin").GetBoolean());

        // The failed existence check must not modify the manifest.
        var after = await File.ReadAllBytesAsync(manifestPath);

        Assert.Equal(before, after);

        var recoveredManifest = TestDataManifest.Load(
            manifestPath,
            environmentId);

        var tracked = Assert.Single(recoveredManifest.Items);

        Assert.Equal(itemId, tracked.Id);
        Assert.Equal(TrackedItemStatus.Active, tracked.Status);
        Assert.Equal(1, recoveredManifest.TotalCount);

        // Exactly one AML call: Cleanup existence check (get).
        // No Delete should be attempted after the 401.
        var requests = factory.ArasClient.Invocations
            .Where(invocation =>
                invocation.Method.Name ==
                nameof(IArasClient.ExecuteAmlAsync))
            .Select(invocation =>
            {
                var aml = Assert.IsType<string>(
                    invocation.Arguments[2]);

                var item = System.Xml.Linq.XDocument.Parse(aml)
                    .Descendants()
                    .First(element =>
                        element.Name.LocalName == "Item");

                return (
                    Action: (string?)item.Attribute("action"),
                    Id: (string?)item.Attribute("id"));
            })
            .ToArray();

        var request = Assert.Single(requests);

        Assert.Equal("get", request.Action);
        Assert.Equal(itemId, request.Id);

        // The Session must recognize the expired token.
        var session = await client.GetFromJsonAsync<JsonElement>(
            "/api/auth/session");

        Assert.False(
            session.GetProperty("authenticated").GetBoolean());

        // The Run lock must be released.
        var coordinator = factory.Services
            .GetRequiredService<RunExecutionCoordinator>();

        Assert.False(coordinator.IsActive(runId));

        // A second cleanup attempt with the same token must be rejected
        // before any additional AML request.
        using var retryResponse = await PostAsync(
            client,
            $"/api/Benchmark/pending-cleanup/{runId:N}/cleanup",
            new { },
            csrf);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            retryResponse.StatusCode);

        // The retry must not modify the manifest.
        Assert.Equal(
            before,
            await File.ReadAllBytesAsync(manifestPath));

        factory.ArasClient.Verify(
            x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                accessToken,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        factory.ArasClient.Verify(
            x => x.GetAccessTokenAsync(
                "admin",
                "test-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }


    [Fact]
    public async Task ManualCleanup_AfterTokenExpires_NewLoginCanRecoverRun()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient();

        const string oldToken = "expired-token-A";
        const string newToken = "valid-token-B";

        var runId = Guid.NewGuid();

        var itemId = Guid.NewGuid()
            .ToString("N")
            .ToUpperInvariant();

        var environmentId =
            $"{TestFactory.ServerUrl}|InnovatorSolutions";

        var manifestPath = Path.Combine(
            factory.StorageDirectory,
            "manifests",
            $"{runId:N}.json");

        var manifest = new TestDataManifest(
            environmentId: environmentId,
            filePath: manifestPath,
            runId: runId.ToString("N"));

        manifest.Track("Part", itemId);

        // First login gets Token A; second login gets Token B.
        factory.ArasClient
            .SetupSequence(x => x.GetAccessTokenAsync(
                "admin",
                "test-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldToken)
            .ReturnsAsync(newToken);

        factory.ArasClient
            .Setup(x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                oldToken,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(
                "Token A expired.",
                inner: null,
                statusCode: HttpStatusCode.Unauthorized));

        // With Token B, Aras confirms the tracked item is missing.
        // Cleanup may safely mark it AlreadyMissing.
        const string noItemsResponse =
            "<Envelope><Body><Fault>" +
            "<faultcode>0</faultcode>" +
            "<faultstring>No items found</faultstring>" +
            "</Fault></Body></Envelope>";

        factory.ArasClient
            .Setup(x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                newToken,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(noItemsResponse);

        var csrf = await GetCsrfAsync(client);

        // Login with Token A.
        using (var login = await PostAsync(
            client,
            "/api/auth/login",
            new
            {
                username = "admin",
                password = "test-password",
                database = "InnovatorSolutions"
            },
            csrf))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        // Token A expires during manual cleanup.
        using (var firstCleanup = await PostAsync(
            client,
            $"/api/Benchmark/pending-cleanup/{runId:N}/cleanup",
            new { },
            csrf))
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                firstCleanup.StatusCode);
        }

        var expiredSession =
            await client.GetFromJsonAsync<JsonElement>(
                "/api/auth/session");

        Assert.False(
            expiredSession.GetProperty("authenticated").GetBoolean());

        // The original tracked item must remain unresolved.
        var beforeRecovery = TestDataManifest.Load(
            manifestPath,
            environmentId);

        Assert.Equal(1, beforeRecovery.TotalCount);

        // Login again; server issues Token B.
        csrf = await GetCsrfAsync(client);

        using (var secondLogin = await PostAsync(
            client,
            "/api/auth/login",
            new
            {
                username = "admin",
                password = "test-password",
                database = "InnovatorSolutions"
            },
            csrf))
        {
            Assert.Equal(HttpStatusCode.OK, secondLogin.StatusCode);
        }

        var newSession =
            await client.GetFromJsonAsync<JsonElement>(
                "/api/auth/session");

        Assert.True(
            newSession.GetProperty("authenticated").GetBoolean());

        // Retry the same pending cleanup run using Token B.
        csrf = await GetCsrfAsync(client);

        using var recovered = await PostAsync(
            client,
            $"/api/Benchmark/pending-cleanup/{runId:N}/cleanup",
            new { },
            csrf);

        var responseText =
            await recovered.Content.ReadAsStringAsync();

        Assert.True(
            recovered.StatusCode == HttpStatusCode.OK,
            $"Recovery failed: {responseText}");

        using var responseJson = JsonDocument.Parse(responseText);

        Assert.True(
            responseJson.RootElement
                .GetProperty("completed")
                .GetBoolean());

        // Recovery must be persisted.
        var afterRecovery = TestDataManifest.Load(
            manifestPath,
            environmentId);

        var tracked = Assert.Single(afterRecovery.Items);

        Assert.Equal(itemId, tracked.Id);
        Assert.Equal(
            TrackedItemStatus.AlreadyMissing,
            tracked.Status);

        Assert.Equal(0, afterRecovery.TotalCount);

        // One failed AML request with A, one successful Read with B.
        factory.ArasClient.Verify(
            x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                oldToken,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        factory.ArasClient.Verify(
            x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                newToken,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        factory.ArasClient.Verify(
            x => x.GetAccessTokenAsync(
                "admin",
                "test-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        var coordinator = factory.Services
            .GetRequiredService<RunExecutionCoordinator>();

        Assert.False(coordinator.IsActive(runId));
    }

    [Fact]
    public async Task StandardCreate_WhenOldRequestReturns401_AfterNewLogin_DoesNotInvalidateNewSession()
    {
        // Arrange
        using var factory = new TestFactory();
        using var client = factory.CreateClient();

        const string tokenA = "old-expired-token";
        const string tokenB = "new-valid-token";

        var createdId = Guid.NewGuid()
            .ToString("N")
            .ToUpperInvariant();

        var createResponse =
            $"<Result><Item type=\"Part\" id=\"{createdId}\" /></Result>";

        var benchmarkStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var releaseBenchmark = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        factory.ArasClient
            .Setup(x => x.GetAccessTokenAsync(
                "admin",
                "old-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenA);

        factory.ArasClient
            .Setup(x => x.GetAccessTokenAsync(
                "admin",
                "new-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenB);

        factory.ArasClient
            .SetupSequence(x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                tokenA,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            // The first measured Create succeeds.
            .ReturnsAsync(createResponse)
            // The old request remains in flight until the new login
            // has completed. It then receives Aras 401.
            .Returns(async () =>
            {
                benchmarkStarted.TrySetResult();

                await releaseBenchmark.Task;

                throw new HttpRequestException(
                    "Aras rejected the old access token.",
                    inner: null,
                    statusCode: HttpStatusCode.Unauthorized);

            #pragma warning disable CS0162
                return string.Empty;
            #pragma warning restore CS0162
            });

        // The new Session will later execute one read-only Custom AML
        // benchmark using Token B. It creates no test data and needs
        // no cleanup.
        factory.ArasClient
            .Setup(x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                tokenB,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                "<Result><Item type=\"Part\" id=\"" +
                createdId +
                "\" /></Result>");

        var oldCsrf = await GetCsrfAsync(client);

        using var oldLoginResponse = await PostAsync(
            client,
            "/api/auth/login",
            new
            {
                username = "admin",
                password = "old-password",
                database = "InnovatorSolutions"
            },
            oldCsrf);

        var oldLoginBody =
            await oldLoginResponse.Content.ReadAsStringAsync();

        Assert.True(
            oldLoginResponse.StatusCode == HttpStatusCode.OK,
            $"Old login failed. HTTP " +
            $"{(int)oldLoginResponse.StatusCode}: {oldLoginBody}");

        using var oldLoginJson =
            JsonDocument.Parse(oldLoginBody);

        Assert.True(
            oldLoginJson.RootElement
                .GetProperty("authenticated")
                .GetBoolean(),
            $"Old login did not authenticate: {oldLoginBody}");

        oldCsrf = await GetCsrfAsync(client);

        // Act
        var oldBenchmarkTask = PostAsync(
            client,
            "/api/benchmark",
            new
            {
                itemType = "Part",
                action = "Create",
                iterations = 3,
                concurrency = 1,
                workloadName = "ConcurrentSessionTest"
            },
            oldCsrf);

        
        try
        {
            // Wait until the old benchmark has executed its first AML.
            await benchmarkStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(10));

            // Login again in the same browser session while the old
            // benchmark request is still running.
            var newCsrf = await GetCsrfAsync(client);

            using var newLoginResponse = await PostAsync(
                client,
                "/api/auth/login",
                new
                {
                    username = "admin",
                    password = "new-password",
                    database = "InnovatorSolutions"
                },
                newCsrf);

            var newLoginBody =
                await newLoginResponse.Content.ReadAsStringAsync();

            Assert.True(
                newLoginResponse.StatusCode == HttpStatusCode.OK,
                $"New login failed. HTTP " +
                $"{(int)newLoginResponse.StatusCode}: {newLoginBody}");

            using var newLoginJson =
                JsonDocument.Parse(newLoginBody);

            Assert.True(
                newLoginJson.RootElement
                    .GetProperty("authenticated")
                    .GetBoolean(),
                $"New login did not authenticate: {newLoginBody}");
        }
        finally
        {
            // Always release the old request, including when the new
            // login fails. This prevents the test from hanging.
            releaseBenchmark.TrySetResult();
        }

        using var oldResponse = await oldBenchmarkTask;

        var oldResponseText =
            await oldResponse.Content.ReadAsStringAsync();

        // Assert: The old request itself requires reauthentication.
        Assert.True(
            oldResponse.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected HTTP 401 for the old request. " +
            $"Actual response: {oldResponseText}");

        using var oldJson =
            JsonDocument.Parse(oldResponseText);

        var oldResult = oldJson.RootElement;

        Assert.True(
            oldResult.GetProperty("requiresLogin").GetBoolean());

        var jobId = oldResult.GetProperty("jobId").GetGuid();

        Assert.NotEqual(Guid.Empty, jobId);

        // The old request must preserve its own partial benchmark state.
        var statistics = oldResult.GetProperty("statistics");

        Assert.Equal(
            2,
            statistics.GetProperty("totalRequests").GetInt32());

        Assert.Equal(
            1,
            statistics.GetProperty("successCount").GetInt32());

        Assert.Equal(
            1,
            statistics.GetProperty("failureCount").GetInt32());

        // The partial report must remain downloadable.
        using var reportResponse = await client.GetAsync(
            $"/api/report/{jobId:D}/json");

        Assert.Equal(
            HttpStatusCode.OK,
            reportResponse.StatusCode);

        var savedReport = await reportResponse.Content
            .ReadFromJsonAsync<BenchmarkReport>();

        Assert.NotNull(savedReport);
        Assert.Equal(2, savedReport.RawMeasurements.Count);
        Assert.Equal(1, savedReport.Statistics.SuccessCount);
        Assert.Equal(1, savedReport.Statistics.FailureCount);

        // The created item must remain available for later cleanup.
        var environmentId =
            $"{TestFactory.ServerUrl}|InnovatorSolutions";

        var manifest = TestDataManifest.Load(
            Path.Combine(
                factory.StorageDirectory,
                "manifests",
                $"{jobId:N}.json"),
            environmentId);

        var tracked = Assert.Single(manifest.Items);

        Assert.Equal(createdId, tracked.Id);
        Assert.Equal(
            TrackedItemStatus.Active,
            tracked.Status);

        // The old request must not invalidate the newer login session.
        var newSession = await client.GetFromJsonAsync<JsonElement>(
            "/api/auth/session");

        Assert.True(
            newSession.GetProperty("authenticated").GetBoolean(),
            "The old benchmark request invalidated the newer login.");

        Assert.Equal(
            "admin",
            newSession.GetProperty("username").GetString());

        Assert.Equal(
            "InnovatorSolutions",
            newSession.GetProperty("database").GetString());

        // The old request must release its Run lock.
        var coordinator = factory.Services
            .GetRequiredService<RunExecutionCoordinator>();

        Assert.False(coordinator.IsActive(jobId));

        // The old request must not execute additional verification
        // or automatic cleanup with the expired Token A.
        factory.ArasClient.Verify(
            x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                tokenA,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // The new login must not have caused an extra token exchange.
        factory.ArasClient.Verify(
            x => x.GetAccessTokenAsync(
                "admin",
                "new-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Prove that the surviving Session uses Token B by running one
        // read-only Custom AML benchmark after the old request returns.
        var newBenchmarkCsrf =
            await GetCsrfAsync(client);

        using var newBenchmarkResponse = await PostAsync(
            client,
            "/api/benchmark",
            new
            {
                aml =
                    "<AML>" +
                    "<Item type=\"Part\" action=\"get\" select=\"id\" />" +
                    "</AML>",
                iterations = 1,
                concurrency = 1,
                workloadName = "NewSessionTokenTest"
            },
            newBenchmarkCsrf);

        var newBenchmarkBody =
            await newBenchmarkResponse.Content.ReadAsStringAsync();

        Assert.True(
            newBenchmarkResponse.StatusCode == HttpStatusCode.OK,
            $"New benchmark failed. HTTP " +
            $"{(int)newBenchmarkResponse.StatusCode}: " +
            newBenchmarkBody);

        // This directly proves that the surviving Session uses Token B,
        // not the expired Token A.
        factory.ArasClient.Verify(
            x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                tokenB,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static async Task<string> GetCsrfAsync(
        HttpClient client)
    {
        using var response = await client.GetAsync(
            "/api/auth/csrf");

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<JsonElement>();

        return result.GetProperty("requestToken").GetString()
            ?? throw new InvalidOperationException(
                "CSRF token is missing.");
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string url,
        object body,
        string csrf)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            url);

        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Content = JsonContent.Create(body);

        return await client.SendAsync(request);
    }

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        public const string ServerUrl =
            "http://localhost/InnovatorServer/Server/InnovatorServer.aspx";

        public Mock<IArasClient> ArasClient { get; } =
            new(MockBehavior.Strict);

        public string StorageDirectory { get; } = Path.Combine(
            Path.GetTempPath(),
            "ArasPerformanceTool401ApiTests",
            Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Aras:ServerUrl"] = ServerUrl,
                        ["Aras:Database"] = "InnovatorSolutions",
                        ["Aras:DatabaseListUrl"] =
                            "https://aras.test/DBList.aspx",
                        ["Storage:DataDirectory"] = StorageDirectory
                    });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IArasClient>();
                services.AddSingleton(ArasClient.Object);

                services.RemoveAll<IHttpClientFactory>();

                services.AddSingleton<IHttpClientFactory>(
                    new FakeHttpClientFactory());
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing && Directory.Exists(StorageDirectory))
            {
                Directory.Delete(
                    StorageDirectory,
                    recursive: true);
            }
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new FakeDatabaseListHandler());
        }
    }

    private sealed class FakeDatabaseListHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            const string xml =
                "<DBList><DB id=\"InnovatorSolutions\"/></DBList>";

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        xml,
                        Encoding.UTF8,
                        "application/xml")
                });
        }
    }
}
