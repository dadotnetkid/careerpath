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
    public string Role => Status.Role;
    public bool IsAdmin => string.Equals(Status.Role, AuthRoles.Admin, StringComparison.OrdinalIgnoreCase);
    public bool IsStudent => string.Equals(Status.Role, AuthRoles.Student, StringComparison.OrdinalIgnoreCase);
    public string DisplayName => string.IsNullOrWhiteSpace(Status.FullName) ? Status.Email : Status.FullName;

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

    /// <summary>
    /// Self-service student registration. On success the server has already
    /// signed the user in (cookie issued), so the returned <see cref="AuthStatusDto"/>
    /// is immediately authoritative.
    /// </summary>
    public async Task<(bool ok, string? error)> RegisterAsync(RegisterRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/auth/register", req);
            if (resp.IsSuccessStatusCode)
            {
                Status = await resp.Content.ReadFromJsonAsync<AuthStatusDto>() ?? new AuthStatusDto();
                AuthStateChanged?.Invoke();
                return (true, null);
            }
            // Try to read {"error":"..."} from the failure body so the dialog can show
            // a useful message (duplicate email, weak password, etc).
            try
            {
                var errObj = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                return (false, errObj != null && errObj.TryGetValue("error", out var msg) ? msg : "Registration failed.");
            }
            catch
            {
                return (false, "Registration failed.");
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
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
