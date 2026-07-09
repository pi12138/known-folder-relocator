using KnownFolderRelocator;
using KnownFolderRelocator.Commands;
using KnownFolderRelocator.FileOperations;
using KnownFolderRelocator.State;

var tests = new (string Name, Action Test)[]
{
    ("target drive resolves under Users", TargetDriveResolvesUnderUsers),
    ("target root rejects C drive", TargetRootRejectsCDrive),
    ("config validation loads GUIDs", ConfigValidationLoadsGuids),
    ("copy missing does not overwrite conflicts", CopyMissingDoesNotOverwriteConflicts),
    ("backup conflicts preserves target", BackupConflictsPreservesTarget),
    ("cleanup parses remove empty dirs", CleanupParsesRemoveEmptyDirs),
    ("cli parses language option", CliParsesLanguageOption),
    ("state serialization round trips", StateSerializationRoundTrips)
};

var failed = 0;
foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static void TargetDriveResolvesUnderUsers()
{
    var root = TargetRootResolver.Resolve("E", null);
    Assert(root.StartsWith(@"E:\Users\", StringComparison.OrdinalIgnoreCase), root);
}

static void TargetRootRejectsCDrive()
{
    AssertThrows<ArgumentException>(() => TargetRootResolver.Resolve(null, @"C:\Users\Test"));
}

static void ConfigValidationLoadsGuids()
{
    using var scope = new TempScope();
    var configPath = Path.Combine(scope.Path, "known-folders.json");
    File.WriteAllText(configPath, """
    [
      {
        "Name": "Desktop",
        "KnownFolderId": "{B4BFCC3A-DB2C-424C-B029-7FE99A87C641}",
        "RegistryName": "Desktop",
        "DirectoryName": "Desktop"
      }
    ]
    """);

    var folders = new KnownFolderConfigStore().Load(configPath);
    Assert(folders.Count == 1, "folder count");
    Assert(folders[0].KnownFolderId == Guid.Parse("{B4BFCC3A-DB2C-424C-B029-7FE99A87C641}"), "guid");
}

static void CopyMissingDoesNotOverwriteConflicts()
{
    using var scope = new TempScope();
    var source = Directory.CreateDirectory(Path.Combine(scope.Path, "source")).FullName;
    var destination = Directory.CreateDirectory(Path.Combine(scope.Path, "destination")).FullName;
    File.WriteAllText(Path.Combine(source, "a.txt"), "source");
    File.WriteAllText(Path.Combine(destination, "a.txt"), "target");

    var summary = new CopyService().CopyMissingItems(source, destination, CopyStrategy.CopyMissing, "20260709-120000", dryRun: false);

    Assert(summary.Conflicts.Count == 1, "conflict count");
    Assert(File.ReadAllText(Path.Combine(destination, "a.txt")) == "target", "target not overwritten");
}

static void BackupConflictsPreservesTarget()
{
    using var scope = new TempScope();
    var source = Directory.CreateDirectory(Path.Combine(scope.Path, "source")).FullName;
    var destination = Directory.CreateDirectory(Path.Combine(scope.Path, "destination")).FullName;
    File.WriteAllText(Path.Combine(source, "a.txt"), "source");
    File.WriteAllText(Path.Combine(destination, "a.txt"), "target");

    var summary = new CopyService().CopyMissingItems(source, destination, CopyStrategy.BackupConflicts, "20260709-120000", dryRun: false);

    Assert(summary.BackedUpConflicts.Count == 1, "backup count");
    Assert(File.ReadAllText(Path.Combine(destination, "a.txt")) == "source", "source copied");
    Assert(File.ReadAllText(Path.Combine(destination, "a.txt.20260709-120000.bak")) == "target", "backup content");
}

static void CleanupParsesRemoveEmptyDirs()
{
    var command = CliParser.Parse(["cleanup", "--dry-run", "--remove-empty-dirs"]);
    Assert(command.Name == CommandName.Cleanup, "cleanup command");
    Assert(command.DryRun, "dry run");
    Assert(command.RemoveEmptyDirs, "remove empty dirs");
}

static void CliParsesLanguageOption()
{
    var command = CliParser.Parse(["--lang", "zh", "verify"]);
    Assert(command.Name == CommandName.Verify, "verify command");
    Assert(command.LanguageCode == "zh", "language code");
}

static void StateSerializationRoundTrips()
{
    using var scope = new TempScope();
    var store = new StateStore(Path.Combine(scope.Path, ".state"));
    var state = new RelocationState
    {
        Mode = "Migrate",
        CopyStrategy = "CopyMissing",
        Timestamp = "20260709-120000",
        TargetRoot = @"E:\Users\Test",
        Folders =
        [
            new FolderState
            {
                Name = "Desktop",
                KnownFolderId = Guid.Parse("{B4BFCC3A-DB2C-424C-B029-7FE99A87C641}"),
                DirectoryName = "Desktop",
                OldPath = @"C:\Users\Test\Desktop",
                NewPath = @"E:\Users\Test\Desktop",
                VerifiedPath = @"E:\Users\Test\Desktop"
            }
        ]
    };

    var path = store.Write(state);
    var roundTrip = store.Read(path);
    Assert(roundTrip.Folders.Count == 1, "folder count");
    Assert(roundTrip.Folders[0].Name == "Desktop", "folder name");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

internal sealed class TempScope : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kfr-tests-" + Guid.NewGuid().ToString("N"));

    public TempScope() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
