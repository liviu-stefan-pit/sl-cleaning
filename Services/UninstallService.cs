using SL_Cleaning.Models;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SL_Cleaning.Services;

/// <summary>
/// Implementation of IUninstallService for uninstalling software.
/// Supports MSI product codes, quiet uninstall strings, and standard uninstall strings.
/// 
/// WARNING: This service executes system commands. Use with caution.
/// In production, consider adding additional safety checks and user prompts.
/// </summary>
public sealed class UninstallService : IUninstallService
{
    /// <summary>
    /// Timeout for uninstall operations (default: 5 minutes).
    /// </summary>
    public TimeSpan UninstallTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// If true, simulates uninstalls without actually executing them (for testing).
    /// </summary>
    public bool DryRun { get; set; } = false;

    public async Task<UninstallResult> UninstallAsync(SoftwareEntry entry, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Determine the best uninstall method
            var method = entry.PreferredUninstallMethod;

            if (method == UninstallMethod.None)
            {
                return UninstallResult.Failed(
                    entry,
                    "No uninstall method available for this software.",
                    UninstallMethod.None,
                    stopwatch.Elapsed);
            }

            // DRY RUN MODE - for testing
            if (DryRun)
            {
                await Task.Delay(500, cancellationToken); // Simulate work
                stopwatch.Stop();
                return UninstallResult.Succeeded(entry, method, stopwatch.Elapsed);
            }

            // Execute the appropriate uninstall method
            return method switch
            {
                UninstallMethod.QuietUninstallString => await ExecuteQuietUninstallAsync(entry, stopwatch, cancellationToken),
                UninstallMethod.MsiProductCode => await ExecuteMsiUninstallAsync(entry, stopwatch, cancellationToken),
                UninstallMethod.UninstallString => await ExecuteStandardUninstallAsync(entry, stopwatch, cancellationToken),
                UninstallMethod.AppxPackage => await ExecuteAppxUninstallAsync(entry, stopwatch, cancellationToken),
                UninstallMethod.WindowsCapability => await ExecuteCapabilityUninstallAsync(entry, stopwatch, cancellationToken),
                _ => UninstallResult.Failed(entry, "Unknown uninstall method.", method, stopwatch.Elapsed)
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return UninstallResult.Failed(
                entry,
                "Uninstall was cancelled.",
                entry.PreferredUninstallMethod,
                stopwatch.Elapsed,
                -1);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return UninstallResult.Failed(
                entry,
                ex.Message,
                entry.PreferredUninstallMethod,
                stopwatch.Elapsed,
                -1);
        }
    }

    /// <summary>
    /// Executes uninstall for Windows Store (AppX/MSIX) apps using PowerShell Remove-AppxPackage.
    /// </summary>
    private async Task<UninstallResult> ExecuteAppxUninstallAsync(
        SoftwareEntry entry,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var packageFullName = entry.PackageFullName!;
        
        // Use PowerShell to remove the AppX package
        var command = $"powershell.exe -NoProfile -NonInteractive -Command \"Get-AppxPackage -AllUsers | Where-Object {{ $_.PackageFullName -eq '{packageFullName}' }} | Remove-AppxPackage -AllUsers\"";

        var (exitCode, output, error) = await ExecuteCommandElevatedAsync(command, cancellationToken);

        stopwatch.Stop();

        var success = exitCode == 0;

        return success
            ? UninstallResult.Succeeded(entry, UninstallMethod.AppxPackage, stopwatch.Elapsed, exitCode)
            : UninstallResult.Failed(entry, error ?? $"Exit code: {exitCode}", UninstallMethod.AppxPackage, stopwatch.Elapsed, exitCode);
    }

