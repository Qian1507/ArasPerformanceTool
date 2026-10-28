using ArasPerformanceTool.Engine.Aras;

namespace ArasPerformanceTool.Engine.Benchmarking.Workloads;

public class UpdateWorkload
{
    private readonly IArasClient _arasClient;

    public UpdateWorkload(IArasClient arasClient)
    {
        _arasClient = arasClient;
    }

    public async Task<string> ExecuteAsync(
        string serverUrl,
        string accessToken,
        string itemType,
        string id,
        IReadOnlyDictionary<string, string> properties,
        CancellationToken cancellationToken = default)
    {
        var aml = AmlBuilder.Update(itemType, id, properties);

        return await _arasClient.ExecuteAmlAsync(
            serverUrl,
            accessToken,
            aml,
            cancellationToken);
    }

    
}