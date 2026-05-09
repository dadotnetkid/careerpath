# Agent Contract Spec — GTS 3 Dataset Migration

**Read this entire document before writing any code.** This is the locked contract that all four parallel agents (ML, Server, Client, Docs) must respect. Any deviation breaks integration.

---

## 1. The change

The project is migrating from the old `data/alumni.csv` (n=301, columns: Id/Name/Course/numeric-Gwa/...) to the new GTS 3 dataset. A canonical cleaned file already exists at:

**`data/alumni_gts3.csv`** — 301 rows, 14 columns, header:
```
YearGraduated,Gwa,Honors,Competencies,EmploymentStatus,CareerAlignment,JobRetention,CareerClarity,RiasecDominant,Interest,FieldOfIndustry,JobLevel,JobTitle,CareerOutcome
```

This file has already been:
- Whitespace-collapsed (`"Very   Satisfactory"` → `"Very Satisfactory"`)
- Typo-corrected (`Entrepreunial`→`Entrepreneurial`, `Problem-solving`→`Problem-Solving`)
- Augmented with a derived binary `CareerOutcome` column

**Do NOT re-clean the source file.** Use `data/alumni_gts3.csv` as-is.

The old `data/alumni.csv` and `data/alumni_raw.csv` stay on disk but are no longer referenced by code.

---

## 2. Locked contracts (DO NOT MODIFY)

The following files are **locked** and have already been written by the orchestrator. Read them but do not edit:

- `src/CareerPath.Shared/Models.cs` — All DTOs (`StudentInput`, `PredictionResult`, `DatasetSummary`, `AlumniRecord`, `FullAssessmentInput`, `FullAssessmentResult`, etc.)
- `src/CareerPath.ML/AlumniData.cs` — `AlumniCsvRow`, `StudentFeatures`, `CareerOutcomePrediction`, `RiasecFeatures`, `RiasecPrediction`
- `src/CareerPath.Server/Data/AppDbContext.cs` — `PredictionLog` entity for audit log

If you believe one of these needs to change, **stop and report** — do not edit. Changing them desyncs the other agents.

---

## 3. Feature set (locked)

Binary classifier features (input to `StudentFeatures`):

| # | Feature | Type | Values |
|---|---|---|---|
| 1 | `YearGraduated` | float | 2021–2025 in training; default = current year at inference |
| 2 | `Gwa` | string (one-hot) | `Outstanding` \| `Very Satisfactory` \| `Satisfactory` \| `Fair` |
| 3 | `Honors` | string (one-hot) | `No Honors` \| `Cum Laude` \| `Magna Cum Laude` \| `Summa Cum Laude` |
| 4 | `Competencies` | string (TF-IDF) | Free text — typical values: "Information Technology Skills", "Communication Skills", "Problem-Solving Skills", "Entrepreneurial Skills", "Organizational Skills", "Human Relations Skills", "Critical Thinking" |
| 5 | `CareerClarity` | string (one-hot) | `Low` \| `Medium` \| `High` |
| 6 | `Interest` | string (TF-IDF) | RIASEC code triplet (e.g. "CSE", "RIC", "CRS"); 6 possible characters R/I/A/S/E/C |

**LEAKAGE COLUMNS — never use as features under any circumstances:**
`CareerAlignment`, `JobTitle`, `FieldOfIndustry`, `EmploymentStatus`, `JobRetention`, `JobLevel`. These are consequences of the outcome.

**`RiasecDominant`** is also not a feature — it's redundant with `Interest` (the first letter of the triplet).

---

## 4. Label derivation (locked)

The binary `Label` (boolean for ML.NET, string for everything else) is:
- `true` / `"ICT-Aligned"` if `CareerAlignment ∈ { "Core ICT", "ICT-Adjacent" }`
- `false` / `"Non-ICT-Aligned"` if `CareerAlignment ∈ { "Non-ICT but Adjacent", "Non-ICT" }`

Note the **modern spelling**: `"ICT-Adjacent"` (one word, no `s` in "Adjacent"). The old data spelled it `"ICT-Adjascent"` — that spelling no longer appears anywhere.

The `data/alumni_gts3.csv` file already contains a precomputed `CareerOutcome` column with these exact strings — agents may either:
- Read the precomputed `CareerOutcome` column directly, or
- Re-derive it from `CareerAlignment` via CustomMapping in the trainer

