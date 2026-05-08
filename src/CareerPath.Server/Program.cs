using Microsoft.EntityFrameworkCore;
using CareerPath.Server.Data;
using CareerPath.Server.Services;

var builder = WebApplication.CreateBuilder(args);

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

// SQLite DB lives in App_Data so we can give the AppPool identity a tightly-scoped
// write permission instead of opening up the whole site folder.
var dbDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dbDir);
var dbPath = Path.Combine(dbDir, "careerpath.db");
Console.WriteLine($"DB path: {dbPath}");
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));

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

var app = builder.Build();

// Ensure DB is created. If the AppPool identity lacks write permission, log and
// continue — prediction logging will fail at runtime but the app stays up.
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

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

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
