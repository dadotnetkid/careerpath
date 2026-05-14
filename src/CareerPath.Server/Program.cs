using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using CareerPath.Server.Data;
using CareerPath.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// One-shot SQLite -> SQL Server migration. Invoke as:
//   dotnet run --project src/CareerPath.Server -- migrate-from-sqlite [path-to-careerpath.db]
// Copies PredictionLogs, TrainingRuns, TrainingFolds into the configured SQL Server, then exits.
if (args.Length > 0 && string.Equals(args[0], "migrate-from-sqlite", StringComparison.OrdinalIgnoreCase))
{
    var sqlitePath = args.Length > 1
        ? args[1]
        : Path.Combine(builder.Environment.ContentRootPath, "App_Data", "careerpath.db");
    var cs = builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
    var exit = await SqliteToSqlServerMigrator.RunAsync(sqlitePath, cs);
    return exit;
}

// Resolve content paths. In production the published folder contains data/ and MLModel/
// next to the binary. In dev (running from bin/Debug/...), the data files live at the
// solution root, so we fall back to walking up to find CareerPath.sln.
var baseDir = AppContext.BaseDirectory;

string ResolveContentPath(string relativeFromBase, string relativeFromSolution)
{
    var fromBase = Path.Combine(baseDir, relativeFromBase);
    if (File.Exists(fromBase)) return fromBase;

    var dir = new DirectoryInfo(baseDir);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CareerPath.sln")))
        dir = dir.Parent;
    return dir != null ? Path.Combine(dir.FullName, relativeFromSolution) : fromBase;
}

var csvPath = ResolveContentPath(Path.Combine("data", "alumni_gts3.csv"), Path.Combine("data", "alumni_gts3.csv"));
var modelPath = ResolveContentPath(Path.Combine("MLModel", "model.zip"), Path.Combine("src", "CareerPath.Server", "MLModel", "model.zip"));
var metricsPath = ResolveContentPath(Path.Combine("MLModel", "metrics.json"), Path.Combine("src", "CareerPath.Server", "MLModel", "metrics.json"));
var cvMetricsPath = ResolveContentPath(Path.Combine("MLModel", "cv_metrics.json"), Path.Combine("src", "CareerPath.Server", "MLModel", "cv_metrics.json"));
var diagnosticMetricsPath = ResolveContentPath(
    Path.Combine("MLModel", "diagnostic_metrics.json"),
    Path.Combine("src", "CareerPath.Server", "MLModel", "diagnostic_metrics.json"));

Console.WriteLine($"Base dir: {baseDir}");
Console.WriteLine($"CSV path: {csvPath} (exists: {File.Exists(csvPath)})");
Console.WriteLine($"Model path: {modelPath} (exists: {File.Exists(modelPath)})");
Console.WriteLine($"Metrics path: {metricsPath} (exists: {File.Exists(metricsPath)})");
Console.WriteLine($"CV metrics path: {cvMetricsPath} (exists: {File.Exists(cvMetricsPath)})");
Console.WriteLine($"Diagnostic metrics path: {diagnosticMetricsPath} (exists: {File.Exists(diagnosticMetricsPath)})");

builder.Configuration["MLModel:MetricsPath"] = metricsPath;
builder.Configuration["MLModel:DiagnosticMetricsPath"] = diagnosticMetricsPath;

// Services
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "CareerPath API", Version = "v1" });
});

// SQL Server connection. The connection string lives in ConnectionStrings:Default
// (see appsettings.json / appsettings.Production.json). EnsureCreated will materialize
// the schema on first startup if the target database is empty.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
Console.WriteLine("DB: SQL Server (see ConnectionStrings:Default)");
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

