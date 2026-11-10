
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArasPerformanceTool.Engine.Aras;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Xunit;

namespace ArasPerformanceTool.Tests.Integration;

public class AuthControllerTests
{
    // ---------------------------------------------------------
    // 1. Valid credentials should establish a login session.
    // ---------------------------------------------------------

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsOkAndCreatesSession()
    {
        using var factory = new AuthTestFactory();

        factory.ArasClient
            .Setup(x => x.GetAccessTokenAsync(
                "admin",
                "correct-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("fake-access-token");

        using var client = factory.CreateClient();

        var csrfToken = await GetCsrfTokenAsync(client);

        var response = await PostWithCsrfAsync(
            client,
            "/api/auth/login",
            new
            {
                username = "admin",
                password = "correct-password",
                database = "InnovatorSolutions"
            },
            csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var loginJson = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.True(
            loginJson.RootElement.GetProperty("authenticated").GetBoolean());

        Assert.Equal(
            "InnovatorSolutions",
            loginJson.RootElement.GetProperty("database").GetString());

        // Verify that the same browser session is authenticated.
        var session = await client.GetFromJsonAsync<JsonElement>(
            "/api/auth/session");

        Assert.True(
            session.GetProperty("authenticated").GetBoolean());

        Assert.Equal(
            "admin",
            session.GetProperty("username").GetString());

        factory.ArasClient.Verify(x => x.GetAccessTokenAsync(
                "admin",
                "correct-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---------------------------------------------------------
    // 2. OAuth rejecting credentials should return HTTP 401.
    // ---------------------------------------------------------

    [Fact]
    public async Task Login_WithRejectedCredentials_ReturnsUnauthorized()
    {
        using var factory = new AuthTestFactory();

        factory.ArasClient
            .Setup(x => x.GetAccessTokenAsync(
                "admin",
                "wrong-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(
                "OAuth authentication rejected.",
                inner: null,
                statusCode: HttpStatusCode.Unauthorized));

        using var client = factory.CreateClient();

        var csrfToken = await GetCsrfTokenAsync(client);

        var response = await PostWithCsrfAsync(
            client,
            "/api/auth/login",
            new
            {
                username = "admin",
                password = "wrong-password",
                database = "InnovatorSolutions"
            },
            csrfToken);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        var session = await client.GetFromJsonAsync<JsonElement>(
            "/api/auth/session");

        Assert.False(
            session.GetProperty("authenticated").GetBoolean());
    }

    // ---------------------------------------------------------
    // 3. Unknown database must be rejected before OAuth.
    // ---------------------------------------------------------

    [Fact]
    public async Task Login_WithInvalidDatabase_ReturnsBadRequest()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateClient();

        var csrfToken = await GetCsrfTokenAsync(client);

        var response = await PostWithCsrfAsync(
            client,
            "/api/auth/login",
            new
            {
                username = "admin",
                password = "correct-password",
                database = "UnknownDatabase"
            },
            csrfToken);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        // Database validation must happen before authentication.
        factory.ArasClient.Verify(x => x.GetAccessTokenAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---------------------------------------------------------
    // 4. Login without CSRF token must return HTTP 400.
    // ---------------------------------------------------------

    [Fact]
    public async Task Login_WithoutCsrfToken_ReturnsBadRequest()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                username = "admin",
                password = "correct-password",
                database = "InnovatorSolutions"
            });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        factory.ArasClient.Verify(x => x.GetAccessTokenAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---------------------------------------------------------
    // 5. Logout must clear the existing authentication session.
    // ---------------------------------------------------------

    [Fact]
    public async Task Logout_AfterLogin_ClearsSession()
    {
        using var factory = new AuthTestFactory();

        factory.ArasClient
            .Setup(x => x.GetAccessTokenAsync(
                "admin",
                "correct-password",
                "InnovatorSolutions",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("fake-access-token");

        using var client = factory.CreateClient();

        var csrfToken = await GetCsrfTokenAsync(client);

        var loginResponse = await PostWithCsrfAsync(
            client,
            "/api/auth/login",
            new
            {
                username = "admin",
                password = "correct-password",
                database = "InnovatorSolutions"
            },
            csrfToken);

        Assert.Equal(
            HttpStatusCode.OK,
            loginResponse.StatusCode);

        var beforeLogout = await client.GetFromJsonAsync<JsonElement>(
            "/api/auth/session");

        Assert.True(
            beforeLogout.GetProperty("authenticated").GetBoolean());

        using var logoutResponse = await PostWithCsrfAsync(
            client,
            "/api/auth/logout",
            new { },
            csrfToken);

        Assert.Equal(
            HttpStatusCode.OK,
            logoutResponse.StatusCode);

        var afterLogout = await client.GetFromJsonAsync<JsonElement>(
            "/api/auth/session");

        Assert.False(
            afterLogout.GetProperty("authenticated").GetBoolean());
    }

    // ---------------------------------------------------------
    // Helpers: CSRF and HTTP POST.
    // ---------------------------------------------------------

    private static async Task<string> GetCsrfTokenAsync(
        HttpClient client)
    {
        using var response = await client.GetAsync(
            "/api/auth/csrf");

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        return json.GetProperty("requestToken").GetString()
            ?? throw new InvalidOperationException(
                "CSRF token was not returned.");
    }

    private static async Task<HttpResponseMessage> PostWithCsrfAsync(
        HttpClient client,
        string url,
        object body,
        string csrfToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            url);

        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        request.Content = JsonContent.Create(body);

        return await client.SendAsync(request);
    }

    // ---------------------------------------------------------
    // Isolated API integration test host.
    // ---------------------------------------------------------

    private sealed class AuthTestFactory : WebApplicationFactory<Program>
    {
        public Mock<IArasClient> ArasClient { get; } = new();

        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Aras:Database"] = "InnovatorSolutions",
                        ["Aras:DatabaseListUrl"] =
                            "https://aras.test/server/DBList.aspx"
                    });
            });

            builder.ConfigureTestServices(services =>
            {
                // Replace Aras with a mock so tests require
                // no running Innovator server or real credentials.
                services.RemoveAll<IArasClient>();
                services.AddSingleton(ArasClient.Object);

                // Simulate the Aras DBList.aspx response.
                var factoryMock = new Mock<IHttpClientFactory>();

                factoryMock
                    .Setup(x => x.CreateClient(
                        It.IsAny<string>()))
                    .Returns(() => new HttpClient(
                        new FakeDatabaseListHandler()));

                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton(factoryMock.Object);
            });
        }
    }

    // ---------------------------------------------------------
    // Simulated Aras database discovery endpoint.
    // ---------------------------------------------------------

    private sealed class FakeDatabaseListHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            const string xml =
                "<DBList><DB id=\"InnovatorSolutions\"/></DBList>";

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    xml,
                    System.Text.Encoding.UTF8,
                    "application/xml")
            };

            return Task.FromResult(response);
        }
    }
}
