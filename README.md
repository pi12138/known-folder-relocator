# Known Folder Relocator

PowerShell tool for moving or re-attaching Windows user known folders to a data drive, so Desktop, Documents, Downloads, Pictures, Music, Videos, and similar folders can survive a Windows reinstall.

The script is intentionally conservative:

- It only changes an allowlist in `known-folders.json`.
- It does not migrate `AppData`, temp folders, cache folders, or the user profile root.
- It does not delete data from `C:`.
- It does not overwrite existing files by default.
- It writes registry and JSON state backups under `.state/`.

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

Preview the operation:

```powershell
.\Move-KnownFolders.ps1 -Mode Migrate -TargetDrive D: -WhatIf
```

Run it:

```powershell
.\Move-KnownFolders.ps1 -Mode Migrate -TargetDrive D: -RestartExplorer
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
.\Move-KnownFolders.ps1 -Mode AttachExisting -TargetDrive D: -RestartExplorer
```

If `D:\Users\<YourUserName>\Documents` and other folders already exist, Windows will be pointed back to those folders. Any new files created in the fresh `C:\Users\<YourUserName>` folders are copied only when missing from the data drive.

If the new Windows username is different from the old one, pass the exact existing root:

```powershell
.\Move-KnownFolders.ps1 -Mode AttachExisting -TargetRoot D:\Users\OldUserName -RestartExplorer
```

## Copy strategies

```powershell
# Default: copy only files missing on the target, do not overwrite conflicts.
.\Move-KnownFolders.ps1 -TargetDrive D: -CopyStrategy CopyMissing

# Only update registry paths. Do not copy files.
.\Move-KnownFolders.ps1 -TargetDrive D: -CopyStrategy NoCopy

# If a target file already exists, rename it to *.bak, then copy the source file.
.\Move-KnownFolders.ps1 -TargetDrive D: -CopyStrategy BackupConflicts
```

## Restore registry paths

Restore from the latest state file:

```powershell
.\Move-KnownFolders.ps1 -Mode Restore -RestartExplorer
```

Restore from a specific state file:

```powershell
.\Move-KnownFolders.ps1 -Mode Restore -RestoreState .\.state\known-folder-relocator-20260709-120000.json -RestartExplorer
```

Restore only changes registry paths back. It does not delete data on the target drive.

## Notes

- Run from a normal user PowerShell session. Administrator is usually not required because the script writes to `HKCU`.
- Use `-WhatIf` before the first real run.
- Close applications that actively use Desktop, Documents, Downloads, Pictures, Music, or Videos before running.
- Sign out and back in if Explorer or applications keep showing old paths.
