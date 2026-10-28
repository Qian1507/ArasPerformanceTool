
using System.Xml.Linq;
using ArasPerformanceTool.Engine.Aras;
using Xunit;

namespace ArasPerformanceTool.Tests.Unit;

public class AmlBuilderTests
{
    [Fact]
    public void Create_ShouldGenerateAddAml()
    {
        var aml = AmlBuilder.Create(
            "Part",
            new Dictionary<string, string>
            {
                ["item_number"] = "LIA-TEST-001",
                ["name"] = "Test Part"
            });

        var item = GetItem(aml);

        Assert.Equal("Part", (string?)item.Attribute("type"));
        Assert.Equal("add", (string?)item.Attribute("action"));
        Assert.Equal("LIA-TEST-001", item.Element("item_number")?.Value);
        Assert.Equal("Test Part", item.Element("name")?.Value);
    }

    [Fact]
    public void Read_ShouldGenerateGetAml()
    {
        var aml = AmlBuilder.Read("Part", "TEST_ID");

        var item = GetItem(aml);

        Assert.Equal("Part", (string?)item.Attribute("type"));
        Assert.Equal("get", (string?)item.Attribute("action"));
        Assert.Equal("TEST_ID", (string?)item.Attribute("id"));
    }

    [Fact]
    public void Update_ShouldGenerateEditAml()
    {
        var aml = AmlBuilder.Update(
            "Part",
            "TEST_ID",
            new Dictionary<string, string>
            {
                ["name"] = "Updated Part"
            });

        var item = GetItem(aml);

        Assert.Equal("edit", (string?)item.Attribute("action"));
        Assert.Equal("TEST_ID", (string?)item.Attribute("id"));
        Assert.Equal("Updated Part", item.Element("name")?.Value);
    }

    [Fact]
    public void Delete_ShouldGenerateDeleteAml()
    {
        var aml = AmlBuilder.Delete("Part", "TEST_ID");

        var item = GetItem(aml);

        Assert.Equal("delete", (string?)item.Attribute("action"));
        Assert.Equal("TEST_ID", (string?)item.Attribute("id"));
    }

    [Fact]
    public void Create_ShouldEscapeSpecialCharacters()
    {
        var aml = AmlBuilder.Create(
            "Part",
            new Dictionary<string, string>
            {
                ["name"] = "A & B <Test>"
            });

        var item = GetItem(aml);

        Assert.Equal("A & B <Test>", item.Element("name")?.Value);
    }

    [Fact]
    public void Read_WithSelect_ShouldIncludeSelectAttribute()
    {
        // Arrange & Act
        var aml = AmlBuilder.Read("Part", id: "12345", select: "id,item_number,name");

        // Assert
        var doc = XDocument.Parse(aml);
        var item = doc.Root?.Element("Item");

        Assert.NotNull(item);
        Assert.Equal("12345", (string?)item.Attribute("id"));
        Assert.Equal("id,item_number,name", (string?)item.Attribute("select"));
        Assert.Equal("get", (string?)item.Attribute("action"));
    }

    [Fact]
    public void Create_WithReadOnlyDictionary_ShouldRenderElementsCorrectly()
    {
        // Arrange
        IReadOnlyDictionary<string, string> props = new Dictionary<string, string>
        {
            ["item_number"] = "PRT-001",
            ["name"] = "Benchmark Test Part"
        };

        // Act
        var aml = AmlBuilder.Create("Part", props);

        // Assert
        var doc = XDocument.Parse(aml);
        var item = doc.Root?.Element("Item");

        Assert.NotNull(item);
        Assert.Equal("Part", (string?)item.Attribute("type"));
        Assert.Equal("add", (string?)item.Attribute("action"));
        Assert.Equal("PRT-001", (string?)item.Element("item_number"));
        Assert.Equal("Benchmark Test Part", (string?)item.Element("name"));
    }
    private static XElement GetItem(string aml)
    {
        var document = XDocument.Parse(aml);
        Assert.Equal("AML", document.Root?.Name.LocalName);

        return Assert.Single(document.Root!.Elements("Item"));
    }
}
