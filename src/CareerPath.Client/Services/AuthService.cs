using System.Net.Http.Json;
using CareerPath.Shared;

namespace CareerPath.Client.Services;

/// <summary>
/// Client-side wrapper around the cookie-based admin auth endpoints
/// (/api/auth/me, /api/auth/login, /api/auth/logout). Holds the most
/// recent <see cref="AuthStatusDto"/> in memory and raises
/// <see cref="AuthStateChanged"/> whenever it changes so layouts and
/// pages can re-render.
///
/// The browser attaches the auth cookie automatically on same-origin
/// requests — there is no manual Authorization header anywhere.
/// </summary>
public class AuthService
{
    private readonly HttpClient _http;

    public event Action? AuthStateChanged;

    public AuthStatusDto Status { get; private set; } = new();
    public bool IsAuthenticated => Status.IsAuthenticated;
    public string Email => Status.Email;

    public AuthService(HttpClient http)
    {
        _http = http;
    }

    public async Task RefreshAsync()
    {
        try
        {
            var resp = await _http.GetFromJsonAsync<AuthStatusDto>("api/auth/me");
            Status = resp ?? new AuthStatusDto();
        }
        catch
        {
            Status = new AuthStatusDto();
        }
        AuthStateChanged?.Invoke();
    }

    public async Task<bool> LoginAsync(LoginRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/auth/login", req);
            if (resp.IsSuccessStatusCode)
            {
                Status = await resp.Content.ReadFromJsonAsync<AuthStatusDto>() ?? new AuthStatusDto();
                AuthStateChanged?.Invoke();
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            await _http.PostAsync("api/auth/logout", null);
        }
        catch
        {
            // Server may already consider us logged out — ignore network errors.
        }
        Status = new AuthStatusDto();
        AuthStateChanged?.Invoke();
    }
}
