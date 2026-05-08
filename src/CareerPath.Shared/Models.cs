using System.ComponentModel.DataAnnotations;

namespace CareerPath.Shared;

public class StudentInput
{
    [Required]
    public string Course { get; set; } = "BSIT";

    [Range(2015, 2035)]
    public int YearGraduated { get; set; } = DateTime.Now.Year;

    [Required]
    [Range(1.00, 5.00)]
    public float Gwa { get; set; }

    [Required]
    public string Competencies { get; set; } = "";

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
    public Dictionary<string, int> ByCourse { get; set; } = new();
    public Dictionary<string, int> ByCareerOutcome { get; set; } = new();
    public Dictionary<string, int> ByEmploymentStatus { get; set; } = new();
    public Dictionary<int, int> ByYear { get; set; } = new();
    public double GwaAverage { get; set; }
    public double GwaMin { get; set; }
    public double GwaMax { get; set; }
}

public class AlumniRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int YearGraduated { get; set; }
    public string Course { get; set; } = "";
    public float Gwa { get; set; }
    public string JobPosition { get; set; } = "";
    public string FieldOfIndustry { get; set; } = "";
    public string Competencies { get; set; } = "";
    public string EmploymentStatus { get; set; } = "";
    public string CareerAlignment { get; set; } = "";
    public string Interest { get; set; } = "";
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

    /// <summary>Step 3: Selected competencies/skills</summary>
    public List<string> Skills { get; set; } = new();
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

    // AI-generated detailed analysis
    public string AiAnalysis { get; set; } = "";
}

public class JobMatch
{
    public string JobTitle { get; set; } = "";
    public string FieldOfIndustry { get; set; } = "";
    public string CareerAlignment { get; set; } = "";
    public int AlumniCount { get; set; }
    public double MatchScore { get; set; }  // 0-100
}
