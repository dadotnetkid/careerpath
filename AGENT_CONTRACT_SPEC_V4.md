# Agent Contract Spec V4 — Admin Auth (Cookie + ASP.NET Identity)

**Read this entire document before writing any code.** Companion to V1/V2/V3 specs. All earlier contracts still apply — V4 only adds capability.

---

## 1. What V4 adds

A simple cookie-based admin authentication using ASP.NET Core Identity + SQLite + the existing `AppDbContext` (now `IdentityDbContext<IdentityUser>`):

- One seeded admin user (read from `appsettings.Production.json`, falls back to placeholder in `appsettings.json` for dev)
- No registration endpoint, no password reset, no roles — admin-only by design
- Login dialog in the top bar of the Blazor client (sign-in button → MudDialog → submit → cookie set)
- `[Authorize]` on `/api/dataset/*`, `/api/metrics/*`, `/api/training/*` — researcher-facing routes
- **NOT** authorized: `/api/predict`, `/api/riasec/*` — public for student-facing demo flow

The student-facing flow (`/`, `/predict`, `/riasec`, `/about`) stays public. Admin pages (`/insights`, `/dataset`) redirect to the login dialog or show a "please sign in" notice when the user hits a 401.

---

## 2. Locked contracts (DO NOT MODIFY)

These have been written by the orchestrator. Read but do not edit:

