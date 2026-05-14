using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CareerPath.Server.Data;

public class AppDbContext : IdentityDbContext<IdentityUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<PredictionLog> PredictionLogs => Set<PredictionLog>();
    public DbSet<TrainingRun> TrainingRuns => Set<TrainingRun>();
    public DbSet<TrainingFold> TrainingFolds => Set<TrainingFold>();
    public DbSet<SavedAssessmentResult> SavedResults => Set<SavedAssessmentResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TrainingFold>()
            .HasOne(f => f.TrainingRun)
            .WithMany(r => r.Folds)
            .HasForeignKey(f => f.TrainingRunId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SavedAssessmentResult>()
            .HasIndex(s => new { s.UserId, s.CreatedAt });
    }
}

public class PredictionLog
{
    public int Id { get; set; }
    public int YearGraduated { get; set; }
    public string Gwa { get; set; } = "";
    public string Honors { get; set; } = "";
    public string Competencies { get; set; } = "";
    public string CareerClarity { get; set; } = "";
    public string Interest { get; set; } = "";
    public string PredictedOutcome { get; set; } = "";
    public float Confidence { get; set; }
    public DateTime PredictedAt { get; set; }
}

// One row per (Algorithm × FoldCount) ingestion. Accumulates over time —
// each `dotnet run --project src/CareerPath.ML` produces a new
// cv_metrics.json which the server ingests on next startup, adding new
// TrainingRun rows; older rows are retained for historical comparison.
public class TrainingRun
{
    public int Id { get; set; }
    public string Algorithm { get; set; } = "";
    public int FoldCount { get; set; }

    public double MeanAccuracy { get; set; }
    public double StdAccuracy { get; set; }
    public double MeanPrecision { get; set; }
    public double StdPrecision { get; set; }
    public double MeanRecall { get; set; }
    public double StdRecall { get; set; }
    public double MeanF1 { get; set; }
    public double StdF1 { get; set; }
    public double MeanAuc { get; set; }
    public double StdAuc { get; set; }

    public DateTime TrainedAt { get; set; }
    public string DatasetVersion { get; set; } = "";

    public List<TrainingFold> Folds { get; set; } = new();
}

public class TrainingFold
{
    public int Id { get; set; }
    public int TrainingRunId { get; set; }
    public TrainingRun TrainingRun { get; set; } = null!;

    public int FoldIndex { get; set; }
    public double Accuracy { get; set; }
    public double Precision { get; set; }
    public double Recall { get; set; }
    public double F1 { get; set; }
    public double Auc { get; set; }
}

// Student-saved wizard result. One row per click of the "Save Result" button
// on the Results step. Scoped by UserId — students only see their own rows.
// AiAnalysis can be long (multiple paragraphs from GPT-4o-mini) so we let SQL
// Server pick NVARCHAR(MAX) by default for the string columns.
public class SavedAssessmentResult
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    public string Gwa { get; set; } = "";
    public string Honors { get; set; } = "";
    public string Competencies { get; set; } = "";

    public string DominantCode { get; set; } = "";
    public string ClarityLabel { get; set; } = "";
    public string ClarityBucket { get; set; } = "";
    public double ClarityTotal { get; set; }

    public string PredictedOutcome { get; set; } = "";
    public float PredictedConfidence { get; set; }
    public string AiAnalysis { get; set; } = "";
    public string Notes { get; set; } = "";
}
