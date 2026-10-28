
using System.Xml.Linq;
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Benchmarking.Workloads;
using Xunit;

namespace ArasPerformanceTool.Tests.Integration;

public class CrudLifecycleTests
{
    private const string OAuthUrl =
        "http://localhost/InnovatorServer/OAuthServer/connect/token";

    private const string ServerUrl =
        "http://localhost/InnovatorServer/Server/InnovatorServer.aspx";

    private const string Database = "InnovatorSolutions";

    // Test 1: Create a Part
    [Fact]
    public async Task CreateWorkload_ShouldCreatePart()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var arasClient = new ArasClient(httpClient, OAuthUrl, Database);

        var token = await GetTokenAsync(arasClient);
        var workload = new CreateWorkload(arasClient);

        var itemNumber = GenerateItemNumber();

        var properties = new Dictionary<string, string>
        {
            ["item_number"] = itemNumber,
            ["name"] = "Create Workload Test"
        };

        // Act
        var result = await workload.ExecuteAsync(
            ServerUrl,
            token,
            "Part",
            properties);

        // Assert
        var xml = XDocument.Parse(result);

        AssertNoSoapFault(xml);

        var createdItem = GetPart(xml);

        Assert.NotNull(createdItem);

        var id = (string?)createdItem.Attribute("id");

