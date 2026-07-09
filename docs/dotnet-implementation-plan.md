# C#/.NET Implementation Plan

## Summary

The long-term implementation should move from PowerShell to a Windows-only
C#/.NET tool. The PowerShell scripts proved that the correct Windows integration
point is the Shell known folder API, but they are not the best foundation for a
maintainable product.

The first C# version should be a CLI. The core logic should be structured so a
GUI can be added later without rewriting migration behavior.

## Goals

- Use Windows Shell APIs as the source of truth for known folder paths.
- Support first-time migration and post-reinstall re-attachment.
- Keep file operations conservative and auditable.
- Preserve restore and cleanup workflows.
- Produce a tool that can be published as a single Windows executable.

## Non-Goals

- Do not support non-Windows platforms.
- Do not migrate `AppData`, temp folders, cache folders, or the profile root.
- Do not delete old `C:` data during migration.
- Do not implement GUI in the first C# version.
- Do not use `IKnownFolderManager::Redirect` in v1; keep file movement under
  explicit program control.

## Recommended Project Layout

```text
src/
  KnownFolderRelocator/
    KnownFolderRelocator.csproj
    Program.cs
    Commands/
    Shell/
    FileOperations/
    State/
tests/
  KnownFolderRelocator.Tests/
legacy/
  Move-KnownFolders.ps1
  Set-KnownFoldersWithShellApi.ps1
known-folders.json
README.md
```

PowerShell scripts should move to `legacy/` once the C# CLI reaches parity.

## CLI Commands

```text
known-folder-relocator verify
known-folder-relocator migrate --target-drive E
known-folder-relocator migrate --target-root E:\Users\pyo1024
known-folder-relocator attach --target-drive E --no-copy
known-folder-relocator restore --state .state\shell-known-folder-xxx.json
known-folder-relocator cleanup --dry-run
known-folder-relocator cleanup --force
```

### Command Behavior

- `verify`
  - Calls `SHGetKnownFolderPath` for each configured known folder.
  - Prints the current Shell API path and whether it exists.
  - Does not modify files or paths.

- `migrate`
  - Uses `SHGetKnownFolderPath` to read the old path.
  - Creates the target folder.
  - Copies missing files by default.
  - Calls `SHSetKnownFolderPath`.
  - Calls `SHGetKnownFolderPath` again to verify.
  - Writes a state file.

- `attach`
  - Points known folders to an existing target root.
  - Defaults to copying missing files from the current old path.
  - Supports `--no-copy` for pure re-attachment after reinstall.

- `restore`
  - Reads a state file.
  - Calls `SHSetKnownFolderPath` for each old path.
  - Verifies each restored path.
  - Does not delete target-drive data.

- `cleanup`
  - Deletes only duplicate old files.
  - Requires `--dry-run` or `--force`.
  - A file is deletable only when the new-path counterpart exists and has the
    same SHA-256 hash.
  - Does not delete directories.

## Windows API Integration

Use P/Invoke wrappers around:

- `SHGetKnownFolderPath`
- `SHSetKnownFolderPath`
- `CoTaskMemFree`
- `SHChangeNotify`

The C# implementation should treat `SHGetKnownFolderPath` as the source of truth
for current known folder paths. It must not infer old paths from English folder
names such as `Desktop` or `Downloads`, because localized Windows profiles can
contain Chinese display folders like `桌面` or `下载`.

## Known Folder Set

Default allowlist:

- Desktop
- Documents
- Downloads
- Pictures
- Music
- Videos
- Favorites

Use known folder GUIDs for all Shell API calls. Keep registry names only for
legacy compatibility or diagnostic output.

## File Copy Policy

Default policy is `CopyMissing`:

- Copy source files only when the target path does not exist.
- Do not overwrite target files.
- Record conflicts in state/log output.
- Skip copying when source and destination resolve to the same path.

Additional policies:

- `NoCopy`: change known folder paths only.
- `BackupConflicts`: rename existing target files to a timestamped backup before
  copying source files.

## State Files

Write JSON state files under `.state/`.

Each state file should include:

- Tool version
- Timestamp
- Windows user name/domain/SID
- Target root
- Copy strategy
- For each folder:
  - Name
  - Known folder GUID
  - Old Shell API path
  - New path
  - Verified path
  - Copy summary
  - Conflicts or errors

## GUI Direction

The first implementation should remain CLI, but the core should be GUI-ready.

Keep business logic outside `Program.cs`:

- `KnownFolderService`
- `MigrationService`
- `CopyService`
- `CleanupService`
- `StateStore`

A later GUI can use WPF or WinUI 3 and call the same services. The GUI should
provide:

- Folder checklist
- Target drive/root picker
- Preview page
- Execute page with progress
- Restore page
- Cleanup dry-run view

## Testing

Automated tests:

- Target root resolution.
- Known folder config validation.
- Copy strategies.
- Conflict handling.
- State serialization/deserialization.
- Cleanup duplicate detection.

Manual Windows validation:

```powershell
.\known-folder-relocator.exe verify
.\known-folder-relocator.exe migrate --target-drive E --dry-run
.\known-folder-relocator.exe migrate --target-drive E
.\known-folder-relocator.exe verify
```

Explorer validation:

```powershell
explorer.exe shell:Desktop
explorer.exe shell:Personal
explorer.exe shell:Downloads
explorer.exe shell:My Pictures
explorer.exe shell:My Music
explorer.exe shell:My Video
```

Acceptance criteria:

- `verify` shows all migrated folders on the target drive.
- Folder Properties > Location shows the target drive.
- New files created through Explorer land on the target drive.
- Restore returns folders to their previous paths.
- Cleanup never deletes files whose target counterpart is missing or differs by
  hash.

## Migration From Current Repository

1. Add the C# solution and CLI project.
2. Port the Shell API logic from `Set-KnownFoldersWithShellApi.ps1`.
3. Port copy/state/restore behavior.
4. Port cleanup behavior.
5. Update README to make C# CLI the primary path.
6. Move PowerShell scripts to `legacy/`.
7. Publish a Windows single-file executable.

