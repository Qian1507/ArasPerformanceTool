using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using ArasPerformanceTool.Engine.Exceptions;

namespace ArasPerformanceTool.Engine.Aras;

public class ArasClient : IArasClient
{
    private readonly HttpClient _httpClient;
    private readonly string _oauthServerUrl;
    private readonly string _database;

    public ArasClient(HttpClient httpClient, string oauthServerUrl, string database)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _oauthServerUrl = oauthServerUrl ?? throw new ArgumentNullException(nameof(oauthServerUrl));
        _database = database ?? throw new ArgumentNullException(nameof(database));
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
            ["username"] = username,
            ["password"] = password,
            ["database"] = _database,
            ["scope"] = "Innovator"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _oauthServerUrl)
        {
            Content = new FormUrlEncodedContent(parameters)
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OAuth token request failed with HTTP status code {(int)response.StatusCode} ({response.StatusCode}).");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        using var jsonDocument = System.Text.Json.JsonDocument.Parse(responseJson);
        var root = jsonDocument.RootElement;

        if (root.TryGetProperty("access_token", out var tokenProp))
        {
            return tokenProp.GetString()!;
        }

        throw new InvalidOperationException("OAuth response does not contain a valid 'access_token'.");
    }

   public async Task<string> ExecuteAmlAsync(
        string serverUrl,
        string accessToken,
        string aml,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, serverUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("SOAPACTION", "ApplyAML");

        var soapEnvelope = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/"">
    <SOAP-ENV:Body>
        <ApplyAML>{aml}</ApplyAML>
    </SOAP-ENV:Body>
    </SOAP-ENV:Envelope>";

        request.Content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"AML HTTP request failed with status code {(int)response.StatusCode} ({response.StatusCode}).");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        EnsureSuccessAmlResponse(responseBody);

        return responseBody;
    }

    private static void EnsureSuccessAmlResponse(string xmlPayload)
    {
        var document = XDocument.Parse(xmlPayload);
        var fault = document.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Fault");

        if (fault is null)
        {
            return;
        }

        var faultCode = fault.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "faultcode")?.Value;

        var faultString = fault.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "faultstring")?.Value;

        var detail = fault.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "detail")?.Value;

        // Code "0" denotes an empty result set (e.g., item not found during a query), not a server failure
        if (faultCode != "0")
        {
            throw new ArasException(
                faultString ?? "AML execution failed with an unspecified SOAP Fault.",
                faultCode,
                faultString,
                detail);
        }
    }
}