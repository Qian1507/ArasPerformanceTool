using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArasPerformanceTool.Api.DTOs;
using ArasPerformanceTool.Engine.Aras;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Xunit;
using Microsoft.AspNetCore.TestHost;
namespace ArasPerformanceTool.Tests.Integration;

[Collection("Aras Integration")]
public class BenchmarkControllerTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public BenchmarkControllerTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task StartBenchmark_ShouldExecuteBenchmark_AndReturnResults()
    {
        await LoginAsync();

        var request = new StartBenchmarkRequest
        {
            ItemType = "Part",
            Iterations = 2,
            Concurrency = 1,
            WorkloadName = "ControllerIntegrationTest"
        };

        var csrfToken = await GetCsrfTokenAsync();

        var postResponse = await PostWithCsrfAsync(
            "/api/benchmark",
            request,
            csrfToken);

        var responseBody =
            await postResponse.Content.ReadAsStringAsync();

        Assert.True(
            postResponse.StatusCode == HttpStatusCode.OK,
            $"HTTP {(int)postResponse.StatusCode}: {responseBody}");

        var result = await postResponse.Content
            .ReadFromJsonAsync<BenchmarkStatusResponse>();

        Assert.NotNull(result);
        Assert.Equal(BenchmarkStatus.Completed, result.Status);
        Assert.Equal("Part", result.ItemType);
        Assert.Equal(2, result.Iterations);
        Assert.NotNull(result.Statistics);
        Assert.Equal(2, result.Statistics.SuccessCount);
        Assert.True(result.Statistics.AverageMs > 0);

        var getResponse = await _client.GetAsync(
            $"/api/benchmark/{result.JobId}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var retrievedResult = await getResponse.Content
            .ReadFromJsonAsync<BenchmarkStatusResponse>();

        Assert.NotNull(retrievedResult);
        Assert.Equal(result.JobId, retrievedResult.JobId);
        Assert.Equal(BenchmarkStatus.Completed, retrievedResult.Status);
    }

    [Fact]
    public async Task StartBenchmark_ShouldReturnBadRequest_WhenItemTypeIsNotSupported()
    {
        var request = new StartBenchmarkRequest
        {
            ItemType = "UnsupportedType",
            Iterations = 1,
            Concurrency = 1
        };

        var response = await _client.PostAsJsonAsync(
            "/api/benchmark",
            request);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task GetBenchmarkStatus_ShouldReturnNotFound_WhenJobDoesNotExist()
    {
        var nonExistentId = Guid.NewGuid();

        var response = await _client.GetAsync(
            $"/api/benchmark/{nonExistentId}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

   [Fact]
    public async Task StartBenchmark_ShouldReportExecutionError_WhenArasExecutionFails()
    {
        using var factory = new FaultyAmlBenchmarkFactory();

        factory.ArasClient
            .Setup(x => x.GetAccessTokenAsync(
                "admin",
                "correct-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("fake-access-token");

        factory.ArasClient
            .Setup(x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(
                "Simulated Aras execution failure."));

        using var client = factory.CreateClient();

        var csrfToken = await GetCsrfTokenAsync(client);

        var loginResponse = await PostWithCsrfAsync(
            client,
            "/api/auth/login",
            new
            {
                username = "admin",
                password = "correct-password",
                database = "InnovatorSolutions"
            },
            csrfToken);

        Assert.Equal(
            HttpStatusCode.OK,
            loginResponse.StatusCode);

        var benchmarkCsrfToken =
            await GetCsrfTokenAsync(client);

        var benchmarkResponse = await PostWithCsrfAsync(
            client,
            "/api/benchmark",
            new StartBenchmarkRequest
            {
                ItemType = "Part",
                Iterations = 1,
                Concurrency = 1,
                WorkloadName = "ErrorReportingTest"
            },
            benchmarkCsrfToken);

        var responseBody =
            await benchmarkResponse.Content.ReadAsStringAsync();

        Console.WriteLine(
            $"[Faulty Benchmark Status]: {benchmarkResponse.StatusCode}");

        Console.WriteLine(
            $"[Faulty Benchmark Body]: {responseBody}");

        Assert.Equal(
            HttpStatusCode.InternalServerError,
            benchmarkResponse.StatusCode);

        var failedResult = await benchmarkResponse.Content
            .ReadFromJsonAsync<BenchmarkStatusResponse>();

        Assert.NotNull(failedResult);
        Assert.Equal(BenchmarkStatus.Failed, failedResult.Status);
        Assert.NotEqual(Guid.Empty, failedResult.JobId);
        Assert.NotNull(failedResult.CompletedAtUtc);
        Assert.False(
            string.IsNullOrWhiteSpace(failedResult.ErrorMessage));
        
        var getResponse = await client.GetAsync(
            $"/api/benchmark/{failedResult.JobId}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var storedResult = await getResponse.Content
            .ReadFromJsonAsync<BenchmarkStatusResponse>();

        Assert.NotNull(storedResult);
        Assert.Equal(BenchmarkStatus.Failed, storedResult.Status);
        Assert.Equal(failedResult.JobId, storedResult.JobId);
        factory.ArasClient.Verify(x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

   [Fact]
    public async Task StartBenchmark_WithoutSession_ReturnsUnauthorized()
    {
        var request = new StartBenchmarkRequest
        {
            ItemType = "Part",
            Iterations = 1,
            Concurrency = 1
        };

        var csrfToken = await GetCsrfTokenAsync();

        var response = await PostWithCsrfAsync(
            "/api/benchmark",
            request,
            csrfToken);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task StartBenchmark_WithoutCsrfToken_ReturnsBadRequest()
    {
        await LoginAsync();

        var request = new StartBenchmarkRequest
        {
            ItemType = "Part",
            Iterations = 1,
            Concurrency = 1
        };

        var response = await _client.PostAsJsonAsync(
            "/api/benchmark",
            request);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }
    private async Task LoginAsync()
    {
        var configuration = _factory.Services
            .GetRequiredService<IConfiguration>();

        var username = configuration["ARAS_USERNAME"]
            ?? throw new InvalidOperationException(
                "ARAS_USERNAME is not configured for integration tests.");

        var password = configuration["ARAS_PASSWORD"]
            ?? throw new InvalidOperationException(
                "ARAS_PASSWORD is not configured for integration tests.");

        var database = configuration["Aras:Database"]
            ?? throw new InvalidOperationException(
                "Aras:Database is not configured for integration tests.");

        var csrfToken = await GetCsrfTokenAsync();

        var response = await PostWithCsrfAsync(
            "/api/auth/login",
            new
            {
                username,
                password,
                database
            },
            csrfToken);

        var responseBody =
            await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Login failed. HTTP {(int)response.StatusCode}: {responseBody}");

        using var loginJson = JsonDocument.Parse(responseBody);

        Assert.True(
            loginJson.RootElement
                .GetProperty("authenticated")
                .GetBoolean(),
            $"Login response did not authenticate the session: {responseBody}");
    }

    private async Task<string> GetCsrfTokenAsync()
    {
        var response = await _client.GetAsync("/api/auth/csrf");

        response.EnsureSuccessStatusCode();

        var json = await response.Content
            .ReadFromJsonAsync<JsonElement>();

        return json.GetProperty("requestToken").GetString()
            ?? throw new InvalidOperationException(
                "CSRF token was not returned.");
    }

    private async Task<HttpResponseMessage> PostWithCsrfAsync(
        string url,
        object body,
        string csrfToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            url);

        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        request.Content = JsonContent.Create(body);

        return await _client.SendAsync(request);
    }

    private sealed class FaultyAmlBenchmarkFactory
        : WebApplicationFactory<Program>
    {
        // Use a unique storage directory for each test factory
        // to prevent test data from interfering with other tests.
        private readonly string _testStorageDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "ArasPerformanceTool.Tests",
                Guid.NewGuid().ToString("N"));
        public Mock<IArasClient> ArasClient { get; } = new();

        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Aras:ServerUrl"] =
                            "https://aras.test/InnovatorServer",

                        ["Aras:Database"] =
                            "InnovatorSolutions",

                        ["Aras:DatabaseListUrl"] =
                            "https://aras.test/server/DBList.aspx",

                        ["Storage:DataDirectory"] = _testStorageDirectory
                    });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IArasClient>();
                services.AddSingleton(ArasClient.Object);

                var factoryMock = new Mock<IHttpClientFactory>();

                factoryMock
                    .Setup(x => x.CreateClient(
                        It.IsAny<string>()))
                    .Returns(() => new HttpClient(
                        new FakeDatabaseListHandler()));

                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton(factoryMock.Object);
            });
        }
    }

    private static async Task<string> GetCsrfTokenAsync(
        HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/csrf");

        response.EnsureSuccessStatusCode();

        var json = await response.Content
            .ReadFromJsonAsync<JsonElement>();

        return json.GetProperty("requestToken").GetString()
            ?? throw new InvalidOperationException(
                "CSRF token was not returned.");
    }

    private static async Task<HttpResponseMessage> PostWithCsrfAsync(
        HttpClient client,
        string url,
        object body,
        string csrfToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            url);

        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        request.Content = JsonContent.Create(body);

        return await client.SendAsync(request);
    }

    private sealed class FakeDatabaseListHandler
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            const string xml =
                "<DBList><DB id=\"InnovatorSolutions\"/></DBList>";

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    xml,
                    System.Text.Encoding.UTF8,
                    "application/xml")
            };

            return Task.FromResult(response);
        }
    }

}