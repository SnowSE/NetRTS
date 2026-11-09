using System.Text;

namespace NetRts.Server.Tests;

/// <summary>
/// Keeps copy-paste out of the code: no run of <see cref="Window"/> meaningful lines may appear twice anywhere
/// in src (C# and JavaScript). When this fails, move the shared code into one function or module that both
/// places call (see "Sources of truth" in CLAUDE.md for where shared pieces live) rather than raising the
/// window or listing an exception.
/// </summary>
public class DuplicationTests
{
    /// <summary>How many meaningful lines in a row make a copy.</summary>
    private const int Window = 10;

    private static readonly string[] Extensions = [".cs", ".js"];

    /// <summary>Generated or third-party code.</summary>
    private static readonly string[] SkippedDirectories = ["bin", "obj", "Migrations", "vendor", "node_modules"];

    [Fact]
    public void No_ten_line_run_of_code_appears_twice()
    {
        var seen = new Dictionary<string, (string File, int Line)>(StringComparer.Ordinal);
        var copies = new List<string>();
        foreach (var file in SourceFiles())
        {
            var lines = Meaningful(file);
            var reported = -1; // report one overlapping run once, not once per line in it
            for (var i = 0; i + Window <= lines.Count; i++)
            {
                var key = string.Join('\n', lines.Skip(i).Take(Window).Select(l => l.Text));
                var here = (File: Relative(file), lines[i].Line);
                if (!seen.TryAdd(key, here) && i > reported)
                {
                    var first = seen[key];
                    copies.Add($"{here.File}:{here.Line} repeats {first.File}:{first.Line}");
                    reported = i + Window - 1;
                }
            }
        }

        Assert.True(copies.Count == 0, "Copied code (move it into something both places call):\n" + string.Join('\n', copies));
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Repo.Path("src"), "*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f)))
            .Where(f => !Relative(f).Split('/').Any(part => SkippedDirectories.Contains(part)))
            .Order(StringComparer.Ordinal);

    /// <summary>Lines that carry code: no blanks, lone brackets, comments, usings or imports.</summary>
    private static List<(int Line, string Text)> Meaningful(string file) =>
        File.ReadLines(file)
            .Select((text, i) => (Line: i + 1, Text: text.Trim()))
            .Where(l => l.Text.Length > 0
                        && l.Text.Any(c => !"{}()[];,".Contains(c))
                        && !l.Text.StartsWith("//", StringComparison.Ordinal)
                        && !l.Text.StartsWith("/*", StringComparison.Ordinal)
                        && !l.Text.StartsWith('*')
                        && !l.Text.StartsWith('#')
                        && !l.Text.StartsWith("using ", StringComparison.Ordinal)
                        && !l.Text.StartsWith("import ", StringComparison.Ordinal))
            .ToList();

    private static string Relative(string file) => Path.GetRelativePath(Repo.Root, file).Replace('\\', '/');
}
