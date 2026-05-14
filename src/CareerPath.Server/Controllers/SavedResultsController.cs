using CareerPath.Server.Data;
using CareerPath.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CareerPath.Server.Controllers;

/// <summary>
/// Student-facing persistence for the wizard's Results step. A student can
/// save the snapshot of their full assessment + binary prediction and revisit
/// it later from the "My Saved Results" page. Rows are scoped by UserId so a
/// student only ever sees their own saves. Admins are deliberately not
/// granted access to this controller — it's not designed as an audit log.
/// </summary>
[ApiController]
[Route("api/results")]
[Authorize(Roles = AuthRoles.Student)]
public class SavedResultsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<IdentityUser> _userManager;

    public SavedResultsController(AppDbContext db, UserManager<IdentityUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpPost]
    public async Task<ActionResult<SavedResultDto>> Save([FromBody] SaveResultRequest req)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var entity = new SavedAssessmentResult
        {
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            Gwa = req.Gwa ?? "",
            Honors = req.Honors ?? "",
            Competencies = req.Competencies ?? "",
            DominantCode = req.DominantCode ?? "",
            ClarityLabel = req.ClarityLabel ?? "",
            ClarityBucket = req.ClarityBucket ?? "",
            ClarityTotal = req.ClarityTotal,
            PredictedOutcome = req.PredictedOutcome ?? "",
            PredictedConfidence = req.PredictedConfidence,
            AiAnalysis = req.AiAnalysis ?? "",
            Notes = req.Notes ?? ""
        };

        _db.SavedResults.Add(entity);
        await _db.SaveChangesAsync();

        return Ok(ToDto(entity));
    }

    [HttpGet]
    public async Task<ActionResult<List<SavedResultDto>>> List()
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var rows = await _db.SavedResults
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return Ok(rows.Select(ToDto).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<SavedResultDto>> Get(int id)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var row = await _db.SavedResults
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

        if (row == null) return NotFound();
        return Ok(ToDto(row));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var row = await _db.SavedResults
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

        if (row == null) return NotFound();

        _db.SavedResults.Remove(row);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static SavedResultDto ToDto(SavedAssessmentResult r) => new()
    {
        Id = r.Id,
        CreatedAt = r.CreatedAt,
        Gwa = r.Gwa,
        Honors = r.Honors,
        Competencies = r.Competencies,
        DominantCode = r.DominantCode,
        ClarityLabel = r.ClarityLabel,
        ClarityBucket = r.ClarityBucket,
        ClarityTotal = r.ClarityTotal,
        PredictedOutcome = r.PredictedOutcome,
        PredictedConfidence = r.PredictedConfidence,
        AiAnalysis = r.AiAnalysis,
        Notes = r.Notes
    };
}
