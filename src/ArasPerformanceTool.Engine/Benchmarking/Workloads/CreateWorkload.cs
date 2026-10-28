
using ArasPerformanceTool.Engine.Aras;

namespace ArasPerformanceTool.Engine.Benchmarking.Workloads;

public class CreateWorkload
{
    private readonly IArasClient _arasClient;

    public CreateWorkload(IArasClient arasClient)
    {
        _arasClient = arasClient;
    }

    public async Task<string> ExecuteAsync(
        string serverUrl,
        string accessToken,
        string itemType,
        IReadOnlyDictionary<string, string> properties,
        CancellationToken cancellationToken = default)
    {
        var aml = AmlBuilder.Create(itemType, properties);

        return await _arasClient.ExecuteAmlAsync(
            serverUrl,
            accessToken,
            aml,
            cancellationToken);
    }
}
