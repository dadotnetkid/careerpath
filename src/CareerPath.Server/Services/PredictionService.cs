using Microsoft.ML;
using CareerPath.ML;
using CareerPath.Shared;

namespace CareerPath.Server.Services;

public class PredictionService
{
    private readonly PredictionEngine<StudentFeatures, CareerOutcomePrediction>? _engine;
    private readonly object _lock = new();
    public bool IsReady => _engine != null;

    public PredictionService(string modelPath)
    {
        if (File.Exists(modelPath))
        {
            var trainer = new CareerPathTrainer();
            _engine = trainer.LoadPredictionEngine(modelPath);
        }
    }

    public PredictionResult Predict(StudentInput input)
    {
        if (_engine == null)
            throw new InvalidOperationException("Model not loaded. Run the ML trainer first.");

        var features = new StudentFeatures
        {
            YearGraduated = input.YearGraduated,
            Course = input.Course,
            Gwa = input.Gwa,
            Competencies = input.Competencies,
            Interest = input.Interest
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

        if (outcome == "ICT-Aligned")
        {
            return $"Based on the analysis of your profile, the model predicts with {pct}% confidence " +
                   $"that your career path is likely to be ICT-Aligned. " +
                   $"Students from {input.Course} with similar academic performance (GWA: {input.Gwa:F2}) " +
                   $"and interests have historically pursued careers in the ICT industry. " +
                   $"This suggests your skills and interests align well with information and communications technology roles.";
        }
        else
        {
            return $"Based on the analysis of your profile, the model predicts with {pct}% confidence " +
                   $"that your career path may lead to a Non-ICT-Aligned field. " +
                   $"This does not indicate a negative outcome; many {input.Course} graduates " +
                   $"successfully apply their technical skills in diverse industries. " +
                   $"Consider exploring how your competencies can be leveraged across different sectors.";
        }
    }
}
