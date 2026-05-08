using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using CareerPath.Shared;
using System.Globalization;
using System.Text.Json;

namespace CareerPath.ML;

public class RiasecTrainer
{
    private readonly MLContext _mlContext;
    private static readonly HashSet<char> ValidRiasec = new() { 'R', 'I', 'A', 'S', 'E', 'C' };

    public RiasecTrainer()
    {
        _mlContext = new MLContext(seed: 42);
    }

    /// <summary>
    /// Load alumni CSV, extract dominant RIASEC letter as multiclass label,
    /// and use profile features (NOT the Interest/RIASEC code column).
    /// </summary>
    public List<RiasecFeatures> LoadData(string csvPath)
    {
        var rows = new List<RiasecFeatures>();

        using var reader = new StreamReader(csvPath);
        var config = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        };
        using var csv = new CsvHelper.CsvReader(reader, config);
        csv.Read();
        csv.ReadHeader();

        while (csv.Read())
        {
            var interest = csv.GetField("Interest") ?? "";

            // Extract dominant RIASEC letter (first valid letter)
            if (interest.Length < 1) continue;
            var dominant = char.ToUpper(interest[0]);
            if (!ValidRiasec.Contains(dominant)) continue;

            // Map to full dimension name for readable labels
            var label = dominant switch
            {
                'R' => "Realistic",
                'I' => "Investigative",
                'A' => "Artistic",
                'S' => "Social",
                'E' => "Enterprising",
                'C' => "Conventional",
                _ => ""
            };
            if (string.IsNullOrEmpty(label)) continue;

            rows.Add(new RiasecFeatures
            {
                Gwa = csv.GetField<float>("Gwa"),
                YearGraduated = csv.GetField<float>("YearGraduated"),
                Course = csv.GetField("Course") ?? "",
                Competencies = csv.GetField("Competencies") ?? "",
                CareerAlignment = csv.GetField("CareerAlignment") ?? "",
                FieldOfIndustry = csv.GetField("FieldOfIndustry") ?? "",
                EmploymentStatus = csv.GetField("EmploymentStatus") ?? "",
                Label = label
            });
        }

