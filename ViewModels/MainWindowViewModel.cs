using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SL_Cleaning.Models;
using SL_Cleaning.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;

namespace SL_Cleaning.ViewModels;

/// <summary>
/// Main ViewModel for the software cleaner application.
/// Implements MVVM pattern using CommunityToolkit.Mvvm.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    // ─────────────────────────────────────────────────────────────────────────
    // Services
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ISoftwareInventoryService _inventoryService;
    private readonly IUninstallService _uninstallService;

    // ─────────────────────────────────────────────────────────────────────────
    // Collections
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Master collection of all software entries.
    /// </summary>
    public ObservableCollection<SoftwareEntry> Software { get; } = new();

    /// <summary>
    /// Filtered/sorted view for DataGrid binding.
    /// </summary>
    public ICollectionView SoftwareView { get; }

    // ─────────────────────────────────────────────────────────────────────────
    // Observable Properties - State
    // ─────────────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownCount))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownCount))]
    private SourceFilterOption _selectedSourceFilter = SourceFilterOption.RegistryOnly;

    [ObservableProperty]
    private bool? _isAllSelected = false;

    /// <summary>
    /// Available source filter options for the dropdown.
    /// </summary>
    public SourceFilterOption[] SourceFilterOptions { get; } = Enum.GetValues<SourceFilterOption>();

    // ─────────────────────────────────────────────────────────────────────────
    // Observable Properties - Overlay State
    // ─────────────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmationMode))]
    [NotifyPropertyChangedFor(nameof(IsResultMode))]
    private bool _isOverlayVisible;

    [ObservableProperty]
    private string _overlayTitle = string.Empty;

    [ObservableProperty]
    private string _overlayMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmationMode))]
    [NotifyPropertyChangedFor(nameof(IsResultMode))]
    [NotifyCanExecuteChangedFor(nameof(UninstallSelectedCommand))]
    private bool _isUninstalling;

    [ObservableProperty]
    private string _currentUninstallItem = string.Empty;

    [ObservableProperty]
    private double _uninstallProgress;

    [ObservableProperty]
    private string _uninstallProgressText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUninstallLog))]
    private string _uninstallLog = string.Empty;

    private OverlayMode _currentOverlayMode = OverlayMode.None;

    // ─────────────────────────────────────────────────────────────────────────
    // Computed Properties
    // ─────────────────────────────────────────────────────────────────────────

    public bool IsNotBusy => !IsBusy;

    public int ShownCount => SoftwareView?.Cast<object>().Count() ?? 0;

    public int SelectedCount => Software.Count(s => s.IsSelected);

    public bool CanUninstall => SelectedCount > 0 && !IsUninstalling && !IsBusy;

    public bool IsConfirmationMode => IsOverlayVisible && _currentOverlayMode == OverlayMode.Confirmation;

    public bool IsResultMode => IsOverlayVisible && _currentOverlayMode == OverlayMode.Result;

    public bool IsDisclaimerMode => IsOverlayVisible && _currentOverlayMode == OverlayMode.Disclaimer;

    public bool HasUninstallLog => !string.IsNullOrWhiteSpace(UninstallLog);

    // ─────────────────────────────────────────────────────────────────────────
    // Cancellation
    // ─────────────────────────────────────────────────────────────────────────

    private CancellationTokenSource? _uninstallCts;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public MainWindowViewModel()
        : this(new PowerShellInventoryService(), new UninstallService())
    {
    }

    public MainWindowViewModel(ISoftwareInventoryService inventoryService, IUninstallService uninstallService)
    {
        _inventoryService = inventoryService;
        _uninstallService = uninstallService;

        // Initialize collection view with filtering and sorting
        SoftwareView = CollectionViewSource.GetDefaultView(Software);
        SoftwareView.Filter = FilterSoftware;
        SoftwareView.SortDescriptions.Add(new SortDescription(nameof(SoftwareEntry.DisplayName), ListSortDirection.Ascending));

        // Subscribe to collection changes for count updates
        Software.CollectionChanged += (_, _) => UpdateCounts();

        // Initial scan on load
        _ = LoadSoftwareAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Commands
    // ─────────────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task RefreshAsync()
    {
        await LoadSoftwareAsync();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var entry in Software.Where(s => SoftwareView.Filter?.Invoke(s) ?? true))
        {
            entry.IsSelected = true;
        }
        IsAllSelected = true;
        UpdateCounts();
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var entry in Software)
        {
            entry.IsSelected = false;
        }
        IsAllSelected = false;
        UpdateCounts();
    }

    [RelayCommand]
    private void ToggleSelection(SoftwareEntry? entry)
    {
        if (entry == null) return;
        entry.IsSelected = !entry.IsSelected;
        UpdateCounts();
    }

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private void UninstallSelected()
    {
        var selected = Software.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0) return;

        _currentOverlayMode = OverlayMode.Confirmation;
        OverlayTitle = "Confirm Uninstall";
        OverlayMessage = $"You are about to uninstall {selected.Count} application(s):\n\n" +
                         string.Join("\n", selected.Take(10).Select(s => $"• {s.DisplayName}")) +
                         (selected.Count > 10 ? $"\n... and {selected.Count - 10} more" : "") +
                         "\n\nThis action cannot be undone.";
        UninstallLog = string.Empty;
        IsOverlayVisible = true;

        OnPropertyChanged(nameof(IsConfirmationMode));
        OnPropertyChanged(nameof(IsResultMode));
    }

    [RelayCommand]
    private async Task ConfirmUninstallAsync()
    {
        _currentOverlayMode = OverlayMode.Progress;
        OnPropertyChanged(nameof(IsConfirmationMode));
        OnPropertyChanged(nameof(IsResultMode));

        IsUninstalling = true;
        UninstallProgress = 0;
        UninstallLog = string.Empty;

        _uninstallCts = new CancellationTokenSource();

        var selected = Software.Where(s => s.IsSelected).ToList();
        var results = new System.Collections.Generic.List<UninstallResult>();
        var startTime = DateTime.Now;

        try
        {
            for (int i = 0; i < selected.Count; i++)
            {
                if (_uninstallCts.Token.IsCancellationRequested)
                    break;

                var entry = selected[i];
                CurrentUninstallItem = $"Uninstalling: {entry.DisplayName}";
                UninstallProgress = (i * 100.0) / selected.Count;
                UninstallProgressText = $"{i + 1} of {selected.Count}";

                var result = await _uninstallService.UninstallAsync(entry, _uninstallCts.Token);
                results.Add(result);

                // Update log with detailed information
                var status = result.Success ? "✓" : "✗";
                UninstallLog += $"[{status}] {entry.DisplayName}\n";
                UninstallLog += $"    Method: {result.MethodUsed}\n";
                UninstallLog += $"    Exit Code: {result.ExitCode}\n";
                UninstallLog += $"    Duration: {result.Duration.TotalSeconds:F1}s\n";
                
                if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    UninstallLog += $"    Error: {result.ErrorMessage}\n";
                }
                
                if (!string.IsNullOrWhiteSpace(result.StandardError))
                {
                    UninstallLog += $"    StdErr: {result.StandardError.Trim()}\n";
                }
                
                UninstallLog += "\n";
            }
        }
        catch (OperationCanceledException)
        {
            UninstallLog += "\n[CANCELLED] Operation was cancelled by user.\n";
        }

        var batchResult = new UninstallBatchResult
        {
            Results = results,
            WasCancelled = _uninstallCts.Token.IsCancellationRequested,
            TotalDuration = DateTime.Now - startTime
        };

        // Show results
        IsUninstalling = false;
        UninstallProgress = 100;
        _currentOverlayMode = OverlayMode.Result;

        OverlayTitle = batchResult.WasCancelled ? "Uninstall Cancelled" : "Uninstall Complete";
        
        // Build detailed message with failure information
        var messageParts = new System.Text.StringBuilder();
        messageParts.AppendLine($"Processed: {batchResult.TotalCount}");
        messageParts.AppendLine($"Successful: {batchResult.SuccessCount}");
        messageParts.AppendLine($"Failed: {batchResult.FailureCount}");
        messageParts.AppendLine($"Duration: {batchResult.TotalDuration:mm\\:ss}");
        
        // Show failure details if any failed
        var failures = results.Where(r => !r.Success).ToList();
        if (failures.Count > 0)
        {
            messageParts.AppendLine();
            messageParts.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            messageParts.AppendLine("FAILED ITEMS:");
            messageParts.AppendLine();
            
            foreach (var failure in failures.Take(5)) // Show up to 5 failures
            {
                messageParts.AppendLine($"✗ {failure.Entry.DisplayName}");
                
                if (!string.IsNullOrWhiteSpace(failure.ErrorMessage))
                {
                    var errorMsg = failure.ErrorMessage.Length > 80 
                        ? failure.ErrorMessage.Substring(0, 77) + "..." 
                        : failure.ErrorMessage;
                    messageParts.AppendLine($"  └─ {errorMsg}");
                }
                else
                {
                    messageParts.AppendLine($"  └─ Exit code: {failure.ExitCode}");
                }
                messageParts.AppendLine();
            }
            
            if (failures.Count > 5)
            {
                messageParts.AppendLine($"... and {failures.Count - 5} more failures");
                messageParts.AppendLine("See full log for complete details.");
            }
        }
        else if (batchResult.SuccessCount > 0)
        {
            messageParts.AppendLine();
            messageParts.AppendLine("✓ All items uninstalled successfully!");
        }
        
        if (!batchResult.WasCancelled && batchResult.TotalCount > 0)
        {
            messageParts.AppendLine();
            messageParts.AppendLine("⚠ Note: Some software may report success");
            messageParts.AppendLine("but remain installed. Refresh to verify.");
        }
        
        OverlayMessage = messageParts.ToString();

        OnPropertyChanged(nameof(IsConfirmationMode));
        OnPropertyChanged(nameof(IsResultMode));

        // Remove successfully uninstalled items from the list
        foreach (var result in results.Where(r => r.Success))
        {
            Software.Remove(result.Entry);
        }

        UpdateCounts();
        _uninstallCts?.Dispose();
        _uninstallCts = null;
    }

    [RelayCommand]
    private void CancelUninstall()
    {
        _uninstallCts?.Cancel();
    }

    [RelayCommand]
    private void CloseOverlay()
    {
        IsOverlayVisible = false;
        _currentOverlayMode = OverlayMode.None;
        OnPropertyChanged(nameof(IsConfirmationMode));
        OnPropertyChanged(nameof(IsResultMode));
        OnPropertyChanged(nameof(IsDisclaimerMode));
    }

    [RelayCommand]
    private void ExportLog()
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|Log files (*.log)|*.log|All files (*.*)|*.*",
                DefaultExt = ".txt",
                FileName = $"SL-Cleaning-Log-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                File.WriteAllText(dialog.FileName, UninstallLog);
                StatusMessage = $"Log exported to {Path.GetFileName(dialog.FileName)}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ShowDisclaimer()
    {
        _currentOverlayMode = OverlayMode.Disclaimer;
        OverlayTitle = "⚠ Important Disclaimer";
        OverlayMessage = "USE AT YOUR OWN RISK\n\n" +
                         "This software is provided \"as is\" without warranty of any kind, either expressed or implied. " +
                         "The author(s) shall not be liable for any damages, including but not limited to data loss, " +
                         "system instability, or software malfunction resulting from the use of this application.\n\n" +
                         "PLEASE NOTE:\n" +
                         "• Always create a system restore point before uninstalling software\n" +
                         "• Uninstalling critical system software may cause system instability\n" +
                         "• Some software may require a system restart after uninstallation\n" +
                         "• The author(s) are not responsible for any consequences of software removal\n\n" +
                         "By using this application, you acknowledge that you understand and accept these risks.\n\n" +
                         "This disclaimer is required for Windows Store publication and general liability protection.";
        UninstallLog = string.Empty;
        IsOverlayVisible = true;

        OnPropertyChanged(nameof(IsConfirmationMode));
        OnPropertyChanged(nameof(IsResultMode));
        OnPropertyChanged(nameof(IsDisclaimerMode));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private Methods
    // ─────────────────────────────────────────────────────────────────────────

    private async Task LoadSoftwareAsync()
    {
        IsBusy = true;
        StatusMessage = "Scanning installed software...";

        try
        {
            Software.Clear();

            var entries = await _inventoryService.ScanAsync();

            foreach (var entry in entries.OrderBy(e => e.DisplayName))
            {
                // Subscribe to property changes for selection tracking
                entry.PropertyChanged += Entry_PropertyChanged;
                entry.IconSource = IconExtractor.GetDefaultIcon();
                Software.Add(entry);
                
                // Load icon asynchronously in background
                if (!string.IsNullOrWhiteSpace(entry.DisplayIcon))
                {
                    _ = Task.Run(() => LoadIconForEntry(entry));
                }
            }

            StatusMessage = $"Found {Software.Count} installed applications";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            UpdateCounts();
        }
    }

    /// <summary>
    /// Loads the icon for a software entry asynchronously.
    /// </summary>
    private void LoadIconForEntry(SoftwareEntry entry)
    {
        try
        {
            System.Windows.Media.ImageSource? resolvedIcon = null;

            foreach (var candidate in IconPathResolver.GetCandidates(entry))
            {
                var found = IconExtractor.TryGetIcon(candidate, out var icon);
                resolvedIcon = icon;

                if (found)
                {
                    break;
                }
            }

            resolvedIcon ??= IconExtractor.GetDefaultIcon();

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                entry.IconSource = resolvedIcon;
            });
        }
        catch
        {
            // Silently fail if icon extraction doesn't work
        }
    }

    private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SoftwareEntry.IsSelected))
        {
            UpdateCounts();
        }
    }

    private bool FilterSoftware(object obj)
    {
        if (obj is not SoftwareEntry entry)
            return false;

        // Apply source filter
        var passesSourceFilter = SelectedSourceFilter switch
        {
            SourceFilterOption.RegistryOnly => entry.Source == "Registry",
            SourceFilterOption.WindowsStoreOnly => entry.Source == "AppX",
            SourceFilterOption.All => true,
            _ => true
        };

        if (!passesSourceFilter)
            return false;

        // Apply search text filter
        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var search = SearchText.Trim();

        return entry.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               (entry.Publisher?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(ShownCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanUninstall));
        UninstallSelectedCommand.NotifyCanExecuteChanged();

        // Update IsAllSelected state
        var visibleItems = Software.Where(s => SoftwareView.Filter?.Invoke(s) ?? true).ToList();
        if (visibleItems.Count == 0)
        {
            IsAllSelected = false;
        }
        else if (visibleItems.All(s => s.IsSelected))
        {
            IsAllSelected = true;
        }
        else if (visibleItems.Any(s => s.IsSelected))
        {
            IsAllSelected = null; // Indeterminate
        }
        else
        {
            IsAllSelected = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        SoftwareView.Refresh();
        UpdateCounts();
    }

    partial void OnIsAllSelectedChanged(bool? value)
    {
        if (value == true)
        {
            SelectAll();
        }
        else if (value == false)
        {
            SelectNone();
        }
    }

    partial void OnSelectedSourceFilterChanged(SourceFilterOption value)
    {
        SoftwareView.Refresh();
        UpdateCounts();
    }

    private enum OverlayMode
    {
        None,
        Confirmation,
        Progress,
        Result,
        Disclaimer
    }
}
