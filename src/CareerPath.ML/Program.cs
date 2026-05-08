using CareerPath.ML;
using System.Text.Json;

Console.WriteLine("=== Career Path ML Training ===\n");

var baseDir = AppContext.BaseDirectory;
// Walk up to find the solution root (where data/ lives)
var dir = new DirectoryInfo(baseDir);
while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CareerPath.sln")))
    dir = dir.Parent;

if (dir == null)
{
    Console.Error.WriteLine("Could not find solution root. Run from within the CareerPath solution.");
    return 1;
}

var csvPath = Path.Combine(dir.FullName, "data", "alumni_gts3.csv");
var modelDir = Path.Combine(dir.FullName, "src", "CareerPath.Server", "MLModel");
var modelPath = Path.Combine(modelDir, "model.zip");

if (!File.Exists(csvPath))
{
    Console.Error.WriteLine($"Dataset not found at: {csvPath}");
    return 1;
}

Console.WriteLine($"Dataset: {csvPath}");
Console.WriteLine($"Model output: {modelPath}\n");

// ── Binary classifier (RF + DT + LR) ──
var trainer = new CareerPathTrainer();
var comparison = trainer.TrainAll(csvPath, modelPath);

Console.WriteLine("\n=== Binary Training Complete ===");
Console.WriteLine($"RF Accuracy: {comparison.RandomForest.Accuracy:P2}");
Console.WriteLine($"DT Accuracy: {comparison.DecisionTree.Accuracy:P2}");
Console.WriteLine($"LR Accuracy: {comparison.LogisticRegression.Accuracy:P2}");

// ── Cross-validation (10-fold + 20-fold) for RF/DT/LR ──
// Reported as stability validation only; the canonical 80/20 split above
// remains the model selection method per Objective 4.
Console.WriteLine("\n=== Cross-Validation (10-fold + 20-fold) ===");
var cvReport = trainer.CrossValidate(csvPath);
var cvJsonPath = Path.Combine(modelDir, "cv_metrics.json");
var cvJson = JsonSerializer.Serialize(cvReport, new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
});
File.WriteAllText(cvJsonPath, cvJson);
Console.WriteLine($"\nCV metrics saved to: {cvJsonPath}");
Console.WriteLine($"CV runs: {cvReport.Runs.Count} (expected 6 = {{RF,DT,LR}} × {{10,20}})");
foreach (var run in cvReport.Runs)
    Console.WriteLine($"  {run.Algorithm,-22} {run.FoldCount,2}-fold  Acc={run.MeanAccuracy:P2} ± {run.StdAccuracy:P2}");

// ── Diagnostic confusion matrices (4×2 + 4×4) ──
// Additive only — the canonical model.zip / metrics.json above are NOT touched.
// Outputs: MLModel/diagnostic_metrics.json + MLModel/model_4class.zip.
Console.WriteLine("\n=== Diagnostic Confusion Matrices (4×2 + 4×4) ===");
var diag = trainer.TrainDiagnostic(csvPath, modelDir);
var diagPath = Path.Combine(modelDir, "diagnostic_metrics.json");
File.WriteAllText(diagPath, JsonSerializer.Serialize(diag, new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
}));
Console.WriteLine($"Diagnostic metrics saved to: {diagPath}");

// ── Multiclass RIASEC ──
Console.WriteLine("\n=== RIASEC Multiclass Training ===\n");
var riasecTrainer = new RiasecTrainer();
var riasecBest = riasecTrainer.TrainAndEvaluate(csvPath, modelPath);
Console.WriteLine($"\n=== RIASEC Training Complete ===");
Console.WriteLine($"Best: {riasecBest.AlgorithmName} — Macro Accuracy: {riasecBest.MacroAccuracy:P2}, Micro Accuracy: {riasecBest.MicroAccuracy:P2}");

return 0;
