using System.Security.Principal;
using KnownFolderRelocator.FileOperations;
using KnownFolderRelocator.Shell;
using KnownFolderRelocator.State;

namespace KnownFolderRelocator;

public enum MigrationMode
{
    Migrate,
    Attach
}

public sealed record MigrationRequest(
    MigrationMode Mode,
    IReadOnlyList<KnownFolderConfig> Folders,
    string TargetRoot,
    CopyStrategy CopyStrategy,
    bool DryRun);

public sealed record MigrationResult(string TargetRoot, string? StateFile, IReadOnlyList<FolderState> Folders);

public sealed class MigrationService(
    KnownFolderService knownFolderService,
    CopyService copyService,
    StateStore stateStore)
{
    public StateStore StateStore { get; } = stateStore;

    public MigrationResult SetKnownFolders(MigrationRequest request)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        if (!request.DryRun)
        {
            Directory.CreateDirectory(request.TargetRoot);
        }

        var state = new RelocationState
        {
            Mode = request.Mode.ToString(),
            CopyStrategy = request.CopyStrategy.ToString(),
            Timestamp = timestamp,
            UserSid = GetCurrentSid(),
            TargetRoot = request.TargetRoot
        };

        foreach (var folder in request.Folders)
        {
            var folderState = ProcessFolder(folder, request, timestamp);
            state.Folders.Add(folderState);
        }

        string? stateFile = null;
        if (!request.DryRun)
        {
            stateFile = StateStore.Write(state);
            knownFolderService.NotifyShellChanged();
        }

        return new MigrationResult(request.TargetRoot, stateFile, state.Folders);
    }

    public void Restore(string stateFile, bool dryRun)
    {
        var state = StateStore.Read(stateFile);
        foreach (var entry in state.Folders)
        {
            if (entry.KnownFolderId == Guid.Empty || string.IsNullOrWhiteSpace(entry.OldPath))
            {
                Console.Error.WriteLine($"Skipping incomplete restore entry: {entry.Name}");
                continue;
            }

            if (!dryRun)
            {
                knownFolderService.SetPath(entry.KnownFolderId, entry.OldPath);
                var restoredPath = knownFolderService.GetPath(entry.KnownFolderId);
                if (!PathHelpers.SamePath(restoredPath, entry.OldPath))
                {
                    throw new InvalidOperationException($"Restore verification failed for {entry.Name}: expected {entry.OldPath}, got {restoredPath}.");
                }
            }
        }

        if (!dryRun)
        {
            knownFolderService.NotifyShellChanged();
        }
    }

    private FolderState ProcessFolder(KnownFolderConfig folder, MigrationRequest request, string timestamp)
    {
        string? oldPath = null;
        var errors = new List<string>();
        try
        {
            oldPath = knownFolderService.GetPath(folder.KnownFolderId);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        var targetPath = Path.Combine(request.TargetRoot, folder.DirectoryName);
        if (!request.DryRun)
        {
            Directory.CreateDirectory(targetPath);
        }

        var copySummary = copyService.CopyMissingItems(oldPath, targetPath, request.CopyStrategy, timestamp, request.DryRun);
        string? verifiedPath = null;
        if (errors.Count == 0 && !request.DryRun)
        {
            knownFolderService.SetPath(folder.KnownFolderId, targetPath);
            verifiedPath = knownFolderService.GetPath(folder.KnownFolderId);
            if (!PathHelpers.SamePath(verifiedPath, targetPath))
            {
                throw new InvalidOperationException($"Verification failed for {folder.Name}: expected {targetPath}, got {verifiedPath}.");
            }
        }

        return new FolderState
        {
            Name = folder.Name,
            KnownFolderId = folder.KnownFolderId,
            RegistryName = folder.RegistryName,
            DirectoryName = folder.DirectoryName,
            OldPath = oldPath,
            NewPath = targetPath,
            VerifiedPath = verifiedPath,
            Copy = copySummary,
            Errors = errors
        };
    }

    private static string? GetCurrentSid()
    {
        try
        {
            return WindowsIdentity.GetCurrent().User?.Value;
        }
        catch
        {
            return null;
        }
    }
}
