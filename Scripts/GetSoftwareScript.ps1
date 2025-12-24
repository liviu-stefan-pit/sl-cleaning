# GetSoftwareScript.ps1
# Outputs a unified JSON array of uninstallable software with proper metadata
# Filters out system components while keeping legitimate software

$ErrorActionPreference = "SilentlyContinue"

function Get-RegistryPrograms {
    $registryPaths = @(
        @{ Path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'; Scope = 'Machine'; Is64Bit = $true },
        @{ Path = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'; Scope = 'Machine'; Is64Bit = $false },
        @{ Path = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'; Scope = 'User'; Is64Bit = $true }
    )

    $results = @()

    foreach ($regPath in $registryPaths) {
        if (-not (Test-Path $regPath.Path)) { continue }
        
        $keys = Get-ChildItem -Path $regPath.Path -ErrorAction SilentlyContinue
        
        foreach ($key in $keys) {
            $props = Get-ItemProperty -Path $key.PSPath -ErrorAction SilentlyContinue
            if (-not $props) { continue }
            
            # Must have DisplayName
            $displayName = $props.DisplayName
            if ([string]::IsNullOrWhiteSpace($displayName)) { continue }
            
            # Skip system components
            if ($props.SystemComponent -eq 1) { continue }
            if ($props.ParentKeyName) { continue }
            
            # Skip Windows updates and hotfixes
            if ($displayName -match '^(Update for|Security Update|Hotfix for|KB\d{6,})') { continue }
            if ($displayName -match '^(Servicing Stack|Microsoft \.NET)') { continue }
            
            # Check if uninstallable
            $uninstallString = $props.UninstallString
            $quietUninstallString = $props.QuietUninstallString
            $isUninstallable = -not [string]::IsNullOrWhiteSpace($uninstallString)
            
            # Determine uninstall method
            $uninstallMethod = if (-not [string]::IsNullOrWhiteSpace($quietUninstallString)) {
                "QuietUninstall"
            } elseif ($props.WindowsInstaller -eq 1) {
                "MSI"
            } elseif ($uninstallString -match 'msiexec') {
                "MSI"
            } elseif ($uninstallString) {
                "Standard"
            } else {
                "None"
            }
            
            $results += [PSCustomObject]@{
                Source = "Registry"
                Name = $displayName.Trim()
                Publisher = if ($props.Publisher) { $props.Publisher.Trim() } else { $null }
                Version = if ($props.DisplayVersion) { $props.DisplayVersion.Trim() } else { $null }
                InstallDate = if ($props.InstallDate) { $props.InstallDate.Trim() } else { $null }
                Size = if ($props.EstimatedSize) { [long]$props.EstimatedSize } else { $null }
                Uninstallable = $isUninstallable
                UninstallCommand = if ($isUninstallable) { $uninstallString.Trim() } else { $null }
                QuietUninstallCommand = if ($quietUninstallString) { $quietUninstallString.Trim() } else { $null }
                UninstallMethod = $uninstallMethod
                ProductCode = if ($key.PSChildName -match '^\{[0-9A-Fa-f-]{36}\}$') { $key.PSChildName } else { $null }
                InstallLocation = if ($props.InstallLocation) { $props.InstallLocation.Trim() } else { $null }
                Is64Bit = $regPath.Is64Bit
            }
        }
    }

    return $results
}

function Get-StoreApps {
    $results = @()
    
    try {
        # Try to get all users' apps, fall back to current user
        try {
            $packages = Get-AppxPackage -AllUsers -ErrorAction Stop
        } catch {
            $packages = Get-AppxPackage -ErrorAction SilentlyContinue
        }
        
        if (-not $packages) { return $results }
        
        foreach ($pkg in $packages) {
            # Skip frameworks and resources
            if ($pkg.IsFramework) { continue }
            if ($pkg.IsResourcePackage) { continue }
            
            # Must have a name
            if ([string]::IsNullOrWhiteSpace($pkg.Name)) { continue }
            
            # Get display name
            $displayName = $pkg.Name
            try {
                $manifest = Get-AppxPackageManifest -Package $pkg.PackageFullName -ErrorAction SilentlyContinue
                if ($manifest -and $manifest.Package.Properties.DisplayName) {
                    $manifestName = $manifest.Package.Properties.DisplayName
                    if ($manifestName -notmatch '^ms-resource:') {
                        $displayName = $manifestName
                    }
                }
            } catch { }
            
            # Clean up technical names
            if ($displayName -match '^Microsoft\.') {
                $cleanName = $displayName -replace '^Microsoft\.', ''
                $cleanName = $cleanName -creplace '([a-z])([A-Z])', '$1 $2'
                $displayName = $cleanName
            }
            
            # Skip system infrastructure packages
            $skipPatterns = @(
                '^(NET|VCLibs|UI\.Xaml|WindowsAppRuntime)',
                'WebExperience|WebMedia|HEIF|VP9|AV1|Raw.*Extension',
                '^Services\.Store|^DesktopAppInstaller'
            )
            
            $shouldSkip = $false
            foreach ($pattern in $skipPatterns) {
                if ($pkg.Name -match $pattern) { $shouldSkip = $true; break }
            }
            if ($shouldSkip) { continue }
            
            # Check if uninstallable
            # Start with NonRemovable property
            $isUninstallable = $pkg.NonRemovable -ne $true
            
            # Additional checks for system and protected apps
            if ($isUninstallable) {
                # System-signed packages are protected by Windows
                if ($pkg.SignatureKind -eq 'System') {
                    $isUninstallable = $false
                }
                
                # Check if it's a provisioned package (pre-installed for all users)
                # These almost always fail to uninstall via Remove-AppxPackage
                try {
                    $provisioned = Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | 
                        Where-Object { $_.PackageName -like "$($pkg.Name)*" }
                    if ($provisioned) {
                        $isUninstallable = $false
                    }
                } catch { }
                
                # Microsoft-published apps with "CN=Microsoft Corporation" are usually protected
                # unless they're clearly user apps (Office, Teams, etc.)
                if ($pkg.Publisher -match 'CN=Microsoft Corporation') {
                    # Allow common user-installable Microsoft apps
                    $allowedMicrosoftApps = @(
                        'Microsoft.Office',
                        'Microsoft.Teams',
                        'Microsoft.SkypeApp',
                        'Microsoft.OneDrive',
                        'Microsoft.Todos',
                        'Microsoft.PowerAutomateDesktop',
                        'Microsoft.RemoteDesktop'
                    )
                    
                    $isAllowed = $false
                    foreach ($allowed in $allowedMicrosoftApps) {
                        if ($pkg.Name -like "$allowed*") {
                            $isAllowed = $true
                            break
                        }
                    }
                    
                    # If it's a Microsoft app and not in the allowed list, block it
                    if (-not $isAllowed) {
                        # Block all Microsoft.Windows.* apps
                        if ($pkg.Name -like 'Microsoft.Windows.*') {
                            $isUninstallable = $false
                        }
                        # Block common problematic Microsoft inbox apps
                        elseif ($pkg.Name -match '^Microsoft\.(Bing|Get|Windows|Xbox|People|Messaging|Store|Wallet|549981C3F5F10)') {
                            $isUninstallable = $false
                        }
                    }
                }
                
                # Known system apps that can't be uninstalled
                $protectedApps = @(
                    'Microsoft.Windows.Cortana',
                    'Microsoft.MicrosoftEdge',
                    'Microsoft.AAD.BrokerPlugin',
                    'Microsoft.AccountsControl',
                    'Microsoft.AsyncTextService',
                    'Microsoft.CredDialogHost',
                    'Microsoft.ECApp',
                    'Microsoft.LockApp',
                    'Microsoft.MicrosoftEdgeDevToolsClient',
                    'Microsoft.Win32WebViewHost',
                    'Microsoft.Windows.Apprep.ChxApp',
                    'Microsoft.Windows.AssignedAccessLockApp',
                    'Microsoft.Windows.CapturePicker',
                    'Microsoft.Windows.CloudExperienceHost',
                    'Microsoft.Windows.ContentDeliveryManager',
                    'Microsoft.Windows.NarratorQuickStart',
                    'Microsoft.Windows.OOBENetworkCaptivePortal',
                    'Microsoft.Windows.OOBENetworkConnectionFlow',
                    'Microsoft.Windows.ParentalControls',
                    'Microsoft.Windows.PeopleExperienceHost',
                    'Microsoft.Windows.PinningConfirmationDialog',
                    'Microsoft.Windows.SecHealthUI',
                    'Microsoft.Windows.SecureAssessmentBrowser',
                    'Microsoft.Windows.ShellExperienceHost',
                    'Microsoft.Windows.XGpuEjectDialog',
                    'Microsoft.XboxGameCallableUI'
                )
                if ($pkg.Name -in $protectedApps) {
                    $isUninstallable = $false
                }
            }
            
            $results += [PSCustomObject]@{
                Source = "AppX"
                Name = $displayName.Trim()
                Publisher = if ($pkg.Publisher) { $pkg.Publisher.Trim() } else { $null }
                Version = if ($pkg.Version) { $pkg.Version.ToString() } else { $null }
                InstallDate = $null
                Size = $null
                Uninstallable = $isUninstallable
                UninstallCommand = $pkg.PackageFullName
                QuietUninstallCommand = $null
                UninstallMethod = if ($isUninstallable) { "AppX" } else { "None" }
                ProductCode = $null
                InstallLocation = if ($pkg.InstallLocation) { $pkg.InstallLocation.Trim() } else { $null }
                Is64Bit = $pkg.Architecture -eq 'X64'
            }
        }
    }
    catch {
        # Silently continue if AppX not available
    }
    
    return $results
}

# Get all software
$registryPrograms = Get-RegistryPrograms
$storeApps = Get-StoreApps

# Combine and deduplicate
$allSoftware = @()
$allSoftware += $registryPrograms
$allSoftware += $storeApps

# Sort by name and remove duplicates
$allSoftware = $allSoftware | Sort-Object Name -Unique

# Output as JSON
$json = $allSoftware | ConvertTo-Json -Depth 3 -Compress:$false

# Handle edge cases
if ($allSoftware.Count -eq 1) {
    $json = "[$json]"
} elseif ($allSoftware.Count -eq 0) {
    $json = "[]"
}

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Write-Output $json
