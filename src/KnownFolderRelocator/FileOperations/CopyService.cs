namespace KnownFolderRelocator.FileOperations;

public sealed class CopyService
{
    public CopySummary CopyMissingItems(string? source, string destination, CopyStrategy strategy, string timestamp, bool dryRun)
    {
        var summary = new CopySummary
        {
            Source = source,
            Destination = destination
        };

        if (strategy == CopyStrategy.NoCopy ||
            string.IsNullOrWhiteSpace(source) ||
            !Directory.Exists(source) ||
            PathHelpers.SamePath(source, destination))
        {
            summary.Skipped = true;
            return summary;
        }

        if (!dryRun)
        {
            Directory.CreateDirectory(destination);
        }

        var sourceRoot = new DirectoryInfo(source).FullName.TrimEnd('\\', '/');
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, directory);
            var targetPath = Path.Combine(destination, relativePath);
            if (!Directory.Exists(targetPath))
            {
                if (!dryRun)
                {
                    Directory.CreateDirectory(targetPath);
                }
                summary.CreatedDirectories++;
            }
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, file);
            var targetPath = Path.Combine(destination, relativePath);
            var targetParent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetParent) && !Directory.Exists(targetParent))
            {
                if (!dryRun)
                {
                    Directory.CreateDirectory(targetParent);
                }
                summary.CreatedDirectories++;
            }

            if (File.Exists(targetPath))
            {
                if (strategy == CopyStrategy.BackupConflicts)
                {
                    var backupPath = $"{targetPath}.{timestamp}.bak";
                    if (!dryRun)
                    {
                        File.Move(targetPath, backupPath, overwrite: true);
                        File.Copy(file, targetPath, overwrite: true);
                    }
                    summary.BackedUpConflicts.Add(new BackupConflict(targetPath, backupPath));
                    summary.CopiedFiles++;
                }
                else
                {
                    summary.Conflicts.Add(targetPath);
                }

                continue;
            }

            if (!dryRun)
            {
                File.Copy(file, targetPath);
            }
            summary.CopiedFiles++;
        }

        return summary;
    }
}
