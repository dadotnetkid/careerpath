using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using CareerPath.Server.Data;
using CareerPath.Server.Services;
using CareerPath.Shared;

namespace CareerPath.Server.Controllers;

[ApiController]
[Route("api/riasec")]
public class RiasecController : ControllerBase
{
    private readonly RiasecService _riasecService;
    private readonly PredictionService _predictionService;
    private readonly DatasetService _datasetService;
    private readonly AppDbContext _db;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ILogger<RiasecController> _logger;

    public RiasecController(
        RiasecService riasecService,
        PredictionService predictionService,
        DatasetService datasetService,
        AppDbContext db,
        UserManager<IdentityUser> userManager,
        ILogger<RiasecController> logger)
    {
        _riasecService = riasecService;
        _predictionService = predictionService;
        _datasetService = datasetService;
        _db = db;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>Get the 30 O*NET Interest Profiler work-activity items</summary>
    [HttpGet("questions")]
    public ActionResult<List<RiasecQuestion>> GetQuestions()
    {
        return Ok(_riasecService.GetQuestions());
    }

    /// <summary>Get available skills/competencies for the dropdown</summary>
    [HttpGet("skills")]
    public ActionResult<List<string>> GetSkills()
    {
        return Ok(_riasecService.GetSkillOptions());
    }

    /// <summary>Get suggested roles for Career Clarity step based on RIASEC code</summary>
    [HttpGet("roles/{dominantCode}")]
    public ActionResult<List<string>> GetSuggestedRoles(string dominantCode)
    {
        if (string.IsNullOrEmpty(dominantCode) || dominantCode.Length < 1)
            return BadRequest(new { error = "Invalid RIASEC code." });

        return Ok(_riasecService.GetSuggestedRoles(dominantCode));
    }

    /// <summary>Full assessment — processes all steps and returns final report</summary>
    [HttpPost("assess")]
    public async Task<ActionResult<FullAssessmentResult>> Assess([FromBody] FullAssessmentInput input)
    {
        // Validate RIASEC answers
        if (input.RiasecAnswers == null || input.RiasecAnswers.Count == 0)
            return BadRequest(new { error = "No RIASEC answers provided." });

        var questions = _riasecService.GetQuestions();
        var validIds = questions.Select(q => q.Id).ToHashSet();

        if (input.RiasecAnswers.Keys.Any(id => !validIds.Contains(id)))
            return BadRequest(new { error = "Unknown Interest Profiler question id in answers." });

        if (input.RiasecAnswers.Values.Any(v => v < RiasecScale.Min || v > RiasecScale.Max))
            return BadRequest(new { error = $"Answer ratings must be between {RiasecScale.Min} and {RiasecScale.Max}." });

        if (input.RiasecAnswers.Count != questions.Count)
            return BadRequest(new { error = $"All {questions.Count} Interest Profiler items must be answered." });

        // Step 1: Compute RIASEC scores & dominant code
        var riasecScores = _riasecService.ComputeRiasecScores(input.RiasecAnswers);
        var (dominantCode, topDimensions) = _riasecService.GetDominantCode(riasecScores);

        // Step 2: Compute Career Clarity
        var clarity = _riasecService.ComputeCareerClarity(input.CareerClarity, dominantCode);

        // Step 3+4: Run matching algorithm
        var matchedJobs = _riasecService.MatchJobs(dominantCode, input.GwaCategory, input.Skills);

        var result = new FullAssessmentResult
        {
            RiasecScores = riasecScores,
            DominantCode = dominantCode,
            TopDimensions = topDimensions,
            Clarity = clarity,
            MatchedJobs = matchedJobs
        };

        // Step 5a: Optionally run the binary classifier so the AI analysis can be
        // seeded with the prediction outcome (GH issue #1, item 8 — keeps the two
        // narratives consistent). The new wizard's Results step always sets this true.
        if (input.IncludePrediction && _predictionService.IsReady)
        {
            try
            {
                var studentInput = new StudentInput
                {
                    YearGraduated = DateTime.Now.Year,
                    Gwa = input.GwaCategory,
                    Honors = input.Honors,
                    Competencies = string.Join(", ", input.Skills),
                    CareerClarity = clarity.ClarityBucket,
                    Interest = dominantCode
                };
                var prediction = _predictionService.Predict(studentInput);
                // "Alumni with similar profiles became" is a top-5 slice of the SAME
                // matched-jobs list shown in the Top Matched Job Roles table — one
                // source of truth, no contradiction between the two views.
                prediction.SimilarAlumniJobs = matchedJobs
                    .Take(5)
                    .Select(j => j.JobTitle)
                    .ToList();
                result.Prediction = prediction;
            }
            catch
            {
                // Prediction is best-effort here — if the model isn't loaded the wizard
                // can still show RIASEC + Clarity + Matched Jobs + AI Analysis.
            }
        }

        // Step 5b: Generate AI analysis. When a prediction is attached it's passed
        // into the prompt so the narrative aligns with the binary classifier.
        result.AiAnalysis = await _riasecService.GenerateAiAnalysis(result, input);

        await RecordAssessment(input, result);

        return Ok(result);
    }

    /// <summary>
    /// Stores the completed assessment as an <see cref="AssessmentRecord"/> so the
    /// admin can export it from the Assessment Records page. Best-effort: a DB
    /// failure is logged but never costs the student their report.
    /// </summary>
    private async Task RecordAssessment(FullAssessmentInput input, FullAssessmentResult result)
    {
        try
        {
            var respondent = User.IsInRole(AuthRoles.Student) ? AuthRoles.Student
                : User.IsInRole(AuthRoles.Admin) ? AuthRoles.Admin
                : "Guest";
            var topMatch = result.MatchedJobs.FirstOrDefault();

            _db.AssessmentRecords.Add(new AssessmentRecord
            {
                CreatedAt = DateTime.UtcNow,
                UserId = _userManager.GetUserId(User) ?? "",
                Respondent = respondent,
                Gwa = input.GwaCategory ?? "",
                Honors = input.Honors ?? "",
                Competencies = string.Join(", ", input.Skills ?? new List<string>()),
                RiasecDominant = result.TopDimensions.FirstOrDefault() ?? "",
                Interest = result.DominantCode,
                RealisticScore = result.RiasecScores.Realistic,
                InvestigativeScore = result.RiasecScores.Investigative,
                ArtisticScore = result.RiasecScores.Artistic,
                SocialScore = result.RiasecScores.Social,
                EnterprisingScore = result.RiasecScores.Enterprising,
                ConventionalScore = result.RiasecScores.Conventional,
                CareerClarity = result.Clarity.ClarityBucket,
                ClarityLabel = result.Clarity.ClarityLabel,
                ClarityScore = result.Clarity.TotalScore,
                DesiredRoles = string.Join("; ", (input.CareerClarity?.DesiredRoles ?? new List<string>())
                    .Where(r => !string.IsNullOrWhiteSpace(r))),
                PredictedOutcome = result.Prediction?.PredictedOutcome ?? "",
                PredictionConfidence = result.Prediction?.Confidence ?? 0,
                SuggestedCareer = topMatch?.JobTitle ?? "",
                SuggestedCareerAlignment = topMatch?.CareerAlignment ?? "",
                SuggestedFieldOfIndustry = topMatch?.FieldOfIndustry ?? "",
                SuggestedCareers = string.Join("; ", result.MatchedJobs.Take(5).Select(j => j.JobTitle))
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record completed assessment.");
        }
    }
}
