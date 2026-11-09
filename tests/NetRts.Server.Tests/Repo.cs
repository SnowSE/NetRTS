namespace NetRts.Server.Tests;

/// <summary>The checked-out repository, for tests that read source files.</summary>
public static class Repo
{
    public static string Root { get; } = Find();

    public static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "NetRts.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Can't find the repository root (NetRts.sln) above the test binaries.");
    }
}
