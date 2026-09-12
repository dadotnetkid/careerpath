using CareerPath.Shared;

namespace CareerPath.Server.Services;

public class RiasecService
{
    private readonly OpenAiService _openAi;
    private readonly DatasetService _dataset;

    public static readonly Dictionary<string, string> DimensionNames = new()
    {
        ["R"] = "Realistic",
        ["I"] = "Investigative",
        ["A"] = "Artistic",
        ["S"] = "Social",
        ["E"] = "Enterprising",
        ["C"] = "Conventional"
    };

    public static readonly Dictionary<string, List<string>> CareerMap = new()
    {
        ["R"] = new() { "Network Administrator", "Systems Engineer", "IT Support Specialist", "DevOps Engineer", "Infrastructure Technician" },
        ["I"] = new() { "Software Developer", "Data Analyst", "Systems Architect", "Cybersecurity Analyst", "Machine Learning Engineer" },
        ["A"] = new() { "UI/UX Designer", "Front-End Developer", "Game Developer", "Multimedia Developer", "Creative Technologist" },
        ["S"] = new() { "System Analyst", "IT Trainer", "IT Support Lead", "Business Analyst", "Technical Account Manager" },
        ["E"] = new() { "IT Project Manager", "IT Consultant", "Systems Manager", "Tech Entrepreneur", "Product Manager" },
        ["C"] = new() { "Database Administrator", "Systems Administrator", "QA Analyst", "IT Auditor", "Data Governance Specialist" }
    };

    // 7 canonical Competencies values from the GTS 3 dataset
    public static readonly List<string> AvailableSkills = new()
    {
        "Information Technology Skills",
        "Communication Skills",
        "Problem-Solving Skills",
        "Entrepreneurial Skills",
        "Organizational Skills",
        "Human Relations Skills",
        "Critical Thinking"
    };

    // ── O*NET Interest Profiler — 30-item short form ──
    // Items are the public-domain work activities published by the U.S. Department
    // of Labor's O*NET program (onetinterestprofiler.org). Five items per RIASEC
    // area; the student rates each on the 5-point RiasecScale. The Area/AreaName
    // is scoring metadata only — it is never shown while the student is answering,
    // so the item's interest area cannot bias the answer.

    private static readonly List<RiasecQuestion> Questions = new()
    {
        new() { Id = 1, Area = "R", AreaName = "Realistic", Text = "Build kitchen cabinets" },
        new() { Id = 2, Area = "I", AreaName = "Investigative", Text = "Develop a new medicine" },
        new() { Id = 3, Area = "A", AreaName = "Artistic", Text = "Write books or plays" },
        new() { Id = 4, Area = "S", AreaName = "Social", Text = "Help people with personal or emotional problems" },
        new() { Id = 5, Area = "E", AreaName = "Enterprising", Text = "Manage a department within a large company" },
        new() { Id = 6, Area = "C", AreaName = "Conventional", Text = "Install software across computers on a large network" },
        new() { Id = 7, Area = "R", AreaName = "Realistic", Text = "Repair household appliances" },
        new() { Id = 8, Area = "I", AreaName = "Investigative", Text = "Study ways to reduce water pollution" },
        new() { Id = 9, Area = "A", AreaName = "Artistic", Text = "Compose or arrange music" },
        new() { Id = 10, Area = "S", AreaName = "Social", Text = "Give career guidance to people" },
        new() { Id = 11, Area = "E", AreaName = "Enterprising", Text = "Start your own business" },
        new() { Id = 12, Area = "C", AreaName = "Conventional", Text = "Operate a calculator" },
        new() { Id = 13, Area = "R", AreaName = "Realistic", Text = "Assemble electronic parts" },
        new() { Id = 14, Area = "I", AreaName = "Investigative", Text = "Conduct chemical experiments" },
        new() { Id = 15, Area = "A", AreaName = "Artistic", Text = "Create special effects for movies" },
        new() { Id = 16, Area = "S", AreaName = "Social", Text = "Perform rehabilitation therapy" },
        new() { Id = 17, Area = "E", AreaName = "Enterprising", Text = "Negotiate business contracts" },
        new() { Id = 18, Area = "C", AreaName = "Conventional", Text = "Keep shipping and receiving records" },
        new() { Id = 19, Area = "R", AreaName = "Realistic", Text = "Drive a truck to deliver packages to offices and homes" },
        new() { Id = 20, Area = "I", AreaName = "Investigative", Text = "Examine blood samples using a microscope" },
        new() { Id = 21, Area = "A", AreaName = "Artistic", Text = "Paint sets for plays" },
        new() { Id = 22, Area = "S", AreaName = "Social", Text = "Do volunteer work at a non-profit organization" },
        new() { Id = 23, Area = "E", AreaName = "Enterprising", Text = "Market a new line of clothing" },
        new() { Id = 24, Area = "C", AreaName = "Conventional", Text = "Inventory supplies using a hand-held computer" },
        new() { Id = 25, Area = "R", AreaName = "Realistic", Text = "Test the quality of parts before shipment" },
        new() { Id = 26, Area = "I", AreaName = "Investigative", Text = "Develop a way to better predict the weather" },
        new() { Id = 27, Area = "A", AreaName = "Artistic", Text = "Write scripts for movies or television shows" },
        new() { Id = 28, Area = "S", AreaName = "Social", Text = "Teach a high-school class" },
        new() { Id = 29, Area = "E", AreaName = "Enterprising", Text = "Sell merchandise at a department store" },
        new() { Id = 30, Area = "C", AreaName = "Conventional", Text = "Stamp, sort, and distribute mail for an organization" },
    };

