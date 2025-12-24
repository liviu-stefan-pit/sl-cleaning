# GetSoftwareScript.ps1
# Scans installed software and outputs JSON array for SL-Cleaning app
# Expected fields: DisplayName, Publisher, DisplayVersion, InstallDate, EstimatedSize,
#                  UninstallString, QuietUninstallString, ProductCode, RegistryPath

param(
    [switch]$OnlyAfterWindowsInstall = $false,
    [switch]$IncludeMicrosoft = $false
)

$ErrorActionPreference = "SilentlyContinue"

function SafeStr($v) { 
    if ($null -eq $v) { $null } else { [string]$v } 
}

function Get-WindowsInstallDate {
    $v = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion").InstallDate
    if (-not $v) { return (Get-Date).AddYears(-50) }
    return ([DateTimeOffset]::FromUnixTimeSeconds([int64]$v)).LocalDateTime.Date
}

function Parse-RegInstallDate([string]$s) {
    if ([string]::IsNullOrWhiteSpace($s)) { return $null }
    if ($s -match '^\d{8}$') {
        $y = [int]$s.Substring(0,4); $m = [int]$s.Substring(4,2); $d = [int]$s.Substring(6,2)
        try { return (Get-Date -Year $y -Month $m -Day $d).Date } catch { return $null }
    }
    return $null
}

function Is-WindowsEssentialRegistry($item) {
    if ($item.SystemComponent -eq 1) { return $true }

    $name = SafeStr $item.DisplayName
    if ([string]::IsNullOrWhiteSpace($name)) { return $true }
    
    $loc = SafeStr $item.InstallLocation

    if ($loc -match '^(C:\\Windows\\|C:\\Program Files\\WindowsApps\\)') { return $true }

    $windowsNoisePatterns = @(
        '(?i)\bWindows (Update|SDK|Feature|Hotfix)\b',
        '(?i)\b(KB\d{6,})\b',
        '(?i)^Update for\b',
        '(?i)^Security Update for\b',
        '(?i)^Servicing Stack\b',
        '(?i)^Microsoft (Update Health Tools)\b'
    )
    foreach ($p in $windowsNoisePatterns) { if ($name -match $p) { return $true } }

    if (-not $IncludeMicrosoft) {
        $builtInMsAppPatterns = @('(?i)^Microsoft Edge\b','(?i)^OneDrive\b')
        foreach ($p in $builtInMsAppPatterns) { if ($name -match $p) { return $true } }
    }

    return $false
}

function Get-InstalledProgramsFromRegistry {
    param([datetime]$windowsInstallDate)
    
    $regPaths = @(
        @{ Path = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"; Type = "Registry64" },
        @{ Path = "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"; Type = "Registry32" },
        @{ Path = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"; Type = "RegistryUser" }
    )

    $results = @()

    foreach ($regInfo in $regPaths) {
        $basePath = $regInfo.Path
        
        if (-not (Test-Path $basePath)) { continue }
        
        $subkeys = Get-ChildItem -Path $basePath -ErrorAction SilentlyContinue
        
        foreach ($subkey in $subkeys) {
            $item = Get-ItemProperty -Path $subkey.PSPath -ErrorAction SilentlyContinue
            
            if (-not $item) { continue }
            if ([string]::IsNullOrWhiteSpace($item.DisplayName)) { continue }
            if (Is-WindowsEssentialRegistry $item) { continue }
            
            # Check install date filter
            if ($OnlyAfterWindowsInstall) {
                $d = Parse-RegInstallDate (SafeStr $item.InstallDate)
                if ($null -ne $d -and $d -lt $windowsInstallDate) { continue }
            }
            
            # Extract product code from path if it looks like a GUID
            $productCode = $null
            $keyName = $subkey.PSChildName
            if ($keyName -match '^\{[0-9A-Fa-f-]{36}\}$') {
                $productCode = $keyName
            }
            
            $results += [PSCustomObject]@{
                DisplayName          = SafeStr $item.DisplayName
                Publisher            = SafeStr $item.Publisher
                DisplayVersion       = SafeStr $item.DisplayVersion
                InstallDate          = SafeStr $item.InstallDate
                EstimatedSize        = if ($item.EstimatedSize) { [long]$item.EstimatedSize } else { $null }
                UninstallString      = SafeStr $item.UninstallString
                QuietUninstallString = SafeStr $item.QuietUninstallString
                ProductCode          = $productCode
                RegistryPath         = $subkey.PSPath -replace '^Microsoft\.PowerShell\.Core\\Registry::', ''
            }
        }
    }

    return $results
}

# Main execution
$winInstall = Get-WindowsInstallDate
$programs = Get-InstalledProgramsFromRegistry -windowsInstallDate $winInstall

# Sort and deduplicate by DisplayName
$programs = $programs | 
    Sort-Object DisplayName, DisplayVersion -Unique |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_.DisplayName) }

# Output as JSON array (UTF-8, no BOM)
$json = $programs | ConvertTo-Json -Depth 3 -Compress:$false

# Handle single item case (ConvertTo-Json doesn't wrap in array)
if ($programs.Count -eq 1) {
    $json = "[$json]"
}
elseif ($programs.Count -eq 0) {
    $json = "[]"
}

# Write to stdout
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Write-Output $json
