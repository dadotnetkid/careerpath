using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CareerPath.Shared;

namespace CareerPath.Server.Services;

public class DatasetService
{
    private readonly List<AlumniRecord> _records;

    public DatasetService(string csvPath)
    {
        _records = LoadCsv(csvPath);
    }

    private static List<AlumniRecord> LoadCsv(string csvPath)
    {
        using var reader = new StreamReader(csvPath);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        };
        using var csv = new CsvReader(reader, config);
        csv.Read();
        csv.ReadHeader();

        var records = new List<AlumniRecord>();
        while (csv.Read())
        {
            records.Add(new AlumniRecord
            {
                YearGraduated = csv.GetField<int>("YearGraduated"),
                Gwa = csv.GetField("Gwa") ?? "",
                Honors = csv.GetField("Honors") ?? "",
                Competencies = csv.GetField("Competencies") ?? "",
                EmploymentStatus = csv.GetField("EmploymentStatus") ?? "",
                CareerAlignment = csv.GetField("CareerAlignment") ?? "",
                JobRetention = csv.GetField("JobRetention") ?? "",
                CareerClarity = csv.GetField("CareerClarity") ?? "",
                RiasecDominant = csv.GetField("RiasecDominant") ?? "",
                Interest = csv.GetField("Interest") ?? "",
                FieldOfIndustry = csv.GetField("FieldOfIndustry") ?? "",
                JobLevel = csv.GetField("JobLevel") ?? "",
                JobTitle = csv.GetField("JobTitle") ?? "",
                CareerOutcome = csv.GetField("CareerOutcome") ?? ""
            });
        }
        return records;
    }

    public DatasetSummary GetSummary()
    {
        return new DatasetSummary
        {
            TotalRecords = _records.Count,
            ByGwa = _records.GroupBy(r => r.Gwa)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByHonors = _records.GroupBy(r => r.Honors)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByCareerOutcome = _records.GroupBy(r => r.CareerOutcome)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByCareerAlignment = _records
                .GroupBy(r => r.CareerAlignment)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .ToDictionary(g => g.Key, g => g.Count()),
            ByCareerClarity = _records.GroupBy(r => r.CareerClarity)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByEmploymentStatus = _records.GroupBy(r => r.EmploymentStatus)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByRiasecDominant = _records.GroupBy(r => r.RiasecDominant)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByYear = _records.GroupBy(r => r.YearGraduated)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByFieldIndustryByRiasec = _records
                .Where(r => !string.IsNullOrWhiteSpace(r.RiasecDominant) && !string.IsNullOrWhiteSpace(r.FieldOfIndustry))
                .GroupBy(r => r.RiasecDominant)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(x => x.FieldOfIndustry).ToDictionary(gg => gg.Key, gg => gg.Count())
                )
        };
    }

    public PagedResult<AlumniRecord> GetRecords(int page, int pageSize, string? honors, string? search)
    {
        var query = _records.AsEnumerable();

        if (!string.IsNullOrEmpty(honors))
            query = query.Where(r => r.Honors.Equals(honors, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(search))
            query = query.Where(r =>
                r.Competencies.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                r.JobTitle.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                r.FieldOfIndustry.Contains(search, StringComparison.OrdinalIgnoreCase));

        var list = query.ToList();
        var items = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new PagedResult<AlumniRecord>
        {
            Total = list.Count,
            Page = page,
            PageSize = pageSize,
            Items = items
        };
    }

    public List<AlumniRecord> GetAllRecords() => _records;

    /// <summary>
    /// Find the most-common JobTitle values among alumni who share the predicted CareerOutcome
    /// AND match at least one of: same Honors, same Gwa, same dominant RIASEC letter (first char of Interest).
    /// </summary>
    public List<string> FindSimilarAlumniJobs(StudentInput input, string predictedOutcome)
    {
        var firstInterestLetter = !string.IsNullOrEmpty(input.Interest)
            ? char.ToUpperInvariant(input.Interest[0])
            : '\0';

        var matchingAlumni = _records
            .Where(r => r.CareerOutcome == predictedOutcome)
            .Where(r =>
                (!string.IsNullOrEmpty(input.Honors) && r.Honors.Equals(input.Honors, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(input.Gwa) && r.Gwa.Equals(input.Gwa, StringComparison.OrdinalIgnoreCase)) ||
                (firstInterestLetter != '\0' && !string.IsNullOrEmpty(r.Interest)
                    && char.ToUpperInvariant(r.Interest[0]) == firstInterestLetter))
            .Select(r => r.JobTitle)
            .Where(j => !string.IsNullOrWhiteSpace(j))
            .GroupBy(j => j)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key)
            .ToList();

        return matchingAlumni;
    }
}
