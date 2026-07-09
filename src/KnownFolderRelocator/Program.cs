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
    var command = CliParser.Parse(args);
    if (command.ShowHelp)
    {
        Console.WriteLine(CliParser.HelpText);
        return 0;
    }

    var configStore = new KnownFolderConfigStore();
    var folders = configStore.Load(configPath);
    var knownFolderService = new KnownFolderService();
    var copyService = new CopyService();
    var stateStore = new StateStore(Path.Combine(workingRoot, ".state"));
    var migrationService = new MigrationService(knownFolderService, copyService, stateStore);
    var cleanupService = new CleanupService(knownFolderService, stateStore);

    return command.Name switch
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
