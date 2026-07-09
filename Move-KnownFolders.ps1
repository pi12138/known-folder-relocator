[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [ValidateSet('Migrate', 'AttachExisting', 'Restore', 'CleanupOld')]
    [string]$Mode = 'Migrate',

    [string]$TargetDrive,

    [string]$TargetRoot,

    [ValidateSet('CopyMissing', 'NoCopy', 'BackupConflicts')]
    [string]$CopyStrategy = 'CopyMissing',

    [string]$ConfigPath,

    [string]$RestoreState,

    [switch]$ForceCleanup,

    [switch]$RestartExplorer
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$ScriptRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ScriptRoot)) {
    $ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $ScriptRoot 'known-folders.json'
}

$UserShellFoldersKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders'
$ShellFoldersKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders'
$UserShellFoldersRegPath = 'HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders'
$ShellFoldersRegPath = 'HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders'

function Assert-Windows {
    if ($PSVersionTable.PSVersion.Major -ge 6 -and -not $IsWindows) {
        throw 'This script must run on Windows.'
    }
}

function Expand-KnownPath {
    param([AllowNull()][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    return [Environment]::ExpandEnvironmentVariables($Path)
}

function Get-Config {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Known folders config was not found: $Path"
    }

    $items = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    foreach ($item in $items) {
        if ([string]::IsNullOrWhiteSpace($item.Name) -or
            [string]::IsNullOrWhiteSpace($item.RegistryName) -or
            [string]::IsNullOrWhiteSpace($item.DirectoryName)) {
            throw "Invalid known folder entry in $Path"
        }
    }

    return $items
}

function Get-RegistryValue {
    param(
        [string]$Key,
        [string]$Name
    )

    try {
        return (Get-ItemPropertyValue -LiteralPath $Key -Name $Name -ErrorAction Stop)
    }
    catch {
        return $null
    }
}

function Set-KnownFolderRegistryValue {
    param(
        [string]$Name,
        [string]$Path
    )

    if ($PSCmdlet.ShouldProcess($UserShellFoldersKey, "set $Name to $Path")) {
        New-ItemProperty -LiteralPath $UserShellFoldersKey -Name $Name -Value $Path -PropertyType ExpandString -Force | Out-Null
    }

    $expandedPath = Expand-KnownPath $Path
    if ($PSCmdlet.ShouldProcess($ShellFoldersKey, "set $Name to $expandedPath")) {
        New-ItemProperty -LiteralPath $ShellFoldersKey -Name $Name -Value $expandedPath -PropertyType String -Force | Out-Null
    }
}

function Resolve-TargetRoot {
    param(
        [AllowNull()][string]$Drive,
        [AllowNull()][string]$Root
    )

    if (-not [string]::IsNullOrWhiteSpace($Root)) {
        $fullRoot = [System.IO.Path]::GetFullPath($Root)
    }
    else {
        if ([string]::IsNullOrWhiteSpace($Drive)) {
            throw 'Specify -TargetDrive or -TargetRoot unless using -Mode Restore.'
        }

        $normalizedDrive = $Drive.Trim()
        if ($normalizedDrive -match '^[A-Za-z]$') {
            $normalizedDrive = "$normalizedDrive`:"
        }
        if ($normalizedDrive -notmatch '^[A-Za-z]:$') {
            throw "Invalid drive value: $Drive"
        }

        $fullRoot = Join-Path "$normalizedDrive\" (Join-Path 'Users' $env:USERNAME)
    }

    $rootDrive = [System.IO.Path]::GetPathRoot($fullRoot)
    if ([string]::IsNullOrWhiteSpace($rootDrive)) {
        throw "Target root must be an absolute path: $fullRoot"
    }
    if ($rootDrive.TrimEnd('\') -ieq 'C:') {
        throw "Target root must not be on C drive: $fullRoot"
    }

    return $fullRoot.TrimEnd('\')
}

function New-StateDirectory {
    $stateDir = Join-Path $ScriptRoot '.state'
    if ($PSCmdlet.ShouldProcess($stateDir, 'create state directory')) {
        New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
    }
    return $stateDir
}

function Export-RegistryBackup {
    param(
        [string]$StateDir,
        [string]$Timestamp
    )

    $userShellBackup = Join-Path $StateDir "user-shell-folders-$Timestamp.reg"
    $shellBackup = Join-Path $StateDir "shell-folders-$Timestamp.reg"

    if ($WhatIfPreference) {
        Write-Host "What if: export registry backup to $userShellBackup and $shellBackup"
        return @{
            UserShellFolders = $userShellBackup
            ShellFolders = $shellBackup
        }
    }

    & reg.exe export $UserShellFoldersRegPath $userShellBackup /y | Out-Null
    & reg.exe export $ShellFoldersRegPath $shellBackup /y | Out-Null

    return @{
        UserShellFolders = $userShellBackup
        ShellFolders = $shellBackup
    }
}

function Copy-MissingItems {
    param(
        [string]$Source,
        [string]$Destination,
        [string]$Strategy,
        [string]$Timestamp
    )

    $summary = [ordered]@{
        Source = $Source
        Destination = $Destination
        CreatedDirectories = 0
        CopiedFiles = 0
        Conflicts = @()
        BackedUpConflicts = @()
        Skipped = $false
    }

    if ($Strategy -eq 'NoCopy') {
        $summary.Skipped = $true
        return $summary
    }

    if ([string]::IsNullOrWhiteSpace($Source) -or -not (Test-Path -LiteralPath $Source)) {
        $summary.Skipped = $true
        return $summary
    }

    if ($PSCmdlet.ShouldProcess($Destination, 'create folder')) {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    }

    $sourceRoot = (Get-Item -LiteralPath $Source).FullName.TrimEnd('\')
    $items = Get-ChildItem -LiteralPath $sourceRoot -Force -Recurse

    foreach ($item in $items) {
        $relativePath = $item.FullName.Substring($sourceRoot.Length).TrimStart('\')
        $targetPath = Join-Path $Destination $relativePath

        if ($item.PSIsContainer) {
            if (-not (Test-Path -LiteralPath $targetPath)) {
                if ($PSCmdlet.ShouldProcess($targetPath, 'create folder')) {
                    New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
                    $summary.CreatedDirectories++
                }
            }
            continue
        }

        $targetParent = Split-Path -Parent $targetPath
        if (-not (Test-Path -LiteralPath $targetParent)) {
            if ($PSCmdlet.ShouldProcess($targetParent, 'create folder')) {
                New-Item -ItemType Directory -Path $targetParent -Force | Out-Null
                $summary.CreatedDirectories++
            }
        }

        if (Test-Path -LiteralPath $targetPath) {
            if ($Strategy -eq 'BackupConflicts') {
                $backupPath = "$targetPath.$Timestamp.bak"
                if ($PSCmdlet.ShouldProcess($targetPath, "move existing conflict to $backupPath")) {
                    Move-Item -LiteralPath $targetPath -Destination $backupPath -Force
                    Copy-Item -LiteralPath $item.FullName -Destination $targetPath -Force
                    $summary.BackedUpConflicts += [ordered]@{
                        Original = $targetPath
                        Backup = $backupPath
                    }
                    $summary.CopiedFiles++
                }
            }
            else {
                $summary.Conflicts += $targetPath
            }
            continue
        }

        if ($PSCmdlet.ShouldProcess($targetPath, "copy from $($item.FullName)")) {
            Copy-Item -LiteralPath $item.FullName -Destination $targetPath
            $summary.CopiedFiles++
        }
    }

    return $summary
}

function Write-StateFile {
    param(
        [string]$StateDir,
        [string]$Timestamp,
        [object]$State
    )

    $stateFile = Join-Path $StateDir "known-folder-relocator-$Timestamp.json"
    if ($PSCmdlet.ShouldProcess($stateFile, 'write state file')) {
        $State | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $stateFile -Encoding UTF8
    }
    return $stateFile
}

function Get-LatestStateFile {
    $stateDir = Join-Path $ScriptRoot '.state'
    if (-not (Test-Path -LiteralPath $stateDir)) {
        throw "State directory was not found: $stateDir"
    }

    $latest = Get-ChildItem -LiteralPath $stateDir -Filter 'known-folder-relocator-*.json' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        throw "No state file was found in $stateDir"
    }

    return $latest.FullName
}

function Invoke-ShellRefresh {
    if ($RestartExplorer) {
        if ($PSCmdlet.ShouldProcess('explorer.exe', 'restart Explorer')) {
            Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
            Start-Process explorer.exe
        }
        return
    }

    Write-Host 'Registry was updated. Sign out and sign back in, or rerun with -RestartExplorer to restart Explorer automatically.'
}

function Invoke-Restore {
    param([AllowNull()][string]$StatePath)

    $path = $StatePath
    if ([string]::IsNullOrWhiteSpace($path)) {
        $path = Get-LatestStateFile
    }
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Restore state file was not found: $path"
    }

    $state = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    foreach ($entry in $state.Folders) {
        if ([string]::IsNullOrWhiteSpace($entry.RegistryName) -or [string]::IsNullOrWhiteSpace($entry.OldUserShellPath)) {
            Write-Warning "Skipping incomplete restore entry: $($entry.Name)"
            continue
        }
        Set-KnownFolderRegistryValue -Name $entry.RegistryName -Path $entry.OldUserShellPath
    }

    Invoke-ShellRefresh
    Write-Host "Restore completed from $path"
}

function Test-SameFileContent {
    param(
        [string]$LeftPath,
        [string]$RightPath
    )

    $left = Get-Item -LiteralPath $LeftPath
    $right = Get-Item -LiteralPath $RightPath
    if ($left.Length -ne $right.Length) {
        return $false
    }

    $leftHash = Get-FileHash -LiteralPath $LeftPath -Algorithm SHA256
    $rightHash = Get-FileHash -LiteralPath $RightPath -Algorithm SHA256
    return $leftHash.Hash -eq $rightHash.Hash
}

function Test-SamePath {
    param(
        [AllowNull()][string]$LeftPath,
        [AllowNull()][string]$RightPath
    )

    if ([string]::IsNullOrWhiteSpace($LeftPath) -or [string]::IsNullOrWhiteSpace($RightPath)) {
        return $false
    }

    $leftFullPath = [System.IO.Path]::GetFullPath((Expand-KnownPath $LeftPath)).TrimEnd('\')
    $rightFullPath = [System.IO.Path]::GetFullPath((Expand-KnownPath $RightPath)).TrimEnd('\')
    return [string]::Equals($leftFullPath, $rightFullPath, [System.StringComparison]::OrdinalIgnoreCase)
}

function Invoke-CleanupOld {
    param(
        [AllowNull()][string]$StatePath
    )

    if (-not $WhatIfPreference -and -not $ForceCleanup) {
        throw 'CleanupOld deletes files. Run with -WhatIf first, then rerun with -ForceCleanup to delete matching duplicate files.'
    }

    $path = $StatePath
    if ([string]::IsNullOrWhiteSpace($path)) {
        $path = Get-LatestStateFile
    }
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Cleanup state file was not found: $path"
    }

    $state = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $summary = [ordered]@{
        StateFile = $path
        MatchedDuplicateFiles = 0
        DeletedFiles = 0
        SkippedMissingTarget = 0
        SkippedDifferentContent = 0
        SkippedInvalidFolder = 0
        Errors = @()
    }

    foreach ($entry in $state.Folders) {
        $oldPath = Expand-KnownPath $entry.ExpandedOldPath
        if ([string]::IsNullOrWhiteSpace($oldPath)) {
            $oldPath = Expand-KnownPath $entry.OldUserShellPath
        }
        $newPath = Expand-KnownPath $entry.NewPath

        if ([string]::IsNullOrWhiteSpace($oldPath) -or
            [string]::IsNullOrWhiteSpace($newPath) -or
            -not (Test-Path -LiteralPath $oldPath) -or
            -not (Test-Path -LiteralPath $newPath)) {
            Write-Warning "Skipping $($entry.Name): old or new folder does not exist."
            $summary.SkippedInvalidFolder++
            continue
        }

        $oldRoot = [System.IO.Path]::GetPathRoot($oldPath)
        if ($oldRoot.TrimEnd('\') -ine 'C:') {
            Write-Warning "Skipping $($entry.Name): old folder is not on C drive: $oldPath"
            $summary.SkippedInvalidFolder++
            continue
        }

        $currentPath = Get-RegistryValue -Key $UserShellFoldersKey -Name $entry.RegistryName
        if (-not (Test-SamePath -LeftPath $currentPath -RightPath $newPath)) {
            Write-Warning "Skipping $($entry.Name): current known folder is not pointing to $newPath."
            $summary.SkippedInvalidFolder++
            continue
        }

        $oldRootPath = (Get-Item -LiteralPath $oldPath).FullName.TrimEnd('\')
        $oldFiles = Get-ChildItem -LiteralPath $oldRootPath -Force -Recurse -File
        foreach ($oldFile in $oldFiles) {
            $relativePath = $oldFile.FullName.Substring($oldRootPath.Length).TrimStart('\')
            $newFilePath = Join-Path $newPath $relativePath

            if (-not (Test-Path -LiteralPath $newFilePath -PathType Leaf)) {
                $summary.SkippedMissingTarget++
                continue
            }

            try {
                if (-not (Test-SameFileContent -LeftPath $oldFile.FullName -RightPath $newFilePath)) {
                    $summary.SkippedDifferentContent++
                    continue
                }

                $summary.MatchedDuplicateFiles++
                if ($PSCmdlet.ShouldProcess($oldFile.FullName, "delete duplicate already present at $newFilePath")) {
                    Remove-Item -LiteralPath $oldFile.FullName -Force
                    $summary.DeletedFiles++
                }
            }
            catch {
                $summary.Errors += [ordered]@{
                    Path = $oldFile.FullName
                    Error = $_.Exception.Message
                }
            }
        }
    }

    $summary | ConvertTo-Json -Depth 6
}

Assert-Windows

if ($Mode -eq 'Restore') {
    Invoke-Restore -StatePath $RestoreState
    return
}

if ($Mode -eq 'CleanupOld') {
    Invoke-CleanupOld -StatePath $RestoreState
    return
}

$folders = Get-Config -Path $ConfigPath
$resolvedTargetRoot = Resolve-TargetRoot -Drive $TargetDrive -Root $TargetRoot
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$stateDir = New-StateDirectory
$registryBackups = Export-RegistryBackup -StateDir $stateDir -Timestamp $timestamp

if ($PSCmdlet.ShouldProcess($resolvedTargetRoot, 'create target root')) {
    New-Item -ItemType Directory -Path $resolvedTargetRoot -Force | Out-Null
}

$state = [ordered]@{
    Tool = 'known-folder-relocator'
    Version = '0.1.0'
    Mode = $Mode
    CopyStrategy = $CopyStrategy
    Timestamp = $timestamp
    UserName = $env:USERNAME
    UserDomain = $env:USERDOMAIN
    UserSid = ([System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value)
    TargetRoot = $resolvedTargetRoot
    RegistryBackups = $registryBackups
    Folders = @()
}

foreach ($folder in $folders) {
    $oldUserShellPath = Get-RegistryValue -Key $UserShellFoldersKey -Name $folder.RegistryName
    $oldShellPath = Get-RegistryValue -Key $ShellFoldersKey -Name $folder.RegistryName
    $expandedOldPath = Expand-KnownPath $oldUserShellPath
    $targetPath = Join-Path $resolvedTargetRoot $folder.DirectoryName

    if (($folder.PSObject.Properties.Name -contains 'Optional') -and $folder.Optional -and
        [string]::IsNullOrWhiteSpace($oldUserShellPath) -and -not (Test-Path -LiteralPath $targetPath)) {
        Write-Host "Skipping optional folder not present on this system: $($folder.Name)"
        continue
    }

    if ($PSCmdlet.ShouldProcess($targetPath, "create target folder for $($folder.Name)")) {
        New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
    }

    $copySummary = Copy-MissingItems -Source $expandedOldPath -Destination $targetPath -Strategy $CopyStrategy -Timestamp $timestamp
    Set-KnownFolderRegistryValue -Name $folder.RegistryName -Path $targetPath

    $state.Folders += [ordered]@{
        Name = $folder.Name
        RegistryName = $folder.RegistryName
        DirectoryName = $folder.DirectoryName
        OldUserShellPath = $oldUserShellPath
        OldShellPath = $oldShellPath
        ExpandedOldPath = $expandedOldPath
        NewPath = $targetPath
        Copy = $copySummary
    }
}

$stateFile = Write-StateFile -StateDir $stateDir -Timestamp $timestamp -State $state
Invoke-ShellRefresh

Write-Host "Completed $Mode. State file: $stateFile"
