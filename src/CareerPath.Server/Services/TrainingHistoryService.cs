using System.Text.Json;
using CareerPath.Server.Data;
using CareerPath.Shared;
using Microsoft.EntityFrameworkCore;

namespace CareerPath.Server.Services;

/// <summary>
/// Ingests cv_metrics.json (produced by the trainer) into the TrainingRun /
/// TrainingFold tables, and exposes the latest + history queries used by
/// /api/training/* endpoints. Idempotent: re-ingesting the same file is a no-op.
/// </summary>
public class TrainingHistoryService
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Reads cv_metrics.json from <paramref name="cvJsonPath"/> and writes any new
    /// (Algorithm, FoldCount, TrainedAt) tuples into the DB. Existing rows are skipped.
    /// Errors are logged to stderr but never thrown — caller is the startup hook
    /// and must not crash the app.
    /// </summary>
    public async Task IngestAsync(string cvJsonPath, AppDbContext db)
    {
        try
        {
            if (!File.Exists(cvJsonPath))
            {
                Console.Error.WriteLine($"CV ingest skipped: file not found at {cvJsonPath}");
                return;
            }

            var json = await File.ReadAllTextAsync(cvJsonPath);
            var report = JsonSerializer.Deserialize<CrossValidationReport>(json, _jsonOpts);
            if (report == null || report.Runs == null || report.Runs.Count == 0)
            {
                Console.Error.WriteLine("CV ingest skipped: report is empty or unparseable.");
                return;
            }

            var inserted = 0;
            var skipped = 0;

            foreach (var run in report.Runs)
            {
                if (string.IsNullOrWhiteSpace(run.Algorithm) || run.FoldCount <= 0)
                    continue;

                // Idempotency: same Algorithm + FoldCount + TrainedAt (UTC ticks) means already ingested.
                var trainedAtUtc = report.TrainedAt.ToUniversalTime();
                var ticks = trainedAtUtc.Ticks;

                var exists = await db.TrainingRuns
                    .AsNoTracking()
                    .AnyAsync(tr =>
                        tr.Algorithm == run.Algorithm &&
                        tr.FoldCount == run.FoldCount &&
                        tr.TrainedAt == trainedAtUtc);

                if (exists)
                {
                    skipped++;
                    continue;
                }

                var entity = new TrainingRun
                {
                    Algorithm = run.Algorithm,
                    FoldCount = run.FoldCount,
                    MeanAccuracy = run.MeanAccuracy,
                    StdAccuracy = run.StdAccuracy,
                    MeanPrecision = run.MeanPrecision,
                    StdPrecision = run.StdPrecision,
                    MeanRecall = run.MeanRecall,
                    StdRecall = run.StdRecall,
                    MeanF1 = run.MeanF1,
                    StdF1 = run.StdF1,
                    MeanAuc = run.MeanAuc,
                    StdAuc = run.StdAuc,
                    TrainedAt = trainedAtUtc,
                    DatasetVersion = report.DatasetVersion ?? "",
                    Folds = (run.Folds ?? new List<CvFoldMetric>())
                        .Select(f => new TrainingFold
                        {
                            FoldIndex = f.FoldIndex,
                            Accuracy = f.Accuracy,
                            Precision = f.Precision,
                            Recall = f.Recall,
                            F1 = f.F1,
                            Auc = f.Auc
                        })
                        .ToList()
                };

                db.TrainingRuns.Add(entity);
                inserted++;
            }

            if (inserted > 0)
            {
                await db.SaveChangesAsync();
            }

            Console.WriteLine($"CV ingest: inserted {inserted} run(s), skipped {skipped} duplicate(s) from {cvJsonPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"CV ingest failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the most-recent TrainingRun per (Algorithm, FoldCount) — at most 6 rows.
    /// Ordered RF, DT, LR; 10-fold before 20-fold within each algorithm.
    /// </summary>
    public async Task<List<TrainingRunDto>> GetLatestAsync(AppDbContext db)
    {
        // Pull all runs with their folds, then group in-memory. The table is small
        // (one row per train invocation per algo+foldCount), so no need for a
        // window-function-style query.
        var allRuns = await db.TrainingRuns
            .AsNoTracking()
            .Include(r => r.Folds)
            .ToListAsync();

        var latestPerCombo = allRuns
            .GroupBy(r => new { r.Algorithm, r.FoldCount })
            .Select(g => g.OrderByDescending(r => r.TrainedAt).First())
            .ToList();

        var algorithmRank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Random Forest"] = 0,
            ["Decision Tree"] = 1,
            ["Logistic Regression"] = 2
        };

        return latestPerCombo
            .OrderBy(r => algorithmRank.TryGetValue(r.Algorithm, out var rank) ? rank : 999)
            .ThenBy(r => r.FoldCount)
            .Select(ToDto)
            .ToList();
    }

    /// <summary>
    /// Returns up to the last 20 runs for a single (algorithm, foldCount) combo,
    /// ordered by TrainedAt ascending (oldest → newest).
    /// </summary>
    public async Task<List<TrainingRunDto>> GetHistoryAsync(string algorithm, int foldCount, AppDbContext db)
    {
        // Take the last 20 by TrainedAt desc on the DB side, then re-order ascending in memory.
        var rows = await db.TrainingRuns
            .AsNoTracking()
            .Include(r => r.Folds)
            .Where(r => r.Algorithm == algorithm && r.FoldCount == foldCount)
            .OrderByDescending(r => r.TrainedAt)
            .Take(20)
            .ToListAsync();

        return rows
            .OrderBy(r => r.TrainedAt)
            .Select(ToDto)
            .ToList();
    }

    private static TrainingRunDto ToDto(TrainingRun r)
    {
        return new TrainingRunDto
        {
            Id = r.Id,
            Algorithm = r.Algorithm,
            FoldCount = r.FoldCount,
            MeanAccuracy = r.MeanAccuracy,
            StdAccuracy = r.StdAccuracy,
            MeanPrecision = r.MeanPrecision,
            StdPrecision = r.StdPrecision,
            MeanRecall = r.MeanRecall,
            StdRecall = r.StdRecall,
            MeanF1 = r.MeanF1,
            StdF1 = r.StdF1,
            MeanAuc = r.MeanAuc,
            StdAuc = r.StdAuc,
            TrainedAt = r.TrainedAt,
            DatasetVersion = r.DatasetVersion,
            Folds = (r.Folds ?? new List<TrainingFold>())
                .OrderBy(f => f.FoldIndex)
                .Select(f => new CvFoldMetric
                {
                    FoldIndex = f.FoldIndex,
                    Accuracy = f.Accuracy,
                    Precision = f.Precision,
                    Recall = f.Recall,
                    F1 = f.F1,
                    Auc = f.Auc
                })
                .ToList()
        };
    }
}
