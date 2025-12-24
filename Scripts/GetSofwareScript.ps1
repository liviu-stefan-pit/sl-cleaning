param(
    [switch]$OnlyAfterWindowsInstall = $true,
    [switch]$IncludeMicrosoft = $false
)

$ErrorActionPreference = "SilentlyContinue"

function SafeStr($v) { if ($null -eq $v) { "" } else { [string]$v } }

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
    $loc  = SafeStr $item.InstallLocation

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
        $builtInMsAppPatterns = @('( ?i)^Microsoft Edge\b','(?i)^OneDrive\b')
        foreach ($p in $builtInMsAppPatterns) { if ($name -match $p) { return $true } }
    }

    return $false
}

function Get-InstalledProgramsFiltered([datetime]$windowsInstallDate) {
    $regPaths = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )

    $raw = foreach ($path in $regPaths) {
        Get-ItemProperty $path |
            Where-Object { $_.DisplayName -and $_.DisplayName.Trim() -ne "" } |
            Select-Object DisplayName, DisplayVersion, Publisher, InstallDate, InstallLocation, SystemComponent,
                @{Name="Source"; Expression={
                    if ($path -like "*WOW6432Node*") { "Registry32" }
                    elseif ($path -like "HKCU:*") { "RegistryUser" }
                    else { "Registry64" }
                }}
    }

    $items = $raw | Where-Object { -not (Is-WindowsEssentialRegistry $_) }

    if ($OnlyAfterWindowsInstall) {
        $items = $items | Where-Object {
            $d = Parse-RegInstallDate (SafeStr $_.InstallDate)
            if ($null -eq $d) { $true } else { $d -ge $windowsInstallDate }
        }
    }

    $items |
        Sort-Object DisplayName, DisplayVersion, Publisher -Unique |
        Select-Object `
            @{Name="Name"; Expression={ SafeStr $_.DisplayName }},
            @{Name="Version"; Expression={ SafeStr $_.DisplayVersion }},
            @{Name="Publisher"; Expression={ SafeStr $_.Publisher }},
            @{Name="InstallDate"; Expression={
                $d = Parse-RegInstallDate (SafeStr $_.InstallDate)
                if ($null -eq $d) { $null } else { $d.ToString("yyyy-MM-dd") }
            }},
            @{Name="InstallLocation"; Expression={ SafeStr $_.InstallLocation }},
            @{Name="Source"; Expression={ SafeStr $_.Source }}
}

function Is-WindowsEssentialAppx($pkg) {
    $name = SafeStr $pkg.Name
    $pub  = SafeStr $pkg.Publisher
    $loc  = SafeStr $pkg.InstallLocation

    if ($loc -match '^(C:\\Windows\\SystemApps\\)') { return $true }
    if ($pkg.IsFramework -eq $true) { return $true }

    if (-not $IncludeMicrosoft) {
        if ($pub -match '(?i)Microsoft Corporation' -or $pub -match '(?i)CN=Microsoft') { return $true }
        if ($name -match '(?i)^Microsoft\.' ) { return $true }
        if ($name -match '(?i)^MicrosoftWindows\.' ) { return $true }
        if ($name -match '(?i)^Windows\.' ) { return $true }
    }

    return $false
}

function Get-StoreAppsFiltered {
    Get-AppxPackage |
        Where-Object { -not (Is-WindowsEssentialAppx $_) } |
        Select-Object `
            @{Name="Name"; Expression={ SafeStr $_.Name }},
            @{Name="Version"; Expression={ SafeStr $_.Version }},
            @{Name="Publisher"; Expression={ SafeStr $_.Publisher }},
            @{Name="InstallDate"; Expression={ $null }},
            @{Name="InstallLocation"; Expression={ SafeStr $_.InstallLocation }},
            @{Name="Source"; Expression={ "Appx" }}
}

$winInstall = Get-WindowsInstallDate
$programs  = Get-InstalledProgramsFiltered -windowsInstallDate $winInstall
$storeApps = Get-StoreAppsFiltered

$all = @()
$all += $programs
$all += $storeApps
$all = $all | Sort-Object Source, Name

# Wrap in an object with metadata
$result = [PSCustomObject]@{
    computer = $env:COMPUTERNAME
    user = $env:USERNAME
    generatedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ss")
    windowsInstalledAt = $winInstall.ToString("yyyy-MM-dd")
    onlyAfterWindowsInstall = [bool]$OnlyAfterWindowsInstall
    includeMicrosoft = [bool]$IncludeMicrosoft
    count = $all.Count
    items = $all
}

# JSON to stdout (depth matters)
$result | ConvertTo-Json -Depth 6
