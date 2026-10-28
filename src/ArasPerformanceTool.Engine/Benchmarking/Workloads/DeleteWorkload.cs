using ArasPerformanceTool.Engine.Aras;

namespace ArasPerformanceTool.Engine.Benchmarking.Workloads;

public class DeleteWorkload
{
    private readonly IArasClient _arasClient;

    public DeleteWorkload(IArasClient arasClient)
    {
        _arasClient = arasClient;
    }

    public async Task<string> ExecuteAsync(
        string serverUrl,
        string accessToken,
        string itemType,
        string id,
        CancellationToken cancellationToken = default)
    {
        var aml = AmlBuilder.Delete(itemType, id);

        return await _arasClient.ExecuteAmlAsync(
            serverUrl,
            accessToken,
            aml,
            cancellationToken);
    }
}