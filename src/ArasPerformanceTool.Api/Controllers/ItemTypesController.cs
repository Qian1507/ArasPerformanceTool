using System.Net;
using System.Xml;
using System.Xml.Linq;
using ArasPerformanceTool.Api.Services;
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace ArasPerformanceTool.Api.Controllers;

[ApiController]
[Route("api/itemtypes")]
public sealed class ItemTypesController : ControllerBase
{
    private readonly IArasClient _arasClient;
    private readonly IConfiguration _configuration;
    private readonly ExpiredArasTokenStore _expiredTokens;
    private readonly ILogger<ItemTypesController> _logger;

    public ItemTypesController(
        IArasClient arasClient,
        IConfiguration configuration,
        ExpiredArasTokenStore expiredTokens,
        ILogger<ItemTypesController> logger)
    {
        _arasClient = arasClient;
        _configuration = configuration;
        _expiredTokens = expiredTokens;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetItemTypes(
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        // 1. Validate current Aras session.
        await HttpContext.Session.LoadAsync(cancellationToken);

        var token = HttpContext.Session.GetString(
            "Aras.AccessToken");

        var database = HttpContext.Session.GetString(
            "Aras.Database");

        var username = HttpContext.Session.GetString(
            "Aras.Username");

        if (string.IsNullOrWhiteSpace(token) ||
            string.IsNullOrWhiteSpace(database) ||
            string.IsNullOrWhiteSpace(username))
        {
            return Unauthorized(new
            {
                 message = "Please sign in to Aras.",
                 requiresLogin = true
            });
        }

        if (_expiredTokens.IsExpired(token))
        {
            return Unauthorized(new
            {
                message = "Aras authentication has expired.",
                requiresLogin = true
            });
        }

        var serverUrl = _configuration["Aras:ServerUrl"];

        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            return StatusCode(500, new
            {
                message = "Aras server is not configured."
            });
        }

        // 2. Query ItemType metadata (read-only).
        const string aml = """
            <AML>
                <Item type="ItemType"
                      action="get"
                      select="id,name,label" />
            </AML>
            """;

        try
        {
            var xml = await _arasClient.ExecuteAmlAsync(
                serverUrl,
                token,
                aml,
                cancellationToken);

            // 3. Parse Aras response.
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            using var textReader = new StringReader(xml);
            using var xmlReader = XmlReader.Create(textReader, settings);

            var document = XDocument.Load(xmlReader);

            // Aras may represent an empty result as faultcode 0.
            var fault = document.Descendants()
                .FirstOrDefault(x =>
                    x.Name.LocalName == "Fault");

            var faultCode = fault?.Descendants()
                .FirstOrDefault(x =>
                    x.Name.LocalName == "faultcode")
                ?.Value
                .Trim();

            if (fault != null && faultCode != "0")
            {
                throw new InvalidDataException(
                    "Unexpected Aras metadata fault.");
            }

            var itemTypes = fault != null
                ? Array.Empty<ItemTypeMetadata>()
                : document.Descendants()
                    .Where(x =>
                        x.Name.LocalName == "Item" &&
                        (string?)x.Attribute("type") == "ItemType")
                    .Select(x => new ItemTypeMetadata(
                        Id: (string?)x.Attribute("id")
                            ?? (string?)x.Elements()
                                .FirstOrDefault(e =>
                                    e.Name.LocalName == "id")
                            ?? "",
                        Name: (string?)x.Elements()
                            .FirstOrDefault(e =>
                                e.Name.LocalName == "name")
                            ?? "",
                        Label: (string?)x.Elements()
                            .FirstOrDefault(e =>
                                e.Name.LocalName == "label")
                    ))
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Id) &&
                        !string.IsNullOrWhiteSpace(x.Name))
                    .OrderBy(x => x.Name,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            return Ok(new
            {
                database,
                count = itemTypes.Length,
                itemTypes
            });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(
                ex, "Aras ItemType request timed out.");

            return StatusCode(504, new
            {
                message = "Aras ItemType request timed out."
            });
        }
        catch (HttpRequestException ex)
            when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            _expiredTokens.MarkExpired(token);

            return Unauthorized(new
            {
                message = "Aras authentication has expired.",
                requiresLogin = true
            });
        }
        catch (Exception ex) when (
            ex is HttpRequestException or
            ArasException or
            XmlException or
            InvalidDataException)
        {
            _logger.LogError(
                ex, "Failed to retrieve Aras ItemTypes.");

            return StatusCode(502, new
            {
                message = "Unable to retrieve ItemTypes from Aras."
            });
        }
    }
}

public sealed record ItemTypeMetadata(
    string Id,
    string Name,
    string? Label);
