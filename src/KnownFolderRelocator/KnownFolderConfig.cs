using System.Text.Json;

namespace KnownFolderRelocator;

public sealed record KnownFolderConfig(
    string Name,
    Guid KnownFolderId,
    string RegistryName,
    string DirectoryName);

public sealed class KnownFolderConfigStore
{
    public IReadOnlyList<KnownFolderConfig> Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Known folders config was not found.", path);
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var entries = JsonSerializer.Deserialize<List<KnownFolderConfigEntry>>(File.ReadAllText(path), options)
            ?? throw new InvalidOperationException($"Invalid known folders config: {path}");

        var folders = new List<KnownFolderConfig>();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) ||
                string.IsNullOrWhiteSpace(entry.KnownFolderId) ||
                string.IsNullOrWhiteSpace(entry.DirectoryName))
            {
                throw new InvalidOperationException($"Invalid known folder entry in {path}.");
            }

            folders.Add(new KnownFolderConfig(
                entry.Name,
                Guid.Parse(entry.KnownFolderId),
                entry.RegistryName ?? string.Empty,
                entry.DirectoryName));
        }

        return folders;
    }

    private sealed record KnownFolderConfigEntry(
        string Name,
        string KnownFolderId,
        string? RegistryName,
        string DirectoryName);
}

public static class TargetRootResolver
{
    public static string Resolve(string? drive, string? root)
    {
        string fullRoot;
        if (!string.IsNullOrWhiteSpace(root))
        {
            fullRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(drive))
            {
                throw new ArgumentException("Specify --target-drive or --target-root.");
            }

            var normalizedDrive = drive.Trim();
            if (normalizedDrive.Length == 1 && char.IsAsciiLetter(normalizedDrive[0]))
            {
                normalizedDrive += ":";
            }
            if (normalizedDrive.Length != 2 || !char.IsAsciiLetter(normalizedDrive[0]) || normalizedDrive[1] != ':')
            {
                throw new ArgumentException($"Invalid drive value: {drive}");
            }

            fullRoot = Path.Combine(normalizedDrive + Path.DirectorySeparatorChar, "Users", Environment.UserName);
        }

        var rootPath = Path.GetPathRoot(fullRoot);
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException($"Target root must be an absolute path: {fullRoot}");
        }
        if (string.Equals(rootPath.TrimEnd('\\', '/'), "C:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Target root must not be on C drive: {fullRoot}");
        }

        return fullRoot.TrimEnd('\\', '/');
    }
}
