using System.Net.Http.Headers;
using System.Net.Http.Json;
using Blazored.LocalStorage;

namespace NetRts.Client.Services;

public class AuthenticatedHttpClient
{
    public HttpClient HttpClient { get; }
    private readonly ILocalStorageService _localStorage;

    public AuthenticatedHttpClient(HttpClient httpClient, ILocalStorageService localStorage)
    {
        HttpClient = httpClient;
        _localStorage = localStorage;
    }

    public async Task<T?> GetAsync<T>(string uri)
    {
        await SetAuthorizationHeader();
        return await HttpClient.GetFromJsonAsync<T>(uri);
    }

    public async Task<T?> GetFromJsonAsync<T>(string uri)
    {
        await SetAuthorizationHeader();
        return await HttpClient.GetFromJsonAsync<T>(uri);
    }

    public async Task<HttpResponseMessage> PostAsync<T>(string uri, T value)
    {
        await SetAuthorizationHeader();
        return await HttpClient.PostAsJsonAsync(uri, value);
    }

    public async Task<HttpResponseMessage> PostAsJsonAsync<T>(string uri, T value)
    {
        await SetAuthorizationHeader();
        return await HttpClient.PostAsJsonAsync(uri, value);
    }

    public async Task<HttpResponseMessage> PutAsJsonAsync<T>(string uri, T value)
    {
        await SetAuthorizationHeader();
        return await HttpClient.PutAsJsonAsync(uri, value);
    }

    private async Task SetAuthorizationHeader()
    {
        var token = await _localStorage.GetItemAsync<string>("authToken");
        if (!string.IsNullOrEmpty(token))
        {
            HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
}
