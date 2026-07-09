namespace KnownFolderRelocator.FileOperations;

public static class PathHelpers
{
    public static string? ExpandPath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Environment.ExpandEnvironmentVariables(path);

    public static bool SamePath(string? leftPath, string? rightPath)
    {
        if (string.IsNullOrWhiteSpace(leftPath) || string.IsNullOrWhiteSpace(rightPath))
        {
            return false;
        }

        var left = Path.GetFullPath(ExpandPath(leftPath)!).TrimEnd('\\', '/');
        var right = Path.GetFullPath(ExpandPath(rightPath)!).TrimEnd('\\', '/');
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
