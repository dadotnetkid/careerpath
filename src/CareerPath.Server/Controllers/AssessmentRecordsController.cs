using System.Globalization;
using System.Text;
using CareerPath.Server.Data;
using CareerPath.Shared;
using CsvHelper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CareerPath.Server.Controllers;

/// <summary>
/// Admin view of every completed assessment (see RiasecController.RecordAssessment).
/// The CSV export follows the alumni GTS 3 column names where the meaning
/// matches, so the new data can be compared side by side with alumni_gts3.csv.
/// </summary>
[ApiController]
[Route("api/assessment-records")]
[Authorize(Roles = AuthRoles.Admin)]
public class AssessmentRecordsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AssessmentRecordsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<AssessmentRecordDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var total = await _db.AssessmentRecords.CountAsync();
        var rows = await _db.AssessmentRecords
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedResult<AssessmentRecordDto>
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = rows.Select(ToDto).ToList()
        });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        var rows = await _db.AssessmentRecords
            .AsNoTracking()
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        await using var buffer = new MemoryStream();
        // UTF-8 with BOM so Excel opens names with ñ and the like correctly.
        await using (var writer = new StreamWriter(buffer, new UTF8Encoding(true), leaveOpen: true))
        await using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            foreach (var header in ExportHeaders) csv.WriteField(header);
            await csv.NextRecordAsync();

            foreach (var r in rows)
            {
                csv.WriteField(ToLocal(r.CreatedAt).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                csv.WriteField(r.Respondent);
                csv.WriteField(r.Gwa);
                csv.WriteField(r.Honors);
                csv.WriteField(r.Competencies);
                csv.WriteField(r.CareerClarity);
                csv.WriteField(r.ClarityLabel);
                csv.WriteField(r.ClarityScore.ToString("0.##", CultureInfo.InvariantCulture));
                csv.WriteField(r.DesiredRoles);
                csv.WriteField(r.RiasecDominant);
                csv.WriteField(r.Interest);
                csv.WriteField(r.RealisticScore);
                csv.WriteField(r.InvestigativeScore);
                csv.WriteField(r.ArtisticScore);
                csv.WriteField(r.SocialScore);
                csv.WriteField(r.EnterprisingScore);
                csv.WriteField(r.ConventionalScore);
                csv.WriteField(r.SuggestedCareerAlignment);
                csv.WriteField(r.SuggestedFieldOfIndustry);
                csv.WriteField(r.SuggestedCareer);
                csv.WriteField(r.SuggestedCareers);
                csv.WriteField(r.PredictedOutcome);
                csv.WriteField(r.PredictionConfidence.ToString("0.####", CultureInfo.InvariantCulture));
                await csv.NextRecordAsync();
            }
        }

        var fileName = $"assessment_records_{DateTime.Now:yyyyMMdd_HHmm}.csv";
        return File(buffer.ToArray(), "text/csv", fileName);
    }

    // Order follows alumni_gts3.csv (academics, competencies, clarity, RIASEC,
    // then career fields), with the model's outputs last.
    private static readonly string[] ExportHeaders =
    {
        "AssessedAt",
        "Respondent",
        "Gwa",
        "Honors",
        "Competencies",
        "CareerClarity",
        "ClarityLabel",
        "ClarityScore",
        "DesiredRoles",
        "RiasecDominant",
        "Interest",
        "RealisticScore",
        "InvestigativeScore",
        "ArtisticScore",
        "SocialScore",
        "EnterprisingScore",
        "ConventionalScore",
        "SuggestedCareerAlignment",
        "SuggestedFieldOfIndustry",
        "SuggestedCareer",
        "SuggestedCareers",
        "PredictedCareerOutcome",
        "PredictionConfidence"
    };

    private static DateTime ToLocal(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    private static AssessmentRecordDto ToDto(AssessmentRecord r) => new()
    {
        Id = r.Id,
        CreatedAt = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
        Respondent = r.Respondent,
        Gwa = r.Gwa,
        Honors = r.Honors,
        Competencies = r.Competencies,
        RiasecDominant = r.RiasecDominant,
        Interest = r.Interest,
        RiasecScores = new RiasecScores
        {
            Realistic = r.RealisticScore,
            Investigative = r.InvestigativeScore,
            Artistic = r.ArtisticScore,
            Social = r.SocialScore,
            Enterprising = r.EnterprisingScore,
            Conventional = r.ConventionalScore
        },
        CareerClarity = r.CareerClarity,
        ClarityLabel = r.ClarityLabel,
        ClarityScore = r.ClarityScore,
        DesiredRoles = r.DesiredRoles,
        PredictedOutcome = r.PredictedOutcome,
        PredictionConfidence = r.PredictionConfidence,
        SuggestedCareer = r.SuggestedCareer,
        SuggestedCareerAlignment = r.SuggestedCareerAlignment,
        SuggestedFieldOfIndustry = r.SuggestedFieldOfIndustry,
        SuggestedCareers = r.SuggestedCareers
    };
}