// ── ASP.NET Core Identity (admin auth, cookie-based) ──
//
// One seeded admin user, no registration / password reset / roles. The
// Identity tables are created by EF inside AppDbContext (which now extends
// IdentityDbContext<IdentityUser>). See AGENT_CONTRACT_SPEC_V4.md §3.
builder.Services.AddIdentityCore<IdentityUser>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = false; // single-admin thesis demo; admin chose the seed password
    options.User.RequireUniqueEmail = true;
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.Cookie.Name = builder.Configuration["Auth:CookieName"] ?? "CareerPath.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(int.TryParse(builder.Configuration["Auth:ExpireDays"], out var d) ? d : 30);
        options.SlidingExpiration = true;
        // ASP.NET Core Identity's default cookie handler returns 302 to /Account/Login
        // which would 404 here. The Blazor client handles 401 by opening the login dialog.
        options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();

var datasetService = new DatasetService(csvPath);
builder.Services.AddSingleton(datasetService);
builder.Services.AddSingleton(new PredictionService(modelPath));

// OpenAI + RIASEC services
var openAiKey = builder.Configuration["OpenAI:ApiKey"] ?? "";
var openAiModel = builder.Configuration["OpenAI:Model"] ?? "gpt-4o-mini";
var openAiService = new OpenAiService(openAiKey, openAiModel);
builder.Services.AddSingleton(openAiService);
builder.Services.AddSingleton(new RiasecService(openAiService, datasetService));

// CV training history ingest + query service. Singleton is fine — the service
// holds no state and uses the AppDbContext passed into each call.
builder.Services.AddSingleton<TrainingHistoryService>();

// Admin-only background trainer used by the Train & Test page. Captures all
// resolved paths so the worker thread doesn't need IConfiguration at runtime.
builder.Services.AddSingleton<ModelTrainingService>(sp => new ModelTrainingService(
    csvPath: csvPath,
    modelDir: Path.GetDirectoryName(modelPath)!,
    modelPath: modelPath,
    cvMetricsPath: cvMetricsPath,
    diagnosticMetricsPath: diagnosticMetricsPath,
    services: sp,
    logger: sp.GetRequiredService<ILogger<ModelTrainingService>>()));

var app = builder.Build();

