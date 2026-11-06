
using System.Diagnostics;
using System.Xml.Linq;
using ArasPerformanceTool.Engine.Aras;
using ArasPerformanceTool.Engine.Benchmarking.Workloads;

namespace ArasPerformanceTool.Engine.Benchmarking;

public sealed record CleanupItemResult(
    string ItemType,
    string ItemId,
    TrackedItemStatus FinalStatus,
    string? ErrorMessage,
    TimeSpan? DeleteDuration,
    TimeSpan? VerificationDuration,
    bool AlreadyMissing = false);

public sealed record CleanupExecutionResult(
    int Found,
    int Deleted,
    int AlreadyMissing,
    int Failed,
    int PreexistingFailures,
    int Remaining,
    TimeSpan Duration,
    IReadOnlyList<CleanupItemResult> Items);

public sealed class TestDataCleanupService
{
    private readonly DeleteWorkload _delete;
    private readonly ReadWorkload _read;

    public TestDataCleanupService(IArasClient arasClient)
    {
        ArgumentNullException.ThrowIfNull(arasClient);

        _delete = new DeleteWorkload(arasClient);
        _read = new ReadWorkload(arasClient);
    }

   
public async Task<CleanupExecutionResult> CleanupAsync(
    TestDataManifest manifest,
    string expectedEnvironmentId,
    string serverUrl,
    string accessToken,
    string? targetItemType = null,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(manifest);

    if (string.IsNullOrWhiteSpace(manifest.EnvironmentId) ||
        !string.Equals(
            manifest.EnvironmentId,
            expectedEnvironmentId,
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "Cleanup environment does not match manifest.");
    }

    var totalWatch = Stopwatch.StartNew();

    // Snapshot all unresolved items for this cleanup execution.
    var targets = manifest.UnresolvedItems
        .Where(x =>
            targetItemType == null ||
            string.Equals(
                x.ItemType,
                targetItemType,
                StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(x => x.CreatedAtUtc)
        .ToArray();

    int preexistingFailures = targets.Count(x =>
        x.Status == TrackedItemStatus.CleanupFailed);

    var details = new List<CleanupItemResult>();

    int deleted = 0;
    int alreadyMissing = 0;

    foreach (var item in targets)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TimeSpan? deleteDuration = null;
        TimeSpan? verificationDuration = null;

        try
        {
            // STEP 1: Check existence before deletion.
            // Applies to Active, DeletePending and CleanupFailed.
            var previousCheck = await CheckMissingAsync(item);

            verificationDuration = previousCheck.Duration;

            if (previousCheck.Missing)
            {
                manifest.MarkAlreadyMissing(
                    item.ItemType,
                    item.Id);

                alreadyMissing++;

                details.Add(new CleanupItemResult(
                    item.ItemType,
                    item.Id,
                    TrackedItemStatus.AlreadyMissing,
                    null,
                    null,
                    verificationDuration,
                    true));

                continue;
            }

            // STEP 2: Persist deletion intent.
            // Avoid rewriting an already pending state.
            if (item.Status != TrackedItemStatus.DeletePending)
            {
                manifest.MarkPending(
                    item.ItemType,
                    item.Id);
            }

            // STEP 3: Execute Delete.
            var deleteWatch = Stopwatch.StartNew();

            string response;

            try
            {
                response = await _delete.ExecuteAsync(
                    serverUrl,
                    accessToken,
                    item.ItemType,
                    item.Id,
                    cancellationToken);
            }
            finally
            {
                deleteWatch.Stop();
                deleteDuration = deleteWatch.Elapsed;
            }

            var deleteXml = XDocument.Parse(response);
            var fault = GetFaultCode(deleteXml);

            // Fault code 0 is a known Aras no-items response.
            // Do not classify it as AlreadyMissing here.
            // Always verify the actual state by Read.
            if (fault != null && fault != "0")
            {
                throw new InvalidOperationException(
                    $"Aras Delete faultcode: {fault}");
            }

            // STEP 4: Verify the item no longer exists.
            var verification = await CheckMissingAsync(item);

            verificationDuration = verification.Duration;

            if (!verification.Missing)
            {
                throw new InvalidOperationException(
                    "Delete was not confirmed by Read.");
            }

            // STEP 5: Record successful deletion.
            // The item existed before Delete and is now missing.
            manifest.MarkDeleted(
                item.ItemType,
                item.Id);

            deleted++;

            details.Add(new CleanupItemResult(
                item.ItemType,
                item.Id,
                TrackedItemStatus.Deleted,
                null,
                deleteDuration,
                verificationDuration,
                false));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Keep the current state for recovery.
            throw;
        }
        catch (Exception ex)
        {
            // A single item failure should not stop cleanup.
            manifest.MarkFailed(
                item.ItemType,
                item.Id,
                ex.Message);

            details.Add(new CleanupItemResult(
                item.ItemType,
                item.Id,
                TrackedItemStatus.CleanupFailed,
                ex.Message,
                deleteDuration,
                verificationDuration));
        }
    }

    totalWatch.Stop();

    // Remaining includes Active, DeletePending and CleanupFailed.
    int remaining = manifest.UnresolvedItems.Count(x =>
        targetItemType == null ||
        string.Equals(
            x.ItemType,
            targetItemType,
            StringComparison.OrdinalIgnoreCase));

    return new CleanupExecutionResult(
        Found: targets.Length,
        Deleted: deleted,
        AlreadyMissing: alreadyMissing,
        Failed: details.Count(x =>
            x.FinalStatus == TrackedItemStatus.CleanupFailed),
        PreexistingFailures: preexistingFailures,
        Remaining: remaining,
        Duration: totalWatch.Elapsed,
        Items: details);

    // Read by ID and determine whether the item is missing.
    async Task<(bool Missing, TimeSpan Duration)>
        CheckMissingAsync(TrackedItem item)
    {
        var watch = Stopwatch.StartNew();

        string response;

        try
        {
            response = await _read.ExecuteAsync(
                serverUrl,
                accessToken,
                item.ItemType,
                id: item.Id,
                cancellationToken: cancellationToken);
        }
        finally
        {
            watch.Stop();
        }

        var xml = XDocument.Parse(response);
        var fault = GetFaultCode(xml);

        if (fault != null)
        {
            if (fault == "0")
            {
                return (true, watch.Elapsed);
            }

            throw new InvalidOperationException(
                $"Aras Read faultcode: {fault}");
        }

        bool exists = xml.Descendants().Any(x =>
            x.Name.LocalName == "Item" &&
            string.Equals(
                (string?)x.Attribute("id"),
                item.Id,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                (string?)x.Attribute("type"),
                item.ItemType,
                StringComparison.OrdinalIgnoreCase));

        // An unrecognized successful response must not
        // automatically be treated as proof of deletion.
        if (!exists)
        {
            throw new InvalidOperationException(
                "Read response did not contain the expected Item " +
                "or a recognized no-items fault.");
        }

        return (false, watch.Elapsed);
    }
}


    private static string? GetFaultCode(XDocument xml)
    {
        var fault = xml.Descendants()
            .FirstOrDefault(x => x.Name.LocalName == "Fault");

        if (fault == null)
            return null;

        return fault.Descendants()
            .FirstOrDefault(x =>
                x.Name.LocalName == "faultcode")?.Value
            ?? string.Empty;
    }
}