- `src/CareerPath.Shared/Models.cs` — new types: `LoginRequest`, `AuthStatusDto`
- `src/CareerPath.Server/Data/AppDbContext.cs` — now extends `IdentityDbContext<IdentityUser>`. EF will auto-migrate 7 Identity tables (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims`) on next `EnsureCreated`.
- `src/CareerPath.Server/CareerPath.Server.csproj` — `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.5 added; `appsettings.Production.json` configured to publish.
- `src/CareerPath.Server/appsettings.json` — `Auth` section with placeholder password.
- `src/CareerPath.Server/appsettings.Production.json` — gitignored, has the real seed password. **Do not modify, do not echo, do not commit.**
- `.gitignore` — `appsettings.Production.json` is gitignored.

---

## 3. Auth flow (locked design)

### Cookie auth, NOT JWT

Same-origin Blazor WASM + ASP.NET Core server → cookies are the right tool. The browser handles them automatically; no need for a token store on the WASM side.

### Endpoints (server)

| Method | Route | Auth | Body | Returns |
|---|---|---|---|---|
| `POST` | `/api/auth/login` | Anonymous | `LoginRequest` | `AuthStatusDto` (sets cookie) |
| `POST` | `/api/auth/logout` | Authenticated | — | `204 No Content` (clears cookie) |
| `GET`  | `/api/auth/me` | Anonymous | — | `AuthStatusDto` |

### `[Authorize]` placement

| Route | Auth requirement | Reasoning |
|---|---|---|
| `/api/auth/*` | mixed (per-action) | login + me are anonymous, logout is authorized |
| `/api/predict` | **anonymous** | Student-facing demo — keep public |
| `/api/predict/status` | **anonymous** | Used by all pages |
| `/api/riasec/*` | **anonymous** | Student-facing demo |
| `/api/dataset` | **`[Authorize]`** | Researcher view |
| `/api/dataset/summary` | **`[Authorize]`** | Used by Insights (admin-only) |
| `/api/metrics` | **`[Authorize]`** | Insights |
| `/api/metrics/comparison` | **`[Authorize]`** | Insights |
| `/api/metrics/diagnostic` | **`[Authorize]`** | Insights |
| `/api/training/latest` | **`[Authorize]`** | Insights |
| `/api/training/history` | **`[Authorize]`** | Insights |

**Caveat:** `/api/dataset/summary` is also referenced by the public-facing `Home.razor` if it shows total record count. Agent M must check — if a public page reads it, EITHER expose a separate anonymous count endpoint OR remove the call from public pages. The simpler path: Home doesn't need this (the existing handoff doc says Home shows hardcoded text), so authorizing summary should be fine. Confirm in Phase 3d.

### Cookie configuration

- Name: read from `Auth:CookieName` config (default `CareerPath.Auth`)
- Expiry: read from `Auth:ExpireDays` config (default 30)
- `SameSite = Lax`, `HttpOnly = true`, `SecurePolicy = SameAsRequest` (so dev http works; production https-only)
- 401 responses for API calls: do NOT redirect to a login page — return raw 401 JSON. The Blazor client handles the 401 by opening the login dialog. (ASP.NET Core Identity's default cookie handler returns 302 to `/Account/Login` which would 404 here.)

### Seed flow

On startup (after `EnsureCreated`):
1. Read `Auth:AdminEmail` and `Auth:AdminPassword` from configuration.
2. Use `UserManager<IdentityUser>` to look up the user by email.
3. If user does not exist:
   - Create with `EmailConfirmed = true`, `UserName = email` (so login can use email-as-username)
   - Set the password via `userManager.CreateAsync(user, password)`
   - Log a Console message "Admin user seeded: {email}"
4. If user exists:
   - Do nothing. Do NOT update the password (so a future password rotation doesn't get reverted by a redeploy).
   - Log "Admin user already exists, skipping seed."

This means: first deploy reads `appsettings.Production.json` and creates the admin user with `GJ@2026!`. Subsequent deploys leave the password alone. To rotate: the admin changes the password via a future endpoint, OR the server admin manually modifies the `AspNetUsers` row's `PasswordHash`. (Out of V4 scope.)

---

## 4. Agent L (Server) — owns `src/CareerPath.Server/**` EXCEPT `Data/AppDbContext.cs` (locked)

### Responsibilities

1. **Update `Program.cs`**:
   - Add Identity registration:
     ```csharp
     builder.Services.AddIdentityCore<IdentityUser>(options => {
         options.Password.RequireDigit = true;
         options.Password.RequiredLength = 8;
         options.Password.RequireNonAlphanumeric = true;
         options.Password.RequireUppercase = true;
         options.User.RequireUniqueEmail = true;
     })
     .AddEntityFrameworkStores<AppDbContext>()
     .AddSignInManager()
     .AddDefaultTokenProviders();
     ```
   - Add cookie auth registration:
     ```csharp
     builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
         .AddCookie(IdentityConstants.ApplicationScheme, options => {
             options.Cookie.Name = builder.Configuration["Auth:CookieName"] ?? "CareerPath.Auth";
             options.Cookie.HttpOnly = true;
             options.Cookie.SameSite = SameSiteMode.Lax;
             options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
             options.ExpireTimeSpan = TimeSpan.FromDays(int.TryParse(builder.Configuration["Auth:ExpireDays"], out var d) ? d : 30);
             options.SlidingExpiration = true;
             options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
             options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
         });
     builder.Services.AddAuthorization();
     ```
   - Add `app.UseAuthentication();` BEFORE `app.UseAuthorization();` and BEFORE `app.MapControllers();`. Make sure the order is: `UseRouting → UseAuthentication → UseAuthorization → MapControllers → MapFallbackToFile`.
   - Add the seed call after `db.Database.EnsureCreated()` (and before the cv ingest, or after — order doesn't matter):
     ```csharp
     try {
         var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
         var email = builder.Configuration["Auth:AdminEmail"] ?? "";
         var password = builder.Configuration["Auth:AdminPassword"] ?? "";
         if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(password)) {
             var existing = await userManager.FindByEmailAsync(email);
             if (existing == null) {
                 var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
                 var result = await userManager.CreateAsync(user, password);
                 if (result.Succeeded) Console.WriteLine($"Admin user seeded: {email}");
                 else Console.Error.WriteLine($"Admin user seed failed: {string.Join(", ", result.Errors.Select(e => e.Description))}");
             } else {
                 Console.WriteLine($"Admin user already exists, skipping seed: {email}");
             }
         } else {
             Console.WriteLine("Auth:AdminEmail / Auth:AdminPassword not configured; skipping admin seed.");
         }
     } catch (Exception ex) {
         Console.Error.WriteLine($"Admin seed failed: {ex.Message}");
     }
     ```
     The outer code that creates `scope` and resolves `db` already exists. Wrap or extend that block so `userManager` is resolved from the same `scope`. Note: Program.cs may need to become a top-level async file or use `.GetAwaiter().GetResult()` for the seed (existing CV ingest already does the latter — follow the same pattern).

2. **Create `Controllers/AuthController.cs`**:
   - `[ApiController]`, `[Route("api/auth")]`
   - Inject `SignInManager<IdentityUser>` and `UserManager<IdentityUser>`
   - Actions:
     - `[HttpPost("login")] [AllowAnonymous]` accepts `LoginRequest`, calls `_signInManager.PasswordSignInAsync(email, password, isPersistent: rememberMe, lockoutOnFailure: false)`. If success returns `Ok(new AuthStatusDto { IsAuthenticated = true, Email = email })`. If failure returns `Unauthorized(new { error = "Invalid email or password." })`.
     - `[HttpPost("logout")] [Authorize]` calls `_signInManager.SignOutAsync()`, returns `NoContent()`.
     - `[HttpGet("me")] [AllowAnonymous]` reads `User.Identity?.IsAuthenticated`. If true returns `AuthStatusDto { IsAuthenticated = true, Email = User.Identity.Name ?? "" }`. If false returns `AuthStatusDto { IsAuthenticated = false, Email = "" }`. (Always 200 OK with the status.)

3. **Add `[Authorize]` attributes to existing controllers** per §3:
   - `Controllers/DatasetController.cs`: class-level `[Authorize]` (covers all actions: index, summary, GetMetrics, GetComparison, GetDiagnostic). NOTE: this puts the existing `/api/metrics/*` actions behind auth too — that's intentional.
   - `Controllers/TrainingHistoryController.cs`: class-level `[Authorize]`
   - `Controllers/PredictController.cs`: keep anonymous (NO change)
   - `Controllers/RiasecController.cs`: keep anonymous (NO change)

4. **Build:** `dotnet build src/CareerPath.Server/CareerPath.Server.csproj` — must succeed.

### Server-side rules

- Do NOT touch `Data/AppDbContext.cs`, `Models.cs`, `appsettings.json` (already configured), `appsettings.Production.json`, `.gitignore`
- Do NOT add new NuGet packages
- Do NOT touch `Services/PredictionService.cs`, `Services/RiasecService.cs`, `Services/OpenAiService.cs`, `Services/DatasetService.cs`, `Services/TrainingHistoryService.cs`
- Do NOT commit anything

### Report back

- File-by-file change list
- Build result
- Confirm seed flow happens once and doesn't run again on subsequent boots
- Any contract conflicts

---

## 5. Agent M (Client) — owns `src/CareerPath.Client/**`

### Responsibilities

1. **Create `Services/AuthService.cs`** (new file) — a singleton service the rest of the client uses:
   ```csharp
   public class AuthService {
       private readonly HttpClient _http;
       public event Action? AuthStateChanged;
       public AuthStatusDto Status { get; private set; } = new();
       public bool IsAuthenticated => Status.IsAuthenticated;

       public AuthService(HttpClient http) { _http = http; }

       public async Task RefreshAsync() {
           try {
               var resp = await _http.GetFromJsonAsync<AuthStatusDto>("api/auth/me");
               Status = resp ?? new AuthStatusDto();
           } catch { Status = new AuthStatusDto(); }
           AuthStateChanged?.Invoke();
       }

       public async Task<bool> LoginAsync(LoginRequest req) {
           var resp = await _http.PostAsJsonAsync("api/auth/login", req);
           if (resp.IsSuccessStatusCode) {
               Status = await resp.Content.ReadFromJsonAsync<AuthStatusDto>() ?? new();
               AuthStateChanged?.Invoke();
               return true;
           }
           return false;
       }

       public async Task LogoutAsync() {
           try { await _http.PostAsync("api/auth/logout", null); } catch { }
           Status = new AuthStatusDto();
           AuthStateChanged?.Invoke();
       }
   }
   ```

2. **Register the service in `Program.cs`** as `Scoped` (Blazor WASM convention for HttpClient-dependent services). Also call `await authService.RefreshAsync()` once during host build to seed initial state.

3. **Create `Components/LoginDialog.razor`** (new file) — a MudBlazor MudDialog:
   - Title: "Admin Sign In" with NVSU green header background `#007F3B`, white text, gold accent border
   - Form: `MudTextField` for email (with `InputType.Email`), `MudTextField` for password (with `InputType.Password` + show/hide toggle button), `MudCheckBox` for "Remember me"
   - DataAnnotationsValidator + ValidationSummary
   - Submit button: full-width primary, label "Sign In"; shows MudProgressCircular while in flight
   - On error: `MudAlert Severity="Severity.Error"` with "Invalid email or password." (the server returns 401 with that exact message — read it via `ReadAsStringAsync` if you want to surface server messages, otherwise use a hardcoded fallback)
   - On success: close the dialog with `MudDialog.Close(DialogResult.Ok(true))`
   - Cancel button to close without signing in

4. **Update `Layout/MainLayout.razor`**:
   - Find or add the top app bar (`MudAppBar`)
   - Right side of the bar:
     - When `!authService.IsAuthenticated`: `MudButton` with `StartIcon="@Icons.Material.Filled.Login"`, label "Sign In", color "Inherit" or "Default" — clicking opens the LoginDialog via `IDialogService.Show<LoginDialog>(...)`
     - When `authService.IsAuthenticated`: `MudMenu` with the user's email as the activator, dropdown items: "Sign Out" (calls `authService.LogoutAsync()` then optionally redirects to home)
   - Subscribe to `authService.AuthStateChanged` and `StateHasChanged()` so the bar updates when login state changes
   - Don't move or restyle the existing logo / nav drawer / page title

5. **Update `Pages/Insights.razor` and `Pages/Dataset.razor`**:
   - At the top of each page (before the existing markup), add a guard:
     ```razor
     @if (!authService.IsAuthenticated) {
         <MudPaper Class="pa-6" Elevation="0">
             <MudAlert Severity="Severity.Info" Variant="Variant.Outlined">
                 This page is for the researcher / admin only. Please sign in via the button in the upper right.
             </MudAlert>
         </MudPaper>
         return;
     }
     // existing page content here
     ```
   - In the `@code` block, in `OnInitializedAsync`, call `await authService.RefreshAsync()` first; if `!IsAuthenticated`, return early without fetching anything (so 401-returning calls don't spam the console)
   - Subscribe to `AuthStateChanged` so the page re-renders if the user signs in while staring at the alert

6. **Update `Layout/NavMenu.razor`**: optionally hide the "Insights" and "Dataset" links when not authenticated, or leave them visible — your call. Recommendation: leave visible (so the panel can see the system has admin pages) but the click goes to the alert.

7. **Build:** `dotnet build src/CareerPath.Client/CareerPath.Client.csproj` — must succeed.

### Client-side rules

- Match existing MudBlazor styling (NVSU green `#007F3B`, gold `#FFD700`, Source Serif 4 headings)
- Do NOT touch `Pages/Home.razor`, `Pages/About.razor`, `Pages/RiasecAssessment.razor` (those are public-facing)
- Do NOT add new NuGet packages
- Do NOT modify `Models.cs` or any backend code
- Do NOT commit anything
- The cookie is set automatically by the browser on the same-origin POST — do NOT manually attach Authorization headers anywhere

### Report back

- File-by-file change list
- Build result
- Confirm the LoginDialog opens from the top bar button
- Confirm the gating on Insights/Dataset works
- Visual decisions for the user menu (when authenticated)
- Any contract conflicts

---

## 6. Out of V4 scope

- Registration / sign-up endpoint (admin-seeded only)
- Password reset (admin will modify SQLite directly if needed)
- Multiple users / roles / role-based authorization
- Email confirmation / SMTP
- 2FA
- External providers (Google, Microsoft)
- Per-prediction user attribution (PredictionLog stays anonymous for now — students don't sign in)
- Migrating existing PredictionLog rows to attribute to users
- Rate limiting on login attempts (Identity has lockout but it's disabled per the lockoutOnFailure: false above — keep simple)

## 7. If you get stuck

Same as V1/V2/V3 — stop and report. Don't silently change a contract.
