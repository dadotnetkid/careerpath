using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Calibrators;
using CareerPath.Shared;
using System.Text.Json;

namespace CareerPath.ML;

public class CareerPathTrainer
{
    private readonly MLContext _mlContext;

    public CareerPathTrainer()
    {
        _mlContext = new MLContext(seed: 42);
    }

    /// <summary>
    /// Loads alumni_gts3.csv and derives the binary `Label` from `CareerAlignment`
    /// via CustomMapping (true if "Core ICT" or "ICT-Adjacent", false otherwise).
    /// We do NOT trust the precomputed `CareerOutcome` column — re-deriving in code
    /// keeps the rule tweakable without re-cleaning the CSV.
    /// </summary>
    public IDataView LoadData(string csvPath)
    {
        var rawData = _mlContext.Data.LoadFromTextFile<AlumniCsvRow>(
            csvPath, hasHeader: true, separatorChar: ',', allowQuoting: true);

        var labelMapping = _mlContext.Transforms.CustomMapping<AlumniCsvRow, LabelOutput>(
            (input, output) =>
            {
                var alignment = (input.CareerAlignment ?? string.Empty).Trim();
                output.Label = string.Equals(alignment, "Core ICT", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(alignment, "ICT-Adjacent", StringComparison.OrdinalIgnoreCase);
            },
            contractName: "CareerOutcomeMapping");

        return labelMapping.Fit(rawData).Transform(rawData);
    }

    public ModelComparison TrainAll(string csvPath, string modelOutputPath)
    {
        var data = LoadData(csvPath);

        var split = _mlContext.Data.TrainTestSplit(data, testFraction: 0.2, seed: 42);
        var trainData = split.TrainSet;
        var testData = split.TestSet;

        var trainCount = (int)trainData.GetColumn<bool>("Label").Count();
        var testCount = (int)testData.GetColumn<bool>("Label").Count();

        Console.WriteLine($"Training set: {trainCount} rows");
        Console.WriteLine($"Test set: {testCount} rows");

        var featurePipeline = BuildFeaturePipeline();

        // ── Random Forest (FastForest + Platt calibrator for probability output) ──
        Console.WriteLine("\n--- Training Random Forest (FastForest) ---");
        var rfTrainer = _mlContext.BinaryClassification.Trainers.FastForest(
            labelColumnName: "Label",
            featureColumnName: "Features",
            numberOfTrees: 100,
            numberOfLeaves: 20,
            minimumExampleCountPerLeaf: 5);
        var rfCalibratedTrainer = _mlContext.BinaryClassification.Calibrators.Platt(labelColumnName: "Label");
        var rfPipeline = featurePipeline.Append(rfTrainer).Append(rfCalibratedTrainer);
        var rfModel = rfPipeline.Fit(trainData);
        var rfMetrics = EvaluateModel(rfModel, testData, trainCount, testCount, "Random Forest (FastForest)");
        ComputeFeatureImportance(rfModel, testData, rfMetrics);

        // Save the RF model (primary)
        Directory.CreateDirectory(Path.GetDirectoryName(modelOutputPath)!);
        _mlContext.Model.Save(rfModel, trainData.Schema, modelOutputPath);
        Console.WriteLine($"Random Forest model saved to: {modelOutputPath}");

        // ── Decision Tree (FastTree with 1 tree) ──
        Console.WriteLine("\n--- Training Decision Tree (FastTree) ---");
        var dtTrainer = _mlContext.BinaryClassification.Trainers.FastTree(
            labelColumnName: "Label",
            featureColumnName: "Features",
            numberOfTrees: 1,
            numberOfLeaves: 20,
            minimumExampleCountPerLeaf: 5,
            learningRate: 1.0);
        var dtPipeline = featurePipeline.Append(dtTrainer);
        var dtModel = dtPipeline.Fit(trainData);
        var dtMetrics = EvaluateModel(dtModel, testData, trainCount, testCount, "Decision Tree (FastTree)");
        ComputeFeatureImportance(dtModel, testData, dtMetrics);

        // ── Logistic Regression ──
        Console.WriteLine("\n--- Training Logistic Regression (LbfgsLogisticRegression) ---");
        var lrTrainer = _mlContext.BinaryClassification.Trainers.LbfgsLogisticRegression(
            labelColumnName: "Label",
            featureColumnName: "Features");
        var lrPipeline = featurePipeline.Append(lrTrainer);
        var lrModel = lrPipeline.Fit(trainData);
        var lrMetrics = EvaluateModel(lrModel, testData, trainCount, testCount, "Logistic Regression (LBFGS)");
        ComputeFeatureImportance(lrModel, testData, lrMetrics);

        var comparison = new ModelComparison
        {
            RandomForest = rfMetrics,
            DecisionTree = dtMetrics,
            LogisticRegression = lrMetrics
        };

        // Save metrics.json next to the model
        var metricsPath = Path.Combine(Path.GetDirectoryName(modelOutputPath)!, "metrics.json");
        var json = JsonSerializer.Serialize(comparison, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(metricsPath, json);
        Console.WriteLine($"\nMetrics saved to: {metricsPath}");

        return comparison;
    }

    /// <summary>
    /// Builds the feature pipeline:
    ///  - one-hot encode Gwa, Honors, CareerClarity (categorical)
    ///  - TF-IDF (FeaturizeText) on Competencies and Interest
    ///  - concatenate with the numeric YearGraduated
    ///  - min-max normalize the resulting Features vector
    /// </summary>
    private IEstimator<ITransformer> BuildFeaturePipeline()
    {
        return _mlContext.Transforms.Categorical.OneHotEncoding("GwaEncoded", "Gwa")
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding("HonorsEncoded", "Honors"))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding("CareerClarityEncoded", "CareerClarity"))
            .Append(_mlContext.Transforms.Text.FeaturizeText("CompetenciesFeaturized", "Competencies"))
            .Append(_mlContext.Transforms.Text.FeaturizeText("InterestFeaturized", "Interest"))
            .Append(_mlContext.Transforms.Concatenate("Features",
                "YearGraduated",
                "GwaEncoded",
                "HonorsEncoded",
                "CareerClarityEncoded",
                "CompetenciesFeaturized",
                "InterestFeaturized"))
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"));
    }

