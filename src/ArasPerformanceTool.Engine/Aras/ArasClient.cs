
using System.Net.Http;
using System.Text.Json;

namespace ArasPerformanceTool.Engine.Aras;

public class ArasClient : IArasClient
{
    private readonly HttpClient _httpClient;
    private readonly string _oauthUrl;
    private readonly string _database;

    public ArasClient(
        HttpClient httpClient,
        string oauthUrl,
        string database)
    {
        _httpClient = httpClient;
        _oauthUrl = oauthUrl;
        _database = database;
    }

    public async Task<string> GetAccessTokenAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "IOMApp",
            ["scope"] = "Innovator",
            ["username"] = username,
            ["password"] = password,
            ["database"] = _database
        };

        using var content = new FormUrlEncodedContent(parameters);

        using var response = await _httpClient.PostAsync(
            _oauthUrl,
            content,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Aras authentication failed. HTTP status: {(int)response.StatusCode}",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken);

        using var json = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);

        return json.RootElement
            .GetProperty("access_token")
            .GetString()
            ?? throw new InvalidOperationException(
                "OAuth response did not contain an access token.");
    }

    
    public async Task<string> ExecuteAmlAsync(
        string serverUrl,
        string accessToken,
        string aml,
        CancellationToken cancellationToken = default)
    {
        var soapEnvelope =
            "<SOAP-ENV:Envelope " +
            "xmlns:SOAP-ENV=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
            "<SOAP-ENV:Body>" +
            aml +
            "</SOAP-ENV:Body>" +
            "</SOAP-ENV:Envelope>";

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            serverUrl);

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", accessToken);

        request.Headers.Add("SOAPAction", "ApplyAML");
        request.Headers.Add("DATABASE", _database);

        request.Content = new StringContent(
            soapEnvelope,
            System.Text.Encoding.UTF8,
            "text/xml");

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return responseBody;
    }

}
