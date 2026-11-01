using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetRts.Server.Tests;

/// <summary>
/// The #/api page and its request files (.http and Postman): served, and every call they name is a real
/// endpoint, so renaming one fails here instead of on a student's screen.
/// </summary>
public class ApiDocsTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/requests/badger-brawl.http", "text/plain")]
    [InlineData("/requests/badger-brawl.postman_collection.json", "application/json")]
    public async Task Request_files_are_served(string path, string contentType)
    {
        var response = await factory.CreateClient().GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Page_names_only_real_endpoints()
    {
        var script = await factory.CreateClient().GetStringAsync("/apidocs.js", Ct);
        var named = Regex.Matches(script, @"'(GET|POST|PUT|PATCH|DELETE) (/api/[^'?\s]*)'")
            .Select(m => Normalize(m.Groups[1].Value, m.Groups[2].Value))
            .ToHashSet();

        Assert.NotEmpty(named);
        Assert.Empty(named.Except(await EndpointsAsync()));
    }

    [Fact]
    public async Task Http_file_calls_real_endpoints()
    {
        var calls = await HttpFileCallsAsync();

        Assert.NotEmpty(calls);
        Assert.Empty(calls.Except(await EndpointsAsync()));
    }

    [Fact]
    public async Task Postman_collection_matches_the_http_file()
    {
        var json = await factory.CreateClient().GetStringAsync("/requests/badger-brawl.postman_collection.json", Ct);
        using var collection = JsonDocument.Parse(json);
        var calls = Requests(collection.RootElement)
            .Select(r => Normalize(r.GetProperty("method").GetString()!, r.GetProperty("url").GetProperty("raw").GetString()!.Replace("{{baseUrl}}", "")))
            .ToList();

        // The two files list the same calls in the same order.
        Assert.Equal(await HttpFileCallsAsync(), calls);
    }

    private async Task<List<string>> HttpFileCallsAsync()
    {
        var text = await factory.CreateClient().GetStringAsync("/requests/badger-brawl.http", Ct);
        return Regex.Matches(text, @"^(GET|POST|PUT|PATCH|DELETE) \{\{server\}\}(\S+)", RegexOptions.Multiline)
            .Select(m => Normalize(m.Groups[1].Value, m.Groups[2].Value))
            .ToList();
    }

    private async Task<HashSet<string>> EndpointsAsync()
    {
        using var openApi = JsonDocument.Parse(await factory.CreateClient().GetStringAsync("/openapi/v1.json", Ct));
        return openApi.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(op => Normalize(op.Name, path.Name)))
            .ToHashSet();
    }

    private static IEnumerable<JsonElement> Requests(JsonElement folder) =>
        folder.GetProperty("item").EnumerateArray()
            .SelectMany(item => item.TryGetProperty("request", out var request) ? [request] : Requests(item));

    /// <summary>"GET /api/v1/matches/{{matchId}}/state?waitForTick=0" and the OpenAPI path both become "GET /api/v1/matches/{}/state".</summary>
    private static string Normalize(string method, string path) =>
        $"{method.ToUpperInvariant()} {Regex.Replace(path.Split('?')[0], @"\{\{?[A-Za-z]+\}\}?", "{}")}";
}
