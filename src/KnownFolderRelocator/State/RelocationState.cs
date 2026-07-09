using System.Text.Json;
using KnownFolderRelocator.FileOperations;

namespace KnownFolderRelocator.State;

public sealed class RelocationState
{
    public string Tool { get; set; } = "known-folder-relocator";
    public string Version { get; set; } = typeof(RelocationState).Assembly.GetName().Version?.ToString() ?? "0.3.0";
    public required string Mode { get; set; }
    public required string CopyStrategy { get; set; }
    public required string Timestamp { get; set; }
    public string UserName { get; set; } = Environment.UserName;
    public string UserDomain { get; set; } = Environment.UserDomainName;
    public string? UserSid { get; set; }
    public required string TargetRoot { get; set; }
    public List<FolderState> Folders { get; set; } = [];
}

public sealed class FolderState
{
    public required string Name { get; set; }
    public required Guid KnownFolderId { get; set; }
    public string? RegistryName { get; set; }
    public required string DirectoryName { get; set; }
    public string? OldPath { get; set; }
    public required string NewPath { get; set; }
    public string? VerifiedPath { get; set; }
    public CopySummary? Copy { get; set; }
    public List<string> Errors { get; set; } = [];
}

public sealed class CleanupSummary
{
    public string? StateFile { get; set; }
    public int MatchedDuplicateFiles { get; set; }
    public int DeletedFiles { get; set; }
    public int SkippedMissingTarget { get; set; }
    public int SkippedDifferentContent { get; set; }
    public int SkippedInvalidFolder { get; set; }
    public int RemovedEmptyDirectories { get; set; }
    public int SkippedNonEmptyDirectories { get; set; }
    public List<CleanupError> Errors { get; set; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, StateStore.JsonOptions);
}

public sealed record CleanupError(string Path, string Error);
