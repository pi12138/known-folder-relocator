using KnownFolderRelocator;
using KnownFolderRelocator.Commands;
using KnownFolderRelocator.FileOperations;
using KnownFolderRelocator.Localization;
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
    var localizer = Localizer.Create();
    if (args.Length > 0)
    {
        command = CliParser.Parse(args);
        localizer = Localizer.Create(command.LanguageCode);
        if (command.ShowHelp)
        {
            Console.WriteLine(localizer.HelpText);
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
        return RunInteractive(folders, knownFolderService, migrationService, cleanupService, localizer);
    }

    return command!.Name switch
    {
        CommandName.Verify => Verify(folders, knownFolderService, localizer),
        CommandName.Migrate => RunMigration(command, folders, migrationService, MigrationMode.Migrate, localizer),
        CommandName.Attach => RunMigration(command, folders, migrationService, MigrationMode.Attach, localizer),
        CommandName.Restore => RunRestore(command, migrationService, localizer),
        CommandName.Cleanup => RunCleanup(command, cleanupService, localizer),
        _ => Fail(localizer.T("UnknownCommand"), localizer)
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int Verify(IReadOnlyList<KnownFolderConfig> folders, KnownFolderService service, Localizer localizer)
{
    EnsureWindows(localizer);
    Console.WriteLine($"{localizer.T("Name"),-12} {localizer.T("Exists"),-7} {localizer.T("Path")}");
    foreach (var folder in folders)
    {
        try
        {
            var path = service.GetPath(folder.KnownFolderId);
            Console.WriteLine($"{folder.Name,-12} {Directory.Exists(path),-7} {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{folder.Name,-12} {localizer.T("Error"),-7} {ex.Message}");
        }
    }

    return 0;
}

static int RunInteractive(
    IReadOnlyList<KnownFolderConfig> folders,
    KnownFolderService knownFolderService,
    MigrationService migrationService,
    CleanupService cleanupService,
    Localizer localizer)
{
    while (true)
    {
        Console.Clear();
        Console.WriteLine(localizer.T("AppTitle"));
        Console.WriteLine();
        Console.WriteLine($"1. {localizer.T("MenuVerify")}");
        Console.WriteLine($"2. {localizer.T("MenuPreviewMigrate")}");
        Console.WriteLine($"3. {localizer.T("MenuRunMigrate")}");
        Console.WriteLine($"4. {localizer.T("MenuPreviewAttach")}");
        Console.WriteLine($"5. {localizer.T("MenuRunAttach")}");
        Console.WriteLine($"6. {localizer.T("MenuRestore")}");
        Console.WriteLine($"7. {localizer.T("MenuPreviewCleanup")}");
        Console.WriteLine($"8. {localizer.T("MenuRunCleanup")}");
        Console.WriteLine($"9. {localizer.T("MenuHelp")}");
        Console.WriteLine($"0. {localizer.T("MenuExit")}");
        Console.WriteLine();
        Console.Write(localizer.T("SelectOption"));

        var choice = (Console.ReadLine() ?? string.Empty).Trim();
        Console.WriteLine();

        try
        {
            switch (choice)
            {
                case "1":
                    Verify(folders, knownFolderService, localizer);
                    Pause(localizer);
                    break;
                case "2":
                    RunInteractiveMigration(folders, migrationService, MigrationMode.Migrate, dryRun: true, localizer);
                    Pause(localizer);
                    break;
                case "3":
                    RunInteractiveMigration(folders, migrationService, MigrationMode.Migrate, dryRun: false, localizer);
                    Pause(localizer);
                    break;
                case "4":
                    RunInteractiveMigration(folders, migrationService, MigrationMode.Attach, dryRun: true, localizer);
                    Pause(localizer);
                    break;
                case "5":
                    RunInteractiveMigration(folders, migrationService, MigrationMode.Attach, dryRun: false, localizer);
                    Pause(localizer);
                    break;
                case "6":
                    RunInteractiveRestore(migrationService, localizer);
                    Pause(localizer);
                    break;
                case "7":
                    RunInteractiveCleanup(cleanupService, force: false, localizer);
                    Pause(localizer);
                    break;
                case "8":
                    RunInteractiveCleanup(cleanupService, force: true, localizer);
                    Pause(localizer);
                    break;
                case "9":
                    Console.WriteLine(localizer.HelpText);
                    Pause(localizer);
                    break;
                case "0":
                case "q":
                case "Q":
                    return 0;
                default:
                    Console.WriteLine(localizer.T("UnknownOption"));
                    Pause(localizer);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Pause(localizer);
        }
    }
}

static void RunInteractiveMigration(
    IReadOnlyList<KnownFolderConfig> folders,
    MigrationService migrationService,
    MigrationMode mode,
    bool dryRun,
    Localizer localizer)
{
    EnsureWindows(localizer);
    var targetRoot = ReadTargetRoot(localizer);
    var copyStrategy = ReadCopyStrategy(mode, localizer);

    if (!dryRun && !ConfirmDestructive(localizer.T("MigrationConfirm"), localizer))
    {
        Console.WriteLine(localizer.T("Canceled"));
        return;
    }

    var result = migrationService.SetKnownFolders(new MigrationRequest(
        mode,
        folders,
        targetRoot,
        copyStrategy,
        dryRun));

    Console.WriteLine($"{mode} {(dryRun ? "preview" : "completed")}. {localizer.T("TargetRoot")}: {result.TargetRoot}");
    PrintFolderResults(result.Folders, dryRun, localizer);
    if (!string.IsNullOrWhiteSpace(result.StateFile))
    {
        Console.WriteLine($"{localizer.T("StateFile")}: {result.StateFile}");
    }
    if (dryRun)
    {
        Console.WriteLine(localizer.T("DryRunOnly"));
    }
}

static void RunInteractiveRestore(MigrationService migrationService, Localizer localizer)
{
    EnsureWindows(localizer);
    Console.Write(localizer.T("StateFilePrompt"));
    var stateFile = (Console.ReadLine() ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(stateFile))
    {
        stateFile = migrationService.StateStore.GetLatestStateFile();
    }

    var dryRun = ReadYesNo(localizer.T("PreviewOnlyPrompt"), defaultValue: true);
    if (!dryRun && !ConfirmDestructive(localizer.T("RestoreConfirm"), localizer))
    {
        Console.WriteLine(localizer.T("Canceled"));
        return;
    }

    migrationService.Restore(stateFile, dryRun);
    Console.WriteLine(string.Format(dryRun ? localizer.T("RestorePreview") : localizer.T("RestoreCompleted"), stateFile));
    if (dryRun)
    {
        Console.WriteLine(localizer.T("DryRunOnly"));
    }
}

static void RunInteractiveCleanup(CleanupService cleanupService, bool force, Localizer localizer)
{
    EnsureWindows(localizer);
    Console.Write(localizer.T("StateFilePrompt"));
    var stateFile = (Console.ReadLine() ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(stateFile))
    {
        stateFile = null;
    }

    var removeEmptyDirs = ReadYesNo(localizer.T("RemoveEmptyDirsPrompt"), defaultValue: false);
    if (force && !ConfirmDestructive(localizer.T("CleanupConfirm"), localizer))
    {
        Console.WriteLine(localizer.T("Canceled"));
        return;
    }

    var result = cleanupService.Cleanup(stateFile, dryRun: !force, removeEmptyDirs);
    Console.WriteLine(result.ToJson());
}

static string ReadTargetRoot(Localizer localizer)
{
    Console.Write(localizer.T("TargetDrivePrompt"));
    var targetDrive = (Console.ReadLine() ?? string.Empty).Trim();
    if (!string.IsNullOrWhiteSpace(targetDrive))
    {
        return TargetRootResolver.Resolve(targetDrive, null);
    }

    Console.Write(localizer.T("TargetRootPrompt"));
    var targetRoot = (Console.ReadLine() ?? string.Empty).Trim();
    return TargetRootResolver.Resolve(null, targetRoot);
}

static CopyStrategy ReadCopyStrategy(MigrationMode mode, Localizer localizer)
{
    Console.WriteLine();
    Console.WriteLine(localizer.T("CopyStrategy"));
    Console.WriteLine(localizer.T("CopyStrategy1"));
    Console.WriteLine(localizer.T("CopyStrategy2"));
    Console.WriteLine(localizer.T("CopyStrategy3"));
    Console.Write(mode == MigrationMode.Attach ? localizer.T("SelectCopyStrategyAttach") : localizer.T("SelectCopyStrategyMigrate"));

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
        _ => throw new ArgumentException(localizer.T("InvalidCopyStrategy"))
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

static bool ConfirmDestructive(string message, Localizer localizer)
{
    Console.WriteLine(message);
    Console.Write(localizer.T("TypeYes"));
    return string.Equals(Console.ReadLine(), "YES", StringComparison.Ordinal);
}

static void Pause(Localizer localizer)
{
    Console.WriteLine();
    Console.Write(localizer.T("PressEnter"));
    Console.ReadLine();
}

static int RunMigration(
    CliCommand command,
    IReadOnlyList<KnownFolderConfig> folders,
    MigrationService migrationService,
    MigrationMode mode,
    Localizer localizer)
{
    EnsureWindows(localizer);
    var targetRoot = TargetRootResolver.Resolve(command.TargetDrive, command.TargetRoot);
    var result = migrationService.SetKnownFolders(new MigrationRequest(
        mode,
        folders,
        targetRoot,
        command.CopyStrategy,
        command.DryRun));

    Console.WriteLine($"{mode} completed. {localizer.T("TargetRoot")}: {result.TargetRoot}");
    PrintFolderResults(result.Folders, command.DryRun, localizer);
    if (!string.IsNullOrWhiteSpace(result.StateFile))
    {
        Console.WriteLine($"{localizer.T("StateFile")}: {result.StateFile}");
    }
    if (command.DryRun)
    {
        Console.WriteLine(localizer.T("DryRunOnly"));
    }

    return 0;
}

static void PrintFolderResults(IReadOnlyList<FolderState> folders, bool dryRun, Localizer localizer)
{
    Console.WriteLine($"{localizer.T("Name"),-12} {localizer.T("Action"),-10} {localizer.T("CurrentToTarget")}");
    foreach (var folder in folders)
    {
        var action = PathHelpers.SamePath(folder.OldPath, folder.NewPath) ? localizer.T("Unchanged") : dryRun ? localizer.T("WouldSet") : localizer.T("Set");
        Console.WriteLine($"{folder.Name,-12} {action,-10} {folder.OldPath ?? "(unknown)"} -> {folder.NewPath}");

        if (folder.Copy is not null)
        {
            if (folder.Copy.Skipped)
            {
                Console.WriteLine($"{"",-12} {localizer.T("Copy"),-10} {localizer.T("Skipped")}");
            }
            else
            {
                Console.WriteLine($"{"",-12} {localizer.T("Copy"),-10} files={folder.Copy.CopiedFiles}, dirs={folder.Copy.CreatedDirectories}, conflicts={folder.Copy.Conflicts.Count}, backups={folder.Copy.BackedUpConflicts.Count}");
            }
        }

        foreach (var error in folder.Errors)
        {
            Console.WriteLine($"{"",-12} {localizer.T("Error"),-10} {error}");
        }
    }
}

static int RunRestore(CliCommand command, MigrationService migrationService, Localizer localizer)
{
    EnsureWindows(localizer);
    var stateFile = command.StatePath;
    if (string.IsNullOrWhiteSpace(stateFile))
    {
        stateFile = migrationService.StateStore.GetLatestStateFile();
    }

    migrationService.Restore(stateFile, command.DryRun);
    Console.WriteLine(string.Format(command.DryRun ? localizer.T("RestorePreview") : localizer.T("RestoreCompleted"), stateFile));
    if (command.DryRun)
    {
        Console.WriteLine(localizer.T("DryRunOnly"));
    }

    return 0;
}

static int RunCleanup(CliCommand command, CleanupService cleanupService, Localizer localizer)
{
    EnsureWindows(localizer);
    if (!command.DryRun && !command.Force)
    {
        throw new InvalidOperationException(localizer.T("CleanupRequiresForce"));
    }

    var result = cleanupService.Cleanup(command.StatePath, command.DryRun, command.RemoveEmptyDirs);
    Console.WriteLine(result.ToJson());
    return 0;
}

static void EnsureWindows(Localizer localizer)
{
    if (!OperatingSystem.IsWindows())
    {
        throw new PlatformNotSupportedException(localizer.T("NotWindows"));
    }
}

static int Fail(string message, Localizer localizer)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine(localizer.HelpText);
    return 1;
}
