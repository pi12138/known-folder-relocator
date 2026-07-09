[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [ValidateSet('Migrate', 'AttachExisting', 'Restore', 'Verify')]
    [string]$Mode = 'Verify',

    [string]$TargetDrive,

    [string]$TargetRoot,

    [ValidateSet('CopyMissing', 'NoCopy', 'BackupConflicts')]
    [string]$CopyStrategy = 'CopyMissing',

    [string]$ConfigPath,

    [string]$StatePath,

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

function Assert-Windows {
    if ($PSVersionTable.PSVersion.Major -ge 6 -and -not $IsWindows) {
        throw 'This script must run on Windows.'
    }
}

function Add-KnownFolderNativeType {
    if ('KnownFolderRelocator.NativeMethods' -as [type]) {
        return
    }

    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace KnownFolderRelocator
{
    public static class NativeMethods
    {
        [DllImport("shell32.dll")]
        public static extern int SHSetKnownFolderPath(
            ref Guid rfid,
            uint dwFlags,
            IntPtr hToken,
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath);

        [DllImport("shell32.dll")]
        public static extern int SHGetKnownFolderPath(
            ref Guid rfid,
            uint dwFlags,
            IntPtr hToken,
            out IntPtr ppszPath);

        [DllImport("ole32.dll")]
        public static extern void CoTaskMemFree(IntPtr pv);

        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(
            uint wEventId,
            uint uFlags,
            IntPtr dwItem1,
            IntPtr dwItem2);
    }
}
'@
}

function Format-HResult {
    param([int]$HResult)

    return ('0x{0:X8}' -f ([uint32]($HResult -band [int64]0xffffffff)))
}

function Expand-KnownPath {
    param([AllowNull()][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    return [Environment]::ExpandEnvironmentVariables($Path)
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

function Get-Config {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Known folders config was not found: $Path"
    }

    $items = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    foreach ($item in $items) {
        if ([string]::IsNullOrWhiteSpace($item.Name) -or
            [string]::IsNullOrWhiteSpace($item.KnownFolderId) -or
            [string]::IsNullOrWhiteSpace($item.DirectoryName)) {
            throw "Invalid known folder entry in $Path"
        }

        try {
            [void]([guid]$item.KnownFolderId)
        }
        catch {
            throw "Invalid KnownFolderId for $($item.Name): $($item.KnownFolderId)"
        }
    }

    return $items
}

function Get-KnownFolderPathById {
    param([string]$KnownFolderId)

    Add-KnownFolderNativeType
    $guid = [guid]$KnownFolderId
    $pathPointer = [IntPtr]::Zero
    $hr = [KnownFolderRelocator.NativeMethods]::SHGetKnownFolderPath([ref]$guid, 0, [IntPtr]::Zero, [ref]$pathPointer)
    if ($hr -ne 0) {
        throw "SHGetKnownFolderPath failed for $KnownFolderId with HRESULT $(Format-HResult $hr)"
    }

    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringUni($pathPointer)
    }
    finally {
        if ($pathPointer -ne [IntPtr]::Zero) {
            [KnownFolderRelocator.NativeMethods]::CoTaskMemFree($pathPointer)
        }
    }
}

function Set-KnownFolderPathById {
    param(
        [string]$KnownFolderId,
        [string]$Path
    )

    Add-KnownFolderNativeType
    $guid = [guid]$KnownFolderId
    $hr = [KnownFolderRelocator.NativeMethods]::SHSetKnownFolderPath([ref]$guid, 0, [IntPtr]::Zero, $Path)
    if ($hr -ne 0) {
        throw "SHSetKnownFolderPath failed for $KnownFolderId -> $Path with HRESULT $(Format-HResult $hr)"
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
            throw 'Specify -TargetDrive or -TargetRoot for Migrate or AttachExisting.'
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

function Get-LatestShellStateFile {
    $stateDir = Join-Path $ScriptRoot '.state'
    if (-not (Test-Path -LiteralPath $stateDir)) {
        throw "State directory was not found: $stateDir"
    }

    $latest = Get-ChildItem -LiteralPath $stateDir -Filter 'shell-known-folder-*.json' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        throw "No shell API state file was found in $stateDir"
    }

    return $latest.FullName
}

function Copy-MissingItems {
    param(
        [AllowNull()][string]$Source,
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
    if (Test-SamePath -LeftPath $Source -RightPath $Destination) {
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

    $stateFile = Join-Path $StateDir "shell-known-folder-$Timestamp.json"
    if ($PSCmdlet.ShouldProcess($stateFile, 'write state file')) {
        $State | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $stateFile -Encoding UTF8
    }
    return $stateFile
}

function Invoke-ShellRefresh {
    Add-KnownFolderNativeType
    if ($PSCmdlet.ShouldProcess('Shell', 'broadcast association changed notification')) {
        [KnownFolderRelocator.NativeMethods]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
    }

    if ($RestartExplorer) {
        if ($PSCmdlet.ShouldProcess('explorer.exe', 'restart Explorer')) {
            Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
            Start-Process explorer.exe
        }
        return
    }

    Write-Host 'Known folders were updated. Sign out and sign back in if Explorer still shows old pinned Quick Access entries.'
}

function Invoke-Verify {
    param([object[]]$Folders)

    $Folders | ForEach-Object {
        $shellPath = $null
        $errorMessage = $null
        try {
            $shellPath = Get-KnownFolderPathById -KnownFolderId $_.KnownFolderId
        }
        catch {
            $errorMessage = $_.Exception.Message
        }

        [pscustomobject]@{
            Name = $_.Name
            KnownFolderId = $_.KnownFolderId
            ShellPath = $shellPath
            Exists = (-not [string]::IsNullOrWhiteSpace($shellPath) -and (Test-Path -LiteralPath $shellPath))
            Error = $errorMessage
        }
    } | Format-Table -AutoSize
}

function Invoke-Restore {
    param([AllowNull()][string]$Path)

    $resolvedStatePath = $Path
    if ([string]::IsNullOrWhiteSpace($resolvedStatePath)) {
        $resolvedStatePath = Get-LatestShellStateFile
    }
    if (-not (Test-Path -LiteralPath $resolvedStatePath)) {
        throw "Restore state file was not found: $resolvedStatePath"
    }

    $state = Get-Content -LiteralPath $resolvedStatePath -Raw | ConvertFrom-Json
    foreach ($entry in $state.Folders) {
        if ([string]::IsNullOrWhiteSpace($entry.KnownFolderId) -or [string]::IsNullOrWhiteSpace($entry.OldPath)) {
            Write-Warning "Skipping incomplete restore entry: $($entry.Name)"
            continue
        }

        if ($PSCmdlet.ShouldProcess($entry.Name, "restore known folder path to $($entry.OldPath)")) {
            Set-KnownFolderPathById -KnownFolderId $entry.KnownFolderId -Path $entry.OldPath
            $restoredPath = Get-KnownFolderPathById -KnownFolderId $entry.KnownFolderId
            if (-not (Test-SamePath -LeftPath $restoredPath -RightPath $entry.OldPath)) {
                throw "Restore verification failed for $($entry.Name): expected $($entry.OldPath), got $restoredPath"
            }
        }
    }

    Invoke-ShellRefresh
    Write-Host "Restore completed from $resolvedStatePath"
}

function Invoke-SetKnownFolders {
    param(
        [ValidateSet('Migrate', 'AttachExisting')]
        [string]$OperationMode,
        [object[]]$Folders
    )

    $resolvedTargetRoot = Resolve-TargetRoot -Drive $TargetDrive -Root $TargetRoot
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $stateDir = New-StateDirectory

    if ($PSCmdlet.ShouldProcess($resolvedTargetRoot, 'create target root')) {
        New-Item -ItemType Directory -Path $resolvedTargetRoot -Force | Out-Null
    }

    $state = [ordered]@{
        Tool = 'known-folder-relocator-shell-api'
        Version = '0.2.0'
        Mode = $OperationMode
        CopyStrategy = $CopyStrategy
        Timestamp = $timestamp
        UserName = $env:USERNAME
        UserDomain = $env:USERDOMAIN
        UserSid = ([System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value)
        TargetRoot = $resolvedTargetRoot
        Folders = @()
    }

    foreach ($folder in $Folders) {
        $oldPath = $null
        try {
            $oldPath = Get-KnownFolderPathById -KnownFolderId $folder.KnownFolderId
        }
        catch {
            Write-Warning "Skipping $($folder.Name): $($_.Exception.Message)"
            continue
        }

        $targetPath = Join-Path $resolvedTargetRoot $folder.DirectoryName
        if ($PSCmdlet.ShouldProcess($targetPath, "create target folder for $($folder.Name)")) {
            New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
        }

        $copySummary = Copy-MissingItems -Source $oldPath -Destination $targetPath -Strategy $CopyStrategy -Timestamp $timestamp

        $verifiedPath = $null
        if ($PSCmdlet.ShouldProcess($folder.Name, "set known folder path to $targetPath")) {
            Set-KnownFolderPathById -KnownFolderId $folder.KnownFolderId -Path $targetPath
            $verifiedPath = Get-KnownFolderPathById -KnownFolderId $folder.KnownFolderId
            if (-not (Test-SamePath -LeftPath $verifiedPath -RightPath $targetPath)) {
                throw "Verification failed for $($folder.Name): expected $targetPath, got $verifiedPath"
            }
        }

        $state.Folders += [ordered]@{
            Name = $folder.Name
            KnownFolderId = $folder.KnownFolderId
            RegistryName = $folder.RegistryName
            DirectoryName = $folder.DirectoryName
            OldPath = $oldPath
            NewPath = $targetPath
            VerifiedPath = $verifiedPath
            Copy = $copySummary
        }
    }

    $stateFile = Write-StateFile -StateDir $stateDir -Timestamp $timestamp -State $state
    Invoke-ShellRefresh
    Write-Host "Completed $OperationMode. State file: $stateFile"
}

Assert-Windows
Add-KnownFolderNativeType
$folders = Get-Config -Path $ConfigPath

switch ($Mode) {
    'Verify' {
        Invoke-Verify -Folders $folders
    }
    'Restore' {
        Invoke-Restore -Path $StatePath
    }
    'Migrate' {
        Invoke-SetKnownFolders -OperationMode Migrate -Folders $folders
    }
    'AttachExisting' {
        Invoke-SetKnownFolders -OperationMode AttachExisting -Folders $folders
    }
}
