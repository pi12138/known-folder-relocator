using KnownFolderRelocator.FileOperations;

namespace KnownFolderRelocator.Commands;

public enum CommandName
{
    Help,
    Verify,
    Migrate,
    Attach,
    Restore,
    Cleanup
}

public sealed record CliCommand(
    CommandName Name,
    string? TargetDrive = null,
    string? TargetRoot = null,
    string? StatePath = null,
    CopyStrategy CopyStrategy = CopyStrategy.CopyMissing,
    bool NoCopy = false,
    bool DryRun = false,
    bool Force = false,
    bool RemoveEmptyDirs = false,
    bool ShowHelp = false);

public static class CliParser
{
    public const string HelpText = """
    Usage:
      known-folder-relocator verify
      known-folder-relocator migrate --target-drive E [--copy-strategy CopyMissing|NoCopy|BackupConflicts] [--dry-run]
      known-folder-relocator migrate --target-root E:\Users\pyo1024 [--dry-run]
      known-folder-relocator attach --target-drive E [--no-copy] [--dry-run]
      known-folder-relocator restore --state .state\shell-known-folder-xxx.json [--dry-run]
      known-folder-relocator cleanup --dry-run [--remove-empty-dirs] [--state .state\shell-known-folder-xxx.json]
      known-folder-relocator cleanup --force [--remove-empty-dirs] [--state .state\shell-known-folder-xxx.json]
    """;

    public static CliCommand Parse(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            return new CliCommand(CommandName.Help, ShowHelp: true);
        }

        var name = args[0].ToLowerInvariant() switch
        {
            "verify" => CommandName.Verify,
            "migrate" => CommandName.Migrate,
            "attach" => CommandName.Attach,
            "restore" => CommandName.Restore,
            "cleanup" => CommandName.Cleanup,
            _ => throw new ArgumentException($"Unknown command: {args[0]}")
        };

        string? targetDrive = null;
        string? targetRoot = null;
        string? statePath = null;
        var strategy = CopyStrategy.CopyMissing;
        var dryRun = false;
        var force = false;
        var removeEmptyDirs = false;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--target-drive":
                    targetDrive = ReadValue(args, ref i, arg);
                    break;
                case "--target-root":
                    targetRoot = ReadValue(args, ref i, arg);
                    break;
                case "--state":
                    statePath = ReadValue(args, ref i, arg);
                    break;
                case "--copy-strategy":
                    strategy = Enum.Parse<CopyStrategy>(ReadValue(args, ref i, arg), ignoreCase: true);
                    break;
                case "--no-copy":
                    strategy = CopyStrategy.NoCopy;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--force":
                    force = true;
                    break;
                case "--remove-empty-dirs":
                    removeEmptyDirs = true;
                    break;
                case "-h":
                case "--help":
                    return new CliCommand(CommandName.Help, ShowHelp: true);
                default:
                    throw new ArgumentException($"Unknown option: {arg}");
            }
        }

        if ((name == CommandName.Migrate || name == CommandName.Attach) &&
            string.IsNullOrWhiteSpace(targetDrive) &&
            string.IsNullOrWhiteSpace(targetRoot))
        {
            throw new ArgumentException("Specify --target-drive or --target-root.");
        }

        return new CliCommand(name, targetDrive, targetRoot, statePath, strategy, strategy == CopyStrategy.NoCopy, dryRun, force, removeEmptyDirs);
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }

    private static bool IsHelp(string value) =>
        value is "-h" or "--help" or "help";
}