    /// <summary>
    /// Executes uninstall for Windows Capabilities using PowerShell Remove-WindowsCapability.
    /// </summary>
    private async Task<UninstallResult> ExecuteCapabilityUninstallAsync(
        SoftwareEntry entry,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var capabilityName = entry.PackageFullName!;
        
        // Use PowerShell to remove the Windows Capability
        var command = $"powershell.exe -NoProfile -NonInteractive -Command \"Remove-WindowsCapability -Online -Name '{capabilityName}'\"";

        var (exitCode, output, error) = await ExecuteCommandElevatedAsync(command, cancellationToken);

        stopwatch.Stop();

        var success = exitCode == 0;

        return success
            ? UninstallResult.Succeeded(entry, UninstallMethod.WindowsCapability, stopwatch.Elapsed, exitCode)
            : UninstallResult.Failed(entry, error ?? $"Exit code: {exitCode}", UninstallMethod.WindowsCapability, stopwatch.Elapsed, exitCode);
    }

    /// <summary>
    /// Executes uninstall using QuietUninstallString.
    /// </summary>
    private async Task<UninstallResult> ExecuteQuietUninstallAsync(
        SoftwareEntry entry, 
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var command = entry.QuietUninstallString!;
        var (exitCode, output, error) = await ExecuteCommandAsync(command, cancellationToken);

        stopwatch.Stop();

        // Exit code 0 or 3010 (reboot required) are considered success
        var success = exitCode == 0 || exitCode == 3010;

        return success
            ? UninstallResult.Succeeded(entry, UninstallMethod.QuietUninstallString, stopwatch.Elapsed, exitCode)
            : UninstallResult.Failed(entry, error ?? $"Exit code: {exitCode}", UninstallMethod.QuietUninstallString, stopwatch.Elapsed, exitCode);
    }

    /// <summary>
    /// Executes uninstall using MSI product code with msiexec.
    /// </summary>
    private async Task<UninstallResult> ExecuteMsiUninstallAsync(
        SoftwareEntry entry,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var productCode = entry.ProductCode!;
        
        // Build msiexec command for quiet uninstall
        // /x = uninstall, /qn = quiet no UI, /norestart = don't restart
        var command = $"msiexec.exe /x {productCode} /qn /norestart";

        var (exitCode, output, error) = await ExecuteCommandAsync(command, cancellationToken);

        stopwatch.Stop();

        // MSI exit codes: 0 = success, 3010 = reboot required, 1605 = not installed
        var success = exitCode == 0 || exitCode == 3010 || exitCode == 1605;

        return success
            ? UninstallResult.Succeeded(entry, UninstallMethod.MsiProductCode, stopwatch.Elapsed, exitCode)
            : UninstallResult.Failed(entry, GetMsiErrorMessage(exitCode) ?? error ?? $"MSI exit code: {exitCode}", 
                UninstallMethod.MsiProductCode, stopwatch.Elapsed, exitCode);
    }

    /// <summary>
    /// Executes standard UninstallString.
    /// Uses shell execute with elevation for better compatibility.
    /// </summary>
    private async Task<UninstallResult> ExecuteStandardUninstallAsync(
        SoftwareEntry entry,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var command = entry.UninstallString!;

        // Try to add silent flags based on installer type
        if (command.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            // Add quiet flags if not already present
            if (!command.Contains("/q", StringComparison.OrdinalIgnoreCase))
            {
                command += " /qn /norestart";
            }
        }
        else
        {
            // For non-MSI installers, append silent switches based on detected type
            var silentSwitches = entry.GetSilentSwitches();
            if (!string.IsNullOrEmpty(silentSwitches) && !HasAnySilentSwitch(command))
            {
                command += " " + silentSwitches;
            }
        }

        // Use elevated execution for better compatibility
        var (exitCode, output, error) = await ExecuteCommandElevatedAsync(command, cancellationToken);

        stopwatch.Stop();

        var success = exitCode == 0 || exitCode == 3010;

        return success
            ? UninstallResult.Succeeded(entry, UninstallMethod.UninstallString, stopwatch.Elapsed, exitCode)
            : UninstallResult.Failed(entry, error ?? $"Exit code: {exitCode}", UninstallMethod.UninstallString, stopwatch.Elapsed, exitCode);
    }

