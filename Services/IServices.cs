using SL_Cleaning.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SL_Cleaning.Services;

/// <summary>
/// Service interface for scanning installed software.
/// </summary>
public interface ISoftwareInventoryService
{
    /// <summary>
    /// Scans the system for installed software.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of installed software entries.</returns>
    Task<IReadOnlyList<SoftwareEntry>> ScanAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Service interface for uninstalling software.
/// </summary>
public interface IUninstallService
{
    /// <summary>
    /// Uninstalls a single software entry.
    /// </summary>
    /// <param name="entry">The software to uninstall.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the uninstall operation.</returns>
    Task<UninstallResult> UninstallAsync(SoftwareEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service interface for file save operations.
/// </summary>
public interface IFileSaveService
{
    /// <summary>
    /// Saves content to a file with user dialog.
    /// </summary>
    /// <param name="content">Content to save.</param>
    /// <param name="defaultFileName">Suggested file name.</param>
    /// <param name="filter">File type filter.</param>
    /// <returns>True if saved successfully.</returns>
    Task<bool> SaveWithDialogAsync(string content, string defaultFileName, string filter);
}
