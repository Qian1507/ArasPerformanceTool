
using System.Net;
using System.Text.Json;
using ArasPerformanceTool.Engine.Benchmarking;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ArasPerformanceTool.Tests.Integration;

public sealed class PendingCleanupApiTests : IDisposable
{
    private const string EnvironmentId =
        "http://localhost/InnovatorServer/Server/InnovatorServer.aspx|InnovatorSolutions";

    private readonly string _directory;

    public PendingCleanupApiTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "ArasPerformanceToolApiTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(
            Path.Combine(_directory, "manifests"));
    }

    [Fact]
    public async Task GetPendingCleanup_WhenActiveItemExists_ReturnsRunWithoutFilePath()
    {
        // Arrange: Create a local manifest containing one active item.
        var runId = Guid.NewGuid();
        var manifestPath = Path.Combine(
            _directory,
            "manifests",
            $"{runId:N}.json");

        var createdAt = DateTime.UtcNow;

        var manifest = new
        {
            SchemaVersion = 1,
            RunId = runId.ToString("N"),
            EnvironmentId,
            CreatedAtUtc = createdAt,
            Items = new[]
            {
                new
                {
                    ItemType = "Part",
                    Id = Guid.NewGuid().ToString("N").ToUpperInvariant(),
                    CreatedAtUtc = createdAt,
                    Status = (int)TrackedItemStatus.Active,
                    Source = 0,
                    ConfigId = (string?)null,
                    ParentItemId = (string?)null,
                    LastError = (string?)null
                }
            }
        };

        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest));

        var before = File.ReadAllBytes(manifestPath);

        // Override configuration so the API reads only
        // this test's temporary directory.
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Aras:ServerUrl"] =
                                "http://localhost/InnovatorServer/Server/InnovatorServer.aspx",
                            ["Aras:Database"] = "InnovatorSolutions",
                            ["Storage:DataDirectory"] = _directory
                        });
                });
            });

        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            "/api/Benchmark/pending-cleanup");

        var responseText = await response.Content.ReadAsStringAsync();

        // Assert: HTTP 200
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;

        // Exactly one pending run.
        var pendingRuns = root.GetProperty("pendingRuns");
        var pending = Assert.Single(pendingRuns.EnumerateArray());

        Assert.Equal(
            runId.ToString("N"),
            pending.GetProperty("runId").GetString());

        Assert.Equal(
            1,
            pending.GetProperty("remainingCount").GetInt32());

        Assert.Equal(
            EnvironmentId,
            pending.GetProperty("environmentId").GetString());

        // No scan errors.
        Assert.Empty(
            root.GetProperty("errors").EnumerateArray());

        // Local file paths must not be exposed.
        Assert.False(
            pending.TryGetProperty("manifestPath", out _));

        Assert.DoesNotContain(
            manifestPath,
            responseText,
            StringComparison.OrdinalIgnoreCase);

        // API must not change persisted JSON.
        var after = File.ReadAllBytes(manifestPath);

        Assert.Equal(before, after);
    }


    [Fact]
    public async Task GetPendingCleanup_WhenManifestIsCorrupted_DoesNotExposeLocalPaths()
    {
        // Arrange: Create a corrupted manifest.
        var manifestDirectory = Path.Combine(
            _directory,
            "manifests");

        var corruptedPath = Path.Combine(
            manifestDirectory,
            "corrupted.json");

        File.WriteAllText(
            corruptedPath,
            "{ invalid json");

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Aras:ServerUrl"] =
                                "http://localhost/InnovatorServer/Server/InnovatorServer.aspx",
                            ["Aras:Database"] = "InnovatorSolutions",
                            ["Storage:DataDirectory"] = _directory
                        });
                });
            });

        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            "/api/Benchmark/pending-cleanup");

        var responseText =
            await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;

        Assert.Empty(
            root.GetProperty("pendingRuns").EnumerateArray());

        var errors = root.GetProperty("errors");
        var error = Assert.Single(errors.EnumerateArray());

        Assert.Equal(
            "corrupted.json",
            error.GetProperty("fileName").GetString());

        // No local filesystem paths in the API response.
        Assert.False(
            error.TryGetProperty("manifestPath", out _));

        Assert.DoesNotContain(
            corruptedPath,
            responseText,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            _directory,
            responseText,
            StringComparison.OrdinalIgnoreCase);

        // Scanning must not modify the corrupted file.
        Assert.Equal(
            "{ invalid json",
            File.ReadAllText(corruptedPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
