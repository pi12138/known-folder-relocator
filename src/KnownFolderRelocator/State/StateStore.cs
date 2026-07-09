using System.Text.Json;

namespace KnownFolderRelocator.State;

public sealed class StateStore(string stateDirectory)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string StateDirectory { get; } = stateDirectory;

    public string Write(RelocationState state)
    {
        Directory.CreateDirectory(StateDirectory);
        var path = Path.Combine(StateDirectory, $"shell-known-folder-{state.Timestamp}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
        return path;
    }

    public RelocationState Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("State file was not found.", path);
        }

        return JsonSerializer.Deserialize<RelocationState>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"Invalid state file: {path}");
    }

    public string GetLatestStateFile()
    {
        if (!Directory.Exists(StateDirectory))
        {
            throw new DirectoryNotFoundException($"State directory was not found: {StateDirectory}");
        }

        var latest = Directory.EnumerateFiles(StateDirectory, "shell-known-folder-*.json")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();

        return latest?.FullName ?? throw new FileNotFoundException($"No shell API state file was found in {StateDirectory}.");
    }
}
