
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace ArasPerformanceTool.Engine.Aras;

public record AmlMetadata(
    string ItemType,
    string Action,
    bool HasItemElement);

/// <summary>
/// Provides AML metadata extraction, validation, and preparation
/// for benchmark execution.
/// </summary>
public static class AmlParser
{
    /// <summary>
    /// Parses AML XML and extracts the ItemType and Action
    /// from the first top-level Item element.
    /// </summary>
    public static AmlMetadata ParseMetadata(string rawXml)
    {
        var document = ParseXml(rawXml);

        var item = GetTopLevelItems(document).FirstOrDefault();

        if (item is null)
        {
            return new AmlMetadata(
                "Unknown",
                "unknown",
                false);
        }

        var itemType = (string?)item.Attribute("type")
            ?? "GenericItem";

        var action = (string?)item.Attribute("action")
            ?? "unspecified";

        return new AmlMetadata(
            itemType,
            action,
            true);
    }

    /// <summary>
    /// Validates whether the AML payload is supported by
    /// the current read-only benchmark mode.
    ///
    /// Only one top-level Item with action="get" is supported.
    /// Nested Item elements must also use action="get".
    ///
    /// This validation is an application-level restriction,
    /// not a security sandbox. Aras permissions and server-side
    /// behavior must still be controlled independently.
    /// </summary>
    public static void ValidateReadOnlyAml(string rawXml)
    {
        var document = ParseXml(rawXml);

        var items = GetTopLevelItems(document).ToList();

        if (items.Count != 1)
        {
            throw new ArgumentException(
                "Read-only benchmark mode requires exactly one top-level Item.");
        }

        var rootItem = items[0];

        foreach (var item in rootItem.DescendantsAndSelf())
        {
            if (item.Name.LocalName != "Item")
            {
                continue;
            }

            var action = (string?)item.Attribute("action");

            if (!string.Equals(
                    action,
                    "get",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Read-only benchmark mode supports only Item elements with action='get'.");
            }

            var itemType = (string?)item.Attribute("type");

            if (string.IsNullOrWhiteSpace(itemType))
            {
                throw new ArgumentException(
                    "Every Item element must specify a valid type attribute.");
            }
        }

        // Reject explicit method execution elements.
        if (rootItem.DescendantsAndSelf().Any(e =>
                e.Name.LocalName is "Method" or "SQL"))
        {
            throw new ArgumentException(
                "Method and SQL elements are not supported in read-only benchmark mode.");
        }
    }

    /// <summary>
    /// Prepares AML for execution by replacing supported
    /// placeholders and ensuring an AML root element.
    ///
    /// Placeholder replacement should only be enabled for
    /// trusted benchmark templates.
    /// </summary>
    public static string PrepareExecutableAml(
        string rawXml,
        int iterationIndex)
    {
        if (iterationIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(iterationIndex));
        }

        if (string.IsNullOrWhiteSpace(rawXml))
        {
            throw new ArgumentException(
                "AML payload cannot be empty.",
                nameof(rawXml));
        }

        var uniqueToken =
            $"BENCH_{Guid.NewGuid():N}"[..18];

        var processed = rawXml
            .Replace("${UNIQUE}", uniqueToken)
            .Replace(
                "${INDEX}",
                iterationIndex.ToString(
                    CultureInfo.InvariantCulture));

        var document = ParseXml(processed);

        if (document.Root is null)
        {
            throw new ArgumentException("XML payload must contain a valid root element.", nameof(rawXml));
        }

        if (document.Root.Name.LocalName == "AML")
        {
            return document.Root.ToString(SaveOptions.DisableFormatting);
        }

        var wrapped = new XElement("AML", document.Root);
        return wrapped.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Parses XML while disabling DTD processing and
    /// external XML resolution.
    /// </summary>
    private static XDocument ParseXml(string rawXml)
    {
        if (string.IsNullOrWhiteSpace(rawXml))
        {
            throw new ArgumentException(
                "AML payload cannot be empty.",
                nameof(rawXml));
        }

        using var stringReader = new StringReader(rawXml);

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        using var reader = XmlReader.Create(
            stringReader,
            settings);

        return XDocument.Load(reader);
    }

    /// <summary>
    /// Returns top-level Item elements from either an AML
    /// root element or a standalone Item element.
    /// </summary>
    private static IEnumerable<XElement> GetTopLevelItems(
        XDocument document)
    {
        var root = document.Root;

        if (root is null)
        {
            return Enumerable.Empty<XElement>();
        }

        if (root.Name.LocalName == "Item")
        {
            return new[] { root };
        }

        if (root.Name.LocalName == "AML")
        {
            return root.Elements()
                .Where(e => e.Name.LocalName == "Item");
        }

        return Enumerable.Empty<XElement>();
    }
}