    public RiasecService(OpenAiService openAi, DatasetService dataset)
    {
        _openAi = openAi;
        _dataset = dataset;
    }

    public List<RiasecQuestion> GetQuestions() => Questions;
    public List<string> GetSkillOptions() => AvailableSkills;

    /// <summary>Get suggested career roles based on RIASEC code for Career Clarity step</summary>
    public List<string> GetSuggestedRoles(string dominantCode)
    {
        var roles = new List<string>();
        foreach (var ch in dominantCode)
        {
            var key = ch.ToString();
            if (CareerMap.TryGetValue(key, out var list))
                roles.AddRange(list);
        }

        // Also add actual job titles from alumni data matching the code
        var alumni = _dataset.GetAllRecords();
        var alumniJobs = alumni
            .Where(a => !string.IsNullOrEmpty(a.Interest) && a.Interest.Length >= 1
                        && dominantCode.Contains(a.Interest[0]))
            .Select(a => a.JobTitle)
            .Where(j => !string.IsNullOrWhiteSpace(j))
            .GroupBy(j => j)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.Key);

        roles.AddRange(alumniJobs);
        return roles.Distinct().ToList();
    }

    // ── Step 1: RIASEC Scoring ──

    /// <summary>
    /// O*NET Interest Profiler scoring: each answer (0 = Strongly Dislike …
    /// 4 = Strongly Like) is added to the score of the area its item belongs to.
    /// With 5 items per area the maximum area score is <see cref="RiasecScale.MaxAreaScore"/>.
    /// Unknown question ids and out-of-range ratings are ignored.
    /// </summary>
    public RiasecScores ComputeRiasecScores(Dictionary<int, int> answers)
    {
        var scores = new RiasecScores();
        var byId = Questions.ToDictionary(q => q.Id, q => q.Area);

        foreach (var (questionId, rating) in answers)
        {
            if (!byId.TryGetValue(questionId, out var area)) continue;
            if (rating < RiasecScale.Min || rating > RiasecScale.Max) continue;

            switch (area)
            {
                case "R": scores.Realistic += rating; break;
                case "I": scores.Investigative += rating; break;
                case "A": scores.Artistic += rating; break;
                case "S": scores.Social += rating; break;
                case "E": scores.Enterprising += rating; break;
                case "C": scores.Conventional += rating; break;
            }
        }
        return scores;
    }

    public (string code, List<string> dimensions) GetDominantCode(RiasecScores scores)
    {
        var ranked = new Dictionary<string, int>
        {
            ["R"] = scores.Realistic,
            ["I"] = scores.Investigative,
            ["A"] = scores.Artistic,
            ["S"] = scores.Social,
            ["E"] = scores.Enterprising,
            ["C"] = scores.Conventional
        }
        .OrderByDescending(x => x.Value)
        .ToList();

        var top3 = ranked.Take(3).Select(x => x.Key).ToList();
        var code = string.Join("", top3);
        var dimensions = top3.Select(k => DimensionNames[k]).ToList();
        return (code, dimensions);
    }

    // ── Step 2: Career Clarity Scoring (PDF spec) ──

    public CareerClarityResult ComputeCareerClarity(CareerClarityInput input, string dominantCode)
    {
        // A. Career Fit Rating — average of fit ratings (max 5)
        double fitScore = input.FitRatings.Count > 0 ? input.FitRatings.Average() : 0;

        // B. Growth Excitement — average of excitement ratings (max 5)
        double excitementScore = input.ExcitementRatings.Count > 0 ? input.ExcitementRatings.Average() : 0;

        // C. Genuine Interest Score — just the genuine interest value (max 5)
        // (popularity and family expectation are captured but not added to clarity score per PDF)
        double genuineInterestScore = input.GenuineInterest;

        // D. Salary-Neutral Consistency — 5 if chose Top 1 (index 0), else 1
        double salaryNeutralScore = input.SalaryNeutralChoice == 0 ? 5.0 : 1.0;

        // E. RIASEC Alignment — check if any desired role aligns with their dominant code
        double riasecAlignmentScore = 1.0;
        if (input.DesiredRoles.Count > 0)
        {
            var topRoleIndex = input.SalaryNeutralChoice;
            if (topRoleIndex >= 0 && topRoleIndex < input.DesiredRoles.Count)
            {
                var chosenRole = input.DesiredRoles[topRoleIndex];
                // Check if the chosen role is in the career map for the dominant RIASEC dimensions
                foreach (var ch in dominantCode)
                {
                    if (CareerMap.TryGetValue(ch.ToString(), out var aligned) &&
                        aligned.Any(c => chosenRole.Contains(c, StringComparison.OrdinalIgnoreCase) ||
                                         c.Contains(chosenRole, StringComparison.OrdinalIgnoreCase)))
                    {
                        riasecAlignmentScore = 5.0;
                        break;
                    }
                }
            }
        }

        double total = fitScore + excitementScore + genuineInterestScore + salaryNeutralScore + riasecAlignmentScore;

        string label = total switch
        {
            >= 21 => "Very High Career Clarity",
            >= 16 => "High Career Clarity",
            >= 11 => "Moderate / Needs Guidance",
            >= 6 => "Low Career Clarity",
            _ => "Highly Uncertain"
        };

        // Discrete bucket used as a feature for the binary classifier.
        // Thresholds are calibrated against the GTS 3 dataset's CareerClarity distribution.
        string bucket = total switch
        {
            >= 17 => "High",
            >= 10 => "Medium",
            _ => "Low"
        };

        return new CareerClarityResult
        {
            FitScore = Math.Round(fitScore, 2),
            ExcitementScore = Math.Round(excitementScore, 2),
            GenuineInterestScore = genuineInterestScore,
            SalaryNeutralScore = salaryNeutralScore,
            RiasecAlignmentScore = riasecAlignmentScore,
            TotalScore = Math.Round(total, 2),
            ClarityLabel = label,
            ClarityBucket = bucket
        };
    }

    // ── Step 3+4: Matching Algorithm ──

    /// <summary>
    /// Match alumni jobs based on:
    ///   - alumni's RiasecDominant (full name like "Conventional") → first letter must match one of
    ///     the user's top-3 RIASEC letters from <paramref name="dominantCode"/>
    ///   AND
    ///   - same Gwa category OR at least one matching skill in Competencies
    /// </summary>
    public List<JobMatch> MatchJobs(string dominantCode, string gwaCategory, List<string> skills)
    {
        var alumni = _dataset.GetAllRecords();
        var topLetters = (dominantCode ?? "").ToUpperInvariant()
            .Where(ch => "RIASEC".Contains(ch))
            .Distinct()
            .ToHashSet();

        var scored = new List<(AlumniRecord record, double score)>();

        foreach (var a in alumni)
        {
            // Map the alumni's full RIASEC dimension name to its single letter.
            var alumniLetter = MapRiasecDominantToLetter(a.RiasecDominant);
            if (alumniLetter == '\0') continue;

            // Required: alumni's RIASEC letter overlaps with user's top-3
            if (!topLetters.Contains(alumniLetter)) continue;

            // Required: same GWA OR at least one matching skill
            bool gwaMatch = !string.IsNullOrEmpty(gwaCategory)
                && a.Gwa.Equals(gwaCategory, StringComparison.OrdinalIgnoreCase);

            int skillsMatched = 0;
            if (skills != null && skills.Count > 0)
            {
                var alumniSkills = (a.Competencies ?? "").ToLowerInvariant();
                skillsMatched = skills.Count(s => !string.IsNullOrWhiteSpace(s) &&
                    alumniSkills.Contains(s.ToLowerInvariant()));
            }

            if (!gwaMatch && skillsMatched == 0) continue;

            double score = 0;
            // RIASEC alignment: bonus if the alumni's letter is the user's primary (first) letter.
            if (!string.IsNullOrEmpty(dominantCode) && char.ToUpperInvariant(dominantCode[0]) == alumniLetter)
                score += 40;
            else
                score += 20;

            if (gwaMatch) score += 15;
            score += skillsMatched * 5;

            scored.Add((a, score));
        }

        // Group by job title and aggregate
        var jobMatches = scored
            .Where(x => !string.IsNullOrWhiteSpace(x.record.JobTitle))
            .GroupBy(x => x.record.JobTitle)
            .Select(g => new JobMatch
            {
                JobTitle = g.Key,
                FieldOfIndustry = g.OrderByDescending(x => x.score).First().record.FieldOfIndustry,
                CareerAlignment = g.OrderByDescending(x => x.score).First().record.CareerAlignment,
                AlumniCount = g.Count(),
                MatchScore = Math.Round(g.Average(x => x.score), 1)
            })
            .OrderByDescending(j => j.MatchScore)
            .ThenByDescending(j => j.AlumniCount)
            .Take(10)
            .ToList();

        // Normalize match scores to 0-100
        if (jobMatches.Count > 0)
        {
            var maxScore = jobMatches.Max(j => j.MatchScore);
            if (maxScore > 0)
            {
                foreach (var j in jobMatches)
                    j.MatchScore = Math.Round(j.MatchScore / maxScore * 100, 1);
            }
        }

        return jobMatches;
    }

    /// <summary>
    /// Map the GTS 3 dataset's full RIASEC dimension name to its first letter.
    /// Returns '\0' if the input is empty or unrecognized.
    /// </summary>
    private static char MapRiasecDominantToLetter(string riasecDominant)
    {
        if (string.IsNullOrWhiteSpace(riasecDominant)) return '\0';
        var trimmed = riasecDominant.Trim();
        return trimmed.ToLowerInvariant() switch
        {
            "realistic" => 'R',
            "investigative" => 'I',
            "artistic" => 'A',
            "social" => 'S',
            "enterprising" => 'E',
            "conventional" => 'C',
            _ => '\0'
        };
    }

    // ── Step 5: AI Analysis via GPT-4o-mini ──

    public async Task<string> GenerateAiAnalysis(FullAssessmentResult result, FullAssessmentInput input)
    {
        var systemPrompt = @"You are a career guidance counselor at a Philippine university's College of Information Technology Education (CITE).
You specialize in RIASEC assessments and career matching for IT students (BSIT, BSCS, BSIS programs).
Provide encouraging, detailed, and actionable career guidance. Use clear language suitable for Filipino college students.

When a binary ML PREDICTION is provided (ICT-Aligned vs Non-ICT-Aligned), your narrative MUST be consistent with it:
  - If predicted ICT-Aligned: affirm the IT track and lean the recommended roles toward IT-core or IT-adjacent careers.
  - If predicted Non-ICT-Aligned: acknowledge their RIASEC strengths and suggest cross-disciplinary or non-IT-primary roles where their skills transfer; do not contradict the prediction by telling them they are clearly suited for IT.
  - Always quote the predicted confidence and explain it as a probability, not a guarantee.

The report shows each section beside the numbers it explains, so the headings must be machine-readable:
  - Put each heading on a line of its own, in capitals, spelled exactly as listed below, with nothing else on that line.
  - Do not number the headings and do not invent headings of your own.
  - Keep each section self-contained; the reader sees it on its own, away from the others.

PERSONALITY PROFILE
CAREER FIT ANALYSIS
ML PREDICTION INTERPRETATION (include only when a prediction was provided)
YOUR CAREER CLARITY
TOP MATCHED ROLES
SKILLS TO DEVELOP
ACTIONABLE NEXT STEPS
ENCOURAGEMENT";

        var jobList = string.Join("\n", result.MatchedJobs.Take(5)
            .Select((j, i) => $"  {i + 1}. {j.JobTitle} ({j.FieldOfIndustry}) — {j.MatchScore}% match, {j.AlumniCount} alumni"));

        var predictionBlock = result.Prediction == null
            ? "(No binary ML prediction available for this assessment.)"
            : $@"Binary ML Prediction (Random Forest, trained on 301 CITE alumni):
  - Outcome: {result.Prediction.PredictedOutcome}
  - Confidence: {(result.Prediction.Confidence * 100):F1}%
  - Class probabilities: {string.Join(", ", result.Prediction.Scores.Select(s => $"{s.Label}={(s.Probability * 100):F1}%"))}
Align your narrative with this prediction (see the system message).";

        var userPrompt = $@"A CITE student just completed the full Career Path Assessment. Here are their results:

RIASEC Scores (O*NET Interest Profiler, each out of 20):
- Realistic: {result.RiasecScores.Realistic}
- Investigative: {result.RiasecScores.Investigative}
- Artistic: {result.RiasecScores.Artistic}
- Social: {result.RiasecScores.Social}
- Enterprising: {result.RiasecScores.Enterprising}
- Conventional: {result.RiasecScores.Conventional}

Dominant RIASEC Code: {result.DominantCode} ({string.Join(", ", result.TopDimensions)})

Career Clarity Score: {result.Clarity.TotalScore}/25 — {result.Clarity.ClarityLabel}
  - Fit Rating: {result.Clarity.FitScore}/5
  - Excitement: {result.Clarity.ExcitementScore}/5
  - Genuine Interest: {result.Clarity.GenuineInterestScore}/5
  - Salary-Neutral Consistency: {result.Clarity.SalaryNeutralScore}/5
  - RIASEC Alignment: {result.Clarity.RiasecAlignmentScore}/5

Desired Roles: {string.Join(", ", input.CareerClarity.DesiredRoles)}
GWA Category: {input.GwaCategory}
Skills: {string.Join(", ", input.Skills)}

Top Matched Job Roles from Alumni Data:
{jobList}

{predictionBlock}

Please provide a verbose, detailed analysis covering:
1. PERSONALITY PROFILE — What their RIASEC code says about them in the IT field
2. CAREER FIT ANALYSIS — Why the top matched roles suit their profile, with specific daily tasks they'd enjoy
3. ML PREDICTION INTERPRETATION (skip if no prediction was provided) — In plain language, explain what the binary outcome and confidence mean. Do NOT contradict it.
4. YOUR CAREER CLARITY — Interpret their clarity score. If low, provide guidance. If high, affirm their direction.
5. TOP MATCHED ROLES — Explain each top 3 matched role consistent with the prediction. If the prediction is Non-ICT-Aligned, highlight roles where their RIASEC strengths transfer outside core IT.
6. SKILLS TO DEVELOP — Based on their current skills and gaps, recommend specific certifications and technologies
7. ACTIONABLE NEXT STEPS — 4-5 concrete things to do right now (projects, certifications, activities)
8. ENCOURAGEMENT — A motivating closing message

Keep the tone warm and professional. Be specific to Philippine IT industry context.";

        try
        {
            return await _openAi.ChatAsync(systemPrompt, userPrompt);
        }
        catch
        {
            return GenerateFallbackAnalysis(result, input);
        }
    }

    private static string GenerateFallbackAnalysis(FullAssessmentResult result, FullAssessmentInput input)
    {
        var topJobs = string.Join(", ", result.MatchedJobs.Take(3).Select(j => j.JobTitle));
        return $"PERSONALITY PROFILE\nYour RIASEC code is {result.DominantCode}, indicating strong interests in " +
               $"{string.Join(", ", result.TopDimensions)}.\n\n" +
               $"YOUR CAREER CLARITY\nYour career clarity score is {result.Clarity.TotalScore}/25 ({result.Clarity.ClarityLabel}).\n\n" +
               $"TOP MATCHED ROLES\nBased on alumni data, your top matches are: {topJobs}.\n\n" +
               $"SKILLS TO DEVELOP\nFocus on: {string.Join(", ", input.Skills)}.\n\n" +
               $"Consider exploring certifications and projects in these areas. " +
               $"Speak with your academic adviser for more personalized recommendations.\n\n" +
               $"(Note: AI-powered detailed analysis is currently unavailable. Please check your API configuration.)";
    }
}
