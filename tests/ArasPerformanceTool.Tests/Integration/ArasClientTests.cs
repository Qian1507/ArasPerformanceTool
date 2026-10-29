
using ArasPerformanceTool.Engine.Aras;
using Xunit;
using ArasPerformanceTool.Engine.Exceptions;

namespace ArasPerformanceTool.Tests.Integration;

public class ArasClientTests
{
    [Fact]
    public async Task GetAccessTokenAsync_ShouldReturnValidToken_WhenCredentialsAreValid()
    {
        // Arrange
        using var httpClient = new HttpClient();

        const string oauthUrl =
            "http://localhost/InnovatorServer/OAuthServer/connect/token";

        const string database = "InnovatorSolutions";

        var username = Environment.GetEnvironmentVariable("ARAS_USERNAME");
        var password = Environment.GetEnvironmentVariable("ARAS_PASSWORD");

        Assert.False(string.IsNullOrWhiteSpace(username));
        Assert.False(string.IsNullOrWhiteSpace(password));

        var client = new ArasClient(
            httpClient,
            oauthUrl,
            database);

        // Act
        var token = await client.GetAccessTokenAsync(
            username!,
            password!);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    
    [Fact]
    public async Task ExecuteAmlAsync_ShouldReturnParts()
    {
        // Arrange
        var username = Environment.GetEnvironmentVariable("ARAS_USERNAME");
        var password = Environment.GetEnvironmentVariable("ARAS_PASSWORD");

        Assert.False(string.IsNullOrWhiteSpace(username));
        Assert.False(string.IsNullOrWhiteSpace(password));

        using var httpClient = new HttpClient();

        var client = new ArasClient(
            httpClient,
            "http://localhost/InnovatorServer/OAuthServer/connect/token",
            "InnovatorSolutions");

        var token = await client.GetAccessTokenAsync(
            username!,
            password!);

        const string serverUrl =
            "http://localhost/InnovatorServer/Server/InnovatorServer.aspx";

        const string aml = """
            <AML>
                <Item type="Part"
                    action="get"
                    select="id,item_number"
                    maxRecords="5" />
            </AML>
            """;

        // Act
        var result = await client.ExecuteAmlAsync(
            serverUrl,
            token,
            aml);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(result));

        var xml = System.Xml.Linq.XDocument.Parse(result);

        Assert.False(
            xml.Descendants()
            .Any(e => e.Name.LocalName == "Fault"),
            "Aras returned a SOAP fault.");

        Assert.True(
            xml.Descendants()
            .Any(e => e.Name.LocalName == "Item"
                    && (string?)e.Attribute("type") == "Part"),
            "No Part items were returned.");
    }


    
    [Fact]
    public async Task GetAccessTokenAsync_ShouldThrow_WhenPasswordIsWrong()
    {
        // Arrange
        var username = Environment.GetEnvironmentVariable("ARAS_USERNAME");

        Assert.False(string.IsNullOrWhiteSpace(username));

        using var httpClient = new HttpClient();

        var client = new ArasClient(
            httpClient,
            "http://localhost/InnovatorServer/OAuthServer/connect/token",
            "InnovatorSolutions");

        const string wrongPassword = "InvalidPassword_ForTesting_123!";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAccessTokenAsync(
                username!,
                wrongPassword));

        Assert.True(
            exception.StatusCode == System.Net.HttpStatusCode.BadRequest ||
            exception.StatusCode == System.Net.HttpStatusCode.Unauthorized,
            $"Unexpected HTTP status: {exception.StatusCode}");
    }


    [Fact]
    public async Task ExecuteAmlAsync_ShouldCreatePart()
    {
        // Arrange
        var username = Environment.GetEnvironmentVariable("ARAS_USERNAME");
        var password = Environment.GetEnvironmentVariable("ARAS_PASSWORD");

        Assert.False(string.IsNullOrWhiteSpace(username));
        Assert.False(string.IsNullOrWhiteSpace(password));

        using var httpClient = new HttpClient();

        var client = new ArasClient(
            httpClient,
            "http://localhost/InnovatorServer/OAuthServer/connect/token",
            "InnovatorSolutions");

        var token = await client.GetAccessTokenAsync(
            username!,
            password!);

        const string serverUrl =
            "http://localhost/InnovatorServer/Server/InnovatorServer.aspx";

        var itemNumber = $"LIA-PROJECT-{Guid.NewGuid():N}"[..24];

        var aml = AmlBuilder.Create(
            "Part",
            new Dictionary<string, string>
            {
                ["item_number"] = itemNumber,
                ["name"] = "AML Create Test"
            });

        // Act
        var result = await client.ExecuteAmlAsync(
            serverUrl,
            token,
            aml);

        // Assert
        var xml = System.Xml.Linq.XDocument.Parse(result);

        var faults = xml.Descendants()
            .Where(e => e.Name.LocalName == "Fault");

        Assert.Empty(faults);

        var createdItem = xml.Descendants()
            .FirstOrDefault(e =>
                e.Name.LocalName == "Item" &&
                (string?)e.Attribute("type") == "Part");

        Assert.NotNull(createdItem);

        var id = (string?)createdItem.Attribute("id");
        Assert.False(string.IsNullOrWhiteSpace(id));
    }

    
    [Fact]
    public async Task ExecuteAmlAsync_ShouldThrowArasException_WhenItemTypeIsInvalid()
    {
        // Arrange
        var username = Environment.GetEnvironmentVariable("ARAS_USERNAME");
        var password = Environment.GetEnvironmentVariable("ARAS_PASSWORD");

        Assert.False(string.IsNullOrWhiteSpace(username));
        Assert.False(string.IsNullOrWhiteSpace(password));

        using var httpClient = new HttpClient();

        var arasClient = new ArasClient(
            httpClient,
            "http://localhost/InnovatorServer/OAuthServer/connect/token",
            "InnovatorSolutions");

        var token = await arasClient.GetAccessTokenAsync(
            username!,
            password!);

        const string serverUrl =
            "http://localhost/InnovatorServer/Server/InnovatorServer.aspx";

        const string invalidAml = """
            <AML>
                <Item type="LIA_NONEXISTENT_ITEMTYPE_999"
                    action="get" />
            </AML>
            """;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArasException>(
            () => arasClient.ExecuteAmlAsync(
                serverUrl,
                token,
                invalidAml));

        Assert.False(string.IsNullOrWhiteSpace(exception.FaultCode));
        Assert.NotEqual("0", exception.FaultCode);
        Assert.False(string.IsNullOrWhiteSpace(exception.FaultString));
    }


}
