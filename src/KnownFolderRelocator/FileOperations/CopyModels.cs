namespace KnownFolderRelocator.FileOperations;

public enum CopyStrategy
{
    CopyMissing,
    NoCopy,
    BackupConflicts
}

public sealed class CopySummary
{
    public string? Source { get; set; }
    public required string Destination { get; set; }
    public int CreatedDirectories { get; set; }
    public int CopiedFiles { get; set; }
    public List<string> Conflicts { get; set; } = [];
    public List<BackupConflict> BackedUpConflicts { get; set; } = [];
    public bool Skipped { get; set; }
}

public sealed record BackupConflict(string Original, string Backup);
