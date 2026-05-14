using System.ComponentModel.DataAnnotations;

namespace CareerPath.Shared;

// ── Binary classifier input ──
//
// FEATURE SET (locked 2026-05 against data/alumni_gts3.csv):
//   1. YearGraduated   numeric
//   2. Gwa             categorical: "Outstanding" | "Very Satisfactory" | "Satisfactory" | "Fair"
//   3. Honors          categorical: "No Honors" | "Cum Laude" | "Magna Cum Laude" | "Summa Cum Laude"
//   4. Competencies    text (TF-IDF)
//   5. CareerClarity   categorical: "Low" | "Medium" | "High"
//   6. Interest        text (RIASEC code triplet, TF-IDF) — e.g. "CSE", "RIC"
//
// Course (BSIT/BSCS/BSIS) was REMOVED — not collected in GTS 3.
// Leakage columns (NEVER features): CareerAlignment, JobTitle, FieldOfIndustry,
// EmploymentStatus, JobRetention, JobLevel.
public class StudentInput
{
    [Range(2015, 2035)]
    public int YearGraduated { get; set; } = DateTime.Now.Year;

    [Required]
    public string Gwa { get; set; } = "";

    [Required]
    public string Honors { get; set; } = "No Honors";

    [Required]
    public string Competencies { get; set; } = "";

    [Required]
    public string CareerClarity { get; set; } = "";

    [Required]
    public string Interest { get; set; } = "";
}

public class PredictionResult
{
    public string PredictedOutcome { get; set; } = "";
    public float Confidence { get; set; }
    public List<ClassScore> Scores { get; set; } = new();
    public string Interpretation { get; set; } = "";
    public List<string> SimilarAlumniJobs { get; set; } = new();
    public DateTime PredictedAt { get; set; }
}

public class ClassScore
{
    public string Label { get; set; } = "";
    public float Probability { get; set; }
}

public class ModelMetrics
{
    public string AlgorithmName { get; set; } = "";
    public double Accuracy { get; set; }
    public double Precision { get; set; }
    public double Recall { get; set; }
    public double F1Score { get; set; }
    public double AreaUnderRocCurve { get; set; }
    public int TrainingRows { get; set; }
    public int TestRows { get; set; }
    public DateTime TrainedAt { get; set; }
    public int[][] ConfusionMatrix { get; set; } = Array.Empty<int[]>();
    public List<string> ClassLabels { get; set; } = new() { "ICT-Aligned", "Non-ICT-Aligned" };
    public List<FeatureImportance> FeatureImportances { get; set; } = new();
}

public class FeatureImportance
{
    public string FeatureName { get; set; } = "";
    public double Importance { get; set; }
}

public class ModelComparison
{
    public ModelMetrics RandomForest { get; set; } = new();
    public ModelMetrics DecisionTree { get; set; } = new();
    public ModelMetrics LogisticRegression { get; set; } = new();
}

public class DatasetSummary
{
    public int TotalRecords { get; set; }
    public Dictionary<string, int> ByGwa { get; set; } = new();
    public Dictionary<string, int> ByHonors { get; set; } = new();
    public Dictionary<string, int> ByCareerOutcome { get; set; } = new();

    /// <summary>4-class breakdown: "Core ICT" / "ICT-Adjacent" / "Non-ICT but Adjacent" / "Non-ICT".</summary>
    public Dictionary<string, int> ByCareerAlignment { get; set; } = new();

    public Dictionary<string, int> ByCareerClarity { get; set; } = new();
    public Dictionary<string, int> ByEmploymentStatus { get; set; } = new();
    public Dictionary<string, int> ByRiasecDominant { get; set; } = new();
    public Dictionary<int, int> ByYear { get; set; } = new();

    /// <summary>
    /// Cross-tab: outer key = RiasecDominant full name (Conventional/Investigative/...),
    /// inner key = FieldOfIndustry, value = count. Used by Insights to show that
    /// RIASEC dimensions map to industries the dataset reflects (sanity check).
    /// </summary>
    public Dictionary<string, Dictionary<string, int>> ByFieldIndustryByRiasec { get; set; } = new();
}

// ── Cross-validation metrics (10-fold, 20-fold) ──
//
// Written to MLModel/cv_metrics.json by the trainer; ingested into the SQLite
// DB on server startup; surfaced in the Insights "CV Stability" section.

