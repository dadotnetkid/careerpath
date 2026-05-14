using Microsoft.EntityFrameworkCore;

namespace CareerPath.Server.Data;

// One-shot copy of PredictionLogs, TrainingRuns, TrainingFolds from the legacy
// App_Data/careerpath.db into the configured SQL Server. Identity tables are
// skipped — startup reseeds the admin user from Auth:AdminEmail/Password.
//
// Triggered by `dotnet run --project src/CareerPath.Server -- migrate-from-sqlite [sqlitePath]`.
// Idempotent at the row level: existing IDs are detected and skipped, so re-running
// after a partial failure is safe.
public static class SqliteToSqlServerMigrator
{
    public static async Task<int> RunAsync(string sqlitePath, string sqlServerConnectionString)
    {
        if (!File.Exists(sqlitePath))
        {
            Console.Error.WriteLine($"Migrator: source SQLite DB not found at {sqlitePath}");
            return 1;
        }

        Console.WriteLine($"Migrator: source SQLite = {sqlitePath}");
        Console.WriteLine($"Migrator: target SQL Server = {RedactPassword(sqlServerConnectionString)}");

        var srcOpts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={sqlitePath}")
            .Options;
        var dstOpts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(sqlServerConnectionString)
            .Options;

        await using var src = new AppDbContext(srcOpts);
        await using var dst = new AppDbContext(dstOpts);

        // Make sure the destination schema exists before we start copying.
        var created = await dst.Database.EnsureCreatedAsync();
        Console.WriteLine($"Migrator: target schema EnsureCreated returned {created}");

        // PredictionLogs — simple flat table, identity column.
        var srcLogs = await src.PredictionLogs.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
        var existingLogIds = await dst.PredictionLogs.AsNoTracking().Select(p => p.Id).ToHashSetAsync();
        var newLogs = srcLogs.Where(l => !existingLogIds.Contains(l.Id)).ToList();
        Console.WriteLine($"Migrator: PredictionLogs source={srcLogs.Count}, already-present={existingLogIds.Count}, to-insert={newLogs.Count}");
        if (newLogs.Count > 0)
        {
            await CopyWithIdentityInsertAsync(dst, "PredictionLogs", async () =>
            {
                dst.PredictionLogs.AddRange(newLogs);
                await dst.SaveChangesAsync();
            });
        }

        // TrainingRuns + TrainingFolds — parent/child. Insert runs first (preserving Id), then
        // re-stamp fold FKs to match.
        var srcRuns = await src.TrainingRuns
            .AsNoTracking()
            .Include(r => r.Folds)
            .OrderBy(r => r.Id)
            .ToListAsync();
        var existingRunIds = await dst.TrainingRuns.AsNoTracking().Select(r => r.Id).ToHashSetAsync();
        var newRuns = srcRuns.Where(r => !existingRunIds.Contains(r.Id)).ToList();
        Console.WriteLine($"Migrator: TrainingRuns source={srcRuns.Count}, already-present={existingRunIds.Count}, to-insert={newRuns.Count}");

        if (newRuns.Count > 0)
        {
            // Detach Folds collections before inserting parents — we'll handle children separately
            // so we can preserve the parent Id under IDENTITY_INSERT, then map fold FKs to the new
            // (equal) ParentId. EF would otherwise also try to insert dependent Folds with their
            // own Ids while IDENTITY_INSERT is only enabled on TrainingRuns.
            var foldsByRunId = newRuns.ToDictionary(r => r.Id, r => r.Folds.ToList());
            foreach (var run in newRuns)
            {
                run.Folds = new List<TrainingFold>();
            }

            await CopyWithIdentityInsertAsync(dst, "TrainingRuns", async () =>
            {
                dst.TrainingRuns.AddRange(newRuns);
                await dst.SaveChangesAsync();
            });

            var allNewFolds = foldsByRunId
                .SelectMany(kv => kv.Value.Select(f =>
                {
                    f.TrainingRun = null!;
                    f.TrainingRunId = kv.Key;
                    return f;
                }))
                .ToList();
            var existingFoldIds = await dst.TrainingFolds.AsNoTracking().Select(f => f.Id).ToHashSetAsync();
            var newFolds = allNewFolds.Where(f => !existingFoldIds.Contains(f.Id)).ToList();
            Console.WriteLine($"Migrator: TrainingFolds to-insert={newFolds.Count}");

            if (newFolds.Count > 0)
            {
                await CopyWithIdentityInsertAsync(dst, "TrainingFolds", async () =>
                {
                    dst.TrainingFolds.AddRange(newFolds);
                    await dst.SaveChangesAsync();
                });
            }
        }

        Console.WriteLine("Migrator: done.");
        return 0;
    }

    // Wraps an insert in SET IDENTITY_INSERT ON/OFF so we can preserve the source Id values.
    // SQL Server refuses to write to an identity column otherwise.
    private static async Task CopyWithIdentityInsertAsync(AppDbContext dst, string tableName, Func<Task> insert)
    {
        var conn = dst.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using (var on = conn.CreateCommand())
        {
            on.CommandText = $"SET IDENTITY_INSERT [{tableName}] ON";
            await on.ExecuteNonQueryAsync();
        }
        try
        {
            await insert();
        }
        finally
        {
            await using var off = conn.CreateCommand();
            off.CommandText = $"SET IDENTITY_INSERT [{tableName}] OFF";
            await off.ExecuteNonQueryAsync();
        }
    }

    private static string RedactPassword(string cs)
    {
        var parts = cs.Split(';', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            var kv = parts[i].Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim().Equals("Password", StringComparison.OrdinalIgnoreCase))
                parts[i] = "Password=***";
        }
        return string.Join(";", parts);
    }
}
