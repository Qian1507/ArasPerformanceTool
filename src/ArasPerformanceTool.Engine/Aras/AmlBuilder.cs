
using System.Xml.Linq;

namespace ArasPerformanceTool.Engine.Aras;

public static class AmlBuilder
{
    // Create a new Aras item
    public static string Create(
        string itemType,
        IReadOnlyDictionary<string, string> properties)
    {
        var item = new XElement("Item",
            new XAttribute("type", itemType),
            new XAttribute("action", "add"));

        foreach (var property in properties)
        {
            item.Add(new XElement(
                property.Key,
                property.Value));
        }

        return Wrap(item);
    }

    // Read items by ItemType or ID
    public static string Read(
        string itemType,
        string? id = null,
        string? select = null)
    {
        var item = new XElement("Item",
            new XAttribute("type", itemType),
            new XAttribute("action", "get"));

        if (!string.IsNullOrWhiteSpace(id))
        {
            item.Add(new XAttribute("id", id));
        }

        if (!string.IsNullOrWhiteSpace(select))
        {
            item.Add(new XAttribute("select", select));
        }

        return Wrap(item);
    }

    // Update an existing Aras item
    public static string Update(
        string itemType,
        string id,
        IReadOnlyDictionary<string, string> properties)
    {
        var item = new XElement("Item",
            new XAttribute("type", itemType),
            new XAttribute("action", "edit"),
            new XAttribute("id", id));

        foreach (var property in properties)
        {
            item.Add(new XElement(
                property.Key,
                property.Value));
        }

        return Wrap(item);
    }

    // Delete an existing Aras item
    public static string Delete(
        string itemType,
        string id)
    {
        var item = new XElement("Item",
            new XAttribute("type", itemType),
            new XAttribute("action", "delete"),
            new XAttribute("id", id));

        return Wrap(item);
    }

    // Wrap an Item element inside an AML element
    private static string Wrap(XElement item)
    {
        return new XElement("AML", item)
            .ToString(SaveOptions.DisableFormatting);
    }
}