// Ensure DB is created. If the AppPool identity lacks write permission, log and
// continue — prediction logging will fail at runtime but the app stays up.
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var dbCreated = db.Database.EnsureCreated();
    Console.WriteLine($"DB EnsureCreated created new file: {dbCreated} (false means DB already existed)");

    // Defensive check: if the target DB pre-dates the IdentityDbContext switch,
    // the AspNetUsers table will be missing and login will 500. EnsureCreated
    // does not retro-add tables to an existing DB, so we surface this explicitly
    // instead of silently failing at sign-in.
    var identityTableCount = db.Database.SqlQueryRaw<int>(
        "SELECT CASE WHEN OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NULL THEN 0 ELSE 1 END AS Value").FirstOrDefault();
    if (identityTableCount == 0)
    {
        Console.Error.WriteLine(
            "WARNING: AspNetUsers table is MISSING from the target SQL Server database. " +
            "Drop the CareerPath database (or its tables) and restart the AppPool to rebuild with Identity tables.");
    }
    else
    {
        Console.WriteLine("Identity tables present.");
    }

    // SavedResults table is a post-EnsureCreated addition (added with the
    // wizard's "Save Result" feature). EF won't retro-add it to an existing
    // DB, so we create it explicitly here. Idempotent.
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF OBJECT_ID(N'[dbo].[SavedResults]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[SavedResults] (
                    [Id] INT IDENTITY(1,1) PRIMARY KEY,
                    [UserId] NVARCHAR(450) NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL,
                    [Gwa] NVARCHAR(MAX) NOT NULL DEFAULT '',
                    [Honors] NVARCHAR(MAX) NOT NULL DEFAULT '',
                    [Competencies] NVARCHAR(MAX) NOT NULL DEFAULT '',
                    [DominantCode] NVARCHAR(MAX) NOT NULL DEFAULT '',
                    [ClarityLabel] NVARCHAR(MAX) NOT NULL DEFAULT '',
                    [ClarityBucket] NVARCHAR(MAX) NOT NULL DEFAULT '',
                    [ClarityTotal] FLOAT NOT NULL DEFAULT 0,
                    [PredictedOutcome] NVARCHAR(MAX) NOT NULL DEFAULT '',
                    [PredictedConfidence] REAL NOT NULL DEFAULT 0,
                    [AiAnalysis] NVARCHAR(MAX) NOT NULL DEFAULT '',
                    [Notes] NVARCHAR(MAX) NOT NULL DEFAULT ''
                );
                CREATE INDEX [IX_SavedResults_UserId_CreatedAt] ON [dbo].[SavedResults] ([UserId], [CreatedAt]);
            END
        ");
        Console.WriteLine("SavedResults table present (created if missing).");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"SavedResults table check failed: {ex.Message}");
    }

    // Seed roles ("Admin", "Student") then the admin user. Both are idempotent
    // so re-runs on each boot are safe. The admin user's password is NOT
    // overwritten if the user already exists — protects manual rotations.
    try
    {
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var roleName in new[] { CareerPath.Shared.AuthRoles.Admin, CareerPath.Shared.AuthRoles.Student })
        {
            if (!roleManager.RoleExistsAsync(roleName).GetAwaiter().GetResult())
            {
                var rr = roleManager.CreateAsync(new IdentityRole(roleName)).GetAwaiter().GetResult();
                Console.WriteLine($"Role seeded: {roleName} (succeeded={rr.Succeeded})");
            }
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var adminEmail = builder.Configuration["Auth:AdminEmail"] ?? "";
        var adminPassword = builder.Configuration["Auth:AdminPassword"] ?? "";
        if (!string.IsNullOrEmpty(adminEmail) && !string.IsNullOrEmpty(adminPassword))
        {
            var existing = userManager.FindByEmailAsync(adminEmail).GetAwaiter().GetResult();
            if (existing == null)
            {
                var user = new IdentityUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
                var result = userManager.CreateAsync(user, adminPassword).GetAwaiter().GetResult();
                if (result.Succeeded)
                {
                    userManager.AddToRoleAsync(user, CareerPath.Shared.AuthRoles.Admin).GetAwaiter().GetResult();
                    Console.WriteLine($"Admin user seeded: {adminEmail}");
                }
                else
                    Console.Error.WriteLine($"Admin user seed failed: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
            else
            {
                // Make sure the seeded admin always has the Admin role — covers
                // upgrades from the pre-roles V4 schema where the user existed
                // without any role assignment.
                if (!userManager.IsInRoleAsync(existing, CareerPath.Shared.AuthRoles.Admin).GetAwaiter().GetResult())
                {
                    userManager.AddToRoleAsync(existing, CareerPath.Shared.AuthRoles.Admin).GetAwaiter().GetResult();
                    Console.WriteLine($"Admin role retro-added to existing user: {adminEmail}");
                }
                else
                {
                    Console.WriteLine($"Admin user already exists with role, skipping seed: {adminEmail}");
                }
            }
        }
        else
        {
            Console.WriteLine("Auth:AdminEmail / Auth:AdminPassword not configured; skipping admin seed.");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Role/admin seed failed: {ex.Message}");
    }

    // Best-effort ingest of any cv_metrics.json the trainer has written. The
    // file may not exist yet (Agent E's trainer may not have run) — that's OK.
    // IngestAsync swallows its own errors, but we wrap belt-and-suspenders too.
    if (File.Exists(cvMetricsPath))
    {
        try
        {
            var historySvc = scope.ServiceProvider.GetRequiredService<TrainingHistoryService>();
            // Synchronous wait is fine here — startup is single-threaded and this
            // table is tiny (≤6 rows per training run).
            historySvc.IngestAsync(cvMetricsPath, db).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"CV ingest failed: {ex.Message}");
        }
    }
    else
    {
        Console.WriteLine($"CV metrics file not found at {cvMetricsPath} — skipping ingest (run `dotnet run --project src/CareerPath.ML` to generate).");
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"DB init failed (prediction logging will be unavailable): {ex.Message}");
}

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "CareerPath API v1"));

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
return 0;
