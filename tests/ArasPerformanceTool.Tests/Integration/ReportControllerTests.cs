using System.Net;
using System.Net.Http.Json;
using ArasPerformanceTool.Api.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ArasPerformanceTool.Tests.Integration;

[Collection("Aras Integration")]
public class ReportControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ReportControllerTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

   [Fact]
    public async Task DownloadReports_ShouldReturnFiles_WhenBenchmarkCompleted()
    {
        // Arrange: Run a small benchmark to generate stored reports
        var request = new StartBenchmarkRequest
        {
            ItemType = "Part",
            Iterations = 1,
            Concurrency = 1,
            WorkloadName = "ReportDownloadIntegrationTest"
        };

        var postResponse = await _client.PostAsJsonAsync("/api/benchmark", request);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        var benchmarkResult = await postResponse.Content.ReadFromJsonAsync<BenchmarkStatusResponse>();
        Assert.NotNull(benchmarkResult);
        var jobId = benchmarkResult.JobId;

        // Act & Assert: Download JSON Report
        var jsonResponse = await _client.GetAsync($"/api/report/{jobId}/json");
        Assert.Equal(HttpStatusCode.OK, jsonResponse.StatusCode);
        Assert.Equal("application/json", jsonResponse.Content.Headers.ContentType?.MediaType);

        var jsonContent = await jsonResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"workloadName\"", jsonContent);
        Assert.Contains("\"statistics\"", jsonContent);
        Assert.Contains("\"rawMeasurements\"", jsonContent);
        Assert.Contains("ReportDownloadIntegrationTest", jsonContent);

        // Act & Assert: Download CSV Report
        var csvResponse = await _client.GetAsync($"/api/report/{jobId}/csv");
        Assert.Equal(HttpStatusCode.OK, csvResponse.StatusCode);
        Assert.Equal("text/csv", csvResponse.Content.Headers.ContentType?.MediaType);

        var csvContent = await csvResponse.Content.ReadAsStringAsync();
        Assert.Contains("Index,WorkloadName,ItemType,ElapsedMs,IsSuccess,ErrorMessage", csvContent);
        Assert.Contains("ReportDownloadIntegrationTest", csvContent);
    }

    [Fact]
    public async Task DownloadJsonReport_ShouldReturnNotFound_WhenJobDoesNotExist()
    {
        // Act: Request non-existent job ID
        var nonExistentId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/report/{nonExistentId}/json");

        // Assert: Verify 404
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadCsvReport_ShouldReturnNotFound_WhenJobDoesNotExist()
    {
        // Act: Request non-existent job ID
        var nonExistentId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/report/{nonExistentId}/csv");

        // Assert: Verify 404
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}