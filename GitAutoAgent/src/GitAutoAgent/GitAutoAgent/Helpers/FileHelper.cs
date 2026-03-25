namespace GitAutoAgent.Helpers;

/// <summary>Utilities for reading and writing files in the local repository clone.</summary>
public static class FileHelper
{
    /// <summary>
    /// Reads all text from a file if it exists and is within the size limit.
    /// Returns null if the file is missing or too large.
    /// </summary>
    /// <param name="path">Absolute path to the file.</param>
    /// <param name="maxBytes">Maximum allowed file size in bytes.</param>
    public static string? TryReadFile(string path, long maxBytes = 50_000)
    {
        if (!File.Exists(path)) return null;

        var info = new FileInfo(path);
        if (info.Length > maxBytes) return null;

        return File.ReadAllText(path);
    }

    /// <summary>
    /// Writes content to a file, creating any required directories.
    /// </summary>
    /// <param name="path">Absolute path to the target file.</param>
    /// <param name="content">Text content to write.</param>
    public static void WriteFile(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, content);
    }

    /// <summary>
    /// Deletes a file if it exists.
    /// </summary>
    public static void DeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>
    /// Recursively enumerates files under <paramref name="rootPath"/>, returning
    /// paths relative to that root.  Hidden directories (starting with '.') and
    /// common noise folders (bin, obj, node_modules, etc.) are skipped.
    /// </summary>
    /// <param name="rootPath">Root directory to enumerate.</param>
    /// <param name="maxFiles">Maximum number of file paths to return.</param>
    /// <param name="maxFileSizeBytes">Files larger than this are excluded.</param>
    public static IReadOnlyList<string> GetRelevantFiles(
        string rootPath,
        int maxFiles = 20,
        long maxFileSizeBytes = 50_000)
    {
        var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "node_modules", ".git", ".vs", ".idea",
            "packages", "dist", "build", "out", "__pycache__", ".cache"
        };

        var results = new List<string>();

        foreach (var file in EnumerateFilesRecursive(rootPath, skipDirs))
        {
            if (results.Count >= maxFiles) break;

            var info = new FileInfo(file);
            if (info.Length > maxFileSizeBytes) continue;

            results.Add(Path.GetRelativePath(rootPath, file));
        }

        return results;
    }

    /// <summary>
    /// Safely deletes a directory and all its contents.
    /// On Windows, read-only files (common in .git) are made writable first.
    /// </summary>
    public static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;

        // Force-remove read-only flags (required for .git objects on Windows)
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var attr = File.GetAttributes(file);
            if ((attr & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(file, attr & ~FileAttributes.ReadOnly);
        }

        Directory.Delete(path, recursive: true);
    }

    // ── private helpers ──────────────────────────────────────────────────────

    private static IEnumerable<string> EnumerateFilesRecursive(
        string dir, HashSet<string> skipDirs)
    {
        IEnumerable<string> subDirs;
        try { subDirs = Directory.EnumerateDirectories(dir); }
        catch { yield break; }

        foreach (var sub in subDirs)
        {
            var name = Path.GetFileName(sub);
            if (name.StartsWith('.') || skipDirs.Contains(name)) continue;

            foreach (var f in EnumerateFilesRecursive(sub, skipDirs))
                yield return f;
        }

        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir); }
        catch { yield break; }

        foreach (var f in files)
            yield return f;
    }
}
