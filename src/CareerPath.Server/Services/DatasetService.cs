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
                Id = csv.GetField("Id") ?? "",
                Name = csv.GetField("Name") ?? "",
                YearGraduated = csv.GetField<int>("YearGraduated"),
                Course = csv.GetField("Course") ?? "",
                Gwa = csv.GetField<float>("Gwa"),
                JobPosition = csv.GetField("JobPosition") ?? "",
                FieldOfIndustry = csv.GetField("FieldOfIndustry") ?? "",
                Competencies = csv.GetField("Competencies") ?? "",
                EmploymentStatus = csv.GetField("EmploymentStatus") ?? "",
                CareerAlignment = csv.GetField("CareerAlignment") ?? "",
                Interest = csv.GetField("Interest") ?? "",
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
            ByCourse = _records.GroupBy(r => r.Course)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByCareerOutcome = _records.GroupBy(r => r.CareerOutcome)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByEmploymentStatus = _records.GroupBy(r => r.EmploymentStatus)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByYear = _records.GroupBy(r => r.YearGraduated)
                .ToDictionary(g => g.Key, g => g.Count()),
            GwaAverage = Math.Round(_records.Average(r => r.Gwa), 4),
            GwaMin = Math.Round(_records.Min(r => (double)r.Gwa), 4),
            GwaMax = Math.Round(_records.Max(r => (double)r.Gwa), 4)
        };
    }

    public PagedResult<AlumniRecord> GetRecords(int page, int pageSize, string? course, string? search)
    {
        var query = _records.AsEnumerable();

        if (!string.IsNullOrEmpty(course))
            query = query.Where(r => r.Course.Equals(course, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(search))
            query = query.Where(r =>
                r.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                r.JobPosition.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                r.FieldOfIndustry.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                r.Competencies.Contains(search, StringComparison.OrdinalIgnoreCase));

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

    public List<string> FindSimilarAlumniJobs(string course, float gwa, string competencies, string interest, string predictedOutcome)
    {
        var matchingAlumni = _records
            .Where(r => r.CareerOutcome == predictedOutcome)
            .Where(r => r.Course.Equals(course, StringComparison.OrdinalIgnoreCase) || Math.Abs(r.Gwa - gwa) < 0.5f)
            .Select(r => r.JobPosition)
            .Where(j => !string.IsNullOrWhiteSpace(j))
            .GroupBy(j => j)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key)
            .ToList();

        return matchingAlumni;
    }
}
