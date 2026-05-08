using Microsoft.ML.Data;

namespace CareerPath.ML;

public class AlumniCsvRow
{
    [LoadColumn(0)] public string Id { get; set; } = "";
    [LoadColumn(1)] public string Name { get; set; } = "";
    [LoadColumn(2)] public float YearGraduated { get; set; }
    [LoadColumn(3)] public string Course { get; set; } = "";
    [LoadColumn(4)] public float Gwa { get; set; }
    [LoadColumn(5)] public string JobPosition { get; set; } = "";
    [LoadColumn(6)] public string FieldOfIndustry { get; set; } = "";
    [LoadColumn(7)] public string Competencies { get; set; } = "";
    [LoadColumn(8)] public string EmploymentStatus { get; set; } = "";
    [LoadColumn(9)] public string CareerAlignment { get; set; } = "";
    [LoadColumn(10)] public string JobRetention { get; set; } = "";
    [LoadColumn(11)] public string Interest { get; set; } = "";
    [LoadColumn(12)] public string CareerOutcome { get; set; } = "";
}

public class StudentFeatures
{
    public float YearGraduated { get; set; }
    public string Course { get; set; } = "";
    public float Gwa { get; set; }
    public string Competencies { get; set; } = "";
    public string Interest { get; set; } = "";
    public bool Label { get; set; }
}

public class CareerOutcomePrediction
{
    [ColumnName("PredictedLabel")]
    public bool PredictedLabel { get; set; }

    [ColumnName("Score")]
    public float Score { get; set; }

    [ColumnName("Probability")]
    public float Probability { get; set; }
}

// ── Multiclass RIASEC Prediction ──

public class RiasecFeatures
{
    public float Gwa { get; set; }
    public float YearGraduated { get; set; }
    public string Course { get; set; } = "";
    public string Competencies { get; set; } = "";
    public string CareerAlignment { get; set; } = "";
    public string FieldOfIndustry { get; set; } = "";
    public string EmploymentStatus { get; set; } = "";
    public string Label { get; set; } = "";  // Dominant RIASEC letter: R, I, A, S, E, C
}

public class RiasecPrediction
{
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = "";

    [ColumnName("Score")]
    public float[] Score { get; set; } = Array.Empty<float>();
}
