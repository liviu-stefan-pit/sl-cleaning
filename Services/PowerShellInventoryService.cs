using SL_Cleaning.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SL_Cleaning.Services;

/// <summary>
/// Implementation of ISoftwareInventoryService using PowerShell script execution.
/// Runs an external PowerShell script that outputs JSON of installed software.
/// </summary>
public sealed class PowerShellInventoryService : ISoftwareInventoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Path to the PowerShell script. Defaults to Scripts/GetSoftwareScript.ps1 in app directory.
    /// </summary>
    public string ScriptPath { get; set; }

    public PowerShellInventoryService()
    {
        // Default script path relative to application directory
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        ScriptPath = Path.Combine(appDir, "Scripts", "GetSoftwareScript.ps1");
    }

    public PowerShellInventoryService(string scriptPath)
    {
        ScriptPath = scriptPath;
    }

    public async Task<IReadOnlyList<SoftwareEntry>> ScanAsync(CancellationToken cancellationToken = default)
    {
        // Verify script exists
        if (!File.Exists(ScriptPath))
        {
            // Try looking in the project directory (for development)
            var devPath = Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "GetSoftwareScript.ps1");
            if (File.Exists(devPath))
            {
                ScriptPath = devPath;
            }
            else
            {
                throw new FileNotFoundException(
                    $"PowerShell script not found at: {ScriptPath}\n" +
                    "Ensure GetSoftwareScript.ps1 exists in the Scripts folder.");
            }
        }

        var output = await RunPowerShellScriptAsync(ScriptPath, cancellationToken);

        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<SoftwareEntry>();
        }

        return ParseJsonOutput(output);
    }

    private async Task<string> RunPowerShellScriptAsync(string scriptPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

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

        // Wait for process with cancellation support
        try
        {
            await process.WaitForExitAsync(cancellationToken);
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
            throw;
        }

        var error = errorBuilder.ToString();
        if (!string.IsNullOrWhiteSpace(error) && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"PowerShell script failed: {error}");
        }

        return outputBuilder.ToString();
    }

    private IReadOnlyList<SoftwareEntry> ParseJsonOutput(string json)
    {
        try
        {
            // Trim any leading/trailing whitespace or BOM
            json = json.Trim().TrimStart('\uFEFF');

            // Handle empty array
            if (json == "[]" || string.IsNullOrEmpty(json))
            {
                return Array.Empty<SoftwareEntry>();
            }

            // Parse the JSON array
            var entries = JsonSerializer.Deserialize<List<SoftwareEntryDto>>(json, JsonOptions);

            if (entries == null)
            {
                return Array.Empty<SoftwareEntry>();
            }

            var result = new List<SoftwareEntry>();

            foreach (var dto in entries)
            {
                // Skip entries without a display name
                if (string.IsNullOrWhiteSpace(dto.Name))
                    continue;

                result.Add(new SoftwareEntry
                {
                    DisplayName = dto.Name!.Trim(),
                    Publisher = dto.Publisher?.Trim(),
                    DisplayVersion = dto.Version?.Trim(),
                    InstallDate = dto.InstallDate?.Trim(),
                    EstimatedSize = dto.Size,
                    Uninstallable = dto.Uninstallable,
                    UninstallString = dto.UninstallCommand?.Trim(),
                    QuietUninstallString = dto.QuietUninstallCommand?.Trim(),
                    ProductCode = dto.ProductCode?.Trim(),
                    RegistryPath = null,
                    InstallerType = dto.UninstallMethod?.Trim(),
                    Is64Bit = dto.Is64Bit,
                    InstallLocation = dto.InstallLocation?.Trim(),
                    ModifyPath = null,
                    WindowsInstaller = dto.UninstallMethod == "MSI",
                    Source = dto.Source?.Trim(),
                    PackageFullName = dto.Source == "AppX" ? dto.UninstallCommand : null
                });
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse PowerShell JSON output: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// DTO for JSON deserialization from PowerShell output.
    /// </summary>
    private sealed class SoftwareEntryDto
    {
        public string? Source { get; set; }
        public string? Name { get; set; }
        public string? Publisher { get; set; }
        public string? Version { get; set; }
        public string? InstallDate { get; set; }
        public long? Size { get; set; }
        public bool Uninstallable { get; set; }
        public string? UninstallCommand { get; set; }
        public string? QuietUninstallCommand { get; set; }
        public string? UninstallMethod { get; set; }
        public string? ProductCode { get; set; }
        public string? InstallLocation { get; set; }
        public bool Is64Bit { get; set; }
    }
}
