
using System.Net;
using System.Text.Json;
using ArasPerformanceTool.Engine.Benchmarking;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;
using ArasPerformanceTool.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ArasPerformanceTool.Engine.Aras;
using Microsoft.AspNetCore.TestHost;
using System.Xml.Linq;

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

    [Fact]
    public async Task PostManualCleanup_WhenManifestDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var runId = Guid.NewGuid();

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
        using var response = await client.PostAsync(
            $"/api/Benchmark/pending-cleanup/{runId:N}/cleanup",
            content: null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var responseText = await response.Content.ReadAsStringAsync();

        Assert.Contains(
            "Pending cleanup run was not found",
            responseText);

        // No manifest should be created by a missing-run request.
        Assert.Empty(Directory.GetFiles(
            Path.Combine(_directory, "manifests"),
            "*.json"));
        
        // The missing-run request must release its execution lock.
        var coordinator = factory.Services
            .GetRequiredService<RunExecutionCoordinator>();

        var acquired = coordinator.TryAcquire(runId);

        try
        {
            Assert.True(
                acquired,
                "The missing-run request did not release its Run lock.");
        }
        finally
        {
            if (acquired)
            {
                coordinator.Release(runId);
            }
        }



    }

    [Fact]
    public async Task PostManualCleanup_WhenEnvironmentMismatch_RejectsRequest()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        var manifestPath = Path.Combine(
            _directory,
            "manifests",
            $"{runId:N}.json");

        // Create a manifest belonging to a different Aras environment.
        var manifest = new
        {
            SchemaVersion = 1,
            RunId = runId.ToString("N"),
            EnvironmentId = "http://different-server/InnovatorServer|OtherDatabase",
            CreatedAtUtc = createdAt,
            Items = new[]
            {
                new
                {
                    ItemType = "Part",
                    Id = Guid.NewGuid().ToString("N").ToUpperInvariant(),
                    CreatedAtUtc = createdAt,
                    Status = (int)TrackedItemStatus.Active,
                    Source = (int)TestItemSource.Standard,
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

        // No Aras requests are expected for an environment mismatch.
        var arasClientMock = new Mock<IArasClient>(MockBehavior.Strict);

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

                // Replace the real Aras client in the test host.
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IArasClient>();

                    services.AddSingleton<IArasClient>(
                        arasClientMock.Object);
                });
            });

        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(
            $"/api/Benchmark/pending-cleanup/{runId:N}/cleanup",
            content: null);

        var responseText = await response.Content.ReadAsStringAsync();

        // Assert: The request must be rejected.
        Assert.Equal(
            HttpStatusCode.InternalServerError,
            response.StatusCode);

        Assert.Contains(
            "Unable to complete manual cleanup",
            responseText,
            StringComparison.OrdinalIgnoreCase);

        // The manifest must remain unchanged.
        var after = File.ReadAllBytes(manifestPath);

        Assert.Equal(before, after);

        // The API must not expose local filesystem paths.
        Assert.DoesNotContain(
            manifestPath,
            responseText,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            _directory,
            responseText,
            StringComparison.OrdinalIgnoreCase);

        // Verify that the execution lock was released.
        var coordinator = factory.Services
            .GetRequiredService<RunExecutionCoordinator>();

        var acquired = coordinator.TryAcquire(runId);

        try
        {
            Assert.True(
                acquired,
                "The rejected cleanup request did not release its Run lock.");
        }
        finally
        {
            if (acquired)
            {
                coordinator.Release(runId);
            }
        }
        // Environment validation must happen before authentication
        // or any AML request.
        arasClientMock.VerifyNoOtherCalls();

    }

    
    [Fact]
    public async Task PostManualCleanup_WhenNoUnresolvedItems_ReturnsCompletedWithoutConnectingToAras()
    {
        // Arrange: Create a completed manifest with no unresolved items.
        var runId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        var manifestPath = Path.Combine(
            _directory,
            "manifests",
            $"{runId:N}.json");

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
                    Status = (int)TrackedItemStatus.Deleted,
                    Source = (int)TestItemSource.Standard,
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

        // No authentication or AML requests should be made.
        var arasClientMock = new Mock<IArasClient>(MockBehavior.Strict);

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

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IArasClient>();

                    services.AddSingleton<IArasClient>(
                        arasClientMock.Object);
                });
            });

        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(
            $"/api/Benchmark/pending-cleanup/{runId:N}/cleanup",
            content: null);

        var responseText = await response.Content.ReadAsStringAsync();

        // Assert: The run is already completed.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;

        Assert.Equal(
            runId.ToString("D"),
            root.GetProperty("runId").GetString());

        Assert.Equal(0, root.GetProperty("found").GetInt32());
        Assert.Equal(0, root.GetProperty("deleted").GetInt32());
        Assert.Equal(0, root.GetProperty("alreadyMissing").GetInt32());
        Assert.Equal(0, root.GetProperty("failed").GetInt32());
        Assert.Equal(0, root.GetProperty("remaining").GetInt32());

        Assert.True(root.GetProperty("completed").GetBoolean());

        // Manifest must remain unchanged.
        var after = File.ReadAllBytes(manifestPath);
        Assert.Equal(before, after);

        // No authentication or AML calls.
        arasClientMock.VerifyNoOtherCalls();

        // The execution lock must be released.
        var coordinator = factory.Services
            .GetRequiredService<RunExecutionCoordinator>();

        var acquired = coordinator.TryAcquire(runId);

        try
        {
            Assert.True(
                acquired,
                "The completed-run request did not release its Run lock.");
        }
        finally
        {
            if (acquired)
            {
                coordinator.Release(runId);
            }
        }
    }

    
   
    [Fact]
    public async Task PostManualCleanup_WhenRunIsAlreadyAcquired_ReturnsConflict()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        var manifestPath = Path.Combine(
            _directory,
            "manifests",
            $"{runId:N}.json");

        // Create a manifest containing one unresolved test item.
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
                    Source = (int)TestItemSource.Standard,
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

        // No authentication or AML requests are allowed.
        var arasClientMock = new Mock<IArasClient>(MockBehavior.Strict);

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

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IArasClient>();

                    services.AddSingleton<IArasClient>(
                        arasClientMock.Object);
                });
            });

        using var client = factory.CreateClient();

        // Acquire the same RunId before sending the POST request.
        var coordinator = factory.Services
            .GetRequiredService<RunExecutionCoordinator>();

        Assert.True(
            coordinator.TryAcquire(runId),
            "Failed to acquire the test Run lock.");

        try
        {
            // Act
            using var response = await client.PostAsync(
                $"/api/Benchmark/pending-cleanup/{runId:N}/cleanup",
                content: null);

            var responseText =
                await response.Content.ReadAsStringAsync();

            // Assert: The request must be rejected.
            Assert.Equal(
                HttpStatusCode.Conflict,
                response.StatusCode);

            Assert.Contains(
                "already executing or being cleaned up",
                responseText,
                StringComparison.OrdinalIgnoreCase);

            // Manifest must not be modified.
            var after = File.ReadAllBytes(manifestPath);
            Assert.Equal(before, after);

            // No authentication or AML calls.
            arasClientMock.VerifyNoOtherCalls();

            // The rejected POST must not release another execution's lock.
            Assert.True(coordinator.IsActive(runId));

            Assert.False(
                coordinator.TryAcquire(runId),
                "The rejected POST unexpectedly released the Run lock.");
        }
        finally
        {
            // Release only the lock acquired by this test.
            coordinator.Release(runId);
        }

        // Verify the Run can be acquired again after test cleanup.
        Assert.True(coordinator.TryAcquire(runId));

        coordinator.Release(runId);
    }


    
    [Fact]
    public async Task PostManualCleanup_WhenItemExists_DeletesAndPersistsResolvedState()
    {
        // Arrange: Create a persistent manifest with one active item.
        var runId = Guid.NewGuid();
        var itemId = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var createdAt = DateTime.UtcNow;

        var manifestPath = Path.Combine(
            _directory,
            "manifests",
            $"{runId:N}.json");

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
                    Id = itemId,
                    CreatedAtUtc = createdAt,
                    Status = (int)TrackedItemStatus.Active,
                    Source = (int)TestItemSource.Standard,
                    ConfigId = (string?)null,
                    ParentItemId = (string?)null,
                    LastError = (string?)null
                }
            }
        };

        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest));

        var arasClientMock = new Mock<IArasClient>(MockBehavior.Strict);

        var executedActions = new List<string>();

        // Simulate OAuth authentication.
        arasClientMock
            .Setup(x => x.GetAccessTokenAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-access-token");

        // Simulate Read -> Delete -> Read.
        arasClientMock
            .Setup(x => x.ExecuteAmlAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                string serverUrl,
                string accessToken,
                string aml,
                CancellationToken cancellationToken) =>
            {
                Assert.Equal(
                    "http://localhost/InnovatorServer/Server/InnovatorServer.aspx",
                    serverUrl);
                Assert.Equal("test-access-token", accessToken);

                var document = XDocument.Parse(aml);
                var item = Assert.Single(
                    document.Descendants("Item"));

                var action = (string?)item.Attribute("action");
                var targetId = (string?)item.Attribute("id");
                var itemType = (string?)item.Attribute("type");

                Assert.Equal("Part", itemType);
                Assert.Equal(itemId, targetId);

                Assert.NotNull(action);
                executedActions.Add(action);

                if (action == "get" && executedActions.Count == 1)
                {
                    // The item exists before deletion.
                    return $"""
                        <AML>
                        <Item type="Part" id="{itemId}" />
                        </AML>
                        """;
                }

                if (action == "delete" && executedActions.Count == 2)
                {
                    // Aras accepts the Delete operation.
                    return """
                        <AML>
                        <Result>OK</Result>
                        </AML>
                        """;
                }

                if (action == "get" && executedActions.Count == 3)
                {
                    // Aras confirms that the item no longer exists.
                    return """
                        <SOAP-ENV:Envelope xmlns:SOAP-ENV="http://schemas.xmlsoap.org/soap/envelope/">
                        <SOAP-ENV:Body>
                            <SOAP-ENV:Fault>
                            <faultcode>0</faultcode>
                            <faultstring>No items found</faultstring>
                            </SOAP-ENV:Fault>
                        </SOAP-ENV:Body>
                        </SOAP-ENV:Envelope>
                        """;
                }

                throw new InvalidOperationException(
                    $"Unexpected AML action or order: {action}");
            });

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
                            ["Storage:DataDirectory"] = _directory,
                            ["ARAS_USERNAME"] = "test-user",
                            ["ARAS_PASSWORD"] = "test-password"
                        });
                });

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IArasClient>();

                    services.AddSingleton<IArasClient>(
                        arasClientMock.Object);
                });
            });

        using var client = factory.CreateClient();

        // Act: Execute manual cleanup.
        using var response = await client.PostAsync(
            $"/api/Benchmark/pending-cleanup/{runId:N}/cleanup",
            content: null);

        var responseText =
            await response.Content.ReadAsStringAsync();

        // Assert: Cleanup completed successfully.
        Assert.True(
            response.IsSuccessStatusCode,
            $"Unexpected response: {response.StatusCode}. {responseText}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var responseDocument = JsonDocument.Parse(responseText);
        var root = responseDocument.RootElement;

        Assert.Equal(1, root.GetProperty("found").GetInt32());
        Assert.Equal(1, root.GetProperty("deleted").GetInt32());
        Assert.Equal(0, root.GetProperty("alreadyMissing").GetInt32());
        Assert.Equal(0, root.GetProperty("failed").GetInt32());
        Assert.Equal(0, root.GetProperty("remaining").GetInt32());
        Assert.True(root.GetProperty("completed").GetBoolean());

        // Verify the exact AML operation sequence.
        Assert.Equal(
            new[] { "get", "delete", "get" },
            executedActions);

        // Verify authentication was called once.
        arasClientMock.Verify(x => x.GetAccessTokenAsync(
            "test-user",
            "test-password",
            It.IsAny<CancellationToken>()),
            Times.Once);

        // Exactly three AML requests: Read, Delete, Read.
        arasClientMock.Verify(x => x.ExecuteAmlAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        arasClientMock.VerifyNoOtherCalls();

        // Reload from disk: do not rely on an in-memory manifest.
        var finalManifest = TestDataManifest.Load(
            manifestPath,
            EnvironmentId);

        Assert.Empty(finalManifest.UnresolvedItems);

        var trackedItem = Assert.Single(finalManifest.Items);

        Assert.Equal(
            TrackedItemStatus.Deleted,
            trackedItem.Status);

        Assert.Equal(itemId, trackedItem.Id);

        // The Run lock must be released after cleanup.
        var coordinator = factory.Services
            .GetRequiredService<RunExecutionCoordinator>();

        var acquired = coordinator.TryAcquire(runId);

        try
        {
            Assert.True(
                acquired,
                "Manual cleanup did not release its Run lock.");
        }
        finally
        {
            if (acquired)
            {
                coordinator.Release(runId);
            }
        }
    }


    
[Fact]
public async Task PostManualCleanup_WhenDeletePendingItemIsMissing_MarksAlreadyMissingWithoutDeleting()
{
    // Arrange: The previous cleanup may have deleted this item,
    // but its final state was not persisted.
    var runId = Guid.NewGuid();
    var itemId = Guid.NewGuid().ToString("N").ToUpperInvariant();
    var createdAt = DateTime.UtcNow;

    var manifestPath = Path.Combine(
        _directory,
        "manifests",
        $"{runId:N}.json");

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
                Id = itemId,
                CreatedAtUtc = createdAt,
                Status = (int)TrackedItemStatus.DeletePending,
                Source = (int)TestItemSource.Standard,
                ConfigId = (string?)null,
                ParentItemId = (string?)null,
                LastError = (string?)null
            }
        }
    };

    File.WriteAllText(
        manifestPath,
        JsonSerializer.Serialize(manifest));

    var arasClientMock = new Mock<IArasClient>(MockBehavior.Strict);

    var actions = new List<string>();

    // Mock authentication.
    arasClientMock
        .Setup(x => x.GetAccessTokenAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync("test-access-token");

    // Aras confirms the item is already missing.
    arasClientMock
        .Setup(x => x.ExecuteAmlAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync((
            string serverUrl,
            string accessToken,
            string aml,
            CancellationToken cancellationToken) =>
        {
            Assert.Equal(
                "http://localhost/InnovatorServer/Server/InnovatorServer.aspx",
                serverUrl);

            Assert.Equal("test-access-token", accessToken);

            var document = XDocument.Parse(aml);

            var item = Assert.Single(
                document.Descendants("Item"));

            Assert.Equal(
                "Part",
                (string?)item.Attribute("type"));

            Assert.Equal(
                itemId,
                (string?)item.Attribute("id"));

            var action = (string?)item.Attribute("action");

            Assert.NotNull(action);
            actions.Add(action);

            // No Delete operation should ever be sent.
            Assert.Equal("get", action);

            return """
                <SOAP-ENV:Envelope xmlns:SOAP-ENV="http://schemas.xmlsoap.org/soap/envelope/">
                  <SOAP-ENV:Body>
                    <SOAP-ENV:Fault>
                      <faultcode>0</faultcode>
                      <faultstring>No items found</faultstring>
                    </SOAP-ENV:Fault>
                  </SOAP-ENV:Body>
                </SOAP-ENV:Envelope>
                """;
        });

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
                        ["Storage:DataDirectory"] = _directory,
                        ["ARAS_USERNAME"] = "test-user",
                        ["ARAS_PASSWORD"] = "test-password"
                    });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IArasClient>();

                services.AddSingleton<IArasClient>(
                    arasClientMock.Object);
            });
        });

    using var client = factory.CreateClient();

    // Act
    using var response = await client.PostAsync(
        $"/api/Benchmark/pending-cleanup/{runId:N}/cleanup",
        content: null);

    var responseText =
        await response.Content.ReadAsStringAsync();

    // Assert: Cleanup completed without another Delete.
    Assert.Equal(
        HttpStatusCode.OK,
        response.StatusCode);

    using var responseDocument = JsonDocument.Parse(responseText);
    var root = responseDocument.RootElement;

    Assert.Equal(1, root.GetProperty("found").GetInt32());
    Assert.Equal(0, root.GetProperty("deleted").GetInt32());
    Assert.Equal(1, root.GetProperty("alreadyMissing").GetInt32());
    Assert.Equal(0, root.GetProperty("failed").GetInt32());
    Assert.Equal(0, root.GetProperty("remaining").GetInt32());

    Assert.True(root.GetProperty("completed").GetBoolean());

    // Exactly one GET, no DELETE.
    Assert.Equal(new[] { "get" }, actions);

    arasClientMock.Verify(x => x.GetAccessTokenAsync(
        "test-user",
        "test-password",
        It.IsAny<CancellationToken>()),
        Times.Once);

    arasClientMock.Verify(x => x.ExecuteAmlAsync(
        It.IsAny<string>(),
        It.IsAny<string>(),
        It.IsAny<string>(),
        It.IsAny<CancellationToken>()),
        Times.Once);

    arasClientMock.VerifyNoOtherCalls();

    // Reload the manifest from disk and verify the persisted state.
    var finalManifest = TestDataManifest.Load(
        manifestPath,
        EnvironmentId);

    var trackedItem = Assert.Single(finalManifest.Items);

    Assert.Equal(
        TrackedItemStatus.AlreadyMissing,
        trackedItem.Status);

    Assert.Empty(finalManifest.UnresolvedItems);

    // The Run lock must be released.
    var coordinator = factory.Services
        .GetRequiredService<RunExecutionCoordinator>();

    var acquired = coordinator.TryAcquire(runId);

    try
    {
        Assert.True(
            acquired,
            "Manual cleanup did not release the Run lock.");
    }
    finally
    {
        if (acquired)
        {
            coordinator.Release(runId);
        }
    }
}