        return rows;
    }

    public RiasecModelMetrics TrainAndEvaluate(string csvPath, string modelOutputPath, int kFolds = 20)
    {
        var rawRows = LoadData(csvPath);
        Console.WriteLine($"\nRIASEC Multiclass Training");
        Console.WriteLine($"Total valid records: {rawRows.Count}");

        // Show label distribution
        var dist = rawRows.GroupBy(r => r.Label).OrderByDescending(g => g.Count());
        Console.WriteLine("Label distribution:");
        foreach (var g in dist)
            Console.WriteLine($"  {g.Key}: {g.Count()} ({g.Count() * 100.0 / rawRows.Count:F1}%)");

        var data = _mlContext.Data.LoadFromEnumerable(rawRows);

        // Build feature pipeline (NO Interest column — that's the label source)
        var pipeline = _mlContext.Transforms.Conversion.MapValueToKey("Label")
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding("CourseEncoded", "Course"))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding("AlignmentEncoded", "CareerAlignment"))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding("StatusEncoded", "EmploymentStatus"))
            .Append(_mlContext.Transforms.Text.FeaturizeText("CompetenciesFeaturized", "Competencies"))
            .Append(_mlContext.Transforms.Text.FeaturizeText("IndustryFeaturized", "FieldOfIndustry"))
            .Append(_mlContext.Transforms.Concatenate("Features",
                "Gwa", "YearGraduated",
                "CourseEncoded", "AlignmentEncoded", "StatusEncoded",
                "CompetenciesFeaturized", "IndustryFeaturized"))
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"));

        // ── SVM (One-vs-All with Linear SVM) ──
        Console.WriteLine("\n--- Training SVM (LinearSvm One-vs-All) ---");
        var svmTrainer = _mlContext.MulticlassClassification.Trainers.OneVersusAll(
            _mlContext.BinaryClassification.Trainers.LinearSvm(labelColumnName: "Label", featureColumnName: "Features"),
            labelColumnName: "Label")
            .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));
        var svmPipeline = pipeline.Append(svmTrainer);
        var svmCv = _mlContext.MulticlassClassification.CrossValidate(data, svmPipeline, numberOfFolds: kFolds, labelColumnName: "Label");
        var svmMetrics = AggregateCvMetrics(svmCv, "SVM (Linear, One-vs-All)", kFolds, rawRows.Count);

        // ── Random Forest (One-vs-All with FastForest) ──
        Console.WriteLine("\n--- Training Random Forest (FastForest One-vs-All) ---");
        var rfTrainer = _mlContext.MulticlassClassification.Trainers.OneVersusAll(
            _mlContext.BinaryClassification.Trainers.FastForest(labelColumnName: "Label", featureColumnName: "Features",
                numberOfTrees: 100, numberOfLeaves: 20),
            labelColumnName: "Label")
            .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));
        var rfPipeline = pipeline.Append(rfTrainer);
        var rfCv = _mlContext.MulticlassClassification.CrossValidate(data, rfPipeline, numberOfFolds: kFolds, labelColumnName: "Label");
        var rfMetrics = AggregateCvMetrics(rfCv, "Random Forest (One-vs-All)", kFolds, rawRows.Count);

        // ── SDCA (Stochastic Dual Coordinate Ascent) ──
        Console.WriteLine("\n--- Training SDCA Maximum Entropy ---");
        var sdcaTrainer = _mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
            labelColumnName: "Label", featureColumnName: "Features")
            .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));
        var sdcaPipeline = pipeline.Append(sdcaTrainer);
        var sdcaCv = _mlContext.MulticlassClassification.CrossValidate(data, sdcaPipeline, numberOfFolds: kFolds, labelColumnName: "Label");
        var sdcaMetrics = AggregateCvMetrics(sdcaCv, "SDCA Maximum Entropy", kFolds, rawRows.Count);

        // Pick the best model and save it
        var bestResult = new[] { ("SVM", svmMetrics, svmCv), ("RF", rfMetrics, rfCv), ("SDCA", sdcaMetrics, sdcaCv) }
            .OrderByDescending(x => x.Item2.MacroAccuracy)
            .First();

        Console.WriteLine($"\nBest model: {bestResult.Item2.AlgorithmName} (Macro Accuracy: {bestResult.Item2.MacroAccuracy:P2})");

        // Train best model on full dataset and save
        ITransformer bestModel;
        if (bestResult.Item1 == "SVM")
            bestModel = svmPipeline.Fit(data);
        else if (bestResult.Item1 == "RF")
            bestModel = rfPipeline.Fit(data);
        else
            bestModel = sdcaPipeline.Fit(data);

        var riasecModelPath = Path.Combine(Path.GetDirectoryName(modelOutputPath)!, "riasec_model.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(riasecModelPath)!);
        _mlContext.Model.Save(bestModel, data.Schema, riasecModelPath);
        Console.WriteLine($"RIASEC model saved to: {riasecModelPath}");

        // Save metrics
        var allMetrics = new RiasecModelComparison
        {
            Svm = svmMetrics,
            RandomForest = rfMetrics,
            Sdca = sdcaMetrics,
            BestModel = bestResult.Item2.AlgorithmName
        };
        var metricsPath = Path.Combine(Path.GetDirectoryName(modelOutputPath)!, "riasec_metrics.json");
        var json = JsonSerializer.Serialize(allMetrics, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(metricsPath, json);
        Console.WriteLine($"RIASEC metrics saved to: {metricsPath}");

        return bestResult.Item2;
    }

    private RiasecModelMetrics AggregateCvMetrics(
        IReadOnlyList<TrainCatalogBase.CrossValidationResult<MulticlassClassificationMetrics>> cvResults,
        string algorithmName, int kFolds, int totalRows)
    {
        var metrics = cvResults.Select(r => r.Metrics).ToList();

        var macroAccuracy = metrics.Average(m => m.MacroAccuracy);
        var microAccuracy = metrics.Average(m => m.MicroAccuracy);
        var logLoss = metrics.Average(m => m.LogLoss);

        Console.WriteLine($"  {kFolds}-Fold CV Results:");
        Console.WriteLine($"  Macro Accuracy: {macroAccuracy:P2}");
        Console.WriteLine($"  Micro Accuracy: {microAccuracy:P2}");
        Console.WriteLine($"  Log Loss:       {logLoss:F4}");

        // Per-class metrics from the last fold's confusion matrix
        var lastCm = metrics.Last().ConfusionMatrix;
        var classLabels = new List<string>();
        var perClassPrecision = new List<double>();
        var perClassRecall = new List<double>();

        for (int i = 0; i < lastCm.NumberOfClasses; i++)
        {
            var precision = lastCm.PerClassPrecision[i];
            var recall = lastCm.PerClassRecall[i];
            perClassPrecision.Add(double.IsNaN(precision) ? 0 : precision);
            perClassRecall.Add(double.IsNaN(recall) ? 0 : recall);
        }

        // Extract confusion matrix counts from the last fold
        var cmCounts = lastCm.Counts;
        var confMatrix = new int[lastCm.NumberOfClasses][];
        for (int i = 0; i < lastCm.NumberOfClasses; i++)
        {
            confMatrix[i] = new int[lastCm.NumberOfClasses];
            for (int j = 0; j < lastCm.NumberOfClasses; j++)
                confMatrix[i][j] = (int)cmCounts[i][j];
        }

        return new RiasecModelMetrics
        {
            AlgorithmName = algorithmName,
            MacroAccuracy = macroAccuracy,
            MicroAccuracy = microAccuracy,
            LogLoss = logLoss,
            KFolds = kFolds,
            TotalRows = totalRows,
            TrainedAt = DateTime.UtcNow,
            PerClassPrecision = perClassPrecision,
            PerClassRecall = perClassRecall,
            ConfusionMatrix = confMatrix
        };
    }

    public PredictionEngine<RiasecFeatures, RiasecPrediction> LoadPredictionEngine(string modelPath)
    {
        var model = _mlContext.Model.Load(modelPath, out var schema);
        return _mlContext.Model.CreatePredictionEngine<RiasecFeatures, RiasecPrediction>(model);
    }
}

// ── Metrics DTOs ──

public class RiasecModelMetrics
{
    public string AlgorithmName { get; set; } = "";
    public double MacroAccuracy { get; set; }
    public double MicroAccuracy { get; set; }
    public double LogLoss { get; set; }
    public int KFolds { get; set; }
    public int TotalRows { get; set; }
    public DateTime TrainedAt { get; set; }
    public List<double> PerClassPrecision { get; set; } = new();
    public List<double> PerClassRecall { get; set; } = new();
    public int[][] ConfusionMatrix { get; set; } = Array.Empty<int[]>();
}

public class RiasecModelComparison
{
    public RiasecModelMetrics Svm { get; set; } = new();
    public RiasecModelMetrics RandomForest { get; set; } = new();
    public RiasecModelMetrics Sdca { get; set; } = new();
    public string BestModel { get; set; } = "";
}
