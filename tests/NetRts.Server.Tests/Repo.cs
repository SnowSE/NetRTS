using System.Diagnostics;

namespace NetRts.Server.Tests;

/// <summary>The checked-out repository, for tests that read source files.</summary>
public static class Repo
{
    public static string Root { get; } = Find();

    public static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    /// <summary>Runs a program (python3, dotnet, node) and returns its exit code and everything it printed.</summary>
    public static (int Exit, string Output) Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(3)))
        {
            throw new TimeoutException($"{program} took too long");
        }

        return (process.ExitCode, output.Result + error.Result);
    }

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
