using System;
using System.Collections.Generic;

namespace SL_Cleaning.Models;

/// <summary>
/// Result of an uninstall operation for a single software entry.
/// </summary>
public sealed class UninstallResult
{
    /// <summary>
    /// The software entry that was processed.
    /// </summary>
    public required SoftwareEntry Entry { get; init; }

    /// <summary>
    /// Whether the uninstall succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Exit code from the uninstall process (0 typically = success).
    /// </summary>
    public int ExitCode { get; init; }

    /// <summary>
    /// Error message if the uninstall failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Standard output from the uninstall process.
    /// </summary>
    public string? StandardOutput { get; init; }

    /// <summary>
    /// Standard error from the uninstall process.
    /// </summary>
    public string? StandardError { get; init; }

    /// <summary>
    /// The method used to uninstall.
    /// </summary>
    public UninstallMethod MethodUsed { get; init; }

    /// <summary>
    /// Time taken for the uninstall operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Timestamp when the uninstall completed.
    /// </summary>
    public DateTime CompletedAt { get; init; } = DateTime.Now;

    /// <summary>
    /// Creates a success result.
    /// </summary>
    public static UninstallResult Succeeded(SoftwareEntry entry, UninstallMethod method, TimeSpan duration, int exitCode = 0)
        => new()
        {
            Entry = entry,
            Success = true,
            ExitCode = exitCode,
            MethodUsed = method,
            Duration = duration
        };

    /// <summary>
    /// Creates a failure result.
    /// </summary>
    public static UninstallResult Failed(SoftwareEntry entry, string error, UninstallMethod method, TimeSpan duration, int exitCode = -1)
        => new()
        {
            Entry = entry,
            Success = false,
            ExitCode = exitCode,
            ErrorMessage = error,
            MethodUsed = method,
            Duration = duration
        };
}

/// <summary>
/// Summary of a batch uninstall operation.
/// </summary>
public sealed class UninstallBatchResult
{
    /// <summary>
    /// Individual results for each software entry.
    /// </summary>
    public IReadOnlyList<UninstallResult> Results { get; init; } = Array.Empty<UninstallResult>();

    /// <summary>
    /// Number of successful uninstalls.
    /// </summary>
    public int SuccessCount => Results.Count(r => r.Success);

    /// <summary>
    /// Number of failed uninstalls.
    /// </summary>
    public int FailureCount => Results.Count(r => !r.Success);

    /// <summary>
    /// Total count of processed entries.
    /// </summary>
    public int TotalCount => Results.Count;

    /// <summary>
    /// Whether all uninstalls succeeded.
    /// </summary>
    public bool AllSucceeded => FailureCount == 0 && TotalCount > 0;

    /// <summary>
    /// Whether the operation was cancelled.
    /// </summary>
    public bool WasCancelled { get; init; }

    /// <summary>
    /// Total time for the batch operation.
    /// </summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>
    /// Generates a summary log string.
    /// </summary>
    public string GetSummaryLog()
    {
        var lines = new List<string>
        {
            "═══════════════════════════════════════════════════════",
            "  UNINSTALL SUMMARY",
            "═══════════════════════════════════════════════════════",
            $"  Total processed: {TotalCount}",
            $"  Successful: {SuccessCount}",
            $"  Failed: {FailureCount}",
            $"  Duration: {TotalDuration:mm\\:ss}",
            WasCancelled ? "  Status: CANCELLED" : "",
            "═══════════════════════════════════════════════════════",
            ""
        };

        foreach (var result in Results)
        {
            var status = result.Success ? "✓ OK" : "✗ FAIL";
            lines.Add($"[{status}] {result.Entry.DisplayName}");
            
            if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                lines.Add($"       Error: {result.ErrorMessage}");
            }
        }

        return string.Join(Environment.NewLine, lines.Where(l => !string.IsNullOrEmpty(l)));
    }
}
