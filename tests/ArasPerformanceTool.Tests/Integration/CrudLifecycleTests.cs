
using System.Xml.Linq;
using ArasPerformanceTool.Engine.Benchmarking.Workloads;
using Xunit;

namespace ArasPerformanceTool.Tests.Integration;

[Collection("Aras Integration")]
public sealed class CrudLifecycleTests
{
    private readonly ArasFixture _aras;

    public CrudLifecycleTests(ArasFixture aras)
    {
        _aras = aras;
    }

    // Test 1: Create a Part
    [Fact]
    public async Task CreateWorkload_ShouldCreatePart()
    {
        // Arrange
        var workload = new CreateWorkload(_aras.Client);

        var properties = new Dictionary<string, string>
        {
            ["item_number"] = GenerateItemNumber(),
            ["name"] = "Create Workload Test"
        };

        // Act
        var result = await workload.ExecuteAsync(
            ArasFixture.ServerUrl,
            _aras.AccessToken,
            "Part",
            properties);

        // Assert
        var xml = XDocument.Parse(result);

        AssertNoSoapFault(xml);

        var createdItem = GetItem(xml, "Part");

        Assert.NotNull(createdItem);

        var id = (string?)createdItem.Attribute("id");

        Assert.False(string.IsNullOrWhiteSpace(id));
    }

    // Test 2: Read Parts
    [Fact]
    public async Task ReadWorkload_ShouldReturnParts()
    {
        // Arrange
        var workload = new ReadWorkload(_aras.Client);

        // Act
        var result = await workload.ExecuteAsync(
            ArasFixture.ServerUrl,
            _aras.AccessToken,
            "Part",
            select: "id,item_number,name");

        // Assert
        var xml = XDocument.Parse(result);

        AssertNoSoapFault(xml);

        var parts = xml.Descendants()
            .Where(e =>
                e.Name.LocalName == "Item" &&
                (string?)e.Attribute("type") == "Part")
            .ToList();

        Assert.NotEmpty(parts);
    }

    // Test 3: Create, Update, and Read the latest Part version
    [Fact]
    public async Task UpdateWorkload_ShouldUpdatePartName()
    {
        // Arrange
        var createWorkload = new CreateWorkload(_aras.Client);
        var updateWorkload = new UpdateWorkload(_aras.Client);
        var readWorkload = new ReadWorkload(_aras.Client);

        var itemNumber = GenerateItemNumber();

        // Step 1: Create a dedicated test Part
        var createResult = await createWorkload.ExecuteAsync(
            ArasFixture.ServerUrl,
            _aras.AccessToken,
            "Part",
            new Dictionary<string, string>
            {
                ["item_number"] = itemNumber,
                ["name"] = "Before Update"
            });

        var createXml = XDocument.Parse(createResult);

        AssertNoSoapFault(createXml);

        var createdItem = GetItem(createXml, "Part");

        Assert.NotNull(createdItem);

        var originalId = (string?)createdItem.Attribute("id");

        Assert.False(string.IsNullOrWhiteSpace(originalId));

        // Step 2: Update the Part
        var updateResult = await updateWorkload.ExecuteAsync(
            ArasFixture.ServerUrl,
            _aras.AccessToken,
            "Part",
            originalId!,
            new Dictionary<string, string>
            {
                ["name"] = "After Update"
            });

        var updateXml = XDocument.Parse(updateResult);

        AssertNoSoapFault(updateXml);

        // Step 3: Extract the ID returned by Update
        var updatedItem = GetItem(updateXml, "Part");

        Assert.NotNull(updatedItem);

        var updatedId = (string?)updatedItem.Attribute("id");

        Assert.False(string.IsNullOrWhiteSpace(updatedId));

        // Step 4: Read the updated Part
        var readResult = await readWorkload.ExecuteAsync(
            ArasFixture.ServerUrl,
            _aras.AccessToken,
            "Part",
            id: updatedId,
            select: "id,name");

        var readXml = XDocument.Parse(readResult);

        AssertNoSoapFault(readXml);

        // Step 5: Verify the updated property
        var part = GetItem(readXml, "Part");

        Assert.NotNull(part);

        var actualName = part.Element("name")?.Value;

        Assert.Equal("After Update", actualName);
    }

    // Test 4: Create and Delete a Part
    [Fact]
    public async Task DeleteWorkload_ShouldDeletePart()
    {
        // Arrange
        var createWorkload = new CreateWorkload(_aras.Client);
        var deleteWorkload = new DeleteWorkload(_aras.Client);
        var readWorkload = new ReadWorkload(_aras.Client);

        var itemNumber = GenerateItemNumber();

        // Step 1: Create a test Part
        var createResult = await createWorkload.ExecuteAsync(
            ArasFixture.ServerUrl,
            _aras.AccessToken,
            "Part",
            new Dictionary<string, string>
            {
                ["item_number"] = itemNumber,
                ["name"] = "To Be Deleted"
            });

        var createXml = XDocument.Parse(createResult);

        AssertNoSoapFault(createXml);

        var createdItem = GetItem(createXml, "Part");

        Assert.NotNull(createdItem);

        var targetId = (string?)createdItem.Attribute("id");

        Assert.False(string.IsNullOrWhiteSpace(targetId));

        // Step 2: Delete the Part
        var deleteResult = await deleteWorkload.ExecuteAsync(
            ArasFixture.ServerUrl,
            _aras.AccessToken,
            "Part",
            targetId!);

        var deleteXml = XDocument.Parse(deleteResult);

        AssertNoSoapFault(deleteXml);

        // Step 3: Verify deletion
        var readResult = await readWorkload.ExecuteAsync(
            ArasFixture.ServerUrl,
            _aras.AccessToken,
            "Part",
            id: targetId);

        AssertItemMissing(readResult, "Part", targetId!);
    }

