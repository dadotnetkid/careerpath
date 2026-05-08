using Microsoft.AspNetCore.Mvc;
using CareerPath.Server.Services;
using CareerPath.Shared;

namespace CareerPath.Server.Controllers;

[ApiController]
[Route("api/riasec")]
public class RiasecController : ControllerBase
{
    private readonly RiasecService _riasecService;

    public RiasecController(RiasecService riasecService)
    {
        _riasecService = riasecService;
    }

    /// <summary>Get the 10 multiple-choice RIASEC questions</summary>
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

        var validKeys = new HashSet<string> { "R", "I", "A", "S", "E", "C" };
        if (input.RiasecAnswers.Values.Any(v => !validKeys.Contains(v)))
            return BadRequest(new { error = "Invalid RIASEC key in answers." });

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

        // Step 5: Generate AI analysis
        result.AiAnalysis = await _riasecService.GenerateAiAnalysis(result, input);

        return Ok(result);
    }
}
