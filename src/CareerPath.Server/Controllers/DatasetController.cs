using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CareerPath.Server.Services;
using CareerPath.Shared;
using System.Text.Json;

namespace CareerPath.Server.Controllers;

[ApiController]
[Authorize]
public class DatasetController : ControllerBase
{
    private readonly DatasetService _datasetService;
    private readonly IConfiguration _config;
    private readonly string _metricsPath;

    public DatasetController(DatasetService datasetService, IConfiguration config)
    {
        _datasetService = datasetService;
        _config = config;
        _metricsPath = config["MLModel:MetricsPath"] ?? "";
    }

    [HttpGet("api/dataset")]
    public ActionResult<PagedResult<AlumniRecord>> GetRecords(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? honors = null,
        [FromQuery] string? search = null)
    {
        var result = _datasetService.GetRecords(page, pageSize, honors, search);
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

    [HttpGet("api/metrics/diagnostic")]
    public async Task<ActionResult<DiagnosticConfusionMatrices>> GetDiagnostic()
    {
        var path = _config["MLModel:DiagnosticMetricsPath"] ?? "";
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            return NotFound(new { error = "Diagnostic metrics not yet generated. Run dotnet run --project src/CareerPath.ML to populate." });
        try
        {
            var json = await System.IO.File.ReadAllTextAsync(path);
            var diag = JsonSerializer.Deserialize<DiagnosticConfusionMatrices>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (diag == null) return NotFound(new { error = "Diagnostic metrics file is empty or invalid." });
            return Ok(diag);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Failed to load diagnostic metrics: {ex.Message}" });
        }
    }
}
