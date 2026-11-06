
using System.Net;
using System.Xml.Linq;
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Exceptions;
using Xunit;

namespace ArasPerformanceTool.Tests.Integration;

[Collection("Aras Integration")]
public sealed class ArasClientTests
{
    private readonly ArasFixture _aras;

    public ArasClientTests(ArasFixture aras)
    {
        _aras = aras;
    }

    [Fact]
    public async Task GetAccessTokenAsync_ShouldReturnValidToken_WhenCredentialsAreValid()
    {
        // Arrange
        var username = Environment.GetEnvironmentVariable("ARAS_USERNAME");
        var password = Environment.GetEnvironmentVariable("ARAS_PASSWORD");

        Assert.False(string.IsNullOrWhiteSpace(username));
        Assert.False(string.IsNullOrWhiteSpace(password));

        // Act: actually request a new token.
        var token = await _aras.Client.GetAccessTokenAsync(
            username!,
            password!);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public async Task ExecuteAmlAsync_ShouldReturnParts()
    {
        // Arrange
        const string aml = """
            <AML>
                <Item type="Part"
                      action="get"
                      select="id,item_number"
                      maxRecords="5" />
            </AML>
            """;

        // Act
        var result = await _aras.ExecuteAmlAsync(aml);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(result));

        var xml = XDocument.Parse(result);

        Assert.DoesNotContain(
            xml.Descendants(),
            e => e.Name.LocalName == "Fault");

        Assert.Contains(
            xml.Descendants(),
            e => e.Name.LocalName == "Item"
                 && (string?)e.Attribute("type") == "Part");
    }

    [Fact]
    public async Task GetAccessTokenAsync_ShouldThrow_WhenPasswordIsWrong()
    {
        // Arrange
        const string wrongPassword =
            "InvalidPassword_ForTesting_123!";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _aras.Client.GetAccessTokenAsync(
                _aras.Username,
                wrongPassword));

        Assert.False(string.IsNullOrWhiteSpace(exception.Message));

        if (exception.StatusCode is not null)
        {
            Assert.True(
                exception.StatusCode is
                    HttpStatusCode.BadRequest or
                    HttpStatusCode.Unauthorized,
                $"Unexpected HTTP status: {exception.StatusCode}");
        }
    }

    [Fact]
    public async Task ExecuteAmlAsync_ShouldCreatePart()
    {
        // Arrange
        var itemNumber =
            $"LIA-PROJECT-{Guid.NewGuid():N}"[..24];

        var properties = new Dictionary<string, string>
        {
            ["item_number"] = itemNumber,
            ["name"] = "AML Create Test"
        };

        string? createdId = null;

        try
        {
            // Act
            var result = await _aras.CreateItemAsync(
                "Part",
                properties);

            // Assert
            var xml = XDocument.Parse(result);

            Assert.DoesNotContain(
                xml.Descendants(),
                e => e.Name.LocalName == "Fault");

            var createdItem = xml.Descendants()
                .FirstOrDefault(e =>
                    e.Name.LocalName == "Item"
                    && (string?)e.Attribute("type") == "Part");

            Assert.NotNull(createdItem);

            createdId = (string?)createdItem.Attribute("id");

            Assert.False(string.IsNullOrWhiteSpace(createdId));
        }
        finally
        {
            // Clean up the Part created by this test only.
            if (!string.IsNullOrWhiteSpace(createdId))
            {
                await _aras.DeleteItemAsync(
                    "Part",
                    createdId);
            }
        }
    }

    [Fact]
    public async Task ExecuteAmlAsync_ShouldThrowArasException_WhenItemTypeIsInvalid()
    {
        // Arrange
        const string invalidAml = """
            <AML>
                <Item type="LIA_NONEXISTENT_ITEMTYPE_999"
                      action="get" />
            </AML>
            """;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArasException>(
            () => _aras.ExecuteAmlAsync(invalidAml));

        Assert.False(
            string.IsNullOrWhiteSpace(exception.FaultCode));

        Assert.NotEqual("0", exception.FaultCode);

        Assert.False(
            string.IsNullOrWhiteSpace(exception.FaultString));
    }
}