public class CrossValidationReport
{
    /// <summary>e.g. "alumni_gts3.csv@2026-05-08"</summary>
    public string DatasetVersion { get; set; } = "";
    public DateTime TrainedAt { get; set; }
    public List<CvAlgorithmRun> Runs { get; set; } = new();
}

public class CvAlgorithmRun
{
    public string Algorithm { get; set; } = "";   // "Random Forest" / "Decision Tree" / "Logistic Regression"
    public int FoldCount { get; set; }            // 10 or 20
    public double MeanAccuracy { get; set; }
    public double StdAccuracy { get; set; }
    public double MeanPrecision { get; set; }
    public double StdPrecision { get; set; }
    public double MeanRecall { get; set; }
    public double StdRecall { get; set; }
    public double MeanF1 { get; set; }
    public double StdF1 { get; set; }
    public double MeanAuc { get; set; }
    public double StdAuc { get; set; }
    public List<CvFoldMetric> Folds { get; set; } = new();
}

public class CvFoldMetric
{
    public int FoldIndex { get; set; }
    public double Accuracy { get; set; }
    public double Precision { get; set; }
    public double Recall { get; set; }
    public double F1 { get; set; }
    public double Auc { get; set; }
}

// ── Diagnostic confusion matrices ──
//
// The canonical model is binary (ICT-Aligned vs Non-ICT-Aligned). For
// transparency the Insights dashboard also shows two diagnostic views:
//
//   1. 4x2: rows = actual 4-class CareerAlignment, columns = predicted
//      binary outcome from the canonical model. Computed by running the
//      binary RF over all 301 alumni and tabulating by actual sub-class.
//
//   2. 4x4: rows = actual 4-class CareerAlignment, columns = predicted
//      4-class CareerAlignment. Requires a SEPARATE multiclass model
//      (model_4class.zip) trained alongside the binary one for diagnostic
//      purposes. Evaluated on the same 80/20 test fold (seed=42).
//
// The 4-class "Non-ICT" group has only 6 rows (~1 in the test fold), so
// the 4x4 is exploratory. The Insights page must surface this caveat.

public class DiagnosticConfusionMatrices
{
    public DateTime ComputedAt { get; set; }

    public ConfusionMatrix4x2 BinaryByActual4Class { get; set; } = new();

    public ConfusionMatrix4x4 Multiclass4x4 { get; set; } = new();

    /// <summary>4-class accuracy of the diagnostic multiclass model on the test fold.</summary>
    public double Multiclass4ClassAccuracy { get; set; }

    /// <summary>Macro-averaged F1 across the 4 classes.</summary>
    public double Multiclass4ClassMacroF1 { get; set; }

    public int Multiclass4ClassTrainRows { get; set; }
    public int Multiclass4ClassTestRows { get; set; }
}

public class ConfusionMatrix4x2
{
    /// <summary>4 actual labels, in this canonical order: Core ICT, ICT-Adjacent, Non-ICT but Adjacent, Non-ICT.</summary>
    public List<string> ActualLabels { get; set; } = new();

    /// <summary>2 predicted labels: ICT-Aligned, Non-ICT-Aligned.</summary>
    public List<string> PredictedLabels { get; set; } = new();

    /// <summary>[4][2] count matrix.</summary>
    public int[][] Counts { get; set; } = Array.Empty<int[]>();

    /// <summary>Row totals (n per actual class) for percentage display.</summary>
    public int[] RowTotals { get; set; } = Array.Empty<int>();
}

public class ConfusionMatrix4x4
{
    /// <summary>4 labels, in this canonical order: Core ICT, ICT-Adjacent, Non-ICT but Adjacent, Non-ICT.</summary>
    public List<string> Labels { get; set; } = new();

    /// <summary>[4][4] count matrix. Rows = actual, columns = predicted.</summary>
    public int[][] Counts { get; set; } = Array.Empty<int[]>();

    public int[] RowTotals { get; set; } = Array.Empty<int>();
}

// API response shape — same as the DB row plus the fold breakout.
public class TrainingRunDto
{
    public int Id { get; set; }
    public string Algorithm { get; set; } = "";
    public int FoldCount { get; set; }
    public double MeanAccuracy { get; set; }
    public double StdAccuracy { get; set; }
    public double MeanPrecision { get; set; }
    public double StdPrecision { get; set; }
    public double MeanRecall { get; set; }
    public double StdRecall { get; set; }
    public double MeanF1 { get; set; }
    public double StdF1 { get; set; }
    public double MeanAuc { get; set; }
    public double StdAuc { get; set; }
    public DateTime TrainedAt { get; set; }
    public string DatasetVersion { get; set; } = "";
    public List<CvFoldMetric> Folds { get; set; } = new();
}

