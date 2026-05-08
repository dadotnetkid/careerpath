using Microsoft.EntityFrameworkCore;

namespace CareerPath.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<PredictionLog> PredictionLogs => Set<PredictionLog>();
}

public class PredictionLog
{
    public int Id { get; set; }
    public string Course { get; set; } = "";
    public int YearGraduated { get; set; }
    public float Gwa { get; set; }
    public string Competencies { get; set; } = "";
    public string Interest { get; set; } = "";
    public string PredictedOutcome { get; set; } = "";
    public float Confidence { get; set; }
    public DateTime PredictedAt { get; set; }
}
