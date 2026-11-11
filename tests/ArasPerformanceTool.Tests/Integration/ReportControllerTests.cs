using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArasPerformanceTool.Api.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace ArasPerformanceTool.Tests.Integration;

[Collection("Aras Integration")]
public class ReportControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ReportControllerTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

   [Fact]
    public async Task DownloadReports_ShouldReturnFiles_WhenBenchmarkCompleted()
    {
        await LoginAsync();
        // Arrange: Run a small benchmark to generate stored reports
        var request = new StartBenchmarkRequest
        {
            ItemType = "Part",
            Iterations = 1,
            Concurrency = 1,
            WorkloadName = "ReportDownloadIntegrationTest"
        };

        var csrfToken = await GetCsrfTokenAsync();

        var postResponse = await PostWithCsrfAsync(
            "/api/benchmark",
            request,
            csrfToken);
       
       var postResponseBody =
            await postResponse.Content.ReadAsStringAsync();

        Assert.True(
            postResponse.StatusCode == HttpStatusCode.OK,
            $"Expected HTTP 200, got {(int)postResponse.StatusCode} " +
            $"({postResponse.StatusCode}).{Environment.NewLine}" +
            $"Response body:{Environment.NewLine}{postResponseBody}");

        var benchmarkResult = await postResponse.Content
            .ReadFromJsonAsync<BenchmarkStatusResponse>();

        Assert.NotNull(benchmarkResult);
        Assert.Equal(BenchmarkStatus.Completed, benchmarkResult.Status);
        Assert.NotEqual(Guid.Empty, benchmarkResult.JobId);

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
}