using ArasPerformanceTool.Engine.Aras;

namespace ArasPerformanceTool.Engine.Benchmarking.Workloads;

public class ReadWorkload
{
    private readonly IArasClient _arasClient;

    public ReadWorkload(IArasClient arasClient)
    {
        _arasClient = arasClient;
    }

    public async Task<string> ExecuteAsync(
        string serverUrl,
        string accessToken,
        string itemType,
        string? id = null,
        string? select = null,
        CancellationToken cancellationToken = default)
    {
        var aml = AmlBuilder.Read(itemType, id, select);

        return await _arasClient.ExecuteAmlAsync(
            serverUrl,
            accessToken,
            aml,
            cancellationToken);
    }
}