    // Test 5: Full CRUD lifecycle for Document
    [Fact]
    public async Task CrudLifecycle_ShouldWorkForDocumentItemType()
    {
        // Arrange
        var createWorkload = new CreateWorkload(_aras.Client);
        var updateWorkload = new UpdateWorkload(_aras.Client);
        var readWorkload = new ReadWorkload(_aras.Client);
        var deleteWorkload = new DeleteWorkload(_aras.Client);

        const string itemType = "Document";

        var docNumber =
            $"DOC-{Guid.NewGuid():N}"[..24];

        // Step 1: Create Document
        var createResult = await createWorkload.ExecuteAsync(
            ArasFixture.ServerUrl,
            _aras.AccessToken,
            itemType,
            new Dictionary<string, string>
            {
                ["item_number"] = docNumber,
                ["name"] = "Initial Document Name",
                ["description"] = "Integration Test Payload"
            });

        var createXml = XDocument.Parse(createResult);

        AssertNoSoapFault(createXml);

        var createdItem = GetItem(createXml, itemType);

        Assert.NotNull(createdItem);

        var docId = (string?)createdItem.Attribute("id");

        Assert.False(string.IsNullOrWhiteSpace(docId));

        // Step 2: Update Document
        var updateResult = await updateWorkload.ExecuteAsync(
            ArasFixture.ServerUrl,
            _aras.AccessToken,
            itemType,
            docId!,
            new Dictionary<string, string>
            {
                ["name"] = "Updated Document Name"
            });

        var updateXml = XDocument.Parse(updateResult);

        AssertNoSoapFault(updateXml);

        var updatedItem = GetItem(updateXml, itemType);

        Assert.NotNull(updatedItem);

        var updatedId =
            (string?)updatedItem.Attribute("id") ?? docId;

        // Step 3: Read Document and verify update
        var readResult = await readWorkload.ExecuteAsync(
            ArasFixture.ServerUrl,
            _aras.AccessToken,
            itemType,
            id: updatedId,
            select: "id,item_number,name");

        var readXml = XDocument.Parse(readResult);

        AssertNoSoapFault(readXml);

        var docItem = GetItem(readXml, itemType);

        Assert.NotNull(docItem);

        Assert.Equal(
            "Updated Document Name",
            docItem.Element("name")?.Value);

        // Step 4: Delete Document
        var deleteResult = await deleteWorkload.ExecuteAsync(
            ArasFixture.ServerUrl,
            _aras.AccessToken,
            itemType,
            updatedId!);

        var deleteXml = XDocument.Parse(deleteResult);

        AssertNoSoapFault(deleteXml);

        // Step 5: Verify deletion
        var verifyResult = await readWorkload.ExecuteAsync(
            ArasFixture.ServerUrl,
            _aras.AccessToken,
            itemType,
            id: updatedId);

        AssertItemMissing(
            verifyResult,
            itemType,
            updatedId!);
    }

    // Generate a unique Part Number
    private static string GenerateItemNumber()
    {
        return $"LIA-PROJECT-{Guid.NewGuid():N}"[..24];
    }

    // Find the first Item of the requested type
    private static XElement? GetItem(
        XDocument xml,
        string itemType)
    {
        return xml.Descendants()
            .FirstOrDefault(e =>
                e.Name.LocalName == "Item" &&
                (string?)e.Attribute("type") == itemType);
    }

    // Verify no SOAP Fault
    private static void AssertNoSoapFault(XDocument xml)
    {
        var fault = xml.Descendants()
            .FirstOrDefault(e =>
                e.Name.LocalName == "Fault");

        Assert.Null(fault);
    }

    // Verify the Item no longer exists in Aras
    private static void AssertItemMissing(
        string responseXml,
        string itemType,
        string itemId)
    {
        var xml = XDocument.Parse(responseXml);

        var fault = xml.Descendants()
            .FirstOrDefault(e =>
                e.Name.LocalName == "Fault");

        if (fault != null)
        {
            // Aras faultcode 0: no items found
            var faultCode = fault.Descendants()
                .FirstOrDefault(e =>
                    e.Name.LocalName == "faultcode")?.Value;

            Assert.Equal("0", faultCode);
        }
        else
        {
            var remainingItem = xml.Descendants()
                .FirstOrDefault(e =>
                    e.Name.LocalName == "Item" &&
                    (string?)e.Attribute("type") == itemType &&
                    (string?)e.Attribute("id") == itemId);

            Assert.Null(remainingItem);
        }
    }
}
