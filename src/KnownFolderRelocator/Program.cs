using KnownFolderRelocator;
using KnownFolderRelocator.Commands;
using KnownFolderRelocator.FileOperations;
using KnownFolderRelocator.Shell;
using KnownFolderRelocator.State;

var appRoot = AppContext.BaseDirectory;
var workingRoot = Directory.GetCurrentDirectory();
var configPath = Path.Combine(workingRoot, "known-folders.json");
if (!File.Exists(configPath))
{
    var colocatedConfig = Path.Combine(appRoot, "known-folders.json");
    if (File.Exists(colocatedConfig))
    {
        configPath = colocatedConfig;
    }
}

try
{
    CliCommand? command = null;
    if (args.Length > 0)
    {
        command = CliParser.Parse(args);
        if (command.ShowHelp)
        {
            Console.WriteLine(CliParser.HelpText);
            return 0;
        }
    }

    var configStore = new KnownFolderConfigStore();
    var folders = configStore.Load(configPath);
    var knownFolderService = new KnownFolderService();
    var copyService = new CopyService();
    var stateStore = new StateStore(Path.Combine(workingRoot, ".state"));
    var migrationService = new MigrationService(knownFolderService, copyService, stateStore);
    var cleanupService = new CleanupService(knownFolderService, stateStore);

    if (args.Length == 0)
    {
        return RunInteractive(folders, knownFolderService, migrationService, cleanupService);
    }

    return command!.Name switch
    {
        CommandName.Verify => Verify(folders, knownFolderService),
        CommandName.Migrate => RunMigration(command, folders, migrationService, MigrationMode.Migrate),
        CommandName.Attach => RunMigration(command, folders, migrationService, MigrationMode.Attach),
        CommandName.Restore => RunRestore(command, migrationService),
        CommandName.Cleanup => RunCleanup(command, cleanupService),
        _ => Fail("Unknown command.")
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int Verify(IReadOnlyList<KnownFolderConfig> folders, KnownFolderService service)
{
    EnsureWindows();
    Console.WriteLine($"{"Name",-12} {"Exists",-7} Path");
    foreach (var folder in folders)
    {
        try
        {
            var path = service.GetPath(folder.KnownFolderId);
            Console.WriteLine($"{folder.Name,-12} {Directory.Exists(path),-7} {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{folder.Name,-12} {"ERROR",-7} {ex.Message}");
        }
    }

    return 0;
}

static int RunInteractive(
    IReadOnlyList<KnownFolderConfig> folders,
    KnownFolderService knownFolderService,
    MigrationService migrationService,
    CleanupService cleanupService)
{
    while (true)
    {
        Console.Clear();
        Console.WriteLine("Known Folder Relocator");
        Console.WriteLine();
        Console.WriteLine("1. Verify current known folder paths");
        Console.WriteLine("2. Preview migration to a target drive/root");
        Console.WriteLine("3. Run migration to a target drive/root");
        Console.WriteLine("4. Preview re-attach to existing target data");
        Console.WriteLine("5. Run re-attach to existing target data");
        Console.WriteLine("6. Restore from latest or specified state file");
        Console.WriteLine("7. Preview cleanup of duplicate old C: files");
        Console.WriteLine("8. Run cleanup of duplicate old C: files");
        Console.WriteLine("9. Show command-line help");
        Console.WriteLine("0. Exit");
        Console.WriteLine();
        Console.Write("Select an option: ");

        var choice = (Console.ReadLine() ?? string.Empty).Trim();
        Console.WriteLine();

        try
        {
            switch (choice)
            {
                case "1":
                    Verify(folders, knownFolderService);
                    Pause();
                    break;
                case "2":
                    RunInteractiveMigration(folders, migrationService, MigrationMode.Migrate, dryRun: true);
                    Pause();
                    break;
                case "3":
                    RunInteractiveMigration(folders, migrationService, MigrationMode.Migrate, dryRun: false);
                    Pause();
                    break;
                case "4":
                    RunInteractiveMigration(folders, migrationService, MigrationMode.Attach, dryRun: true);
                    Pause();
                    break;
                case "5":
                    RunInteractiveMigration(folders, migrationService, MigrationMode.Attach, dryRun: false);
                    Pause();
                    break;
                case "6":
                    RunInteractiveRestore(migrationService);
                    Pause();
                    break;
                case "7":
                    RunInteractiveCleanup(cleanupService, force: false);
                    Pause();
                    break;
                case "8":
                    RunInteractiveCleanup(cleanupService, force: true);
                    Pause();
                    break;
                case "9":
                    Console.WriteLine(CliParser.HelpText);
                    Pause();
                    break;
                case "0":
                case "q":
                case "Q":
                    return 0;
                default:
                    Console.WriteLine("Unknown option.");
                    Pause();
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Pause();
        }
    }
}

static void RunInteractiveMigration(
    IReadOnlyList<KnownFolderConfig> folders,
    MigrationService migrationService,
    MigrationMode mode,
    bool dryRun)
{
    EnsureWindows();
    var targetRoot = ReadTargetRoot();
    var copyStrategy = ReadCopyStrategy(mode);

    if (!dryRun && !ConfirmDestructive("This will update Windows known folder paths. Continue?"))
    {
        Console.WriteLine("Canceled.");
        return;
    }

    var result = migrationService.SetKnownFolders(new MigrationRequest(
        mode,
        folders,
        targetRoot,
        copyStrategy,
        dryRun));

    Console.WriteLine($"{mode} {(dryRun ? "preview" : "completed")}. Target root: {result.TargetRoot}");
    PrintFolderResults(result.Folders, dryRun);
    if (!string.IsNullOrWhiteSpace(result.StateFile))
    {
        Console.WriteLine($"State file: {result.StateFile}");
    }
    if (dryRun)
    {
        Console.WriteLine("Dry run only. No files or Shell paths were changed.");
    }
}

static void RunInteractiveRestore(MigrationService migrationService)
{
    EnsureWindows();
    Console.Write("State file path, or blank for latest: ");
    var stateFile = (Console.ReadLine() ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(stateFile))
    {
        stateFile = migrationService.StateStore.GetLatestStateFile();
    }

    var dryRun = ReadYesNo("Preview only? [Y/n]: ", defaultValue: true);
    if (!dryRun && !ConfirmDestructive("This will restore known folder paths from the selected state file. Continue?"))
    {
        Console.WriteLine("Canceled.");
        return;
    }

    migrationService.Restore(stateFile, dryRun);
    Console.WriteLine($"Restore {(dryRun ? "preview" : "completed")} from {stateFile}");
    if (dryRun)
    {
        Console.WriteLine("Dry run only. No Shell paths were changed.");
    }
}

static void RunInteractiveCleanup(CleanupService cleanupService, bool force)
{
    EnsureWindows();
    Console.Write("State file path, or blank for latest: ");
    var stateFile = (Console.ReadLine() ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(stateFile))
    {
        stateFile = null;
    }

    var removeEmptyDirs = ReadYesNo("Remove empty old directories too? [y/N]: ", defaultValue: false);
    if (force && !ConfirmDestructive("This will delete duplicate old C: files whose target counterpart has the same SHA-256 hash. Continue?"))
    {
        Console.WriteLine("Canceled.");
        return;
    }

    var result = cleanupService.Cleanup(stateFile, dryRun: !force, removeEmptyDirs);
    Console.WriteLine(result.ToJson());
}

static string ReadTargetRoot()
{
    Console.Write("Target drive, for example E: (leave blank to enter full target root): ");
    var targetDrive = (Console.ReadLine() ?? string.Empty).Trim();
    if (!string.IsNullOrWhiteSpace(targetDrive))
    {
        return TargetRootResolver.Resolve(targetDrive, null);
    }

    Console.Write("Target root, for example E:\\Users\\pyo1024: ");
    var targetRoot = (Console.ReadLine() ?? string.Empty).Trim();
    return TargetRootResolver.Resolve(null, targetRoot);
}

static CopyStrategy ReadCopyStrategy(MigrationMode mode)
{
    Console.WriteLine();
    Console.WriteLine("Copy strategy:");
    Console.WriteLine("1. CopyMissing (default, do not overwrite target files)");
    Console.WriteLine("2. NoCopy (only update known folder paths)");
    Console.WriteLine("3. BackupConflicts (backup target conflicts, then copy source files)");
    Console.Write(mode == MigrationMode.Attach ? "Select copy strategy [2]: " : "Select copy strategy [1]: ");

    var input = (Console.ReadLine() ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(input))
    {
        return mode == MigrationMode.Attach ? CopyStrategy.NoCopy : CopyStrategy.CopyMissing;
    }

    return input switch
    {
        "1" => CopyStrategy.CopyMissing,
        "2" => CopyStrategy.NoCopy,
        "3" => CopyStrategy.BackupConflicts,
        _ => throw new ArgumentException("Invalid copy strategy.")
    };
}

static bool ReadYesNo(string prompt, bool defaultValue)
{
    Console.Write(prompt);
    var input = (Console.ReadLine() ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(input))
    {
        return defaultValue;
    }

    return input.Equals("y", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("yes", StringComparison.OrdinalIgnoreCase);
}

static bool ConfirmDestructive(string message)
{
    Console.WriteLine(message);
    Console.Write("Type YES to continue: ");
    return string.Equals(Console.ReadLine(), "YES", StringComparison.Ordinal);
}

static void Pause()
{
    Console.WriteLine();
    Console.Write("Press Enter to continue...");
    Console.ReadLine();
}

static int RunMigration(
    CliCommand command,
    IReadOnlyList<KnownFolderConfig> folders,
    MigrationService migrationService,
    MigrationMode mode)
{
    EnsureWindows();
    var targetRoot = TargetRootResolver.Resolve(command.TargetDrive, command.TargetRoot);
    var result = migrationService.SetKnownFolders(new MigrationRequest(
        mode,
        folders,
        targetRoot,
        command.CopyStrategy,
        command.DryRun));

    Console.WriteLine($"{mode} completed. Target root: {result.TargetRoot}");
    PrintFolderResults(result.Folders, command.DryRun);
    if (!string.IsNullOrWhiteSpace(result.StateFile))
    {
        Console.WriteLine($"State file: {result.StateFile}");
    }
    if (command.DryRun)
    {
        Console.WriteLine("Dry run only. No files or Shell paths were changed.");
    }

    return 0;
}

static void PrintFolderResults(IReadOnlyList<FolderState> folders, bool dryRun)
{
    Console.WriteLine($"{"Name",-12} {"Action",-10} Current path -> Target path");
    foreach (var folder in folders)
    {
        var action = PathHelpers.SamePath(folder.OldPath, folder.NewPath) ? "Unchanged" : dryRun ? "WouldSet" : "Set";
        Console.WriteLine($"{folder.Name,-12} {action,-10} {folder.OldPath ?? "(unknown)"} -> {folder.NewPath}");

        if (folder.Copy is not null)
        {
            if (folder.Copy.Skipped)
            {
                Console.WriteLine($"{"",-12} {"Copy",-10} skipped");
            }
            else
            {
                Console.WriteLine($"{"",-12} {"Copy",-10} files={folder.Copy.CopiedFiles}, dirs={folder.Copy.CreatedDirectories}, conflicts={folder.Copy.Conflicts.Count}, backups={folder.Copy.BackedUpConflicts.Count}");
            }
        }

        foreach (var error in folder.Errors)
        {
            Console.WriteLine($"{"",-12} {"Error",-10} {error}");
        }
    }
}

static int RunRestore(CliCommand command, MigrationService migrationService)
{
    EnsureWindows();
    var stateFile = command.StatePath;
    if (string.IsNullOrWhiteSpace(stateFile))
    {
        stateFile = migrationService.StateStore.GetLatestStateFile();
    }

    migrationService.Restore(stateFile, command.DryRun);
    Console.WriteLine($"Restore completed from {stateFile}");
    if (command.DryRun)
    {
        Console.WriteLine("Dry run only. No Shell paths were changed.");
    }

    return 0;
}

static int RunCleanup(CliCommand command, CleanupService cleanupService)
{
    EnsureWindows();
    if (!command.DryRun && !command.Force)
    {
        throw new InvalidOperationException("cleanup requires --dry-run or --force.");
    }

    var result = cleanupService.Cleanup(command.StatePath, command.DryRun, command.RemoveEmptyDirs);
    Console.WriteLine(result.ToJson());
    return 0;
}

static void EnsureWindows()
{
    if (!OperatingSystem.IsWindows())
    {
        throw new PlatformNotSupportedException("known-folder-relocator must run on Windows.");
    }
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine(CliParser.HelpText);
    return 1;
}