[Fact]
public async Task PostManualCleanup_WhenOneItemFails_ContinuesAndPreservesFailure()
{
    // Arrange: Two tracked items in the same persistent manifest.
    var runId = Guid.NewGuid();
    var failedItemId = Guid.NewGuid().ToString("N").ToUpperInvariant();
    var successfulItemId = Guid.NewGuid().ToString("N").ToUpperInvariant();
    var createdAt = DateTime.UtcNow;

    var manifestPath = Path.Combine(
        _directory,
        "manifests",
        $"{runId:N}.json");

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
                Id = failedItemId,
                CreatedAtUtc = createdAt,
                Status = (int)TrackedItemStatus.Active,
                Source = (int)TestItemSource.Standard,
                ConfigId = (string?)null,
                ParentItemId = (string?)null,
                LastError = (string?)null
            },
            new
            {
                ItemType = "Part",
                Id = successfulItemId,
                CreatedAtUtc = createdAt.AddSeconds(-1),
                Status = (int)TrackedItemStatus.Active,
                Source = (int)TestItemSource.Standard,
                ConfigId = (string?)null,
                ParentItemId = (string?)null,
                LastError = (string?)null
            }
        }
    };

    File.WriteAllText(
        manifestPath,
        JsonSerializer.Serialize(manifest));

    // Strict Mock: no real Aras connection is allowed.
    var arasClientMock = new Mock<IArasClient>(MockBehavior.Strict);

    var executedActions = new List<(string Id, string Action)>();

    arasClientMock
        .Setup(x => x.GetAccessTokenAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync("test-access-token");

    arasClientMock
        .Setup(x => x.ExecuteAmlAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync((
            string serverUrl,
            string accessToken,
            string aml,
            CancellationToken cancellationToken) =>
        {
            Assert.Equal(
                "http://localhost/InnovatorServer/Server/InnovatorServer.aspx",
                serverUrl);

            Assert.Equal("test-access-token", accessToken);

            var document = XDocument.Parse(aml);

            var item = Assert.Single(
                document.Descendants(),
                x => x.Name.LocalName == "Item");
            var itemType = (string?)item.Attribute("type");
            var targetId = (string?)item.Attribute("id");
            var action = (string?)item.Attribute("action");

            Assert.Equal("Part", itemType);
            Assert.NotNull(targetId);
            Assert.NotNull(action);

            executedActions.Add((targetId!, action!));

            if (targetId == failedItemId)
            {
                // First item: Read returns a simulated Aras SOAP fault.
                Assert.Equal("get", action);

                return """
                    <SOAP-ENV:Envelope xmlns:SOAP-ENV="http://schemas.xmlsoap.org/soap/envelope/">
                      <SOAP-ENV:Body>
                        <SOAP-ENV:Fault>
                          <faultcode>1</faultcode>
                          <faultstring>Simulated read failure</faultstring>
                        </SOAP-ENV:Fault>
                      </SOAP-ENV:Body>
                    </SOAP-ENV:Envelope>
                    """;
            }

            if (targetId == successfulItemId)
            {
                if (action == "get")
                {
                    // Before Delete: the item exists.
                    // After Delete: the item is missing.
                    var deleteAlreadyExecuted = executedActions.Any(x =>
                        x.Id == successfulItemId &&
                        x.Action == "delete");

                    if (!deleteAlreadyExecuted)
                    {
                        return $"""
                            <AML>
                              <Item type="Part" id="{successfulItemId}" />
                            </AML>
                            """;
                    }

                    return """
                        <SOAP-ENV:Envelope xmlns:SOAP-ENV="http://schemas.xmlsoap.org/soap/envelope/">
                          <SOAP-ENV:Body>
                            <SOAP-ENV:Fault>
                              <faultcode>0</faultcode>
                              <faultstring>No items found</faultstring>
                            </SOAP-ENV:Fault>
                          </SOAP-ENV:Body>
                        </SOAP-ENV:Envelope>
                        """;
                }

                if (action == "delete")
                {
                    return """
                        <AML>
                          <Result>OK</Result>
                        </AML>
                        """;
                }
            }

            throw new InvalidOperationException(
                $"Unexpected AML request: {targetId}, {action}");
        });

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
                        ["Storage:DataDirectory"] = _directory,
                        ["ARAS_USERNAME"] = "test-user",
                        ["ARAS_PASSWORD"] = "test-password"
                    });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IArasClient>();

                services.AddSingleton<IArasClient>(
                    arasClientMock.Object);
            });
        });

    using var client = factory.CreateClient();

    // Act: Run manual cleanup.
    using var response = await client.PostAsync(
        $"/api/Benchmark/pending-cleanup/{runId:N}/cleanup",
        content: null);

    var responseText = await response.Content.ReadAsStringAsync();

    // Assert: HTTP 200 means the cleanup attempt finished,
    // not that every item was successfully resolved.
    Assert.Equal(
        HttpStatusCode.OK,
        response.StatusCode);

    using var responseDocument = JsonDocument.Parse(responseText);
    var root = responseDocument.RootElement;

    Assert.Equal(2, root.GetProperty("found").GetInt32());
    Assert.Equal(1, root.GetProperty("deleted").GetInt32());
    Assert.Equal(0, root.GetProperty("alreadyMissing").GetInt32());
    Assert.Equal(1, root.GetProperty("failed").GetInt32());
    Assert.Equal(1, root.GetProperty("remaining").GetInt32());
    Assert.False(root.GetProperty("completed").GetBoolean());

    // The failed item's Read must not be followed by Delete.
    Assert.Equal(
        new[]
        {
            (failedItemId, "get"),
            (successfulItemId, "get"),
            (successfulItemId, "delete"),
            (successfulItemId, "get")
        },
        executedActions);

    arasClientMock.Verify(x => x.GetAccessTokenAsync(
        "test-user",
        "test-password",
        It.IsAny<CancellationToken>()),
        Times.Once);

    arasClientMock.Verify(x => x.ExecuteAmlAsync(
        It.IsAny<string>(),
        It.IsAny<string>(),
        It.IsAny<string>(),
        It.IsAny<CancellationToken>()),
        Times.Exactly(4));

    arasClientMock.VerifyNoOtherCalls();

    // Reload from disk to verify persistence.
    var finalManifest = TestDataManifest.Load(
        manifestPath,
        EnvironmentId);

    var failedItem = Assert.Single(
    finalManifest.Items,
    x => x.Id == failedItemId);

    Assert.Equal(
        TrackedItemStatus.CleanupFailed,
        failedItem.Status);
    
    Assert.Contains(
    "Aras Read faultcode: 1",
    failedItem.LastError ?? string.Empty);

    var successfulItem = Assert.Single(
    finalManifest.Items,
    x => x.Id == successfulItemId);


    Assert.Equal(
        TrackedItemStatus.Deleted,
        successfulItem.Status);

    // Only the failed item remains unresolved.
    var unresolved = Assert.Single(
        finalManifest.UnresolvedItems);

    Assert.Equal(failedItemId, unresolved.Id);

    // Verify the Run lock was released.
    var coordinator = factory.Services
        .GetRequiredService<RunExecutionCoordinator>();

    var acquired = coordinator.TryAcquire(runId);

    try
    {
        Assert.True(
            acquired,
            "Manual cleanup did not release its Run lock.");
    }
    finally
    {
        if (acquired)
        {
            coordinator.Release(runId);
        }
    }
}



