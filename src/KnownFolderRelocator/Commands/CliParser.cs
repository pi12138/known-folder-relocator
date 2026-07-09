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
    string? LanguageCode = null,
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
    public static CliCommand Parse(string[] args)
    {
        var languageCode = ExtractLanguage(args, out args);
        if (args.Length == 0 || IsHelp(args[0]))
        {
            return new CliCommand(CommandName.Help, LanguageCode: languageCode, ShowHelp: true);
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
                    return new CliCommand(CommandName.Help, LanguageCode: languageCode, ShowHelp: true);
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

        return new CliCommand(name, languageCode, targetDrive, targetRoot, statePath, strategy, strategy == CopyStrategy.NoCopy, dryRun, force, removeEmptyDirs);
    }

    private static string? ExtractLanguage(string[] args, out string[] remainingArgs)
    {
        string? languageCode = null;
        var remaining = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--lang")
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("--lang requires a value.");
                }

                languageCode = args[++i];
                continue;
            }

            remaining.Add(args[i]);
        }

        remainingArgs = remaining.ToArray();
        return languageCode;
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