    private ModelMetrics EvaluateModel(ITransformer model, IDataView testData,
        int trainCount, int testCount, string algorithmName)
    {
        var predictions = model.Transform(testData);
        var metrics = _mlContext.BinaryClassification.Evaluate(predictions, labelColumnName: "Label");

        Console.WriteLine($"  Accuracy:  {metrics.Accuracy:P2}");
        Console.WriteLine($"  Precision: {metrics.PositivePrecision:P2}");
        Console.WriteLine($"  Recall:    {metrics.PositiveRecall:P2}");
        Console.WriteLine($"  F1 Score:  {metrics.F1Score:P2}");
        Console.WriteLine($"  AUC:       {metrics.AreaUnderRocCurve:P2}");

        // Confusion matrix counts (2x2)
        var cm = metrics.ConfusionMatrix;
        var counts = cm.Counts;
        var confMatrix = new int[2][];
        confMatrix[0] = new int[] { (int)counts[0][0], (int)counts[0][1] };
        confMatrix[1] = new int[] { (int)counts[1][0], (int)counts[1][1] };

        Console.WriteLine($"  Confusion Matrix: TP={confMatrix[0][0]} FN={confMatrix[0][1]} FP={confMatrix[1][0]} TN={confMatrix[1][1]}");

        return new ModelMetrics
        {
            AlgorithmName = algorithmName,
            Accuracy = metrics.Accuracy,
            Precision = metrics.PositivePrecision,
            Recall = metrics.PositiveRecall,
            F1Score = metrics.F1Score,
            AreaUnderRocCurve = metrics.AreaUnderRocCurve,
            TrainingRows = trainCount,
            TestRows = testCount,
            TrainedAt = DateTime.UtcNow,
            ConfusionMatrix = confMatrix,
            ClassLabels = new List<string> { "ICT-Aligned", "Non-ICT-Aligned" }
        };
    }

