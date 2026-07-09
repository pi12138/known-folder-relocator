# Known Folder Relocator

PowerShell tool for moving or re-attaching Windows user known folders to a data drive, so Desktop, Documents, Downloads, Pictures, Music, Videos, and similar folders can survive a Windows reinstall.

The recommended script is `Set-KnownFoldersWithShellApi.ps1`. It uses the Windows Shell `SHSetKnownFolderPath` API instead of only writing registry values, so it more closely matches Windows' built-in folder Location workflow.

The script is intentionally conservative:

- It only changes an allowlist in `known-folders.json`.
- It does not migrate `AppData`, temp folders, cache folders, or the user profile root.
- It does not delete data from `C:`.
- It does not overwrite existing files by default.
- It writes JSON state backups under `.state/`.

## Folders

Default allowlist:

- Desktop
- Documents
- Downloads
- Pictures
- Music
- Videos
- Favorites

Edit `known-folders.json` if you want to include more known folders.

## First-time migration

If PowerShell blocks the script with an execution policy error, run it with a
process-local bypass:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\Set-KnownFoldersWithShellApi.ps1 -Mode Migrate -TargetDrive D: -WhatIf
```

Or start the current PowerShell session with:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

This only changes the policy for the current PowerShell process.

Preview the operation:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\Set-KnownFoldersWithShellApi.ps1 -Mode Migrate -TargetDrive D: -WhatIf
```

Run it:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\Set-KnownFoldersWithShellApi.ps1 -Mode Migrate -TargetDrive D: -RestartExplorer
```

The default target layout is:

```text
D:\Users\<YourUserName>\Desktop
D:\Users\<YourUserName>\Documents
D:\Users\<YourUserName>\Downloads
```

Existing files from the current profile are copied only when the target file does not already exist.

## Re-attach after reinstalling Windows

After reinstalling Windows, install or copy this repository again, then run:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\Set-KnownFoldersWithShellApi.ps1 -Mode AttachExisting -TargetDrive D: -RestartExplorer
```

If `D:\Users\<YourUserName>\Documents` and other folders already exist, Windows will be pointed back to those folders. Any new files created in the fresh `C:\Users\<YourUserName>` folders are copied only when missing from the data drive.

If the new Windows username is different from the old one, pass the exact existing root:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\Set-KnownFoldersWithShellApi.ps1 -Mode AttachExisting -TargetRoot D:\Users\OldUserName -RestartExplorer
```

## Copy strategies

```powershell
# Default: copy only files missing on the target, do not overwrite conflicts.
powershell.exe -ExecutionPolicy Bypass -File .\Set-KnownFoldersWithShellApi.ps1 -Mode Migrate -TargetDrive D: -CopyStrategy CopyMissing

# Only update known folder paths. Do not copy files.
powershell.exe -ExecutionPolicy Bypass -File .\Set-KnownFoldersWithShellApi.ps1 -Mode AttachExisting -TargetDrive D: -CopyStrategy NoCopy

# If a target file already exists, rename it to *.bak, then copy the source file.
powershell.exe -ExecutionPolicy Bypass -File .\Set-KnownFoldersWithShellApi.ps1 -Mode Migrate -TargetDrive D: -CopyStrategy BackupConflicts
```

## Verify and restore

Verify current Shell API paths:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\Set-KnownFoldersWithShellApi.ps1 -Mode Verify
```

Restore from the latest state file:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\Set-KnownFoldersWithShellApi.ps1 -Mode Restore -RestartExplorer
```

Restore from a specific state file:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\Set-KnownFoldersWithShellApi.ps1 -Mode Restore -StatePath .\.state\shell-known-folder-20260709-120000.json -RestartExplorer
```

Restore only changes known folder paths back. It does not delete data on the target drive.

## Clean up old C: files

After you verify the known folders work from the data drive, you can remove
duplicate files left in the old `C:\Users\<YourUserName>` known folder
locations.

Preview first:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\Move-KnownFolders.ps1 -Mode CleanupOld -WhatIf
```

Delete matching duplicates:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\Move-KnownFolders.ps1 -Mode CleanupOld -ForceCleanup
```

`CleanupOld` uses the latest `.state\known-folder-relocator-*.json` file by
default when one exists. If no state file exists, it infers old paths from
`C:\Users\<YourUserName>` and current known folder registry values.

To use a specific state file:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\Move-KnownFolders.ps1 -Mode CleanupOld -RestoreState .\.state\known-folder-relocator-20260709-120000.json -WhatIf
```

Cleanup rules:

- Only files from previous C: known folder paths are considered.
- A file is deleted only when the matching target file exists and has the same SHA-256 hash.
- Different, missing, or uncertain files are skipped.
- Folder directories are not deleted.
- Real deletion requires `-ForceCleanup`.

## Notes

- Run from a normal user PowerShell session. Administrator is usually not required because the script writes to `HKCU`.
- Use `-WhatIf` before the first real run.
- Close applications that actively use Desktop, Documents, Downloads, Pictures, Music, or Videos before running.
- If Quick Access still contains old pinned folders, unpin those entries and pin the new Shell folders again.
- `Move-KnownFolders.ps1` is retained as the legacy registry-based script. New usage should prefer `Set-KnownFoldersWithShellApi.ps1`.
