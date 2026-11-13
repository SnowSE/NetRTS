using System.Text.RegularExpressions;
using NetRts.Bots.Blocks;

namespace NetRts.Server.Tests;

/// <summary>
/// The edit page's "Ready for more than blocks?" downloads: blocks/python.js and blocks/csharp.js write a
/// program's rules as code, and blocks/export.js puts them into the bots in blocks/export/. These keep the
/// generators, the templates and the block vocabulary in step.
/// </summary>
public partial class BlockExportTests
{
    private static string Blocks(params string[] parts) => File.ReadAllText(Repo.Path(["src", "NetRts.Server", "wwwroot", "blocks", .. parts]));

    [Theory]
    [InlineData("python.js")]
    [InlineData("csharp.js")]
    public void Every_block_kind_has_code(string generator)
    {
        var js = Blocks(generator);
        var vocabulary = BlockProgramValidator.Vocabulary;
        var missing = vocabulary.ConditionKinds.Concat(vocabulary.ActionKinds).Where(kind => !js.Contains($"case '{kind}'", StringComparison.Ordinal));
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_python_call_is_on_the_python_bot()
    {
        var template = Blocks("export", "bot.py.txt");
        var missing = PythonUse().Matches(Blocks("python.js")).Select(m => m.Groups[1].Value).Distinct()
            .Where(name => !template.Contains($"def {name}(", StringComparison.Ordinal) && !template.Contains($"self.{name} =", StringComparison.Ordinal));
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_csharp_call_is_on_the_csharp_bot()
    {
        var template = Blocks("export", "Bot.cs.txt");
        var missing = CSharpUse().Matches(Blocks("csharp.js")).Select(m => m.Groups[1].Value).Distinct()
            .Where(name => !Regex.IsMatch(template, $@"public [^\n=;]*\b{name}\b"));
        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("bot.py.txt")]
    [InlineData("Bot.cs.txt")]
    public void Every_placeholder_is_filled(string template)
    {
        var export = Blocks("export.js");
        var unfilled = Placeholder().Matches(Blocks("export", template)).Select(m => m.Groups[1].Value).Distinct()
            .Where(name => !export.Contains($"    {name}:", StringComparison.Ordinal));
        Assert.Empty(unfilled);
    }

    [Fact]
    public void The_python_bot_parses()
    {
        var file = Fill("bot.py.txt", rules: "    pass", switches: "{\"Attack!\": False}");
        var (exit, output) = Repo.Run("python3", "-c", "import ast, sys; ast.parse(open(sys.argv[1]).read())", file);
        Assert.True(exit == 0, output);
    }

    [Fact]
    public void The_csharp_bot_builds()
    {
        var file = Fill("Bot.cs.txt", rules: "        bot.Train(\"Worker\");", switches: "        [\"Attack!\"] = false,", name: "Bot.cs");
        var (exit, output) = Repo.Run("dotnet", "build", file, "--nologo", "-v", "q");
        Assert.True(exit == 0, output);
    }

    /// <summary>A template with its placeholders filled, saved in a new folder of its own.</summary>
    private static string Fill(string template, string rules, string switches, string name = "bot.py")
    {
        var text = Placeholder().Replace(Blocks("export", template), m => m.Groups[1].Value switch
        {
            "RULES" => rules,
            "SWITCHES" => switches,
            "SERVER" => "http://localhost:5080",
            _ => "Test",
        });
        var dir = Directory.CreateTempSubdirectory("netrts-export-");
        var path = Path.Combine(dir.FullName, name);
        File.WriteAllText(path, text);
        return path;
    }

    [GeneratedRegex(@"\bbot\.([a-z_]+)\b(?!\.)")]   // not bot.py.txt, a file name
    private static partial Regex PythonUse();

    [GeneratedRegex(@"\bbot\.([A-Z]\w*)")]
    private static partial Regex CSharpUse();

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex Placeholder();
}
