using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Calibrators;
using Microsoft.ML.Trainers.FastTree;
using CareerPath.Shared;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace CareerPath.ML;

public class CareerPathTrainer
{
    private readonly MLContext _mlContext;

    // Class-balance weights — recomputed in LoadData from the actual class frequencies
    // of the dataset, then used by (a) the example weight column passed to each trainer
    // and (b) surfaced into ModelMetrics so the manuscript can quote them.
    // Static so CustomMapping lambdas (which must be field-stable for ML.NET serializer)
    // can capture them without complaining.
    private static float _positiveClassWeight = 1f;
    private static float _negativeClassWeight = 1f;

    public CareerPathTrainer()
    {
        _mlContext = new MLContext(seed: 42);
    }

    /// <summary>
    /// Loads alumni_gts3.csv, derives the binary `Label` from `CareerAlignment`
    /// via CustomMapping (true if "Core ICT" or "ICT-Adjacent", false otherwise),
    /// and attaches an `ExampleWeight` column for class balancing (inverse class
    /// frequency: minority class rows get a higher weight so the trainer pays
    /// equal attention to both outcomes).
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

        var labelled = labelMapping.Fit(rawData).Transform(rawData);

        // Compute class frequencies once over the entire dataset and store on the
        // static fields. The example-weight mapping below reads these — keeping the
        // lambda free of captured state makes the saved pipeline portable.
        var labels = labelled.GetColumn<bool>("Label").ToArray();
        int positives = labels.Count(l => l);
        int negatives = labels.Length - positives;
        if (positives > 0 && negatives > 0)
        {
            _positiveClassWeight = (float)labels.Length / (2f * positives);
            _negativeClassWeight = (float)labels.Length / (2f * negatives);
        }
        else
        {
            _positiveClassWeight = 1f;
            _negativeClassWeight = 1f;
        }
        Console.WriteLine($"  Class balance weights: positive={_positiveClassWeight:F3} (n={positives}), " +
                          $"negative={_negativeClassWeight:F3} (n={negatives})");

        var weightMapping = _mlContext.Transforms.CustomMapping<LabelInputForWeight, WeightOutput>(
            (input, output) => { output.ExampleWeight = input.Label ? _positiveClassWeight : _negativeClassWeight; },
            contractName: "ClassBalanceWeights");

