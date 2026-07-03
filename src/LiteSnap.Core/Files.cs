namespace LiteSnap.Core;

public static class Files
{
    /// <summary>
    /// Checks whether <paramref name="targetPath"/> resolves to a location
    /// inside or equal to <paramref name="rootPath"/>, preventing path-traversal escapes.
    /// Both paths are fully normalized before comparison.
    /// </summary>
    public static bool IsDirectoryWithinRoot(string targetPath, string rootPath)
    {
        var fullTarget = Path.GetFullPath(targetPath);
        var fullRoot = Path.GetFullPath(rootPath);

        // Ensure root ends with separator so "/home/user" does not match "/home/user2"
        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
            fullRoot += Path.DirectorySeparatorChar;

        return fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
}