[Fact]
public async Task PostManualCleanup_WhenRetried_CleansOnlyRemainingFailedItem()
{
    // Arrange: Two tracked items in one persistent Run.
    var runId = Guid.NewGuid();
    var failedItemId = Guid.NewGuid().ToString("N").ToUpperInvariant();
    var successfulItemId = Guid.NewGuid().ToString("N").ToUpperInvariant();
    var createdAt = DateTime.UtcNow;

    var manifestPath = Path.Combine(
        _directory,
        "manifests",
        $"{runId:N}.json");

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
                Id = failedItemId,
                CreatedAtUtc = createdAt,
                Status = (int)TrackedItemStatus.Active,
                Source = (int)TestItemSource.Standard,
                ConfigId = (string?)null,
                ParentItemId = (string?)null,
                LastError = (string?)null
            },
            new
            {
                ItemType = "Part",
                Id = successfulItemId,
                CreatedAtUtc = createdAt.AddSeconds(-1),
                Status = (int)TrackedItemStatus.Active,
                Source = (int)TestItemSource.Standard,
                ConfigId = (string?)null,
                ParentItemId = (string?)null,
                LastError = (string?)null
            }
        }
    };

    File.WriteAllText(
        manifestPath,
        JsonSerializer.Serialize(manifest));

    var arasClientMock = new Mock<IArasClient>(MockBehavior.Strict);

    // Each request represents a separate cleanup attempt.
    var attempt = 1;

    var actions = new List<(int Attempt, string Id, string Action)>();

    // Track which items were deleted in the current simulation.
    var deletedItems = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase);

    arasClientMock
        .Setup(x => x.GetAccessTokenAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync("test-access-token");

    arasClientMock
        .Setup(x => x.ExecuteAmlAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync((
            string serverUrl,
            string accessToken,
            string aml,
            CancellationToken cancellationToken) =>
        {
            Assert.Equal(
                "http://localhost/InnovatorServer/Server/InnovatorServer.aspx",
                serverUrl);

            Assert.Equal("test-access-token", accessToken);

            var document = XDocument.Parse(aml);

            var item = Assert.Single(
                document.Descendants(),
                x => x.Name.LocalName == "Item");

            var itemType = (string?)item.Attribute("type");
            var targetId = (string?)item.Attribute("id");
            var action = (string?)item.Attribute("action");

            Assert.Equal("Part", itemType);
            Assert.NotNull(targetId);
            Assert.NotNull(action);

            actions.Add((attempt, targetId!, action!));

            // Attempt 1: Item A fails during its initial Read.
            if (attempt == 1 && targetId == failedItemId)
            {
                Assert.Equal("get", action);

                return """
                    <SOAP-ENV:Envelope xmlns:SOAP-ENV="http://schemas.xmlsoap.org/soap/envelope/">
                      <SOAP-ENV:Body>
                        <SOAP-ENV:Fault>
                          <faultcode>1</faultcode>
                          <faultstring>Simulated read failure</faultstring>
                        </SOAP-ENV:Fault>
                      </SOAP-ENV:Body>
                    </SOAP-ENV:Envelope>
                    """;
            }

            // Attempt 2 must never touch Item B.
            if (attempt == 2 && targetId == successfulItemId)
            {
                throw new InvalidOperationException(
                    "Already deleted Item B was processed again.");
            }

            // Attempt 3 must never call Aras.
            if (attempt == 3)
            {
                throw new InvalidOperationException(
                    "Completed Run unexpectedly called Aras.");
            }

            if (action == "get")
            {
                if (deletedItems.Contains(targetId!))
                {
                    return """
                        <SOAP-ENV:Envelope xmlns:SOAP-ENV="http://schemas.xmlsoap.org/soap/envelope/">
                          <SOAP-ENV:Body>
                            <SOAP-ENV:Fault>
                              <faultcode>0</faultcode>
                              <faultstring>No items found</faultstring>
                            </SOAP-ENV:Fault>
                          </SOAP-ENV:Body>
                        </SOAP-ENV:Envelope>
                        """;
                }

                return $"""
                    <AML>
                      <Item type="Part" id="{targetId}" />
                    </AML>
                    """;
            }

            if (action == "delete")
            {
                deletedItems.Add(targetId!);

                return """
                    <AML>
                      <Result>OK</Result>
                    </AML>
                    """;
            }

            throw new InvalidOperationException(
                $"Unexpected AML action: {action}");
        });

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
                        ["Storage:DataDirectory"] = _directory,
                        ["ARAS_USERNAME"] = "test-user",
                        ["ARAS_PASSWORD"] = "test-password"
                    });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IArasClient>();

                services.AddSingleton<IArasClient>(
                    arasClientMock.Object);
            });
        });

    using var client = factory.CreateClient();

    var endpoint =
        $"/api/Benchmark/pending-cleanup/{runId:N}/cleanup";

    // =====================================================
    // Attempt 1: A fails, B succeeds.
    // =====================================================

    using (var response = await client.PostAsync(
        endpoint,
        content: null))
    {
        var responseText =
            await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;

        Assert.Equal(2, root.GetProperty("found").GetInt32());
        Assert.Equal(1, root.GetProperty("deleted").GetInt32());
        Assert.Equal(0, root.GetProperty("alreadyMissing").GetInt32());
        Assert.Equal(1, root.GetProperty("failed").GetInt32());
        Assert.Equal(1, root.GetProperty("remaining").GetInt32());
        Assert.False(root.GetProperty("completed").GetBoolean());
    }

    // Reload the first attempt's persisted state.
    var afterFirst = TestDataManifest.Load(
        manifestPath,
        EnvironmentId);

    Assert.Equal(
        TrackedItemStatus.CleanupFailed,
        Assert.Single(
            afterFirst.Items,
            x => x.Id == failedItemId).Status);

    Assert.Equal(
        TrackedItemStatus.Deleted,
        Assert.Single(
            afterFirst.Items,
            x => x.Id == successfulItemId).Status);

    Assert.Single(afterFirst.UnresolvedItems);

    Assert.Equal(
        new[]
        {
            (1, failedItemId, "get"),
            (1, successfulItemId, "get"),
            (1, successfulItemId, "delete"),
            (1, successfulItemId, "get")
        },
        actions);

    // =====================================================
    // Attempt 2: Retry only A.
    // =====================================================

    attempt = 2;

    using (var response = await client.PostAsync(
        endpoint,
        content: null))
    {
        var responseText =
            await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("found").GetInt32());
        Assert.Equal(1, root.GetProperty("deleted").GetInt32());
        Assert.Equal(0, root.GetProperty("alreadyMissing").GetInt32());
        Assert.Equal(0, root.GetProperty("failed").GetInt32());
        Assert.Equal(0, root.GetProperty("remaining").GetInt32());
        Assert.True(root.GetProperty("completed").GetBoolean());
    }

    // Verify only A was processed during the retry.
    Assert.Equal(
        new[]
        {
            (2, failedItemId, "get"),
            (2, failedItemId, "delete"),
            (2, failedItemId, "get")
        },
        actions.Where(x => x.Attempt == 2));

    // Verify final state was persisted to disk.
    var afterSecond = TestDataManifest.Load(
        manifestPath,
        EnvironmentId);

    Assert.Empty(afterSecond.UnresolvedItems);

    Assert.Equal(2, afterSecond.Items.Count);

    Assert.All(
        afterSecond.Items,
        item => Assert.Equal(
            TrackedItemStatus.Deleted,
            item.Status));

    // =====================================================
    // Attempt 3: Completed Run must be a no-op.
    // =====================================================

    attempt = 3;

    var callsBeforeThird = actions.Count;

    var manifestBeforeThird = File.ReadAllBytes(manifestPath);

    using (var response = await client.PostAsync(
        endpoint,
        content: null))
    {
        var responseText =
            await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;

        Assert.Equal(0, root.GetProperty("found").GetInt32());
        Assert.Equal(0, root.GetProperty("deleted").GetInt32());
        Assert.Equal(0, root.GetProperty("failed").GetInt32());
        Assert.Equal(0, root.GetProperty("remaining").GetInt32());
        Assert.True(root.GetProperty("completed").GetBoolean());
    }

    Assert.Equal(callsBeforeThird, actions.Count);

    // Only attempts 1 and 2 should authenticate.
    arasClientMock.Verify(x => x.GetAccessTokenAsync(
        "test-user",
        "test-password",
        It.IsAny<CancellationToken>()),
        Times.Exactly(2));

    // Attempt 1: 4 AML calls.
    // Attempt 2: 3 AML calls.
    // Attempt 3: 0 AML calls.
    arasClientMock.Verify(x => x.ExecuteAmlAsync(
        It.IsAny<string>(),
        It.IsAny<string>(),
        It.IsAny<string>(),
        It.IsAny<CancellationToken>()),
        Times.Exactly(7));

    arasClientMock.VerifyNoOtherCalls();

    // The final no-op request must not change the manifest.
    var afterThird = TestDataManifest.Load(
        manifestPath,
        EnvironmentId);

    Assert.Empty(afterThird.UnresolvedItems);

    // Verify the third POST did not modify manifest content.
    Assert.Equal(
        manifestBeforeThird,
        File.ReadAllBytes(manifestPath));

    // Verify the Run lock is released.
    var coordinator = factory.Services
        .GetRequiredService<RunExecutionCoordinator>();

    var acquired = coordinator.TryAcquire(runId);

    try
    {
        Assert.True(
            acquired,
            "Manual cleanup did not release its Run lock.");
    }
    finally
    {
        if (acquired)
        {
            coordinator.Release(runId);
        }
    }
}

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
