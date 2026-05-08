using Microsoft.AspNetCore.Mvc;
using CareerPath.Server.Services;
using CareerPath.Shared;
using System.Text.Json;

namespace CareerPath.Server.Controllers;

[ApiController]
public class DatasetController : ControllerBase
{
    private readonly DatasetService _datasetService;
    private readonly string _metricsPath;

    public DatasetController(DatasetService datasetService, IConfiguration config)
    {
        _datasetService = datasetService;
        _metricsPath = config["MLModel:MetricsPath"] ?? "";
    }

    [HttpGet("api/dataset")]
    public ActionResult<PagedResult<AlumniRecord>> GetRecords(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? course = null,
        [FromQuery] string? search = null)
    {
        var result = _datasetService.GetRecords(page, pageSize, course, search);
        return Ok(result);
    }

    [HttpGet("api/dataset/summary")]
    public ActionResult<DatasetSummary> GetSummary()
    {
        return Ok(_datasetService.GetSummary());
    }

    [HttpGet("api/metrics")]
    public ActionResult<ModelMetrics> GetMetrics()
    {
        if (!System.IO.File.Exists(_metricsPath))
            return NotFound(new { error = "Model not yet trained. Run `dotnet run --project src/CareerPath.ML` first." });

        var json = System.IO.File.ReadAllText(_metricsPath);
        var comparison = JsonSerializer.Deserialize<ModelComparison>(json);
        return Ok(comparison?.RandomForest);
    }

    [HttpGet("api/metrics/comparison")]
    public ActionResult<ModelComparison> GetComparison()
    {
        if (!System.IO.File.Exists(_metricsPath))
            return NotFound(new { error = "Model not yet trained. Run `dotnet run --project src/CareerPath.ML` first." });

        var json = System.IO.File.ReadAllText(_metricsPath);
        var comparison = JsonSerializer.Deserialize<ModelComparison>(json);
        return Ok(comparison);
    }
}
