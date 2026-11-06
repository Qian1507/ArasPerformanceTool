
using System.Threading;
using System.Threading.Tasks;
using ArasPerformanceTool.Engine.Models;
using ArasPerformanceTool.Engine.Benchmarking;

namespace ArasPerformanceTool.Engine.Services;

public interface IBenchmarkExecutionService<in TRequest>
{
    Task<BenchmarkReport> RunAsync(
        TRequest request, 
        string serverUrl, 
        string accessToken, 
        CancellationToken cancellationToken = default);
}