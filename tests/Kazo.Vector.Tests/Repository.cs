namespace Kazo.Vector.Tests;

/// <summary>Where the repository is, found from the test binaries.</summary>
internal static class Repository
{
    /// <summary>Gets the folder that holds <c>Kazo.Vector.slnx</c>.</summary>
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kazo.Vector.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Kazo.Vector.slnx not found above " + AppContext.BaseDirectory);
    }
}
