using Microsoft.Extensions.Hosting;

namespace ArasPerformanceTool.Api.BackgroundJobs;

public class BenchmarkHostedService : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}