Both are acceptable. The trainer should re-derive (so changing the rule later doesn't require re-cleaning the CSV).

---

## 5. RIASEC dimension mapping (locked)

The dataset's `RiasecDominant` column stores the FULL dimension name. Map to single letters:

| Full name | Letter |
|---|---|
| Realistic | R |
| Investigative | I |
| Artistic | A |
| Social | S |
| Enterprising | E |
| Conventional | C |

`RiasecTrainer` predicts the single letter from profile features. At inference, the user's RIASEC code is the dominant letter from their assessment (already computed by `/riasec` flow).

---

## 6. Career Clarity derivation (for inference)

The `RiasecAssessment` page produces a `CareerClarityResult` with a continuous `TotalScore` (0–25). For ML inference we need the discrete bucket. **Server's `RiasecService.ComputeCareerClarity`** must populate `ClarityBucket` (a new field on `CareerClarityResult`):

| TotalScore range | ClarityBucket |
|---|---|
| ≥ 17 | `High` |
| 10–16.99 | `Medium` |
| < 10 | `Low` |

These thresholds are calibrated from the `Career_Clarity` column distribution in the GTS 3 dataset (Low 100 / Medium 164 / High 37).

The frontend uses `Clarity.ClarityBucket` (not `ClarityLabel`) when sending the `StudentInput` to `/api/predict`.

---

## 7. API contracts (locked — agents implement these)

### `POST /api/predict`
```json
// Request: StudentInput
{
  "yearGraduated": 2026,
  "gwa": "Very Satisfactory",
  "honors": "Cum Laude",
  "competencies": "Information Technology Skills, Problem-Solving Skills",
  "careerClarity": "High",
  "interest": "IRC"
}
// Response: PredictionResult
{
  "predictedOutcome": "ICT-Aligned",
  "confidence": 0.78,
  "scores": [
    { "label": "ICT-Aligned", "probability": 0.78 },
    { "label": "Non-ICT-Aligned", "probability": 0.22 }
  ],
  "interpretation": "Based on...",
  "similarAlumniJobs": ["Software Developer", "..."],
  "predictedAt": "2026-05-08T12:00:00Z"
}
```

### `GET /api/dataset?page=1&pageSize=50&honors=Cum%20Laude&search=technology`
Filters supported: `honors` (replaces old `course`), `search` (substring match across Competencies, JobTitle, FieldOfIndustry).

### `GET /api/dataset/summary` → `DatasetSummary`
Returns the new shape: `ByGwa`, `ByHonors`, `ByCareerOutcome`, `ByCareerClarity`, `ByEmploymentStatus`, `ByRiasecDominant`, `ByYear`. **No `ByCourse`** anymore.

### `GET /api/metrics` → `ModelMetrics` (RF only)
### `GET /api/metrics/comparison` → `ModelComparison`
Feature importance section will list 6 features: `YearGraduated`, `Gwa`, `Honors`, `Competencies`, `CareerClarity`, `Interest`.

### `POST /api/riasec/assess` → `FullAssessmentResult` (existing)
After computing RIASEC + Clarity + matched jobs, the server should NOT auto-call the binary predictor. The frontend triggers prediction explicitly via `POST /api/predict` after the user enters GWA/Honors/Competencies on the report page.

---

## 8. Agent scopes (NO file overlap allowed)

### Agent A (ML) — owns `src/CareerPath.ML/**`
- Rewrite `CareerPathTrainer.cs`:
  - `LoadData(csvPath)`: load `alumni_gts3.csv`, derive Label from `CareerAlignment` via CustomMapping
  - `BuildFeaturePipeline()`: one-hot encode `Gwa`, `Honors`, `CareerClarity`; TF-IDF on `Competencies` and `Interest`; concatenate with `YearGraduated`; normalize-min-max
  - `TrainAll(...)`: unchanged structure (RF + DT + LR), saves `model.zip` + `metrics.json`
  - `ComputeFeatureImportance(...)`: permute the 6 raw columns (YearGraduated, Gwa, Honors, Competencies, CareerClarity, Interest)
  - `LoadPredictionEngine(...)`: returns `PredictionEngine<StudentFeatures, CareerOutcomePrediction>`
- Rewrite `RiasecTrainer.cs`:
  - Use CsvHelper to read `RiasecDominant` column directly (full name → first letter)
  - Profile features: YearGraduated, Gwa, Honors, Competencies, CareerClarity (no Interest — that would leak)
  - Save `riasec_model.zip` + `riasec_metrics.json`
- Run `dotnet run --project src/CareerPath.ML` and verify:
  - `src/CareerPath.Server/MLModel/model.zip` exists
  - `src/CareerPath.Server/MLModel/metrics.json` exists with FeatureImportances populated
  - `src/CareerPath.Server/MLModel/riasec_model.zip` exists
  - `src/CareerPath.Server/MLModel/riasec_metrics.json` exists
  - All three binary models report Accuracy ≥ 0.55 (sanity check)

### Agent B (Server) — owns `src/CareerPath.Server/**` EXCEPT `Data/AppDbContext.cs` (locked)
- Rewrite `Services/DatasetService.cs` to read `alumni_gts3.csv` into `List<AlumniRecord>` with the new column names. Update `GetSummary()` to populate the new `DatasetSummary` shape. Update `GetRecords(...)` to filter by `honors` instead of `course`. Update `FindSimilarAlumniJobs(...)` to use `Honors`/`Gwa`/`Interest` similarity (no `Course`).
- Rewrite `Services/PredictionService.cs` to map `StudentInput` → `StudentFeatures` with the new fields. Update the `Interpretation` text to NOT reference Course.
- Update `Controllers/PredictController.cs` to write the new `PredictionLog` shape (no `Course` field).
- Update `Program.cs` so `csvPath` resolves to `data/alumni_gts3.csv` (was `data/alumni.csv`).
- Update `Services/RiasecService.cs`:
  - `MatchJobs(...)` reads `JobTitle` instead of `JobPosition` (column rename) and uses `RiasecDominant` (full name) instead of parsing first letter of `Interest`.
  - `ComputeCareerClarity(...)` populates the new `ClarityBucket` field on the result (see §6 thresholds).
  - `GetSkillOptions()` returns the 7 canonical Competencies values from §3.
- Update `Controllers/DatasetController.cs` filter param from `course` to `honors`.
- **Wipe** `src/CareerPath.Server/App_Data/careerpath.db` if it exists (schema is incompatible). The DB will be recreated on next launch via `EnsureCreated`.

### Agent C (Client) — owns `src/CareerPath.Client/**`
- Update `Pages/Dataset.razor`: replace Course filter dropdown with Honors filter (4 values from §3); replace Course column in the table with Honors + CareerClarity columns; update column for `JobPosition`→`JobTitle`.
- Update `Pages/Insights.razor`: feature importance bar chart is auto-driven by `metrics.FeatureImportances` so should mostly just work; update any hardcoded references to "Course" or 5-feature counts.
- Update `Pages/RiasecAssessment.razor`: after the assessment report renders, add a new "Predict My Career Outcome" section. It contains:
  - GWA dropdown (4 values)
  - Honors dropdown (4 values)
  - Competencies multi-select OR textarea (use the same 7 canonical values from §3)
  - "Predict" button that POSTs to `/api/predict` with `StudentInput` populated from: `yearGraduated = DateTime.Now.Year`, `gwa = <dropdown>`, `honors = <dropdown>`, `competencies = <selected joined by ", ">`, `careerClarity = finalResult.Clarity.ClarityBucket`, `interest = finalResult.DominantCode`
  - Result panel below the button: badge with PredictedOutcome, confidence %, both class probabilities as bars, interpretation text, similar alumni jobs as chips
- Update `Pages/Home.razor`: remove any "Course" mention in copy.
- Keep MudBlazor styling consistent with the existing pages (NVSU green `#007F3B`, gold `#FFD700`, Source Serif 4 headings).

### Agent D (Docs) — owns repo-root `*.md` files
- Update `MANUSCRIPT_EDITS.md`:
  - **Reverse** old edit #5 (Career Clarity is back IN as a feature)
  - **Add** new edit: "Course (BSIT/BSCS/BSIS) was removed because GTS 3 doesn't collect it"
  - **Add** new edit: "GWA reframed as 4-level categorical (Outstanding/Very Satisfactory/Satisfactory/Fair) reflecting the GTS 3 instrument, not numeric 1.00–5.00"
  - **Add** new edit: "Honors added as a feature"
  - Update the manuscript-page-22 feature list to: GWA (categorical), Year Graduated, Honors, Competencies, Career Clarity, RIASEC Interest (6 features)
  - Update the "n=301" caveat to note that the dataset has been re-collected with the GTS 3 instrument and now includes Career Clarity (previously missing)
- Update `CLAUDE_CODE_HANDOFF.md`:
  - Section 3.2 (Features the manuscript names): replace the old 5-feature list with the new 6-feature list
  - Section 3.4 (Data leakage): keep but update column names to match GTS 3 (`JobTitle` not `JobPosition`, `FieldOfIndustry` same, etc.)
  - Section 3.6 (Realistic accuracy expectation): note that the retrained metrics will replace the old 67% / 70% / 57% numbers — the new metrics live in `metrics.json` after retraining
  - Add a "Migration history: 2026-05 GTS 3 cutover" section at the end pointing to this spec file
- Update `CLAUDE.md`: now that the migration is done, this file should describe the **current** state of the codebase (what was migrated, where the new dataset lives, the locked feature set), not the open-decision list. Strip out the "open decisions not yet answered" section entirely.

---

## 9. Build verification (each agent runs at least once)

After making changes, each agent runs:
```
dotnet build CareerPath.sln
```
from the repo root and confirms it succeeds with zero errors. Warnings about LF/CRLF or `Nullable` reference type warnings are OK; `error CS....` is not.

ML agent additionally runs:
```
dotnet run --project src/CareerPath.ML
```
and confirms the four output files (model.zip, metrics.json, riasec_model.zip, riasec_metrics.json) are produced.

---

## 10. Out of scope for agents

- Browser end-to-end testing (orchestrator does this in Phase 3)
- Cross-validation experiments / hyperparameter tuning (manuscript locks RF defaults)
- Adding new features beyond the locked 6
- Switching the binary target back to 4-class
- Adding SVM (Objective 6 only requires RF + DT + LR)
- Adding new pages or removing existing ones (only modify the listed pages)
- Touching `appsettings.json`, OpenAI integration, `.csproj` package versions

---

## 11. If you get stuck

If a contract clash forces you to choose between violating the spec and producing broken code, **stop and report the conflict** in your final summary. Do not silently change a contract. The orchestrator will resolve it and update the spec for the next agent.
