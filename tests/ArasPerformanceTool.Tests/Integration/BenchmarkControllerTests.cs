using System.Net;
using System.Net.Http.Json;
using ArasPerformanceTool.Api.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ArasPerformanceTool.Tests.Integration;

public class BenchmarkControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public BenchmarkControllerTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task StartBenchmark_ShouldExecuteBenchmark_AndReturnResults()
    {
        // Arrange: Prepare valid benchmark request
        var request = new StartBenchmarkRequest
        {
            ItemType = "Part",
            Iterations = 2,
            Concurrency = 1,
            WorkloadName = "ControllerIntegrationTest"
        };

        // Act: POST /api/benchmark
        var postResponse = await _client.PostAsJsonAsync("/api/benchmark", request);

        var responseBody = await postResponse.Content.ReadAsStringAsync();
        Console.WriteLine($"[HTTP Status]: {postResponse.StatusCode}");
        Console.WriteLine($"[Response Body]: {responseBody}");

        // Assert: Verify successful execution and response structure
        //Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        Assert.True(
            postResponse.StatusCode == HttpStatusCode.OK,
            $"HTTP {(int)postResponse.StatusCode}: {responseBody}");

        var result = await postResponse.Content.ReadFromJsonAsync<BenchmarkStatusResponse>();
        Assert.NotNull(result);
        Assert.Equal(BenchmarkStatus.Completed, result.Status);
        Assert.Equal("Part", result.ItemType);
        Assert.Equal(2, result.Iterations);
        Assert.NotNull(result.Statistics);
        Assert.Equal(2, result.Statistics.SuccessCount);
        Assert.True(result.Statistics.AverageMs > 0);

        // Act & Assert: Retrieve the stored benchmark via GET /api/benchmark/{id}
        var getResponse = await _client.GetAsync($"/api/benchmark/{result.JobId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var retrievedResult = await getResponse.Content.ReadFromJsonAsync<BenchmarkStatusResponse>();
        Assert.NotNull(retrievedResult);
        Assert.Equal(result.JobId, retrievedResult.JobId);
        Assert.Equal(BenchmarkStatus.Completed, retrievedResult.Status);
    }

    [Fact]
    public async Task StartBenchmark_ShouldReturnBadRequest_WhenItemTypeIsNotSupported()
    {
        // Arrange: Prepare request with unsupported ItemType
        var request = new StartBenchmarkRequest
        {
            ItemType = "UnsupportedType",
            Iterations = 1,
            Concurrency = 1
        };

        // Act: POST /api/benchmark
        var response = await _client.PostAsJsonAsync("/api/benchmark", request);

        // Assert: Expect 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetBenchmarkStatus_ShouldReturnNotFound_WhenJobDoesNotExist()
    {
        // Act: Query non-existent Job ID
        var nonExistentId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/benchmark/{nonExistentId}");

        // Assert: Expect 404 Not Found
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    
    [Fact]
    public async Task StartBenchmark_ShouldReportExecutionError_WhenConfigurationIsInvalid()
    {
        // Arrange: Override the Aras server URL to simulate connection failure
        using var customFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Aras:ServerUrl"] =
                                "http://127.0.0.1:59999/InvalidInnovatorServer/Server.aspx"
                        });
                });
            });

        using var faultyClient = customFactory.CreateClient();

        var request = new StartBenchmarkRequest
        {
            ItemType = "Part",
            Iterations = 1,
            Concurrency = 1,
            WorkloadName = "ErrorReportingTest"
        };

        // Act: Start a benchmark with an unreachable AML server
        var response = await faultyClient.PostAsJsonAsync(
            "/api/benchmark",
            request);

        // Assert: The API reports execution failure
        Assert.Equal(
            HttpStatusCode.InternalServerError,
            response.StatusCode);

        var failedResult = await response.Content
            .ReadFromJsonAsync<BenchmarkStatusResponse>();

        Assert.NotNull(failedResult);
        Assert.Equal(BenchmarkStatus.Failed, failedResult.Status);

        // Verify the failed job has an identifier and completion time
        Assert.NotEqual(Guid.Empty, failedResult.JobId);
        Assert.NotNull(failedResult.CompletedAtUtc);

        // Failed requests should provide a safe error message
        Assert.False(
            string.IsNullOrWhiteSpace(failedResult.ErrorMessage));

        // Internal connection details should not be exposed
        Assert.DoesNotContain(
            "127.0.0.1:59999",
            failedResult.ErrorMessage);

        // Assert: The failed job can still be retrieved by its ID
        var getResponse = await faultyClient.GetAsync(
            $"/api/benchmark/{failedResult.JobId}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var storedResult = await getResponse.Content
            .ReadFromJsonAsync<BenchmarkStatusResponse>();

        Assert.NotNull(storedResult);
        Assert.Equal(BenchmarkStatus.Failed, storedResult.Status);
        Assert.Equal(failedResult.JobId, storedResult.JobId);
    }

}