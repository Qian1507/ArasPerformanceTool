
using System.Text.Json;

namespace ArasPerformanceTool.Engine.Benchmarking;

public sealed record PendingCleanupRun(
    string RunId,
    string EnvironmentId,
    DateTime CreatedAtUtc,
    int RemainingCount,
    string ManifestPath);

public sealed record ManifestLoadError(
    string ManifestPath,
    string ErrorMessage);

public sealed record ManifestScanResult(
    IReadOnlyList<PendingCleanupRun> PendingRuns,
    IReadOnlyList<ManifestLoadError> Errors);

/// <summary>
/// Scans persisted test data manifests and identifies
/// runs with unresolved items.
///
/// This service is read-only.
/// It does not modify manifests or delete Aras items.
/// </summary>
public sealed class TestDataManifestStore
{
    private readonly string _manifestDirectory;

    public TestDataManifestStore(string manifestDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            manifestDirectory);

        _manifestDirectory =
            Path.GetFullPath(manifestDirectory);
    }

    public ManifestScanResult Scan(
        string expectedEnvironmentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            expectedEnvironmentId);

        var pendingRuns = new List<PendingCleanupRun>();
        var errors = new List<ManifestLoadError>();

        // 1. Missing directory is not an error.
        if (!Directory.Exists(_manifestDirectory))
        {
            return new ManifestScanResult(
                pendingRuns,
                errors);
        }

        // 2. Enumerate files and handle directory-level failures.
        string[] paths;

        try
        {
            paths = Directory.GetFiles(
                _manifestDirectory,
                "*.json",
                SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(new ManifestLoadError(
                _manifestDirectory,
                $"Unable to scan manifest directory: {ex.Message}"));

            return new ManifestScanResult(
                pendingRuns,
                errors);
        }

        // 3. Process each manifest independently.
        foreach (var path in paths)
        {
            try
            {
                // Read environment metadata before loading.
                // Other environments are skipped, not treated
                // as corrupted files.
                using (var document = JsonDocument.Parse(
                    File.ReadAllText(path)))
                {
                    var root = document.RootElement;

                    if (root.ValueKind != JsonValueKind.Object ||
                        !root.TryGetProperty(
                            "EnvironmentId",
                            out var environmentProperty) ||
                        environmentProperty.ValueKind != JsonValueKind.String)
                    {
                        throw new InvalidDataException(
                            "Manifest EnvironmentId is missing or invalid.");
                    }

                    var storedEnvironmentId =
                        environmentProperty.GetString();

                    if (string.IsNullOrWhiteSpace(storedEnvironmentId))
                    {
                        throw new InvalidDataException(
                            "Manifest EnvironmentId is missing or empty.");
                    }

                    if (!string.Equals(
                        storedEnvironmentId,
                        expectedEnvironmentId,
                        StringComparison.Ordinal))
                    {
                        continue;
                    }
                }
                

                // 4. Load using existing manifest validation.
                // SchemaVersion and environment validation
                // remain the responsibility of Load().
                var manifest = TestDataManifest.Load(
                    path,
                    expectedEnvironmentId);

                // 5. Count only unresolved test items.
                var remainingCount =
                    manifest.UnresolvedItems.Count;

                // Fully resolved manifests remain on disk,
                // but do not require manual cleanup.
                if (remainingCount == 0)
                {
                    continue;
                }

                // 6. Record pending cleanup information.
                pendingRuns.Add(new PendingCleanupRun(
                    manifest.RunId,
                    manifest.EnvironmentId!,
                    manifest.CreatedAtUtc,
                    remainingCount,
                    path));
            }
            catch (Exception ex) when (
                ex is IOException or
                InvalidDataException or
                UnauthorizedAccessException or
                JsonException or
                InvalidOperationException or
                ArgumentException)
            {
                // A failed file must not prevent other
                // manifests from being scanned.
                errors.Add(new ManifestLoadError(
                    path,
                    ex.Message));
            }
        }

        // 7. Return newest pending runs first.
        return new ManifestScanResult(
            pendingRuns
                .OrderByDescending(x => x.CreatedAtUtc)
                .ToList(),
            errors);
    }
}
