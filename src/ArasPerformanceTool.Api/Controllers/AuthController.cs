
using System.Net;
using System.Xml;
using System.Xml.Linq;
using ArasPerformanceTool.Engine.Aras;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ArasPerformanceTool.Api.Services;

namespace ArasPerformanceTool.Api.Controllers;

public sealed record ArasLoginRequest(
    string? Username,
    string? Password,
    string? Database);

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private const string TokenKey = "Aras.AccessToken";
    private const string DatabaseKey = "Aras.Database";
    private const string UsernameKey = "Aras.Username";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;
    private readonly IArasClient _arasClient;
    private readonly IAntiforgery _antiforgery;
    private readonly ExpiredArasTokenStore _expiredArasTokens;

    public AuthController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AuthController> logger,
        IArasClient arasClient,
        IAntiforgery antiforgery,
        ExpiredArasTokenStore expiredArasTokens)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
        _arasClient = arasClient;
        _antiforgery = antiforgery;
        _expiredArasTokens = expiredArasTokens;
    }

    // ---------------------------------------------------------
    // GET /api/auth/csrf
    // ---------------------------------------------------------

    [HttpGet("csrf")]
    public IActionResult GetCsrfToken()
    {
        Response.Headers["Cache-Control"] = "no-store";

        var tokens = _antiforgery.GetAndStoreTokens(
            HttpContext);

        return Ok(new
        {
            requestToken = tokens.RequestToken
        });
    }

    // ---------------------------------------------------------
    // GET /api/auth/databases
    // ---------------------------------------------------------

    [HttpGet("databases")]
    public async Task<IActionResult> GetDatabases(
        CancellationToken cancellationToken)
    {
        try
        {
            var databases = await LoadDatabasesAsync(
                cancellationToken);

            return Ok(new { databases });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(
                ex,
                "Aras database list request timed out.");

            return StatusCode(504, new
            {
                message = "Aras database list request timed out."
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(
                ex,
                "Aras database list configuration is invalid.");

            return StatusCode(500, new
            {
                message = "Aras database list is not configured correctly."
            });
        }
        catch (Exception ex) when (
            ex is HttpRequestException or
            InvalidDataException or
            XmlException)
        {
            _logger.LogError(
                ex,
                "Failed to retrieve Aras databases.");

            return StatusCode(502, new
            {
                message = "Unable to retrieve Aras databases."
            });
        }
    }

    // ---------------------------------------------------------
    // POST /api/auth/login
    // ---------------------------------------------------------

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] ArasLoginRequest request,
        CancellationToken cancellationToken)
    {
        // Validate CSRF before processing credentials.
        if (!await _antiforgery.IsRequestValidAsync(
            HttpContext))
        {
            return BadRequest(new
            {
                message =
                    "Invalid request verification token. " +
                    "Refresh the page and retry."
            });
        }

        if (request is null ||
            string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.Database))
        {
            return BadRequest(new
            {
                message =
                    "Username, password and database are required."
            });
        }

        var username = request.Username.Trim();
        var requestedDatabase = request.Database.Trim();

        // -----------------------------------------------------
        // Step 1: Retrieve available databases.
        // -----------------------------------------------------

        string[] databases;

        try
        {
            databases = await LoadDatabasesAsync(
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(
                ex,
                "Database lookup timed out during login.");

            return StatusCode(504, new
            {
                message = "Database lookup timed out."
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(
                ex,
                "Database list configuration is invalid.");

            return StatusCode(500, new
            {
                message =
                    "Aras database list is not configured correctly."
            });
        }
        catch (Exception ex) when (
            ex is HttpRequestException or
            InvalidDataException or
            XmlException)
        {
            _logger.LogError(
                ex,
                "Database lookup failed during login.");

            return StatusCode(502, new
            {
                message = "Unable to retrieve Aras databases."
            });
        }

        // -----------------------------------------------------
        // Step 2: Resolve the database using the server's name.
        // -----------------------------------------------------

        var database = databases.FirstOrDefault(x =>
            string.Equals(
                x,
                requestedDatabase,
                StringComparison.OrdinalIgnoreCase));

        if (database is null)
        {
            return BadRequest(new
            {
                message = "The selected database is not available."
            });
        }

        // -----------------------------------------------------
        // Step 3: Authenticate against Aras OAuth.
        // -----------------------------------------------------

        string token;

        try
        {
            token = await _arasClient.GetAccessTokenAsync(
                username,
                request.Password,
                database,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(
                ex,
                "Aras OAuth request timed out.");

            return StatusCode(504, new
            {
                message = "Aras authentication timed out."
            });
        }
        catch (HttpRequestException ex)
            when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning(
                "Aras rejected the authentication request.");

            return Unauthorized(new
            {
                message = "Aras authentication was rejected."
            });
        }
        catch (ArasAuthenticationException)
        {
            return Unauthorized(new
            {
                message = "Invalid username or password."
            });
        }
        catch (HttpRequestException ex)
        {
            // An OAuth HTTP 400 is not necessarily a bad password.
            // It may also indicate an OAuth configuration error.
            _logger.LogError(
                ex,
                "Aras OAuth request failed.");

            return StatusCode(502, new
            {
                message = "Unable to complete Aras authentication."
            });
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or
            System.Text.Json.JsonException)
        {
            _logger.LogError(
                ex,
                "Invalid Aras OAuth response.");

            return StatusCode(502, new
            {
                message = "Aras returned an invalid authentication response."
            });
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return StatusCode(502, new
            {
                message = "Aras returned an invalid access token."
            });
        }

        // -----------------------------------------------------
        // Step 4: Establish the server-side session.
        // -----------------------------------------------------

        await HttpContext.Session.LoadAsync(cancellationToken);

        HttpContext.Session.Clear();

        HttpContext.Session.SetString(TokenKey, token);
        HttpContext.Session.SetString(DatabaseKey, database);
        HttpContext.Session.SetString(UsernameKey, username);

        await HttpContext.Session.CommitAsync(cancellationToken);

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new
        {
            authenticated = true,
            username,
            database
        });
    }

    // ---------------------------------------------------------
    // GET /api/auth/session
    // ---------------------------------------------------------

    [HttpGet("session")]
    public async Task<IActionResult> GetSession(
        CancellationToken cancellationToken)
    {
        Response.Headers["Cache-Control"] = "no-store";

        await HttpContext.Session.LoadAsync(cancellationToken);

        var token = HttpContext.Session.GetString(TokenKey);
        var database = HttpContext.Session.GetString(DatabaseKey);
        var username = HttpContext.Session.GetString(UsernameKey);

        if (string.IsNullOrWhiteSpace(token) ||
            string.IsNullOrWhiteSpace(database) ||
            string.IsNullOrWhiteSpace(username))
        {
            return Ok(new
            {
                authenticated = false
            });
        }
        if (_expiredArasTokens.IsExpired(token))
        {
            return Ok(new
            {
                authenticated = false,
                requiresLogin = true
            });
        }
        return Ok(new
        {
            authenticated = true,
            username,
            database
        });
    }

    // ---------------------------------------------------------
    // POST /api/auth/logout
    // ---------------------------------------------------------

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        CancellationToken cancellationToken)
    {
        if (!await _antiforgery.IsRequestValidAsync(
            HttpContext))
        {
            return BadRequest(new
            {
                message =
                    "Invalid request verification token. " +
                    "Refresh the page and retry."
            });
        }

        await HttpContext.Session.LoadAsync(cancellationToken);

        HttpContext.Session.Clear();

        await HttpContext.Session.CommitAsync(cancellationToken);

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new
        {
            authenticated = false
        });
    }

    // ---------------------------------------------------------
    // Shared database discovery logic.
    // ---------------------------------------------------------

    private async Task<string[]> LoadDatabasesAsync(
        CancellationToken cancellationToken)
    {
        var url = _configuration["Aras:DatabaseListUrl"];

        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var databaseListUri) ||
            (databaseListUri.Scheme != Uri.UriSchemeHttp &&
             databaseListUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Aras database list URL is missing or invalid.");
        }

        var client = _httpClientFactory.CreateClient();

        using var response = await client.GetAsync(
            databaseListUri,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(
            cancellationToken);

        var document = XDocument.Parse(xml);

        if (document.Root?.Name.LocalName != "DBList")
        {
            throw new InvalidDataException(
                "Unexpected Aras database list response.");
        }

        var databases = document.Root
            .Elements()
            .Where(x => x.Name.LocalName == "DB")
            .Select(x => ((string?)x.Attribute("id"))?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (databases.Length == 0)
        {
            throw new InvalidDataException(
                "Aras database list contains no selectable databases.");
        }

        return databases;
    }
}