        return weightMapping.Fit(labelled).Transform(labelled);
    }

    /// <summary>Latest weights computed by <see cref="LoadData"/>. Static — read after a load.</summary>
    public static (float positive, float negative) GetCurrentClassWeights()
        => (_positiveClassWeight, _negativeClassWeight);

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
        // ExampleWeight is wired into FastForest so minority-class rows pull harder.
        Console.WriteLine("\n--- Training Random Forest (FastForest) ---");
        var rfTrainer = _mlContext.BinaryClassification.Trainers.FastForest(
            labelColumnName: "Label",
            featureColumnName: "Features",
            exampleWeightColumnName: "ExampleWeight",
            numberOfTrees: 100,
            numberOfLeaves: 20,
            minimumExampleCountPerLeaf: 5);
        var rfCalibratedTrainer = _mlContext.BinaryClassification.Calibrators.Platt(labelColumnName: "Label");
        var rfPipeline = featurePipeline.Append(rfTrainer).Append(rfCalibratedTrainer);
        var rfModel = rfPipeline.Fit(trainData);
        var rfMetrics = EvaluateModel(rfModel, testData, trainCount, testCount, "Random Forest (FastForest)");
        ComputeFeatureImportance(rfModel, testData, rfMetrics);
        ComputeMdiFeatureImportance(rfModel, trainData, rfMetrics);

        // Save the RF model (primary)
        Directory.CreateDirectory(Path.GetDirectoryName(modelOutputPath)!);
        _mlContext.Model.Save(rfModel, trainData.Schema, modelOutputPath);
        Console.WriteLine($"Random Forest model saved to: {modelOutputPath}");

        // ── Decision Tree (FastTree with 1 tree) ──
        Console.WriteLine("\n--- Training Decision Tree (FastTree) ---");
        var dtTrainer = _mlContext.BinaryClassification.Trainers.FastTree(
            labelColumnName: "Label",
            featureColumnName: "Features",
            exampleWeightColumnName: "ExampleWeight",
            numberOfTrees: 1,
            numberOfLeaves: 20,
            minimumExampleCountPerLeaf: 5,
            learningRate: 1.0);
        var dtPipeline = featurePipeline.Append(dtTrainer);
        var dtModel = dtPipeline.Fit(trainData);
        var dtMetrics = EvaluateModel(dtModel, testData, trainCount, testCount, "Decision Tree (FastTree)");
        ComputeFeatureImportance(dtModel, testData, dtMetrics);
        ComputeMdiFeatureImportance(dtModel, trainData, dtMetrics);

        // ── Logistic Regression ──
        Console.WriteLine("\n--- Training Logistic Regression (LbfgsLogisticRegression) ---");
        var lrTrainer = _mlContext.BinaryClassification.Trainers.LbfgsLogisticRegression(
            labelColumnName: "Label",
            featureColumnName: "Features",
            exampleWeightColumnName: "ExampleWeight");
        var lrPipeline = featurePipeline.Append(lrTrainer);
        var lrModel = lrPipeline.Fit(trainData);
        var lrMetrics = EvaluateModel(lrModel, testData, trainCount, testCount, "Logistic Regression (LBFGS)");
        ComputeFeatureImportance(lrModel, testData, lrMetrics);
        // LR has no tree structure — MDI is undefined; leave FeatureImportancesMdi empty.

        var (wPos, wNeg) = GetCurrentClassWeights();
        foreach (var m in new[] { rfMetrics, dtMetrics, lrMetrics })
        {
            m.PositiveClassWeight = wPos;
            m.NegativeClassWeight = wNeg;
        }

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

    /// <summary>
    /// Mean Decrease in Impurity (MDI) feature importance for tree-based models.
    /// The trained ensemble exposes per-slot split-gain via <c>GetFeatureWeights</c>;
    /// we read the Features-column SlotNames metadata to map each encoded slot back
    /// to its raw column name (YearGraduated, Gwa, Honors, CareerClarity,
    /// Competencies, Interest), sum, and normalize to 100%.
    ///
    /// Skips silently if the model isn't a tree ensemble (LR), which is fine —
    /// MDI is only defined for tree-based methods.
    /// </summary>
    private void ComputeMdiFeatureImportance(ITransformer model, IDataView trainData, ModelMetrics metrics)
    {
        try
        {
            var tree = FindTreeEnsembleParameters(model);
            if (tree == null) return;

            // Per-slot gain weights from the ensemble.
            VBuffer<float> weights = default;
            tree.GetFeatureWeights(ref weights);
            var weightArray = weights.DenseValues().ToArray();
            if (weightArray.Length == 0) return;

            // Slot names for the Features column let us roll encoded slots back up
            // to their raw input feature (e.g. "GwaEncoded.Outstanding" -> "Gwa").
            var transformed = model.Transform(trainData);
            var featuresCol = transformed.Schema.GetColumnOrNull("Features");
            if (featuresCol == null) return;
            VBuffer<ReadOnlyMemory<char>> slotNamesBuffer = default;
            featuresCol.Value.GetSlotNames(ref slotNamesBuffer);
            var slotNames = slotNamesBuffer.DenseValues().Select(c => c.ToString()).ToArray();

            // Slot count and weight count can differ if Normalize collapsed something;
            // in practice ML.NET keeps them aligned, but guard anyway.
            int n = Math.Min(slotNames.Length, weightArray.Length);

            // Map each slot's name prefix to a raw input bucket. Names look like
            // "GwaEncoded.Outstanding", "CompetenciesFeaturized.programming", or
            // just "YearGraduated" for the lone numeric column.
            var prefixToRaw = new (string Prefix, string Raw)[]
            {
                ("YearGraduated",           "YearGraduated"),
                ("GwaEncoded",              "Gwa"),
                ("HonorsEncoded",           "Honors"),
                ("CareerClarityEncoded",    "CareerClarity"),
                ("CompetenciesFeaturized",  "Competencies"),
                ("InterestFeaturized",      "Interest"),
            };

            var sumPerRaw = prefixToRaw.ToDictionary(p => p.Raw, _ => 0.0);
            for (int i = 0; i < n; i++)
            {
                var slot = slotNames[i];
                foreach (var (prefix, raw) in prefixToRaw)
                {
                    if (slot.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        sumPerRaw[raw] += weightArray[i];
                        break;
                    }
                }
            }

            var total = sumPerRaw.Values.Sum();
            if (total <= 0) return;

            metrics.FeatureImportancesMdi = sumPerRaw
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new FeatureImportance
                {
                    FeatureName = kv.Key,
                    Importance = Math.Round(kv.Value / total * 100, 2)
                })
                .ToList();

            Console.WriteLine("  MDI Feature Importance:");
            foreach (var fi in metrics.FeatureImportancesMdi)
                Console.WriteLine($"    {fi.FeatureName}: {fi.Importance:F2}%");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MDI feature importance computation skipped: {ex.Message}");
        }
    }

    /// <summary>
    /// Walks the transformer chain looking for a tree-ensemble parameter object.
    /// Handles both calibrated (RF + Platt) and uncalibrated (FastTree) wrappers,
    /// since the calibrator nests the tree params under a "SubModel" property.
    /// Uses reflection because the public concrete types differ across trainer
    /// flavors and aren't always exposed via a clean interface.
    /// </summary>
    private static TreeEnsembleModelParameters? FindTreeEnsembleParameters(ITransformer model)
    {
        // Recurse into chains.
        if (model is IEnumerable<ITransformer> chain)
        {
            foreach (var inner in chain)
            {
                var found = FindTreeEnsembleParameters(inner);
                if (found != null) return found;
            }
        }

        var modelProp = FindPropertyByName(model.GetType(), "Model");
        if (modelProp == null) return null;
        var modelObj = modelProp.GetValue(model);
        if (modelObj == null) return null;

        if (modelObj is TreeEnsembleModelParameters tree) return tree;

        // Calibrated wrapper: walk the SubModel chain (CalibratedModelParametersBase has SubModel).
        var visited = new HashSet<object>();
        var current = modelObj;
        while (current != null && visited.Add(current))
        {
            if (current is TreeEnsembleModelParameters t) return t;
            var subModelProp = FindPropertyByName(current.GetType(), "SubModel");
            if (subModelProp == null) break;
            current = subModelProp.GetValue(current);
        }
        return null;
    }

    /// <summary>
    /// Walks the inheritance chain looking for a public instance property by name.
    /// We avoid <c>Type.GetProperty(name)</c> because <c>CalibratedModelParametersBase</c>
    /// hides a base property with <c>new</c>, which causes the runtime to throw
    /// <c>AmbiguousMatchException</c>. Returning the most-derived declaration first
    /// (i.e. the typed override) gives us the concrete runtime instance.
    /// </summary>
    private static PropertyInfo? FindPropertyByName(Type? type, string name)
    {
        while (type != null)
        {
            var p = type.GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (p != null) return p;
            type = type.BaseType;
        }
        return null;
    }

    /// <summary>
    /// Runs 10-fold and 20-fold cross-validation for RF, DT, and LR (same hyperparameters
    /// as <see cref="TrainAll"/>). Returns a <see cref="CrossValidationReport"/> with six
    /// <see cref="CvAlgorithmRun"/>s — one per (algorithm × foldCount) combo. Per-fold metrics
    /// (Accuracy / PositivePrecision / PositiveRecall / F1 / AUC) are summarized as mean and
    /// sample (Bessel-corrected, n-1) standard deviation.
    ///
    /// CV is reported as stability validation only — the canonical model selection metric
    /// remains the 80/20 split produced by <see cref="TrainAll"/>.
    /// </summary>
    public CrossValidationReport CrossValidate(string csvPath)
    {
        var data = LoadData(csvPath);

        var algorithms = new (string Name, IEstimator<ITransformer> Trainer)[]
        {
            ("Random Forest", BuildRfTrainerWithCalibrator()),
            ("Decision Tree", _mlContext.BinaryClassification.Trainers.FastTree(
                labelColumnName: "Label",
                featureColumnName: "Features",
                exampleWeightColumnName: "ExampleWeight",
                numberOfTrees: 1,
                numberOfLeaves: 20,
                minimumExampleCountPerLeaf: 5,
                learningRate: 1.0)),
            ("Logistic Regression", _mlContext.BinaryClassification.Trainers.LbfgsLogisticRegression(
                labelColumnName: "Label",
                featureColumnName: "Features",
                exampleWeightColumnName: "ExampleWeight"))
        };

        var foldCounts = new[] { 10, 20 };
        var runs = new List<CvAlgorithmRun>(algorithms.Length * foldCounts.Length);

        foreach (var (algoName, trainer) in algorithms)
        {
            foreach (var foldCount in foldCounts)
            {
                Console.WriteLine($"\n--- Cross-Validating {algoName} ({foldCount}-fold) ---");

                var pipeline = BuildFeaturePipeline().Append(trainer);
                var cvResults = _mlContext.BinaryClassification.CrossValidate(
                    data,
                    pipeline,
                    numberOfFolds: foldCount,
                    labelColumnName: "Label",
                    samplingKeyColumnName: null,
                    seed: 42);

                var folds = new List<CvFoldMetric>(cvResults.Count);
                int idx = 0;
                foreach (var r in cvResults)
                {
                    folds.Add(new CvFoldMetric
                    {
                        FoldIndex = r.Fold >= 0 ? r.Fold : idx,
                        Accuracy = r.Metrics.Accuracy,
                        Precision = r.Metrics.PositivePrecision,
                        Recall = r.Metrics.PositiveRecall,
                        F1 = r.Metrics.F1Score,
                        Auc = r.Metrics.AreaUnderRocCurve
                    });
                    idx++;
                }

                var run = new CvAlgorithmRun
                {
                    Algorithm = algoName,
                    FoldCount = foldCount,
                    MeanAccuracy = Mean(folds, f => f.Accuracy),
                    StdAccuracy = SampleStdDev(folds, f => f.Accuracy),
                    MeanPrecision = Mean(folds, f => f.Precision),
                    StdPrecision = SampleStdDev(folds, f => f.Precision),
                    MeanRecall = Mean(folds, f => f.Recall),
                    StdRecall = SampleStdDev(folds, f => f.Recall),
                    MeanF1 = Mean(folds, f => f.F1),
                    StdF1 = SampleStdDev(folds, f => f.F1),
                    MeanAuc = Mean(folds, f => f.Auc),
                    StdAuc = SampleStdDev(folds, f => f.Auc),
                    Folds = folds
                };

                Console.WriteLine($"  Mean Accuracy: {run.MeanAccuracy:P2} (± {run.StdAccuracy:P2})");
                Console.WriteLine($"  Mean F1:       {run.MeanF1:P2} (± {run.StdF1:P2})");
                Console.WriteLine($"  Mean AUC:      {run.MeanAuc:P2} (± {run.StdAuc:P2})");

                runs.Add(run);
            }
        }

        return new CrossValidationReport
        {
            DatasetVersion = $"alumni_gts3.csv@{File.GetLastWriteTimeUtc(csvPath):yyyy-MM-dd}",
            TrainedAt = DateTime.UtcNow,
            Runs = runs
        };
    }

    /// <summary>
    /// Builds the two diagnostic confusion matrices surfaced on the Insights page
    /// alongside the canonical 2×2 (which stays unchanged):
    ///
    ///   1. <b>4×2</b>: rows = actual 4-class <c>CareerAlignment</c>, columns =
    ///      predicted binary outcome from the canonical RF (loaded from
    ///      <c>model.zip</c> in <paramref name="outputDir"/>). Built by running
    ///      the binary model over all 301 alumni and tabulating by sub-class.
    ///   2. <b>4×4</b>: rows = actual 4-class, columns = predicted 4-class. Trains
    ///      a SEPARATE multiclass One-vs-All FastForest model (saved as
    ///      <c>model_4class.zip</c>) on the same 80/20 split (seed=42) used by
    ///      <see cref="TrainAll"/>, and evaluates on the test fold.
    ///
    /// The binary <c>model.zip</c> and <c>metrics.json</c> are NOT touched —
    /// V3 is purely additive. The label order (rows + cols) is fixed at
    /// <c>["Core ICT","ICT-Adjacent","Non-ICT but Adjacent","Non-ICT"]</c>; the
    /// client renders by index so order MUST be respected.
    /// </summary>
    public DiagnosticConfusionMatrices TrainDiagnostic(string csvPath, string outputDir)
    {
        // Canonical row/col label order. The client renders by index — do not reorder.
        var classLabels = new[] { "Core ICT", "ICT-Adjacent", "Non-ICT but Adjacent", "Non-ICT" };
        var binaryLabels = new[] { "ICT-Aligned", "Non-ICT-Aligned" };

        // ── 1. Load alumni rows with their original CareerAlignment via CsvHelper ──
        // We need both the StudentFeatures (to feed the binary model) and the
        // 4-class CareerAlignment string (to bucket the rows). Re-loading via
        // CsvHelper is the cleanest way to get both in lockstep.
        var alumni = LoadAlumniWithAlignment(csvPath);
        Console.WriteLine($"\n--- Diagnostic: loaded {alumni.Count} alumni rows ---");

        // ── 2. 4×2 matrix: load canonical model.zip, predict for every alumni ──
        var binaryModelPath = Path.Combine(outputDir, "model.zip");
        if (!File.Exists(binaryModelPath))
            throw new FileNotFoundException(
                $"Diagnostic 4×2 matrix needs the canonical binary model at {binaryModelPath}. " +
                "Run TrainAll(...) first.");

        var engine = LoadPredictionEngine(binaryModelPath);

        var counts4x2 = new int[4][];
        for (int i = 0; i < 4; i++) counts4x2[i] = new int[2];
        var rowTotals4x2 = new int[4];

        foreach (var row in alumni)
        {
            var rowIdx = Array.IndexOf(classLabels, row.CareerAlignment);
            if (rowIdx < 0) continue; // row with unrecognized alignment — skip (shouldn't happen with cleaned data)

            var pred = engine.Predict(row.Features);
            // PredictedLabel == true means binary "ICT-Aligned" (col 0), false means "Non-ICT-Aligned" (col 1).
            var colIdx = pred.PredictedLabel ? 0 : 1;
            counts4x2[rowIdx][colIdx]++;
            rowTotals4x2[rowIdx]++;
        }

        Console.WriteLine("  4×2 matrix (rows=actual 4-class, cols=predicted binary):");
        for (int i = 0; i < 4; i++)
            Console.WriteLine($"    {classLabels[i],-22}  ICT-Aligned={counts4x2[i][0],3}  Non-ICT-Aligned={counts4x2[i][1],3}  total={rowTotals4x2[i]}");

        var matrix4x2 = new ConfusionMatrix4x2
        {
            ActualLabels = classLabels.ToList(),
            PredictedLabels = binaryLabels.ToList(),
            Counts = counts4x2,
            RowTotals = rowTotals4x2
        };

        // ── 3. Train a fresh 4-class multiclass model ──
        Console.WriteLine("\n--- Diagnostic: training 4-class multiclass model (One-vs-All FastForest) ---");

        var fourClassRows = alumni
            .Where(a => Array.IndexOf(classLabels, a.CareerAlignment) >= 0)
            .Select(a => new CareerAlignment4ClassFeatures
            {
                YearGraduated = a.Features.YearGraduated,
                Gwa = a.Features.Gwa,
                Honors = a.Features.Honors,
                Competencies = a.Features.Competencies,
                CareerClarity = a.Features.CareerClarity,
                Interest = a.Features.Interest,
                Label = a.CareerAlignment
            })
            .ToList();

        var multiclassData = _mlContext.Data.LoadFromEnumerable(fourClassRows);
        var multiclassSplit = _mlContext.Data.TrainTestSplit(multiclassData, testFraction: 0.2, seed: 42);
        var multiclassTrain = multiclassSplit.TrainSet;
        var multiclassTest = multiclassSplit.TestSet;

        var trainCount4Class = (int)multiclassTrain.GetColumn<string>("Label").Count();
        var testCount4Class = (int)multiclassTest.GetColumn<string>("Label").Count();
        Console.WriteLine($"  4-class train={trainCount4Class}, test={testCount4Class}");

        // Mirror the binary feature pipeline so comparison stays honest:
        // one-hot Gwa/Honors/CareerClarity, TF-IDF Competencies/Interest, concat YearGraduated, normalize.
        // Then MapValueToKey before the trainer, MapKeyToValue after, so PredictedLabel is the
        // original 4-class string.
        var multiclassPipeline = _mlContext.Transforms.Categorical.OneHotEncoding("GwaEncoded", "Gwa")
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
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(_mlContext.Transforms.Conversion.MapValueToKey("Label", "Label"))
            .Append(_mlContext.MulticlassClassification.Trainers.OneVersusAll(
                _mlContext.BinaryClassification.Trainers.FastForest(
                    labelColumnName: "Label",
                    featureColumnName: "Features",
                    numberOfTrees: 100,
                    numberOfLeaves: 20,
                    minimumExampleCountPerLeaf: 5),
                labelColumnName: "Label"))
            .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

        var multiclassModel = multiclassPipeline.Fit(multiclassTrain);

        // Save the diagnostic 4-class model
        var multiclassModelPath = Path.Combine(outputDir, "model_4class.zip");
        Directory.CreateDirectory(outputDir);
        _mlContext.Model.Save(multiclassModel, multiclassTrain.Schema, multiclassModelPath);
        Console.WriteLine($"  4-class model saved to: {multiclassModelPath}");

        // ── 4. Evaluate on the test fold — manually tabulate to keep label order canonical ──
        // After the pipeline runs, the "Label" column has been replaced by a Key (UInt32),
        // so we read actual labels from the pre-transform test IDataView and predicted labels
        // from the post-transform output. Both arrays are in row order, so they zip correctly.
        var actualLabels = multiclassTest.GetColumn<string>("Label").ToArray();
        var testPredictions = multiclassModel.Transform(multiclassTest);
        var predictedLabels = testPredictions.GetColumn<string>("PredictedLabel").ToArray();

        if (actualLabels.Length != predictedLabels.Length)
            throw new InvalidOperationException(
                $"Test fold actual/predicted length mismatch: {actualLabels.Length} vs {predictedLabels.Length}");

        var counts4x4 = new int[4][];
        for (int i = 0; i < 4; i++) counts4x4[i] = new int[4];
        var rowTotals4x4 = new int[4];

        for (int i = 0; i < actualLabels.Length; i++)
        {
            var rowIdx = Array.IndexOf(classLabels, actualLabels[i]);
            var colIdx = Array.IndexOf(classLabels, predictedLabels[i]);
            if (rowIdx < 0 || colIdx < 0) continue;
            counts4x4[rowIdx][colIdx]++;
            rowTotals4x4[rowIdx]++;
        }

        Console.WriteLine("  4×4 matrix (rows=actual, cols=predicted; canonical order):");
        Console.WriteLine($"    {"",-22}  {"CoreICT",8} {"ICT-Adj",8} {"Non-ICT-A",10} {"Non-ICT",8}  total");
        for (int i = 0; i < 4; i++)
            Console.WriteLine($"    {classLabels[i],-22}  {counts4x4[i][0],8} {counts4x4[i][1],8} {counts4x4[i][2],10} {counts4x4[i][3],8}  {rowTotals4x4[i]}");

        // Per-class precision/recall/F1 (canonical order), then macro-F1 and accuracy
        double diagonal = 0;
        var f1s = new double[4];
        for (int c = 0; c < 4; c++)
        {
            int tp = counts4x4[c][c];
            int fn = rowTotals4x4[c] - tp;
            int fp = 0;
            for (int r = 0; r < 4; r++) if (r != c) fp += counts4x4[r][c];

            double precision = (tp + fp) == 0 ? 0.0 : (double)tp / (tp + fp);
            double recall = (tp + fn) == 0 ? 0.0 : (double)tp / (tp + fn);
            double f1 = (precision + recall) == 0 ? 0.0 : 2 * precision * recall / (precision + recall);
            f1s[c] = f1;
            diagonal += tp;

            Console.WriteLine($"    {classLabels[c],-22}  precision={precision:F3}  recall={recall:F3}  F1={f1:F3}");
        }

        double accuracy4Class = actualLabels.Length == 0 ? 0.0 : diagonal / actualLabels.Length;
        double macroF1 = f1s.Average();
        Console.WriteLine($"  Multiclass accuracy: {accuracy4Class:P2}  Macro-F1: {macroF1:P2}  (test fold n={actualLabels.Length})");

        var matrix4x4 = new ConfusionMatrix4x4
        {
            Labels = classLabels.ToList(),
            Counts = counts4x4,
            RowTotals = rowTotals4x4
        };

        return new DiagnosticConfusionMatrices
        {
            ComputedAt = DateTime.UtcNow,
            BinaryByActual4Class = matrix4x2,
            Multiclass4x4 = matrix4x4,
            Multiclass4ClassAccuracy = accuracy4Class,
            Multiclass4ClassMacroF1 = macroF1,
            Multiclass4ClassTrainRows = trainCount4Class,
            Multiclass4ClassTestRows = testCount4Class
        };
    }

    /// <summary>
    /// Reads alumni_gts3.csv via CsvHelper and produces (StudentFeatures, CareerAlignment)
    /// pairs in CSV row order. Used by <see cref="TrainDiagnostic"/> so we can run the
    /// binary engine over each row and bucket the prediction by the 4-class label.
    /// </summary>
    private static List<(StudentFeatures Features, string CareerAlignment)> LoadAlumniWithAlignment(string csvPath)
    {
        var rows = new List<(StudentFeatures, string)>();

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
            var alignment = (csv.GetField("CareerAlignment") ?? "").Trim();

            float year = 0f;
            float.TryParse(csv.GetField("YearGraduated") ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out year);

            var features = new StudentFeatures
            {
                YearGraduated = year,
                Gwa = (csv.GetField("Gwa") ?? "").Trim(),
                Honors = (csv.GetField("Honors") ?? "").Trim(),
                Competencies = (csv.GetField("Competencies") ?? "").Trim(),
                CareerClarity = (csv.GetField("CareerClarity") ?? "").Trim(),
                Interest = (csv.GetField("Interest") ?? "").Trim(),
                Label = false // not used at inference; the binary engine ignores it
            };

            rows.Add((features, alignment));
        }

        return rows;
    }

    /// <summary>
    /// RF + Platt calibrator wrapped as a single estimator so the CV pipeline can
    /// append it just like <see cref="TrainAll"/> does.
    /// </summary>
    private IEstimator<ITransformer> BuildRfTrainerWithCalibrator()
    {
        var rfTrainer = _mlContext.BinaryClassification.Trainers.FastForest(
            labelColumnName: "Label",
            featureColumnName: "Features",
            exampleWeightColumnName: "ExampleWeight",
            numberOfTrees: 100,
            numberOfLeaves: 20,
            minimumExampleCountPerLeaf: 5);
        var calibrator = _mlContext.BinaryClassification.Calibrators.Platt(labelColumnName: "Label");
        return rfTrainer.Append(calibrator);
    }

    private static double Mean(IReadOnlyList<CvFoldMetric> folds, Func<CvFoldMetric, double> selector)
    {
        if (folds.Count == 0) return 0;
        // Filter NaN values that ML.NET sometimes emits when a fold has zero positives/negatives.
        var values = folds.Select(selector).Where(v => !double.IsNaN(v)).ToList();
        return values.Count == 0 ? 0 : values.Average();
    }

    private static double SampleStdDev(IReadOnlyList<CvFoldMetric> folds, Func<CvFoldMetric, double> selector)
    {
        var values = folds.Select(selector).Where(v => !double.IsNaN(v)).ToList();
        if (values.Count < 2) return 0;
        var mean = values.Average();
        var sumSquaredDeviations = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSquaredDeviations / (values.Count - 1));
    }
}

public class LabelOutput
{
    public bool Label { get; set; }
}

// Tiny intermediate row shapes for the class-balance CustomMapping. The mapping
// reads only Label and writes only ExampleWeight; ML.NET copies the rest through.
public class LabelInputForWeight
{
    public bool Label { get; set; }
}

public class WeightOutput
{
    public float ExampleWeight { get; set; }
}
