using CareerPath.ML;

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

// ── Multiclass RIASEC ──
Console.WriteLine("\n=== RIASEC Multiclass Training ===\n");
var riasecTrainer = new RiasecTrainer();
var riasecBest = riasecTrainer.TrainAndEvaluate(csvPath, modelPath);
Console.WriteLine($"\n=== RIASEC Training Complete ===");
Console.WriteLine($"Best: {riasecBest.AlgorithmName} — Macro Accuracy: {riasecBest.MacroAccuracy:P2}, Micro Accuracy: {riasecBest.MicroAccuracy:P2}");

return 0;
