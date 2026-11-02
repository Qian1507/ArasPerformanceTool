using ArasPerformanceTool.Engine.Benchmarking;
using ArasPerformanceTool.Engine.Storage;
using Xunit;

namespace ArasPerformanceTool.Tests.Unit;

public class FileResultStoreTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly FileResultStore _store;

    public FileResultStoreTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"ReportStoreTests_{Guid.NewGuid():N}");
        _store = new FileResultStore(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_ShouldPersistJsonAndCsvFiles_AndBeRetrievable()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var config = new BenchmarkConfiguration("Part", Iterations: 1, Concurrency: 1);
        var stats = new BenchmarkStatistics(1, 1, 0, 0.1, 10.0, 50.0, 50.0, 50.0, 50.0, 50.0, 50.0);
        var measurements = new List<OperationMeasurement>
        {
            new(ElapsedMilliseconds: 50.0, IsSuccess: true, ErrorMessage: null)
        };
        var report = new BenchmarkReport("TestWorkload", config, stats, measurements);

        // Act
        await _store.SaveAsync(jobId, report);

        // Assert
        Assert.True(_store.Exists(jobId));

        var json = await _store.GetJsonAsync(jobId);
        Assert.NotNull(json);
        Assert.Contains("TestWorkload", json);

        var csv = await _store.GetCsvAsync(jobId);
        Assert.NotNull(csv);
        Assert.Contains("50.000", csv);
    }

    [Fact]
    public async Task GetJsonAsync_ShouldReturnNull_WhenReportDoesNotExist()
    {
        // Act
        var result = await _store.GetJsonAsync(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    
    [Fact]
    public async Task GetCsvAsync_ShouldReturnNull_WhenReportDoesNotExist()
    {
        // Arrange
        var jobId = Guid.NewGuid();

        // Act
        var result = await _store.GetCsvAsync(jobId);

        // Assert
        Assert.Null(result);
        Assert.False(_store.Exists(jobId));
    }

}