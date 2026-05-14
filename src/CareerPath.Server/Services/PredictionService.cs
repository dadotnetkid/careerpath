using Microsoft.ML;
using CareerPath.ML;
using CareerPath.Shared;

namespace CareerPath.Server.Services;

public class PredictionService
{
    private PredictionEngine<StudentFeatures, CareerOutcomePrediction>? _engine;
    private readonly object _lock = new();
    public bool IsReady => _engine != null;

    public PredictionService(string modelPath)
    {
        Reload(modelPath);
    }

    /// <summary>
    /// Swap in a freshly-trained model without restarting the host. Called by
    /// <c>ModelTrainingService</c> after a successful retrain.
    /// </summary>
    public void Reload(string modelPath)
    {
        if (!File.Exists(modelPath)) return;
        var trainer = new CareerPathTrainer();
        var fresh = trainer.LoadPredictionEngine(modelPath);
        lock (_lock)
        {
            _engine = fresh;
        }
    }

    public PredictionResult Predict(StudentInput input)
    {
        if (_engine == null)
            throw new InvalidOperationException("Model not loaded. Run the ML trainer first.");

        var features = new StudentFeatures
        {
            YearGraduated = input.YearGraduated,
            Gwa = input.Gwa,
            Honors = input.Honors,
            Competencies = input.Competencies,
            CareerClarity = input.CareerClarity,
            Interest = input.Interest,
            Label = false  // ignored at inference
        };

        CareerOutcomePrediction prediction;
        lock (_lock)
        {
            prediction = _engine.Predict(features);
        }

        var predictedOutcome = prediction.PredictedLabel ? "ICT-Aligned" : "Non-ICT-Aligned";
        var confidence = prediction.Probability;
        if (!prediction.PredictedLabel)
            confidence = 1f - confidence;

        var ictProb = prediction.Probability;
        var nonIctProb = 1f - prediction.Probability;

        var interpretation = GenerateInterpretation(input, predictedOutcome, confidence);

        return new PredictionResult
        {
            PredictedOutcome = predictedOutcome,
            Confidence = confidence,
            Scores = new List<ClassScore>
            {
                new() { Label = "ICT-Aligned", Probability = ictProb },
                new() { Label = "Non-ICT-Aligned", Probability = nonIctProb }
            },
            Interpretation = interpretation,
            SimilarAlumniJobs = new List<string>(),
            PredictedAt = DateTime.UtcNow
        };
    }

    private static string GenerateInterpretation(StudentInput input, string outcome, float confidence)
    {
        var pct = (confidence * 100).ToString("F1");
        var competencies = string.IsNullOrWhiteSpace(input.Competencies) ? "your competencies" : input.Competencies;
        var gwa = string.IsNullOrWhiteSpace(input.Gwa) ? "your GWA category" : input.Gwa;
        var riasec = string.IsNullOrWhiteSpace(input.Interest) ? "your RIASEC code" : input.Interest;

        if (outcome == "ICT-Aligned")
        {
            return $"Based on the analysis of your profile, the model predicts with {pct}% confidence " +
                   $"that your career path is likely to be ICT-Aligned. " +
                   $"Alumni with a similar profile - competencies in {competencies}, GWA category \"{gwa}\", " +
                   $"and RIASEC code {riasec} - have historically pursued careers in the ICT industry. " +
                   $"This suggests your skills and interests align well with information and communications technology roles.";
        }
        else
        {
            return $"Based on the analysis of your profile, the model predicts with {pct}% confidence " +
                   $"that your career path may lead to a Non-ICT-Aligned field. " +
                   $"This is not a negative outcome; many graduates with competencies in {competencies} " +
                   $"and RIASEC code {riasec} successfully apply their skills across diverse industries. " +
                   $"Consider exploring how a GWA category of \"{gwa}\" combined with your interests can be leveraged in different sectors.";
        }
    }
}
