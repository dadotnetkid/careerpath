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

    public static readonly List<string> AvailableSkills = new()
    {
        "Technical Skills",
        "Programming Skills",
        "Data Analysis & Business Intelligence",
        "Network Administration",
        "Software Development",
        "Technical Writing",
        "Cybersecurity",
        "Database Management",
        "Machine Learning & AI",
        "Communication Skills",
        "Human Relations Skills",
        "Entrepreneurial Skills",
        "Information Technology Skills",
        "Problem-Solving Skills",
        "Critical Thinking Skills",
        "Leadership",
        "Organizational Skills"
    };

    // ── RIASEC Multiple-Choice Questions ──

    private static readonly List<RiasecQuestion> Questions = new()
    {
        new()
        {
            Id = 1,
            Scenario = "During a group IT project, which role would you naturally gravitate toward?",
            Options = new()
            {
                new() { Key = "R", Label = "Realistic", Text = "Setting up the servers, configuring hardware, and deploying the system" },
                new() { Key = "I", Label = "Investigative", Text = "Analyzing requirements, designing the system architecture, and solving complex algorithms" },
                new() { Key = "A", Label = "Artistic", Text = "Designing the user interface, choosing color schemes, and crafting the user experience" },
                new() { Key = "S", Label = "Social", Text = "Interviewing users to gather requirements and coordinating team communication" },
                new() { Key = "E", Label = "Enterprising", Text = "Leading the project, assigning tasks, and presenting progress to stakeholders" },
                new() { Key = "C", Label = "Conventional", Text = "Writing documentation, tracking tasks, and ensuring quality standards are met" }
            }
        },
        new()
        {
            Id = 2,
            Scenario = "You have a free weekend to work on a personal tech project. What excites you most?",
            Options = new()
            {
                new() { Key = "R", Label = "Realistic", Text = "Building a home lab with Raspberry Pi, setting up a local network, or assembling a PC" },
                new() { Key = "I", Label = "Investigative", Text = "Experimenting with a new programming language or exploring a machine learning dataset" },
                new() { Key = "A", Label = "Artistic", Text = "Designing a portfolio website or creating digital art and animations" },
                new() { Key = "S", Label = "Social", Text = "Volunteering to teach coding basics to younger students or writing a tech tutorial blog" },
                new() { Key = "E", Label = "Enterprising", Text = "Planning a startup idea, drafting a business model, or pitching an app concept" },
                new() { Key = "C", Label = "Conventional", Text = "Organizing your files, setting up automated backups, or building a personal budgeting spreadsheet" }
            }
        },
        new()
        {
            Id = 3,
            Scenario = "You encounter a critical bug in a system just before deployment. What is your instinct?",
            Options = new()
            {
                new() { Key = "R", Label = "Realistic", Text = "Roll up my sleeves and debug the server logs, check the network, and fix it hands-on" },
                new() { Key = "I", Label = "Investigative", Text = "Trace through the code systematically, analyze the root cause, and find the most efficient fix" },
                new() { Key = "A", Label = "Artistic", Text = "Think of a creative workaround or alternative approach the team hasn't considered" },
                new() { Key = "S", Label = "Social", Text = "Coordinate with the team, communicate the issue clearly, and help everyone stay calm" },
                new() { Key = "E", Label = "Enterprising", Text = "Take charge of the situation, decide priorities, and manage the crisis response" },
                new() { Key = "C", Label = "Conventional", Text = "Document the bug, follow the incident response protocol, and ensure proper testing before re-deploying" }
            }
        },
        new()
        {
            Id = 4,
            Scenario = "Which type of IT course or training would you sign up for first?",
            Options = new()
            {
                new() { Key = "R", Label = "Realistic", Text = "Hands-on network administration or Cisco router configuration workshop" },
                new() { Key = "I", Label = "Investigative", Text = "Advanced algorithms, data science, or cybersecurity analysis course" },
                new() { Key = "A", Label = "Artistic", Text = "UI/UX design bootcamp or game development with Unity/Unreal" },
                new() { Key = "S", Label = "Social", Text = "IT service management, user support best practices, or communication skills for tech" },
                new() { Key = "E", Label = "Enterprising", Text = "IT project management, digital transformation strategy, or tech entrepreneurship seminar" },
                new() { Key = "C", Label = "Conventional", Text = "Database administration, SQL mastery, or IT audit and compliance training" }
            }
        },
        new()
        {
            Id = 5,
            Scenario = "Imagine your ideal workday five years from now. What does it look like?",
            Options = new()
            {
                new() { Key = "R", Label = "Realistic", Text = "On-site at a data center, configuring servers, and keeping systems running smoothly" },
                new() { Key = "I", Label = "Investigative", Text = "Deep in code, solving complex problems, and researching new technologies at my desk" },
                new() { Key = "A", Label = "Artistic", Text = "Designing beautiful interfaces, prototyping new features, and brainstorming creative solutions" },
                new() { Key = "S", Label = "Social", Text = "Meeting with clients, training teams on new tools, and helping users solve their problems" },
                new() { Key = "E", Label = "Enterprising", Text = "Leading meetings, making strategic decisions, and steering the direction of IT projects" },
                new() { Key = "C", Label = "Conventional", Text = "Managing databases, generating reports, and ensuring all systems are well-documented and compliant" }
            }
        },
        new()
        {
            Id = 6,
            Scenario = "A company asks you to improve their IT operations. Where would you focus first?",
            Options = new()
            {
                new() { Key = "R", Label = "Realistic", Text = "Upgrade their physical infrastructure — better servers, faster network, proper cabling" },
                new() { Key = "I", Label = "Investigative", Text = "Analyze their system performance data to find bottlenecks and optimize the architecture" },
                new() { Key = "A", Label = "Artistic", Text = "Redesign their customer-facing applications for a better user experience" },
                new() { Key = "S", Label = "Social", Text = "Survey employees about their pain points and improve IT support and training" },
                new() { Key = "E", Label = "Enterprising", Text = "Propose a digital transformation roadmap and present it to top management" },
                new() { Key = "C", Label = "Conventional", Text = "Audit their data management, standardize processes, and implement proper documentation" }
            }
        },
        new()
        {
            Id = 7,
            Scenario = "Which achievement would make you most proud?",
            Options = new()
            {
                new() { Key = "R", Label = "Realistic", Text = "Successfully deploying and maintaining a reliable, zero-downtime server infrastructure" },
                new() { Key = "I", Label = "Investigative", Text = "Publishing a research paper or solving a problem no one else could crack" },
                new() { Key = "A", Label = "Artistic", Text = "Winning a design award or having users praise the look and feel of your app" },
                new() { Key = "S", Label = "Social", Text = "Receiving heartfelt thanks from a team or client you helped through a tough tech problem" },
                new() { Key = "E", Label = "Enterprising", Text = "Leading a project to successful launch and being recognized as the driving force behind it" },
                new() { Key = "C", Label = "Conventional", Text = "Passing a major compliance audit with zero findings due to your thorough documentation" }
            }
        },
        new()
        {
            Id = 8,
            Scenario = "You are assigned to develop a new system from scratch. What part excites you the most?",
            Options = new()
            {
                new() { Key = "R", Label = "Realistic", Text = "Setting up the development environment, CI/CD pipeline, and deployment infrastructure" },
                new() { Key = "I", Label = "Investigative", Text = "Choosing the right tech stack, designing the database schema, and writing core logic" },
                new() { Key = "A", Label = "Artistic", Text = "Wireframing the screens, designing the visual identity, and making it look polished" },
                new() { Key = "S", Label = "Social", Text = "Running user interviews, understanding what people actually need, and doing usability testing" },
                new() { Key = "E", Label = "Enterprising", Text = "Scoping the project, managing the budget and timeline, and coordinating the team" },
                new() { Key = "C", Label = "Conventional", Text = "Planning the data models, writing technical specifications, and setting up testing procedures" }
            }
        },
        new()
        {
            Id = 9,
            Scenario = "Which of these IT-related books or topics would you read first?",
            Options = new()
            {
                new() { Key = "R", Label = "Realistic", Text = "\"Network Warrior\" — practical guide to network engineering and troubleshooting" },
                new() { Key = "I", Label = "Investigative", Text = "\"Cracking the Coding Interview\" — deep dive into algorithms and problem-solving" },
                new() { Key = "A", Label = "Artistic", Text = "\"Don't Make Me Think\" — principles of intuitive web design and UX" },
                new() { Key = "S", Label = "Social", Text = "\"The Phoenix Project\" — story about IT teamwork, communication, and DevOps culture" },
                new() { Key = "E", Label = "Enterprising", Text = "\"The Lean Startup\" — building tech products and making business decisions" },
                new() { Key = "C", Label = "Conventional", Text = "\"Database Design for Mere Mortals\" — structured approach to data organization" }
            }
        },
        new()
        {
            Id = 10,
            Scenario = "What kind of feedback from a colleague would mean the most to you?",
            Options = new()
            {
                new() { Key = "R", Label = "Realistic", Text = "\"The system you set up is rock-solid — zero issues since deployment\"" },
                new() { Key = "I", Label = "Investigative", Text = "\"Your solution was brilliant — no one else could have figured that out\"" },
                new() { Key = "A", Label = "Artistic", Text = "\"The design looks amazing — users love how it feels to use\"" },
                new() { Key = "S", Label = "Social", Text = "\"You really understood what we needed and made the whole process smooth\"" },
                new() { Key = "E", Label = "Enterprising", Text = "\"Your leadership kept the project on track — we couldn't have shipped without you\"" },
                new() { Key = "C", Label = "Conventional", Text = "\"Your documentation is so thorough — it saved us hours during the audit\"" }
            }
        }
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

        // Also add actual job positions from alumni data matching the code
        var alumni = _dataset.GetAllRecords();
        var alumniJobs = alumni
            .Where(a => !string.IsNullOrEmpty(a.Interest) && a.Interest.Length >= 1
                        && dominantCode.Contains(a.Interest[0]))
            .Select(a => a.JobPosition)
            .Where(j => !string.IsNullOrWhiteSpace(j))
            .GroupBy(j => j)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.Key);

        roles.AddRange(alumniJobs);
        return roles.Distinct().ToList();
    }

    // ── Step 1: RIASEC Scoring ──

    public RiasecScores ComputeRiasecScores(Dictionary<int, string> answers)
    {
        var scores = new RiasecScores();
        foreach (var answer in answers.Values)
        {
            switch (answer)
            {
                case "R": scores.Realistic++; break;
                case "I": scores.Investigative++; break;
                case "A": scores.Artistic++; break;
                case "S": scores.Social++; break;
                case "E": scores.Enterprising++; break;
                case "C": scores.Conventional++; break;
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

        return new CareerClarityResult
        {
            FitScore = Math.Round(fitScore, 2),
            ExcitementScore = Math.Round(excitementScore, 2),
            GenuineInterestScore = genuineInterestScore,
            SalaryNeutralScore = salaryNeutralScore,
            RiasecAlignmentScore = riasecAlignmentScore,
            TotalScore = Math.Round(total, 2),
            ClarityLabel = label
        };
    }

    // ── Step 3+4: Matching Algorithm ──

    public List<JobMatch> MatchJobs(string dominantCode, string gwaCategory, List<string> skills)
    {
        var alumni = _dataset.GetAllRecords();

        // Score each alumni record based on match criteria
        var scored = new List<(AlumniRecord record, double score)>();

        foreach (var a in alumni)
        {
            if (string.IsNullOrEmpty(a.Interest) || a.Interest.Length < 2) continue;

            double score = 0;

            // RIASEC code match — compare dominant letters
            var alumniCode = a.Interest.ToUpper();
            for (int i = 0; i < Math.Min(dominantCode.Length, alumniCode.Length); i++)
            {
                if (dominantCode.Contains(alumniCode[i]))
                    score += (i == 0) ? 40 : 20; // Primary match worth more
            }
            if (dominantCode.Length >= 1 && alumniCode.Length >= 1 && dominantCode[0] == alumniCode[0])
                score += 15; // Bonus for exact first-letter match

            // GWA category match
            var alumniGwaCategory = CategorizeGwa(a.Gwa);
            if (alumniGwaCategory == gwaCategory)
                score += 15;
            else if (AreAdjacentGwaCategories(alumniGwaCategory, gwaCategory))
                score += 7;

            // Skills/competency match
            if (skills.Count > 0)
            {
                var alumniSkills = a.Competencies.ToLower();
                int matched = skills.Count(s => alumniSkills.Contains(s.ToLower()) ||
                    alumniSkills.Contains(s.Split(' ')[0].ToLower()));
                score += matched * 5;
            }

            if (score > 0)
                scored.Add((a, score));
        }

        // Group by job position and aggregate
        var jobMatches = scored
            .GroupBy(x => x.record.JobPosition)
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

    private static string CategorizeGwa(float gwa) => gwa switch
    {
        <= 1.25f => "Outstanding",
        <= 2.00f => "Very Satisfactory",
        <= 2.75f => "Satisfactory",
        _ => "Fair"
    };

    private static bool AreAdjacentGwaCategories(string a, string b)
    {
        var order = new[] { "Outstanding", "Very Satisfactory", "Satisfactory", "Fair" };
        int ia = Array.IndexOf(order, a), ib = Array.IndexOf(order, b);
        return ia >= 0 && ib >= 0 && Math.Abs(ia - ib) == 1;
    }

    // ── Step 5: AI Analysis via GPT-4o-mini ──

    public async Task<string> GenerateAiAnalysis(FullAssessmentResult result, FullAssessmentInput input)
    {
        var systemPrompt = @"You are a career guidance counselor at a Philippine university's College of Information Technology Education (CITE).
You specialize in RIASEC assessments and career matching for IT students (BSIT, BSCS, BSIS programs).
Provide encouraging, detailed, and actionable career guidance. Use clear language suitable for Filipino college students.
Structure your response with these sections, each separated by a blank line:
PERSONALITY PROFILE, CAREER FIT ANALYSIS, YOUR CAREER CLARITY, TOP MATCHED ROLES, SKILLS TO DEVELOP, ACTIONABLE NEXT STEPS, ENCOURAGEMENT.";

        var jobList = string.Join("\n", result.MatchedJobs.Take(5)
            .Select((j, i) => $"  {i + 1}. {j.JobTitle} ({j.FieldOfIndustry}) — {j.MatchScore}% match, {j.AlumniCount} alumni"));

        var userPrompt = $@"A CITE student just completed the full Career Path Assessment. Here are their results:

RIASEC Scores (out of 10):
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

Please provide a verbose, detailed analysis covering:
1. PERSONALITY PROFILE — What their RIASEC code says about them in the IT field
2. CAREER FIT ANALYSIS — Why the top matched roles suit their profile, with specific daily tasks they'd enjoy
3. YOUR CAREER CLARITY — Interpret their clarity score. If low, provide guidance. If high, affirm their direction.
4. TOP MATCHED ROLES — Explain each top 3 matched role, what alumni in those roles typically do, and why it matches
5. SKILLS TO DEVELOP — Based on their current skills and gaps, recommend specific certifications and technologies
6. ACTIONABLE NEXT STEPS — 4-5 concrete things to do right now (projects, certifications, activities)
7. ENCOURAGEMENT — A motivating closing message

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
