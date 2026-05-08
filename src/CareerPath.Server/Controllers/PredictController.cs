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
    private readonly AppDbContext _db;

    public PredictController(PredictionService predictionService, DatasetService datasetService, AppDbContext db)
    {
        _predictionService = predictionService;
        _datasetService = datasetService;
        _db = db;
    }

    [HttpPost]
    public async Task<ActionResult<PredictionResult>> Predict([FromBody] StudentInput input)
    {
        if (!_predictionService.IsReady)
            return StatusCode(503, new { error = "Model not loaded. Run the ML trainer first." });

        var result = _predictionService.Predict(input);

        // Find similar alumni jobs
        result.SimilarAlumniJobs = _datasetService.FindSimilarAlumniJobs(
            input.Course, input.Gwa, input.Competencies, input.Interest, result.PredictedOutcome);

        // Log the prediction
        _db.PredictionLogs.Add(new PredictionLog
        {
            Course = input.Course,
            YearGraduated = input.YearGraduated,
            Gwa = input.Gwa,
            Competencies = input.Competencies,
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