public class AlumniRecord
{
    public int YearGraduated { get; set; }
    public string Gwa { get; set; } = "";
    public string Honors { get; set; } = "";
    public string Competencies { get; set; } = "";
    public string EmploymentStatus { get; set; } = "";
    public string CareerAlignment { get; set; } = "";
    public string JobRetention { get; set; } = "";
    public string CareerClarity { get; set; } = "";
    public string RiasecDominant { get; set; } = "";
    public string Interest { get; set; } = "";
    public string FieldOfIndustry { get; set; } = "";
    public string JobLevel { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string CareerOutcome { get; set; } = "";
}

public class PagedResult<T>
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<T> Items { get; set; } = new();
}

public class ModelStatus
{
    public bool Ready { get; set; }
}

// ── Auth ──
//
// Cookie-based auth. Two roles: "Admin" (researcher — full access to
// Dataset / Train & Test / Insights) and "Student" (CITE student — completes
// the career-path wizard, can save their own results). Both kinds of user
// live in AspNetUsers; the role lives in AspNetUserRoles. Admin is seeded
// from configuration on first startup; students self-register via POST
// /api/auth/register.

public static class AuthRoles
{
    public const string Admin = "Admin";
    public const string Student = "Student";
}

public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";

    /// <summary>If true, the auth cookie persists across browser restarts.</summary>
    public bool RememberMe { get; set; } = false;
}

public class RegisterRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, MinLength(8)]
    public string Password { get; set; } = "";

    /// <summary>Display name (optional). Used in the top bar; falls back to email.</summary>
    public string FullName { get; set; } = "";
}

public class AuthStatusDto
{
    public bool IsAuthenticated { get; set; }
    public string Email { get; set; } = "";

    /// <summary>"Admin" or "Student" when authenticated; empty when anonymous.</summary>
    public string Role { get; set; } = "";

    /// <summary>Display name when set on the user record; falls back to email on the client.</summary>
    public string FullName { get; set; } = "";
}

// ── RIASEC + Career Clarity Workflow ──

public class RiasecQuestion
{
    public int Id { get; set; }
    public string Scenario { get; set; } = "";
    public List<RiasecOption> Options { get; set; } = new();
}

public class RiasecOption
{
    public string Key { get; set; } = "";          // "R", "I", "A", "S", "E", "C"
    public string Label { get; set; } = "";         // e.g. "Realistic"
    public string Text { get; set; } = "";          // The answer text
}

public class RiasecScores
{
    public int Realistic { get; set; }
    public int Investigative { get; set; }
    public int Artistic { get; set; }
    public int Social { get; set; }
    public int Enterprising { get; set; }
    public int Conventional { get; set; }
}

// ── Career Clarity (from PDF spec) ──

public class CareerClarityInput
{
    /// <summary>3 roles the student is considering</summary>
    public List<string> DesiredRoles { get; set; } = new();

    /// <summary>A. Career Fit Rating per role (1-5)</summary>
    public List<int> FitRatings { get; set; } = new();

    /// <summary>B. Growth & Learning Excitement per role (1-5)</summary>
    public List<int> ExcitementRatings { get; set; } = new();

    /// <summary>C1. Genuine interest in career work (1-5)</summary>
    public int GenuineInterest { get; set; }

    /// <summary>C2. Career is popular/high-paying/in demand (1-5)</summary>
    public int PopularityMotivation { get; set; }

    /// <summary>C3. Career is expected by family (1-5)</summary>
    public int FamilyExpectation { get; set; }

    /// <summary>D. Salary-neutral choice — which role index (0-2) from DesiredRoles</summary>
    public int SalaryNeutralChoice { get; set; }
}

public class CareerClarityResult
{
    public double FitScore { get; set; }              // A avg (max 5)
    public double ExcitementScore { get; set; }       // B avg (max 5)
    public double GenuineInterestScore { get; set; }  // C1 (max 5)
    public double SalaryNeutralScore { get; set; }    // D: 5 if same as top 1, else 1
    public double RiasecAlignmentScore { get; set; }  // E: 5 if aligned, else 1
    public double TotalScore { get; set; }            // A+B+C+D+E (max 25)
    public string ClarityLabel { get; set; } = "";    // "Very High", "High", "Moderate", "Low", "Highly Uncertain"

