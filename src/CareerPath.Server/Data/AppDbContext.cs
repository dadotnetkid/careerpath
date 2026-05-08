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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TrainingFold>()
            .HasOne(f => f.TrainingRun)
            .WithMany(r => r.Folds)
            .HasForeignKey(f => f.TrainingRunId)
            .OnDelete(DeleteBehavior.Cascade);
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
