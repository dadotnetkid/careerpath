using System.Text.Json;
using CareerPath.ML;
using CareerPath.Server.Controllers;
using CareerPath.Server.Data;
using Microsoft.Extensions.DependencyInjection;

namespace CareerPath.Server.Services;

/// <summary>
/// Singleton that owns the background "Retrain" workflow surfaced on the
/// Train &amp; Test admin page. Wraps a single Task.Run, guards against
/// concurrent starts, and re-ingests the freshly written cv_metrics.json
/// into the DB once training finishes so the Insights / Train &amp; Test
/// charts pick up the new numbers without a server restart.
///
/// Training takes ~60–90 seconds depending on the box; the controller returns
/// 202 immediately and the page polls /api/training/run/status every 3s.
/// </summary>
public class ModelTrainingService
{
    private readonly string _csvPath;
    private readonly string _modelDir;
    private readonly string _modelPath;
    private readonly string _cvMetricsPath;
    private readonly string _diagnosticMetricsPath;
    private readonly IServiceProvider _services;
    private readonly ILogger<ModelTrainingService> _logger;
    private readonly object _lock = new();

    private TrainingStatusDto _status = new() { Status = "Idle" };

    public ModelTrainingService(
        string csvPath,
        string modelDir,
        string modelPath,
        string cvMetricsPath,
        string diagnosticMetricsPath,
        IServiceProvider services,
        ILogger<ModelTrainingService> logger)
    {
        _csvPath = csvPath;
        _modelDir = modelDir;
        _modelPath = modelPath;
        _cvMetricsPath = cvMetricsPath;
        _diagnosticMetricsPath = diagnosticMetricsPath;
        _services = services;
        _logger = logger;
    }

    public bool TryStart(out string? error)
    {
        lock (_lock)
        {
            if (_status.Status == "Running")
            {
                error = "A training run is already in progress.";
                return false;
            }
            _status = new TrainingStatusDto
            {
                Status = "Running",
                StartedAt = DateTime.UtcNow
            };
        }
        error = null;
        _ = Task.Run(RunAsync);
        return true;
    }

    public TrainingStatusDto GetStatus()
    {
        lock (_lock)
        {
            return new TrainingStatusDto
            {
                Status = _status.Status,
                Error = _status.Error,
                StartedAt = _status.StartedAt,
                CompletedAt = _status.CompletedAt
            };
        }
    }

    private async Task RunAsync()
    {
        var jsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        try
        {
            _logger.LogInformation("ModelTrainingService: starting training run.");

            // 1) Binary classifier (RF + DT + LR) — writes model.zip + metrics.json
            var trainer = new CareerPathTrainer();
            trainer.TrainAll(_csvPath, _modelPath);

            // 2) Cross-validation (10 + 20 fold per algo)
            var cv = trainer.CrossValidate(_csvPath);
            await File.WriteAllTextAsync(_cvMetricsPath, JsonSerializer.Serialize(cv, jsonOpts));

            // 3) Diagnostic 4×2 + 4×4 confusion matrices
            var diag = trainer.TrainDiagnostic(_csvPath, _modelDir);
            await File.WriteAllTextAsync(_diagnosticMetricsPath, JsonSerializer.Serialize(diag, jsonOpts));

            // 4) RIASEC multiclass
            var riasecTrainer = new RiasecTrainer();
            riasecTrainer.TrainAndEvaluate(_csvPath, _modelPath);

            // 5) Re-ingest the freshly written cv_metrics.json so Insights / Train & Test
            //    show the new run without waiting for a server restart.
            using (var scope = _services.CreateScope())
            {
                var history = scope.ServiceProvider.GetRequiredService<TrainingHistoryService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await history.IngestAsync(_cvMetricsPath, db);
            }

            // 6) Hot-swap the prediction engine so /api/predict starts using the new model.
            var predSvc = _services.GetRequiredService<PredictionService>();
            predSvc.Reload(_modelPath);

            lock (_lock)
            {
                _status = new TrainingStatusDto
                {
                    Status = "Completed",
                    StartedAt = _status.StartedAt,
                    CompletedAt = DateTime.UtcNow
                };
            }
            _logger.LogInformation("ModelTrainingService: training run completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ModelTrainingService: training run failed.");
            lock (_lock)
            {
                _status = new TrainingStatusDto
                {
                    Status = "Failed",
                    Error = ex.Message,
                    StartedAt = _status.StartedAt,
                    CompletedAt = DateTime.UtcNow
                };
            }
        }
    }
}