    /// <summary>
    /// Permutation feature importance over the 6 raw input columns:
    /// YearGraduated, Gwa, Honors, Competencies, CareerClarity, Interest.
    /// Permutes each column 10 times, measures average accuracy drop,
    /// normalizes drops to sum to 100%, and stores in metrics.FeatureImportances.
    /// </summary>
    private void ComputeFeatureImportance(ITransformer model, IDataView testData, ModelMetrics metrics)
    {
        try
        {
            var baseAccuracy = metrics.Accuracy;
            var featureNames = new[] { "YearGraduated", "Gwa", "Honors", "Competencies", "CareerClarity", "Interest" };

            // Extract raw test data into typed arrays
            var years = testData.GetColumn<float>("YearGraduated").ToArray();
            var gwas = testData.GetColumn<string>("Gwa").ToArray();
            var honors = testData.GetColumn<string>("Honors").ToArray();
            var comps = testData.GetColumn<string>("Competencies").ToArray();
            var clarities = testData.GetColumn<string>("CareerClarity").ToArray();
            var interests = testData.GetColumn<string>("Interest").ToArray();
            var labels = testData.GetColumn<bool>("Label").ToArray();
            var n = labels.Length;
            var rng = new Random(42);

            var dropPerFeature = new Dictionary<string, double>();

            foreach (var featureName in featureNames)
            {
                double totalDrop = 0;
                const int permRuns = 10;

                for (int run = 0; run < permRuns; run++)
                {
                    var shuffled = Enumerable.Range(0, n).OrderBy(_ => rng.Next()).ToArray();
                    var rows = new List<StudentFeatures>(n);

                    for (int i = 0; i < n; i++)
                    {
                        rows.Add(new StudentFeatures
                        {
                            YearGraduated = featureName == "YearGraduated" ? years[shuffled[i]] : years[i],
                            Gwa = featureName == "Gwa" ? gwas[shuffled[i]] : gwas[i],
                            Honors = featureName == "Honors" ? honors[shuffled[i]] : honors[i],
                            Competencies = featureName == "Competencies" ? comps[shuffled[i]] : comps[i],
                            CareerClarity = featureName == "CareerClarity" ? clarities[shuffled[i]] : clarities[i],
                            Interest = featureName == "Interest" ? interests[shuffled[i]] : interests[i],
                            Label = labels[i]
                        });
                    }

                    var permData = _mlContext.Data.LoadFromEnumerable(rows);
                    var permPredictions = model.Transform(permData);
                    var permMetrics = _mlContext.BinaryClassification.Evaluate(permPredictions, labelColumnName: "Label");
                    totalDrop += Math.Max(0, baseAccuracy - permMetrics.Accuracy);
                }

                dropPerFeature[featureName] = totalDrop / permRuns;
            }

            var total = dropPerFeature.Values.Sum();
            if (total > 0)
            {
                metrics.FeatureImportances = dropPerFeature
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => new FeatureImportance
                    {
                        FeatureName = kv.Key,
                        Importance = Math.Round(kv.Value / total * 100, 2)
                    })
                    .ToList();
            }
            else
            {
                // Zero accuracy drop for every feature (rare). Still emit a row per
                // feature so downstream charts don't render an empty list.
                metrics.FeatureImportances = featureNames
                    .Select(f => new FeatureImportance { FeatureName = f, Importance = 0 })
                    .ToList();
            }

            Console.WriteLine("  Feature Importance:");
            foreach (var fi in metrics.FeatureImportances)
                Console.WriteLine($"    {fi.FeatureName}: {fi.Importance:F2}%");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Feature importance computation skipped: {ex.Message}");
        }
    }

    public PredictionEngine<StudentFeatures, CareerOutcomePrediction> LoadPredictionEngine(string modelPath)
    {
        var model = _mlContext.Model.Load(modelPath, out _);
        return _mlContext.Model.CreatePredictionEngine<StudentFeatures, CareerOutcomePrediction>(model);
    }
}

public class LabelOutput
{
    public bool Label { get; set; }
}
