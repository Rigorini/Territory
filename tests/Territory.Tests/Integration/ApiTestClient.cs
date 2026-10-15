using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Territory.Api.Security;

namespace Territory.Tests.Integration;

/// <summary>HTTPS test client with a cookie jar that sends the antiforgery header like the Angular client.</summary>
public sealed record ApiTestClient(HttpClient Http, CookieContainerHandler Cookies)
{
    public static readonly Uri BaseAddress = new("https://localhost");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static ApiTestClient Create(DbApiFactory factory)
    {
        var cookies = new CookieContainerHandler();
        return new ApiTestClient(factory.CreateDefaultClient(BaseAddress, cookies), cookies);
    }

    public async Task<HttpResponseMessage> LoginAsync(string username, string password)
    {
        await Http.GetAsync("/api/auth/antiforgery", Ct);
        return await SendAsync(HttpMethod.Post, "/api/auth/login", new { username, password });
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var token = Cookies.Container.GetCookies(BaseAddress)[AuthSetup.XsrfCookieName]?.Value;
        request.Headers.Add(AuthSetup.XsrfHeaderName, token);
        return Http.SendAsync(request, Ct);
    }

    public static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return body.GetProperty("code").GetString();
    }
}
