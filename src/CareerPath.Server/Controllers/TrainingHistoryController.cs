using CareerPath.Server.Data;
using CareerPath.Server.Services;
using CareerPath.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareerPath.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/training")]
public class TrainingHistoryController : ControllerBase
{
    private readonly TrainingHistoryService _historyService;
    private readonly AppDbContext _db;

    public TrainingHistoryController(TrainingHistoryService historyService, AppDbContext db)
    {
        _historyService = historyService;
        _db = db;
    }

    /// <summary>
    /// Most-recent CV run per (Algorithm × FoldCount). Returns up to 6 rows
    /// (RF/DT/LR × 10/20-fold). Empty list if the trainer hasn't been run yet.
    /// </summary>
    [HttpGet("latest")]
    public async Task<ActionResult<List<TrainingRunDto>>> GetLatest()
    {
        var rows = await _historyService.GetLatestAsync(_db);
        return Ok(rows);
    }

    /// <summary>
    /// Last 20 CV runs for a specific (algorithm, foldCount) combo, oldest first.
    /// foldCount must be 10 or 20.
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<List<TrainingRunDto>>> GetHistory(
        [FromQuery] string algorithm,
        [FromQuery] int foldCount)
    {
        if (string.IsNullOrWhiteSpace(algorithm))
            return BadRequest(new { error = "algorithm query parameter is required." });

        if (foldCount != 10 && foldCount != 20)
            return BadRequest(new { error = "foldCount must be 10 or 20." });

        var rows = await _historyService.GetHistoryAsync(algorithm, foldCount, _db);
        return Ok(rows);
    }
}
