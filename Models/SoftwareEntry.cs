using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Globalization;

namespace SL_Cleaning.Models;

/// <summary>
/// Represents an installed software entry parsed from PowerShell JSON output.
/// Uses ObservableObject for IsSelected binding support.
/// </summary>
public partial class SoftwareEntry : ObservableObject
{
    /// <summary>
    /// Unique identifier for this entry (generated at parse time).
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name of the software.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Publisher/vendor name.
    /// </summary>
    public string? Publisher { get; init; }

    /// <summary>
    /// Version string as displayed in registry.
    /// </summary>
    public string? DisplayVersion { get; init; }

    /// <summary>
    /// Raw install date string from registry (typically YYYYMMDD format).
    /// </summary>
    public string? InstallDate { get; init; }

    /// <summary>
    /// Estimated size in KB from registry.
    /// </summary>
    public long? EstimatedSize { get; init; }

    /// <summary>
    /// Full uninstall command string.
    /// </summary>
    public string? UninstallString { get; init; }

    /// <summary>
    /// Quiet/silent uninstall command string (preferred for automation).
    /// </summary>
    public string? QuietUninstallString { get; init; }

    /// <summary>
    /// MSI Product Code GUID if available.
    /// </summary>
    public string? ProductCode { get; init; }

    /// <summary>
    /// Registry key path where this entry was found.
    /// </summary>
    public string? RegistryPath { get; init; }

    /// <summary>
    /// Whether this entry is selected for uninstall.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    // ─────────────────────────────────────────────────────────────────────────
    // Computed Display Properties
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Formatted install date for display (e.g., "2024-03-15").
    /// </summary>
    public string? InstallDateFormatted
    {
        get
        {
            if (string.IsNullOrWhiteSpace(InstallDate))
                return null;

            // Try parsing YYYYMMDD format
            if (InstallDate.Length == 8 &&
                DateTime.TryParseExact(InstallDate, "yyyyMMdd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date.ToString("yyyy-MM-dd");
            }

            return InstallDate;
        }
    }

    /// <summary>
    /// Formatted size for display (e.g., "125 MB").
    /// </summary>
    public string? SizeFormatted
    {
        get
        {
            if (!EstimatedSize.HasValue || EstimatedSize.Value <= 0)
                return null;

            var sizeKb = EstimatedSize.Value;

            return sizeKb switch
            {
                < 1024 => $"{sizeKb} KB",
                < 1024 * 1024 => $"{sizeKb / 1024.0:F1} MB",
                _ => $"{sizeKb / (1024.0 * 1024.0):F2} GB"
            };
        }
    }

    /// <summary>
    /// Whether this entry can be uninstalled (has uninstall info).
    /// </summary>
    public bool CanUninstall =>
        !string.IsNullOrWhiteSpace(UninstallString) ||
        !string.IsNullOrWhiteSpace(QuietUninstallString) ||
        !string.IsNullOrWhiteSpace(ProductCode);

    /// <summary>
    /// Determines the best uninstall method available.
    /// </summary>
    public UninstallMethod PreferredUninstallMethod
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(QuietUninstallString))
                return UninstallMethod.QuietUninstallString;

            if (!string.IsNullOrWhiteSpace(ProductCode))
                return UninstallMethod.MsiProductCode;

            if (!string.IsNullOrWhiteSpace(UninstallString))
                return UninstallMethod.UninstallString;

            return UninstallMethod.None;
        }
    }

    public override string ToString() => DisplayName;
}

/// <summary>
/// Available methods for uninstalling software.
/// </summary>
public enum UninstallMethod
{
    None,
    UninstallString,
    QuietUninstallString,
    MsiProductCode
}
