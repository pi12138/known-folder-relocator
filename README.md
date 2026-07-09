# Known Folder Relocator

Windows-only C#/.NET CLI for moving or re-attaching user known folders to a data
drive. It uses the Windows Shell known folder APIs for Desktop, Documents,
Downloads, Pictures, Music, Videos, and Favorites.

The tool is intentionally conservative:

- It only changes the allowlist in `known-folders.json`.
- It uses known folder GUIDs instead of English folder names.
- It does not migrate `AppData`, temp folders, cache folders, or the profile root.
- It does not delete old `C:` data during migration or restore.
- It does not overwrite existing files by default.
- It writes JSON state files under `.state/`.

## Build

Requires the .NET 8 SDK on Windows.

```powershell
dotnet build .\KnownFolderRelocator.sln
dotnet publish .\src\KnownFolderRelocator\KnownFolderRelocator.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

The project is configured for a single-file `win-x64` executable.

For the normal release flow, use the publish script:

```powershell
.\scripts\publish.ps1
```

It builds, runs tests, publishes a self-contained single-file executable, and
copies `known-folders.json` into:

```text
artifacts\publish\win-x64\
```

## Commands

Verify current Shell API paths:

```powershell
.\known-folder-relocator.exe verify
```

Preview a first-time migration:

```powershell
.\known-folder-relocator.exe migrate --target-drive D: --dry-run
```

Run a first-time migration:

```powershell
.\known-folder-relocator.exe migrate --target-drive D:
```

The default target layout is:

```text
D:\Users\<YourUserName>\Desktop
D:\Users\<YourUserName>\Documents
D:\Users\<YourUserName>\Downloads
```

Re-attach after reinstalling Windows:

```powershell
.\known-folder-relocator.exe attach --target-drive D:
```

If the new Windows username is different from the old one, pass the existing
root:

```powershell
.\known-folder-relocator.exe attach --target-root D:\Users\OldUserName
```

Restore from a state file:

```powershell
.\known-folder-relocator.exe restore --state .\.state\shell-known-folder-20260709-120000.json
```

Cleanup duplicate old files:

```powershell
.\known-folder-relocator.exe cleanup --dry-run
.\known-folder-relocator.exe cleanup --force
```

Cleanup only deletes old files when the target counterpart exists and has the
same SHA-256 hash.

To also remove old directories that become empty after duplicate files are
removed:

```powershell
.\known-folder-relocator.exe cleanup --dry-run --remove-empty-dirs
.\known-folder-relocator.exe cleanup --force --remove-empty-dirs
```

Non-empty directories are skipped.

## Copy Strategies

Default behavior copies missing files only and records conflicts:

```powershell
.\known-folder-relocator.exe migrate --target-drive D: --copy-strategy CopyMissing
```

Only update known folder paths:

```powershell
.\known-folder-relocator.exe attach --target-drive D: --no-copy
```

Back up target conflicts before copying source files:

```powershell
.\known-folder-relocator.exe migrate --target-drive D: --copy-strategy BackupConflicts
```

## Tests

The tests are a dependency-free console project:

```powershell
dotnet run --project .\tests\KnownFolderRelocator.Tests\KnownFolderRelocator.Tests.csproj
```

## Manual Validation

```powershell
.\known-folder-relocator.exe verify
.\known-folder-relocator.exe migrate --target-drive D: --dry-run
.\known-folder-relocator.exe migrate --target-drive D:
.\known-folder-relocator.exe verify
```

Explorer checks:

```powershell
explorer.exe shell:Desktop
explorer.exe shell:Personal
explorer.exe shell:Downloads
explorer.exe shell:My Pictures
explorer.exe shell:My Music
explorer.exe shell:My Video
```

Acceptance checks:

- `verify` shows migrated folders on the target drive.
- Folder Properties > Location shows the target drive.
- New files created through Explorer land on the target drive.
- Restore returns folders to their previous paths.
- Cleanup never deletes files whose target counterpart is missing or differs by
  hash.
- Empty old directories are removed only when `--remove-empty-dirs` is specified.

## Legacy PowerShell

The original PowerShell implementation is retained under `legacy/`:

- `legacy/Set-KnownFoldersWithShellApi.ps1`
- `legacy/Move-KnownFolders.ps1`

Use the .NET CLI for new migrations. The legacy scripts are kept for audit,
fallback, and behavioral comparison.
