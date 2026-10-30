using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetRts.Server.Tests;

/// <summary>The #/colab page's notebook: served as files, and the .ipynb kept in step with its source.</summary>
public class ColabTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/colab/badger_brawl.py", "text/plain")]
    [InlineData("/colab/badger_brawl.ipynb", "application/x-ipynb+json")]
    public async Task Notebook_files_are_served(string path, string contentType)
    {
        var response = await factory.CreateClient().GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Notebook_matches_its_source()
    {
        var http = factory.CreateClient();
        var source = await http.GetStringAsync("/colab/badger_brawl.py", Ct);
        using var notebook = JsonDocument.Parse(await http.GetStringAsync("/colab/badger_brawl.ipynb", Ct));

        var expected = Regex.Split(source.ReplaceLineEndings("\n"), @"^# %%.*\n", RegexOptions.Multiline)
            .Skip(1)
            .Zip(Regex.Matches(source.ReplaceLineEndings("\n"), @"^# %%(.*)$", RegexOptions.Multiline))
            .Where(cell => cell.Second.Groups[1].Value.Trim() != "[markdown]")
            .Select(cell => cell.First.Trim('\n'))
            .ToList();
        var actual = notebook.RootElement.GetProperty("cells").EnumerateArray()
            .Where(cell => cell.GetProperty("cell_type").GetString() == "code")
            .Select(cell => string.Concat(cell.GetProperty("source").EnumerateArray().Select(line => line.GetString())))
            .ToList();

        // If this fails, run: python tools/make_colab_notebook.py
        Assert.Equal(expected, actual);
    }
}