    /// <summary>
    /// Executes a command line string and captures output.
    /// </summary>
    private async Task<(int ExitCode, string? Output, string? Error)> ExecuteCommandAsync(
        string command,
        CancellationToken cancellationToken)
    {
        // Parse the command to extract executable and arguments
        var (executable, arguments) = ParseCommand(command);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        return await RunProcessWithOutputAsync(startInfo, cancellationToken);
    }

    /// <summary>
    /// Executes a command with elevation (Run as Administrator).
    /// Note: Cannot capture output when using shell execute with elevation.
    /// </summary>
    private async Task<(int ExitCode, string? Output, string? Error)> ExecuteCommandElevatedAsync(
        string command,
        CancellationToken cancellationToken)
    {
        var (executable, arguments) = ParseCommand(command);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas"  // Request elevation
        };

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled UAC prompt
            return (-1, null, "User cancelled the elevation request.");
        }

        // Wait with timeout and cancellation
        using var timeoutCts = new CancellationTokenSource(UninstallTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore kill errors
            }

            if (timeoutCts.IsCancellationRequested)
            {
                return (-1, null, "Uninstall operation timed out.");
            }
            throw;
        }

        return (process.ExitCode, null, null);
    }

    /// <summary>
    /// Runs a process and captures its output.
    /// </summary>
    private async Task<(int ExitCode, string? Output, string? Error)> RunProcessWithOutputAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
                outputBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait with timeout and cancellation
        using var timeoutCts = new CancellationTokenSource(UninstallTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore kill errors
            }

            if (timeoutCts.IsCancellationRequested)
            {
                return (-1, null, "Uninstall operation timed out.");
            }
            throw;
        }

        return (process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
    }

    /// <summary>
    /// Checks if the command already contains common silent/quiet switches.
    /// </summary>
    private static bool HasAnySilentSwitch(string command)
    {
        var silentPatterns = new[]
        {
            "/S", "/s", "/silent", "/Silent", "/SILENT",
            "/quiet", "/Quiet", "/QUIET", "/q",
            "/VERYSILENT", "-silent", "-quiet",
            "/SUPPRESSMSGBOXES", "/norestart"
        };

        return silentPatterns.Any(p => command.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Parses a command string into executable and arguments.
    /// Handles quoted paths.
    /// </summary>
    private static (string Executable, string Arguments) ParseCommand(string command)
    {
        command = command.Trim();

        // Check if it starts with a quoted path
        if (command.StartsWith('"'))
        {
            var endQuote = command.IndexOf('"', 1);
            if (endQuote > 0)
            {
                var exe = command[1..endQuote];
                var args = command[(endQuote + 1)..].TrimStart();
                return (exe, args);
            }
        }

        // Check for common patterns like "cmd /c" or direct executable
        var match = Regex.Match(command, @"^(\S+\.exe)\s*(.*)$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return (match.Groups[1].Value, match.Groups[2].Value);
        }

        // Fallback: use cmd.exe to execute the command
        return ("cmd.exe", $"/c \"{command}\"");
    }

    /// <summary>
    /// Gets a human-readable error message for common MSI error codes.
    /// </summary>
    private static string? GetMsiErrorMessage(int exitCode)
    {
        return exitCode switch
        {
            1601 => "Windows Installer service could not be accessed.",
            1602 => "User cancelled the installation.",
            1603 => "Fatal error during installation.",
            1604 => "Installation suspended, incomplete.",
            1605 => "Product is not currently installed.",
            1618 => "Another installation is in progress.",
            1619 => "Installation package could not be opened.",
            1620 => "Installation package path could not be found.",
            1622 => "Error opening installation log file.",
            1623 => "Language not supported by this installation package.",
            1625 => "Installation prohibited by system policy.",
            1638 => "Another version of this product is already installed.",
            1639 => "Invalid command line argument.",
            3010 => "Restart required to complete the installation.",
            _ => null
        };
    }
}