    /// <summary>Discrete bucket for ML feature: "Low" | "Medium" | "High". Derived from TotalScore.</summary>
    public string ClarityBucket { get; set; } = "";
}

// ── Full Assessment Input (all steps combined) ──

public class FullAssessmentInput
{
    /// <summary>Step 1: RIASEC answers — QuestionId → chosen key ("R","I","A","S","E","C")</summary>
    public Dictionary<int, string> RiasecAnswers { get; set; } = new();

    /// <summary>Step 2: Career Clarity quiz data</summary>
    public CareerClarityInput CareerClarity { get; set; } = new();

    /// <summary>Step 3: GWA category</summary>
    public string GwaCategory { get; set; } = "";  // "Outstanding", "Very Satisfactory", "Satisfactory", "Fair"

    /// <summary>Step 3: Honors category — "No Honors", "Cum Laude", "Magna Cum Laude", "Summa Cum Laude"</summary>
    public string Honors { get; set; } = "No Honors";

    /// <summary>Step 3: Selected competencies/skills</summary>
    public List<string> Skills { get; set; } = new();

    /// <summary>
    /// If true, the server also runs the binary classifier (/api/predict logic)
    /// and includes the outcome in <see cref="FullAssessmentResult.Prediction"/>.
    /// The AI analysis prompt is then seeded with the prediction so the two
    /// narratives agree. The new wizard's Results step always sets this true.
    /// </summary>
    public bool IncludePrediction { get; set; } = false;
}

// ── Full Assessment Result ──

public class FullAssessmentResult
{
    // RIASEC
    public RiasecScores RiasecScores { get; set; } = new();
    public string DominantCode { get; set; } = "";           // e.g. "RIC"
    public List<string> TopDimensions { get; set; } = new(); // Top 3 dimension names

    // Career Clarity
    public CareerClarityResult Clarity { get; set; } = new();

    // Matched Job Roles from alumni data
    public List<JobMatch> MatchedJobs { get; set; } = new();

    // AI-generated detailed analysis. With FullAssessmentInput.IncludePrediction = true,
    // the prompt is seeded with the binary prediction outcome so the AI narrative and
    // the prediction interpretation agree (GH issue #1, item 8).
    public string AiAnalysis { get; set; } = "";

    // Binary ML prediction (ICT-Aligned vs Non-ICT-Aligned). Populated when
    // FullAssessmentInput.IncludePrediction = true (the wizard's Results step
    // requests both in one round-trip).
    public PredictionResult? Prediction { get; set; }
}

public class JobMatch
{
    public string JobTitle { get; set; } = "";
    public string FieldOfIndustry { get; set; } = "";
    public string CareerAlignment { get; set; } = "";
    public int AlumniCount { get; set; }
    public double MatchScore { get; set; }  // 0-100
}

// ── Saved Results (student-facing persistence) ──
//
// A student can click "Save Result" on the wizard's Results step to persist a
// snapshot of their full assessment + binary prediction. Saved rows are visible
// only to the signed-in student (UserId scoped) and listed on /my-results.

public class SaveResultRequest
{
    // Inputs (so the saved row is self-contained and re-renderable)
    public string Gwa { get; set; } = "";
    public string Honors { get; set; } = "";
    public string Competencies { get; set; } = "";

    // Computed outputs
    public string DominantCode { get; set; } = "";
    public string ClarityLabel { get; set; } = "";
    public string ClarityBucket { get; set; } = "";
    public double ClarityTotal { get; set; }
    public string PredictedOutcome { get; set; } = "";
    public float PredictedConfidence { get; set; }
    public string AiAnalysis { get; set; } = "";

    // Optional student-supplied note
    public string Notes { get; set; } = "";
}

public class SavedResultDto
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Gwa { get; set; } = "";
    public string Honors { get; set; } = "";
    public string Competencies { get; set; } = "";
    public string DominantCode { get; set; } = "";
    public string ClarityLabel { get; set; } = "";
    public string ClarityBucket { get; set; } = "";
    public double ClarityTotal { get; set; }
    public string PredictedOutcome { get; set; } = "";
    public float PredictedConfidence { get; set; }
    public string AiAnalysis { get; set; } = "";
    public string Notes { get; set; } = "";
}
