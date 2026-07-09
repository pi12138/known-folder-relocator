using System.Security.Cryptography;
using KnownFolderRelocator.Shell;
using KnownFolderRelocator.State;

namespace KnownFolderRelocator.FileOperations;

public sealed class CleanupService(KnownFolderService knownFolderService, StateStore stateStore)
{
    public CleanupSummary Cleanup(string? statePath, bool dryRun, bool removeEmptyDirs)
    {
        statePath ??= stateStore.GetLatestStateFile();
        var state = stateStore.Read(statePath);
        var summary = new CleanupSummary { StateFile = statePath };

        foreach (var entry in state.Folders)
        {
            var oldPath = PathHelpers.ExpandPath(entry.OldPath);
            var newPath = PathHelpers.ExpandPath(entry.NewPath);

            if (string.IsNullOrWhiteSpace(oldPath) ||
                string.IsNullOrWhiteSpace(newPath) ||
                !Directory.Exists(oldPath) ||
                !Directory.Exists(newPath))
            {
                summary.SkippedInvalidFolder++;
                continue;
            }

            var oldRoot = Path.GetPathRoot(oldPath);
            var newRoot = Path.GetPathRoot(newPath);
            if (!string.Equals(oldRoot?.TrimEnd('\\', '/'), "C:", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(newRoot?.TrimEnd('\\', '/'), "C:", StringComparison.OrdinalIgnoreCase) ||
                PathHelpers.SamePath(oldPath, newPath))
            {
                summary.SkippedInvalidFolder++;
                continue;
            }

            var currentPath = knownFolderService.GetPath(entry.KnownFolderId);
            if (!PathHelpers.SamePath(currentPath, newPath))
            {
                summary.SkippedInvalidFolder++;
                continue;
            }

            CleanupFolder(oldPath, newPath, dryRun, removeEmptyDirs, summary);
        }

        return summary;
    }

    private static void CleanupFolder(string oldPath, string newPath, bool dryRun, bool removeEmptyDirs, CleanupSummary summary)
    {
        var oldRoot = new DirectoryInfo(oldPath).FullName.TrimEnd('\\', '/');
        foreach (var oldFile in Directory.EnumerateFiles(oldRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(oldRoot, oldFile);
            var newFilePath = Path.Combine(newPath, relativePath);
            if (!File.Exists(newFilePath))
            {
                summary.SkippedMissingTarget++;
                continue;
            }

            try
            {
                if (!SameFileContent(oldFile, newFilePath))
                {
                    summary.SkippedDifferentContent++;
                    continue;
                }

                summary.MatchedDuplicateFiles++;
                if (!dryRun)
                {
                    File.Delete(oldFile);
                    summary.DeletedFiles++;
                }
            }
            catch (Exception ex)
            {
                summary.Errors.Add(new CleanupError(oldFile, ex.Message));
            }
        }

        if (removeEmptyDirs)
        {
            RemoveEmptyDirectories(oldRoot, dryRun, summary);
        }
    }

    private static void RemoveEmptyDirectories(string oldRoot, bool dryRun, CleanupSummary summary)
    {
        foreach (var directory in Directory.EnumerateDirectories(oldRoot, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            TryRemoveEmptyDirectory(directory, dryRun, summary);
        }

        TryRemoveEmptyDirectory(oldRoot, dryRun, summary);
    }

    private static void TryRemoveEmptyDirectory(string directory, bool dryRun, CleanupSummary summary)
    {
        try
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any())
            {
                summary.SkippedNonEmptyDirectories++;
                return;
            }

            if (!dryRun)
            {
                Directory.Delete(directory);
            }
            summary.RemovedEmptyDirectories++;
        }
        catch (Exception ex)
        {
            summary.Errors.Add(new CleanupError(directory, ex.Message));
        }
    }

    private static bool SameFileContent(string leftPath, string rightPath)
    {
        var left = new FileInfo(leftPath);
        var right = new FileInfo(rightPath);
        if (left.Length != right.Length)
        {
            return false;
        }

        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(leftPath))) ==
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rightPath)));
    }
}
