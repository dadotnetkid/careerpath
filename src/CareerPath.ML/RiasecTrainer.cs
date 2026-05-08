using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using CareerPath.Shared;
using System.Globalization;
using System.Text.Json;

namespace CareerPath.ML;

/// <summary>
/// Multiclass classifier predicting the dominant RIASEC dimension (R/I/A/S/E/C)
/// from profile features. Reads `RiasecDominant` (full dimension name) from
/// alumni_gts3.csv and maps it to a single uppercase letter.
///
/// Profile features used (no Interest — that would leak the answer since
/// Interest is the dominant letter triplet):
///   YearGraduated, Gwa, Honors, Competencies, CareerClarity.
/// </summary>
public class RiasecTrainer
{
    private readonly MLContext _mlContext;

    private static readonly Dictionary<string, string> DimensionToLetter = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Realistic", "R" },
        { "Investigative", "I" },
        { "Artistic", "A" },
        { "Social", "S" },
        { "Enterprising", "E" },
        { "Conventional", "C" }
    };

    public RiasecTrainer()
    {
        _mlContext = new MLContext(seed: 42);
    }

    /// <summary>
    /// Reads alumni_gts3.csv with CsvHelper and produces RiasecFeatures rows.
    /// Skips rows where RiasecDominant doesn't map to a valid R/I/A/S/E/C letter.
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
            var dimension = (csv.GetField("RiasecDominant") ?? "").Trim();
            if (string.IsNullOrEmpty(dimension)) continue;
            if (!DimensionToLetter.TryGetValue(dimension, out var label)) continue;

            float year = 0f;
            float.TryParse(csv.GetField("YearGraduated") ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out year);

            rows.Add(new RiasecFeatures
            {
                YearGraduated = year,
                Gwa = csv.GetField("Gwa") ?? "",
                Honors = csv.GetField("Honors") ?? "",
                Competencies = csv.GetField("Competencies") ?? "",
                CareerClarity = csv.GetField("CareerClarity") ?? "",
                Label = label
            });
        }

        return rows;
    }

    public RiasecModelMetrics TrainAndEvaluate(string csvPath, string modelOutputPath)
    {
        var rawRows = LoadData(csvPath);
        Console.WriteLine($"\nRIASEC Multiclass Training");
        Console.WriteLine($"Total valid records: {rawRows.Count}");

        var dist = rawRows.GroupBy(r => r.Label).OrderByDescending(g => g.Count());
        Console.WriteLine("Label distribution:");
        foreach (var g in dist)
            Console.WriteLine($"  {g.Key}: {g.Count()} ({g.Count() * 100.0 / rawRows.Count:F1}%)");

        var data = _mlContext.Data.LoadFromEnumerable(rawRows);

        // 80/20 split with seed=42 for reproducibility
        var split = _mlContext.Data.TrainTestSplit(data, testFraction: 0.2, seed: 42);
        var trainData = split.TrainSet;
        var testData = split.TestSet;

        var trainCount = rawRows.Count - (int)testData.GetColumn<string>("Label").Count();
        var testCount = (int)testData.GetColumn<string>("Label").Count();

        // Feature pipeline: one-hot Gwa/Honors/CareerClarity, TF-IDF Competencies,
        // concatenate with YearGraduated, normalize.
        var featurePipeline = _mlContext.Transforms.Conversion.MapValueToKey("Label")
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding("GwaEncoded", "Gwa"))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding("HonorsEncoded", "Honors"))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding("CareerClarityEncoded", "CareerClarity"))
            .Append(_mlContext.Transforms.Text.FeaturizeText("CompetenciesFeaturized", "Competencies"))
            .Append(_mlContext.Transforms.Concatenate("Features",
                "YearGraduated",
                "GwaEncoded",
                "HonorsEncoded",
                "CareerClarityEncoded",
                "CompetenciesFeaturized"))
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"));

        // ── SDCA Maximum Entropy (default) ──
        Console.WriteLine("\n--- Training SDCA Maximum Entropy ---");
        var sdcaTrainer = _mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                labelColumnName: "Label", featureColumnName: "Features")
            .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));
        var sdcaPipeline = featurePipeline.Append(sdcaTrainer);
        var sdcaModel = sdcaPipeline.Fit(trainData);
        var sdcaMetrics = EvaluateMulticlass(sdcaModel, testData, trainCount, testCount, "SDCA Maximum Entropy");

        // ── LBFGS Maximum Entropy (alternative) ──
        Console.WriteLine("\n--- Training LBFGS Maximum Entropy ---");
        var lbfgsTrainer = _mlContext.MulticlassClassification.Trainers.LbfgsMaximumEntropy(
                labelColumnName: "Label", featureColumnName: "Features")
            .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));
        var lbfgsPipeline = featurePipeline.Append(lbfgsTrainer);
        var lbfgsModel = lbfgsPipeline.Fit(trainData);
        var lbfgsMetrics = EvaluateMulticlass(lbfgsModel, testData, trainCount, testCount, "LBFGS Maximum Entropy");

        // Pick the better model. Default to SDCA on a tie.
        ITransformer bestModel;
        RiasecModelMetrics bestMetrics;
        if (lbfgsMetrics.MicroAccuracy > sdcaMetrics.MicroAccuracy)
        {
            bestModel = lbfgsModel;
            bestMetrics = lbfgsMetrics;
        }
        else
        {
            bestModel = sdcaModel;
            bestMetrics = sdcaMetrics;
        }

        Console.WriteLine($"\nBest RIASEC model: {bestMetrics.AlgorithmName} (Micro Accuracy: {bestMetrics.MicroAccuracy:P2})");

        // Save best model + metrics to MLModel/ alongside model.zip
        var outDir = Path.GetDirectoryName(modelOutputPath)!;
        Directory.CreateDirectory(outDir);
        var riasecModelPath = Path.Combine(outDir, "riasec_model.zip");
        _mlContext.Model.Save(bestModel, trainData.Schema, riasecModelPath);
        Console.WriteLine($"RIASEC model saved to: {riasecModelPath}");

        var allMetrics = new RiasecModelComparison
        {
            Sdca = sdcaMetrics,
            Lbfgs = lbfgsMetrics,
            BestModel = bestMetrics.AlgorithmName
        };
        var metricsPath = Path.Combine(outDir, "riasec_metrics.json");
        var json = JsonSerializer.Serialize(allMetrics, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(metricsPath, json);
        Console.WriteLine($"RIASEC metrics saved to: {metricsPath}");

        return bestMetrics;
    }

    private RiasecModelMetrics EvaluateMulticlass(ITransformer model, IDataView testData,
        int trainCount, int testCount, string algorithmName)
    {
        var predictions = model.Transform(testData);
        var metrics = _mlContext.MulticlassClassification.Evaluate(predictions, labelColumnName: "Label");

        Console.WriteLine($"  Macro Accuracy: {metrics.MacroAccuracy:P2}");
        Console.WriteLine($"  Micro Accuracy: {metrics.MicroAccuracy:P2}");
        Console.WriteLine($"  Log Loss:       {metrics.LogLoss:F4}");

        var cm = metrics.ConfusionMatrix;
        var perClassPrecision = new List<double>();
        var perClassRecall = new List<double>();
        for (int i = 0; i < cm.NumberOfClasses; i++)
        {
            var precision = cm.PerClassPrecision[i];
            var recall = cm.PerClassRecall[i];
            perClassPrecision.Add(double.IsNaN(precision) ? 0 : precision);
            perClassRecall.Add(double.IsNaN(recall) ? 0 : recall);
        }

        var counts = cm.Counts;
        var confMatrix = new int[cm.NumberOfClasses][];
        for (int i = 0; i < cm.NumberOfClasses; i++)
        {
            confMatrix[i] = new int[cm.NumberOfClasses];
            for (int j = 0; j < cm.NumberOfClasses; j++)
                confMatrix[i][j] = (int)counts[i][j];
        }

        return new RiasecModelMetrics
        {
            AlgorithmName = algorithmName,
            MacroAccuracy = metrics.MacroAccuracy,
            MicroAccuracy = metrics.MicroAccuracy,
            LogLoss = metrics.LogLoss,
            TrainingRows = trainCount,
            TestRows = testCount,
            TrainedAt = DateTime.UtcNow,
            ClassLabels = cm.PerClassPrecision.Select((_, i) => $"Class{i}").ToList(),
            PerClassPrecision = perClassPrecision,
            PerClassRecall = perClassRecall,
            ConfusionMatrix = confMatrix
        };
    }

    public PredictionEngine<RiasecFeatures, RiasecPrediction> LoadPredictionEngine(string modelPath)
    {
        var model = _mlContext.Model.Load(modelPath, out _);
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
    public int TrainingRows { get; set; }
    public int TestRows { get; set; }
    public DateTime TrainedAt { get; set; }
    public List<string> ClassLabels { get; set; } = new();
    public List<double> PerClassPrecision { get; set; } = new();
    public List<double> PerClassRecall { get; set; } = new();
    public int[][] ConfusionMatrix { get; set; } = Array.Empty<int[]>();
}

public class RiasecModelComparison
{
    public RiasecModelMetrics Sdca { get; set; } = new();
    public RiasecModelMetrics Lbfgs { get; set; } = new();
    public string BestModel { get; set; } = "";
}
