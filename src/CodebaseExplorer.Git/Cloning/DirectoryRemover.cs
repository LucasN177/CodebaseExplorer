namespace CodebaseExplorer.Git.Cloning;

/// <summary>
/// Deletes an app-managed clone directory. Git marks object files read-only (which blocks deletion on Windows), and a
/// clone may contain symbolic links: links are removed, never followed.
/// </summary>
internal static class DirectoryRemover
{
    public static void Delete(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        {
            return;
        }

        if (directory.LinkTarget is not null)
        {
            directory.Delete();
            return;
        }

        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo child && child.LinkTarget is null)
            {
                Delete(child.FullName);
                continue;
            }

            entry.Attributes = FileAttributes.Normal;
            entry.Delete();
        }

        directory.Attributes = FileAttributes.Directory;
        directory.Delete();
    }
}