        Assert.False(string.IsNullOrWhiteSpace(id));
    }

    // Test 2: Read Parts
    [Fact]
    public async Task ReadWorkload_ShouldReturnParts()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var arasClient = new ArasClient(httpClient, OAuthUrl, Database);

        var token = await GetTokenAsync(arasClient);
        var workload = new ReadWorkload(arasClient);

        // Act
        var result = await workload.ExecuteAsync(
            ServerUrl,
            token,
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
        using var httpClient = new HttpClient();
        var arasClient = new ArasClient(httpClient, OAuthUrl, Database);

        var token = await GetTokenAsync(arasClient);

        var createWorkload = new CreateWorkload(arasClient);
        var updateWorkload = new UpdateWorkload(arasClient);
        var readWorkload = new ReadWorkload(arasClient);

        var itemNumber = GenerateItemNumber();

        // Step 1: Create a dedicated test Part
        var createResult = await createWorkload.ExecuteAsync(
            ServerUrl,
            token,
            "Part",
            new Dictionary<string, string>
            {
                ["item_number"] = itemNumber,
                ["name"] = "Before Update"
            });

        var createXml = XDocument.Parse(createResult);

        AssertNoSoapFault(createXml);

        var createdItem = GetPart(createXml);

        Assert.NotNull(createdItem);

        var originalId = (string?)createdItem.Attribute("id");

        Assert.False(string.IsNullOrWhiteSpace(originalId));

        // Step 2: Update the Part
        var updateResult = await updateWorkload.ExecuteAsync(
            ServerUrl,
            token,
            "Part",
            originalId!,
            new Dictionary<string, string>
            {
                ["name"] = "After Update"
            });

        var updateXml = XDocument.Parse(updateResult);

        AssertNoSoapFault(updateXml);

        // Step 3: Extract the ID returned by Update
        var updatedItem = GetPart(updateXml);

        Assert.NotNull(updatedItem);

        var updatedId = (string?)updatedItem.Attribute("id");

        Assert.False(string.IsNullOrWhiteSpace(updatedId));

        // Step 4: Read the updated Part using its returned ID
        var readResult = await readWorkload.ExecuteAsync(
            ServerUrl,
            token,
            "Part",
            id: updatedId,
            select: "id,name");

        var readXml = XDocument.Parse(readResult);

        AssertNoSoapFault(readXml);

        // Step 5: Verify the updated property
        var part = GetPart(readXml);

        Assert.NotNull(part);

        var actualName = part.Element("name")?.Value;

        Assert.Equal("After Update", actualName);
    }

    // Generate a unique Part Number within the 32-character limit
    private static string GenerateItemNumber()
    {
        return $"LIA-PROJECT-{Guid.NewGuid():N}"[..24];
    }

    // Acquire an OAuth access token
    private static async Task<string> GetTokenAsync(ArasClient arasClient)
    {
        var username = Environment.GetEnvironmentVariable("ARAS_USERNAME");
        var password = Environment.GetEnvironmentVariable("ARAS_PASSWORD");

        Assert.False(string.IsNullOrWhiteSpace(username));
        Assert.False(string.IsNullOrWhiteSpace(password));

        return await arasClient.GetAccessTokenAsync(
            username!,
            password!);
    }

    // Find the first Part item in an Aras response
    private static XElement? GetPart(XDocument xml)
    {
        return xml.Descendants()
            .FirstOrDefault(e =>
                e.Name.LocalName == "Item" &&
                (string?)e.Attribute("type") == "Part");
    }

    // Verify the response does not contain a SOAP Fault
    private static void AssertNoSoapFault(XDocument xml)
    {
        var fault = xml.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Fault");

        Assert.Null(fault);
    }

    // Test 4: Create and Delete a Part
    [Fact]
    public async Task DeleteWorkload_ShouldDeletePart()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var arasClient = new ArasClient(httpClient, OAuthUrl, Database);

        var token = await GetTokenAsync(arasClient);

        var createWorkload = new CreateWorkload(arasClient);
        var deleteWorkload = new DeleteWorkload(arasClient);
        var readWorkload = new ReadWorkload(arasClient);

        var itemNumber = GenerateItemNumber();

        // Step 1: Create a test Part
        var createResult = await createWorkload.ExecuteAsync(
            ServerUrl,
            token,
            "Part",
            new Dictionary<string, string>
            {
                ["item_number"] = itemNumber,
                ["name"] = "To Be Deleted"
            });

        var createXml = XDocument.Parse(createResult);
        AssertNoSoapFault(createXml);

        var createdItem = GetPart(createXml);
        Assert.NotNull(createdItem);

        var targetId = (string?)createdItem.Attribute("id");
        Assert.False(string.IsNullOrWhiteSpace(targetId));

        // Step 2: Delete the Part
        var deleteResult = await deleteWorkload.ExecuteAsync(
            ServerUrl,
            token,
            "Part",
            targetId!);

        var deleteXml = XDocument.Parse(deleteResult);
        AssertNoSoapFault(deleteXml);

        
        // Step 3: Verify that the deleted Part no longer exists
        var readResult = await readWorkload.ExecuteAsync(
            ServerUrl,
            token,
            "Part",
            id: targetId);

        var readXml = XDocument.Parse(readResult);

        // Check for SOAP Fault
        var fault = readXml.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Fault");

        if (fault != null)
        {
            // Aras AML error code 0 means no items found
            var faultCode = fault.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "faultcode")?.Value
                ?? fault.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "faultcode")?.Value;

            Assert.Equal("0", faultCode);
        }
        else
        {
            // No fault: the response must contain no matching Part
            var remainingPart = readXml.Descendants()
                .FirstOrDefault(e =>
                    e.Name.LocalName == "Item" &&
                    (string?)e.Attribute("type") == "Part" &&
                    (string?)e.Attribute("id") == targetId);

            Assert.Null(remainingPart);
        }      
    }

    // Test 5: Verify full CRUD lifecycle on a second ItemType (Document)
    [Fact]
    public async Task CrudLifecycle_ShouldWorkForDocumentItemType()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var arasClient = new ArasClient(httpClient, OAuthUrl, Database);

        var token = await GetTokenAsync(arasClient);

        var createWorkload = new CreateWorkload(arasClient);
        var updateWorkload = new UpdateWorkload(arasClient);
        var readWorkload = new ReadWorkload(arasClient);
        var deleteWorkload = new DeleteWorkload(arasClient);

        const string itemType = "Document";
        var docNumber = $"DOC-{Guid.NewGuid():N}"[..24];

        // 1. Create Document
        var createResult = await createWorkload.ExecuteAsync(
            ServerUrl,
            token,
            itemType,
            new Dictionary<string, string>
            {
                ["item_number"] = docNumber,
                ["name"] = "Initial Document Name",
                ["description"] = "Integration Test Payload"
            });

        var createXml = XDocument.Parse(createResult);
        AssertNoSoapFault(createXml);

        var createdItem = createXml.Descendants()
            .FirstOrDefault(e =>
                e.Name.LocalName == "Item" &&
                (string?)e.Attribute("type") == itemType);

        Assert.NotNull(createdItem);
        var docId = (string?)createdItem.Attribute("id");
        Assert.False(string.IsNullOrWhiteSpace(docId));

        // 2. Update Document
        var updateResult = await updateWorkload.ExecuteAsync(
            ServerUrl,
            token,
            itemType,
            docId!,
            new Dictionary<string, string>
            {
                ["name"] = "Updated Document Name"
            });

        var updateXml = XDocument.Parse(updateResult);
        AssertNoSoapFault(updateXml);

        var updatedItem = updateXml.Descendants()
            .FirstOrDefault(e =>
                e.Name.LocalName == "Item" &&
                (string?)e.Attribute("type") == itemType);

        Assert.NotNull(updatedItem);
        var updatedId = (string?)updatedItem.Attribute("id") ?? docId;

        // 3. Read Document and verify property update
        var readResult = await readWorkload.ExecuteAsync(
            ServerUrl,
            token,
            itemType,
            id: updatedId,
            select: "id,item_number,name");

        var readXml = XDocument.Parse(readResult);
        AssertNoSoapFault(readXml);

        var docItem = readXml.Descendants()
            .FirstOrDefault(e =>
                e.Name.LocalName == "Item" &&
                (string?)e.Attribute("type") == itemType);

        Assert.NotNull(docItem);
        Assert.Equal("Updated Document Name", docItem.Element("name")?.Value);

        // 4. Delete Document
        var deleteResult = await deleteWorkload.ExecuteAsync(
            ServerUrl,
            token,
            itemType,
            updatedId);

        var deleteXml = XDocument.Parse(deleteResult);
        AssertNoSoapFault(deleteXml);

        
        // 5. Verify Document is removed
        var verifyResult = await readWorkload.ExecuteAsync(
            ServerUrl,
            token,
            itemType,
            id: updatedId);

        var verifyXml = XDocument.Parse(verifyResult);

        var fault = verifyXml.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Fault");

        if (fault != null)
        {
            // Verify Aras returned the "no items found" code
            var faultCode = fault.Descendants()
                .FirstOrDefault(e =>
                    e.Name.LocalName == "faultcode")?.Value;

            Assert.Equal("0", faultCode);
        }
        else
        {
            // Verify that the deleted Document is absent
            var remainingDoc = verifyXml.Descendants()
                .FirstOrDefault(e =>
                    e.Name.LocalName == "Item" &&
                    (string?)e.Attribute("type") == itemType &&
                    (string?)e.Attribute("id") == updatedId);

            Assert.Null(remainingDoc);
        }

    }

}
