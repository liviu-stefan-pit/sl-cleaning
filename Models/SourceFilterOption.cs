namespace SL_Cleaning.Models;

/// <summary>
/// Filter options for software source.
/// </summary>
public enum SourceFilterOption
{
    /// <summary>
    /// Show only registry-based installed programs.
    /// </summary>
    RegistryOnly,

    /// <summary>
    /// Show only Windows Store (AppX/MSIX) apps.
    /// </summary>
    WindowsStoreOnly,

    /// <summary>
    /// Show all installed software from all sources.
    /// </summary>
    All
}
