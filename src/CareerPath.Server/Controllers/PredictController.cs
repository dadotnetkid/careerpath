using Microsoft.AspNetCore.Mvc;
using CareerPath.Server.Data;
using CareerPath.Server.Services;
using CareerPath.Shared;

namespace CareerPath.Server.Controllers;

[ApiController]
[Route("api/predict")]
public class PredictController : ControllerBase
{
    private readonly PredictionService _predictionService;
    private readonly DatasetService _datasetService;
    private readonly RiasecService _riasecService;
    private readonly AppDbContext _db;

    public PredictController(
        PredictionService predictionService,
        DatasetService datasetService,
        RiasecService riasecService,
        AppDbContext db)
    {
        _predictionService = predictionService;
        _datasetService = datasetService;
        _riasecService = riasecService;
        _db = db;
    }

    [HttpPost]
    public async Task<ActionResult<PredictionResult>> Predict([FromBody] StudentInput input)
    {
        if (!_predictionService.IsReady)
            return StatusCode(503, new { error = "Model not loaded. Run the ML trainer first." });

        var result = _predictionService.Predict(input);

        // SimilarAlumniJobs is a top-5 slice of the SAME RIASEC-scored job-match list
        // that the wizard's "Top Matched Job Roles" table shows. Single source of truth:
        // the two views can never disagree (GH issue #1, follow-up).
        var skills = string.IsNullOrWhiteSpace(input.Competencies)
            ? new List<string>()
            : input.Competencies.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        var matches = _riasecService.MatchJobs(input.Interest, input.Gwa, skills);
        result.SimilarAlumniJobs = matches.Take(5).Select(j => j.JobTitle).ToList();

        // Log the prediction (new schema — no Course, Gwa is string)
        _db.PredictionLogs.Add(new PredictionLog
        {
            YearGraduated = input.YearGraduated,
            Gwa = input.Gwa,
            Honors = input.Honors,
            Competencies = input.Competencies,
            CareerClarity = input.CareerClarity,
            Interest = input.Interest,
            PredictedOutcome = result.PredictedOutcome,
            Confidence = result.Confidence,
            PredictedAt = result.PredictedAt
        });
        await _db.SaveChangesAsync();

        return Ok(result);
    }

    [HttpGet("status")]
    public ActionResult<ModelStatus> GetStatus()
    {
        return Ok(new ModelStatus { Ready = _predictionService.IsReady });
    }
}
