using System.Net.Http.Headers;
using ArasPerformanceTool.Engine.Aras;
using Xunit;

namespace ArasPerformanceTool.Tests.Integration;

public sealed class ArasFixture : IAsyncLifetime
{
    public const string EnvironmentId = "local-aras-test";

    public const string Database = "InnovatorSolutions";

    public const string OAuthTokenUrl =
        "http://localhost/InnovatorServer/OAuthServer/connect/token";

    public const string ServerUrl =
        "http://localhost/InnovatorServer/Server/InnovatorServer.aspx";

    private readonly HttpClient _httpClient = new();

    public ArasClient Client { get; private set; } = null!;

    public string AccessToken { get; private set; } = string.Empty;

    public string Username { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var username = Environment.GetEnvironmentVariable("ARAS_USERNAME");
        var password = Environment.GetEnvironmentVariable("ARAS_PASSWORD");

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException(
                "Missing environment variable: ARAS_USERNAME.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Missing environment variable: ARAS_PASSWORD.");
        }

        Username = username;

        Client = new ArasClient(
            _httpClient,
            OAuthTokenUrl,
            Database);

        AccessToken = await Client.GetAccessTokenAsync(
            username,
            password);

        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            throw new InvalidOperationException(
                "Aras authentication returned an empty access token.");
        }
    }

    public Task DisposeAsync()
    {
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    public async Task<string> ExecuteAmlAsync(
        string aml,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aml);

        return await Client.ExecuteAmlAsync(
            ServerUrl,
            AccessToken,
            aml,
            cancellationToken);
    }

    public async Task<string> CreateItemAsync(
        string itemType,
        IReadOnlyDictionary<string, string> properties,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemType);
        ArgumentNullException.ThrowIfNull(properties);

        var aml = AmlBuilder.Create(
            itemType,
            properties.ToDictionary(
                x => x.Key,
                x => x.Value));

        return await ExecuteAmlAsync(
            aml,
            cancellationToken);
    }

    
public async Task<string> GetItemAsync(
    string itemType,
    string? id = null,
    string? select = null,
    CancellationToken cancellationToken = default)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(itemType);

    var aml = AmlBuilder.Read(
        itemType,
        id,
        select);

    return await ExecuteAmlAsync(
        aml,
        cancellationToken);
}


    public async Task<string> DeleteItemAsync(
        string itemType,
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemType);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var aml = AmlBuilder.Delete(
            itemType,
            id);

        return await ExecuteAmlAsync(
            aml,
            cancellationToken);
    }
}