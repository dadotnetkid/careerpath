using Microsoft.ML.Data;

namespace CareerPath.ML;

// CSV schema: data/alumni_gts3.csv
// Column order MUST match the CSV header (LoadColumn indices are 0-based).
//
// 0  YearGraduated     int
// 1  Gwa               string  ("Outstanding" | "Very Satisfactory" | "Satisfactory" | "Fair")
// 2  Honors            string  ("No Honors" | "Cum Laude" | "Magna Cum Laude" | "Summa Cum Laude")
// 3  Competencies      string  (free text)
// 4  EmploymentStatus  string  ← LEAKAGE: not a feature
// 5  CareerAlignment   string  ← LEAKAGE: source of binary label, never a feature
// 6  JobRetention      string  ← LEAKAGE: not a feature
// 7  CareerClarity     string  ("Low" | "Medium" | "High")
// 8  RiasecDominant    string  ← redundant with Interest, not a feature
// 9  Interest          string  (RIASEC code triplet, e.g. "CSE")
// 10 FieldOfIndustry   string  ← LEAKAGE
// 11 JobLevel          string  ← LEAKAGE
// 12 JobTitle          string  ← LEAKAGE
// 13 CareerOutcome     string  ← derived binary label written by data-cleaning step
public class AlumniCsvRow
{
    [LoadColumn(0)] public float YearGraduated { get; set; }
    [LoadColumn(1)] public string Gwa { get; set; } = "";
    [LoadColumn(2)] public string Honors { get; set; } = "";
    [LoadColumn(3)] public string Competencies { get; set; } = "";
    [LoadColumn(4)] public string EmploymentStatus { get; set; } = "";
    [LoadColumn(5)] public string CareerAlignment { get; set; } = "";
    [LoadColumn(6)] public string JobRetention { get; set; } = "";
    [LoadColumn(7)] public string CareerClarity { get; set; } = "";
    [LoadColumn(8)] public string RiasecDominant { get; set; } = "";
    [LoadColumn(9)] public string Interest { get; set; } = "";
    [LoadColumn(10)] public string FieldOfIndustry { get; set; } = "";
    [LoadColumn(11)] public string JobLevel { get; set; } = "";
    [LoadColumn(12)] public string JobTitle { get; set; } = "";
    [LoadColumn(13)] public string CareerOutcome { get; set; } = "";
}

// Feature schema fed to the binary classifier at training and inference time.
// PredictionService must populate this from StudentInput; CareerPathTrainer
// pipelines this directly.
public class StudentFeatures
{
    public float YearGraduated { get; set; }
    public string Gwa { get; set; } = "";
    public string Honors { get; set; } = "";
    public string Competencies { get; set; } = "";
    public string CareerClarity { get; set; } = "";
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
//
// Predicts the dominant RIASEC dimension (R/I/A/S/E/C) from profile features.
// Reads the `RiasecDominant` column directly from alumni_gts3.csv (full
// dimension name like "Conventional", which is mapped to a single letter).
public class RiasecFeatures
{
    public float YearGraduated { get; set; }
    public string Gwa { get; set; } = "";
    public string Honors { get; set; } = "";
    public string Competencies { get; set; } = "";
    public string CareerClarity { get; set; } = "";
    public string Label { get; set; } = "";  // R/I/A/S/E/C (single uppercase letter)
}

public class RiasecPrediction
{
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = "";

    [ColumnName("Score")]
    public float[] Score { get; set; } = Array.Empty<float>();
}
