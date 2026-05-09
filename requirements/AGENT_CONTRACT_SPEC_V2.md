# Agent Contract Spec V2 — Cross-Validation, 4-class Display, RIASEC×Industry

**Read this entire document before writing any code.** Companion to `AGENT_CONTRACT_SPEC.md` (the GTS 3 migration spec). All V1 contracts still apply — V2 only adds capabilities.

---

## 1. What V2 adds

Three new capabilities:

1. **Cross-validation metrics** (10-fold AND 20-fold) for RF/DT/LR, persisted to SQLite, surfaced on Insights as "CV Stability"
2. **4-class CareerAlignment display** in the dataset summary (alongside the existing 2-class CareerOutcome)
3. **RIASEC × Field_Industry cross-tab** as a sanity-check panel on Insights

The existing 80/20 split + `metrics.json` flow is **unchanged** — V2 is additive. The 80/20 single split remains the canonical model selection method per Objective 4; CV metrics are reported as stability validation only.

---

## 2. Locked contracts (DO NOT MODIFY)

These have been written by the orchestrator. Read but do not edit:

- `src/CareerPath.Shared/Models.cs` — see new types: `CrossValidationReport`, `CvAlgorithmRun`, `CvFoldMetric`, `TrainingRunDto`. `DatasetSummary` now has `ByCareerAlignment` and `ByFieldIndustryByRiasec`.
- `src/CareerPath.Server/Data/AppDbContext.cs` — new entities: `TrainingRun`, `TrainingFold`. Existing `PredictionLog` unchanged.

The DB will auto-migrate on next server boot via `EnsureCreated`. **There is no existing data to preserve** in the new tables; they're brand new.

---

## 3. The cv_metrics.json contract

The trainer writes this file to `src/CareerPath.Server/MLModel/cv_metrics.json`. The server reads it on startup and ingests into `TrainingRun` + `TrainingFold` rows (idempotently — see §6).

Shape (matches `CrossValidationReport`):

```json
{
  "datasetVersion": "alumni_gts3.csv@2026-05-08",
  "trainedAt": "2026-05-08T12:34:56Z",
  "runs": [
    {
      "algorithm": "Random Forest",
      "foldCount": 10,
      "meanAccuracy": 0.71, "stdAccuracy": 0.058,
      "meanPrecision": 0.74, "stdPrecision": 0.082,
      "meanRecall": 0.69, "stdRecall": 0.094,
      "meanF1": 0.71, "stdF1": 0.067,
      "meanAuc": 0.78, "stdAuc": 0.054,
      "folds": [
        { "foldIndex": 0, "accuracy": 0.73, "precision": 0.75, "recall": 0.72, "f1": 0.73, "auc": 0.79 },
        { "foldIndex": 1, ... }
      ]
    },
    { "algorithm": "Random Forest", "foldCount": 20, ... },
    { "algorithm": "Decision Tree", "foldCount": 10, ... },
    { "algorithm": "Decision Tree", "foldCount": 20, ... },
    { "algorithm": "Logistic Regression", "foldCount": 10, ... },
    { "algorithm": "Logistic Regression", "foldCount": 20, ... }
  ]
}
```

**Six runs total:** {RF, DT, LR} × {10-fold, 20-fold}.

`stdAccuracy` and friends = sample standard deviation across the folds (Bessel-corrected, n-1 in the denominator).

---

## 4. Agent E (ML) — owns `src/CareerPath.ML/**` AND the new `cv_metrics.json` output

### Responsibilities

1. **Add a `CrossValidate(string csvPath, string outputJsonPath)` method to `CareerPathTrainer`.** It must:
   - Load data via the existing `LoadData(csvPath)` (which derives the binary `Label` from `CareerAlignment` via CustomMapping — already in place; do NOT re-implement)
   - Build the same feature pipeline as `BuildFeaturePipeline()` so CV metrics are comparable to the 80/20 metrics
   - Use ML.NET's `MLContext.BinaryClassification.CrossValidate(data, estimator, numberOfFolds: N, labelColumnName: "Label", samplingKeyColumnName: null, seed: 42)` for each (algorithm × foldCount) combo
   - For each fold extract Accuracy / PositivePrecision / PositiveRecall / F1Score / AreaUnderRocCurve into a `CvFoldMetric`
   - Compute per-algorithm-per-foldcount Mean and (Bessel-corrected) sample StdDev for each metric
   - Serialize the full `CrossValidationReport` to `outputJsonPath` with indented JSON

2. **Update `CareerPath.ML/Program.cs`** so it calls `CrossValidate(...)` after the existing `TrainAll(...)`. Output path: `src/CareerPath.Server/MLModel/cv_metrics.json` (use the same path-resolution pattern as the existing `metrics.json` output).

3. **Build + run** `dotnet run --project src/CareerPath.ML` and confirm `cv_metrics.json` is produced with all 6 runs populated. Sanity check: each algorithm's mean accuracy at fold-count 10 should be within 5 percentage points of its 80/20 accuracy in `metrics.json` (RF ≈ 76%, DT ≈ 78%, LR ≈ 74%).

### Trainer-side rules

- Use `MLContext(seed: 42)` for reproducibility (already the existing pattern)
- Do NOT touch `model.zip` / `metrics.json` / `riasec_*.{zip,json}` — those are V1 outputs and stay as-is
- `Algorithm` name strings MUST be exactly: `"Random Forest"`, `"Decision Tree"`, `"Logistic Regression"` (the server uses these as natural keys for ingest matching)
- `datasetVersion` = `$"alumni_gts3.csv@{File.GetLastWriteTimeUtc(csvPath):yyyy-MM-dd}"` (lets the server detect re-trains on the same dataset version vs. new dataset)
- Do NOT modify `Models.cs` or `AppDbContext.cs` — locked
- Do NOT commit anything

### Report back

- Paste the 6-row mean accuracy table (algorithm × foldCount)
- Note any algorithm whose CV mean differs from its 80/20 number by > 5 percentage points (potential signal that the 80/20 split is unrepresentative)

---

## 5. Agent F (Server) — owns `src/CareerPath.Server/**` EXCEPT `Data/AppDbContext.cs` (locked)

### Responsibilities

1. **Update `Services/DatasetService.cs`** — `GetSummary()` now also populates:
   - `ByCareerAlignment`: 4-way distribution of the raw `CareerAlignment` column
   - `ByFieldIndustryByRiasec`: outer key = `RiasecDominant` (full name), inner key = `FieldOfIndustry`, value = count. Only include alumni who have both fields populated (skip blanks).

2. **Create `Services/TrainingHistoryService.cs`** with:
   - `Task IngestAsync(string cvJsonPath, AppDbContext db)`: reads the JSON, idempotently writes `TrainingRun` + `TrainingFold` rows. Idempotency rule: a `TrainingRun` is considered "already ingested" if there exists a row with same `Algorithm` AND same `FoldCount` AND same `TrainedAt` (UTC ticks comparison). If found, skip; else insert with all child folds.
   - `List<TrainingRunDto> GetLatest(AppDbContext db)`: returns the most-recent `TrainingRun` per (Algorithm × FoldCount) — 6 rows max — with their folds attached.
   - `List<TrainingRunDto> GetHistory(string algorithm, int foldCount, AppDbContext db)`: returns up to 20 most recent runs for a single (algo, foldCount) combo, oldest first.

3. **Create `Controllers/TrainingHistoryController.cs`**:
   - `GET /api/training/latest` → `List<TrainingRunDto>` (latest per algo+foldCount)
   - `GET /api/training/history?algorithm=Random Forest&foldCount=10` → `List<TrainingRunDto>`

4. **Update `Program.cs`** to:
   - Add a startup hook (after `db.Database.EnsureCreated()`) that resolves `cv_metrics.json` (use same `ResolveContentPath` pattern), and if it exists calls `TrainingHistoryService.IngestAsync(...)`. Wrap in try/catch — log errors but don't crash the app.
   - Register `TrainingHistoryService` in the DI container.

5. **Build:** `dotnet build src/CareerPath.Server/CareerPath.Server.csproj` must succeed.

### Server-side rules

- Do NOT touch `Data/AppDbContext.cs` — locked
- Do NOT touch `Services/PredictionService.cs`, `Services/RiasecService.cs`, `Services/OpenAiService.cs`, or any V1 controller — they are working
- Do NOT modify the existing `cv_metrics.json` schema; treat it as read-only
- Reuse the existing `appsettings.json` — do NOT add new config keys
- Do NOT commit anything

### Report back

- File-by-file change list
- Build result
- Confirm `EnsureCreated` will create both new tables on first server boot

---

## 6. Agent G (Client) — owns `src/CareerPath.Client/**`

### Responsibilities

1. **Update `Pages/Insights.razor`** — add THREE new sections, in this order, after the existing "Feature Importance" bar chart and before the existing "Training Info" section:

   **Section A: "Cross-Validation Stability"**
   - Fetch `GET /api/training/latest` on `OnInitializedAsync`
   - For each algorithm (3 rows × 2 fold-counts = 6 cards, or a 3×2 grid), show a card containing:
     - Header: `Algorithm — N-fold` (e.g. "Random Forest — 10-fold")
     - Mean Accuracy as a big number with ± StdAccuracy below it (e.g. "71.0% ± 5.8%")
     - A small inline mini-bar chart of per-fold accuracies (the `Folds` array). Use vertical `MudProgressLinear` slivers or simple CSS `<div>` bars side-by-side.
     - Below: 4 small numbers — Precision / Recall / F1 / AUC — each as `Mean ± Std`
   - If `/api/training/latest` returns empty (e.g. trainer hasn't been run yet), show a friendly "Cross-validation metrics not yet available — run `dotnet run --project src/CareerPath.ML` to generate them."

   **Section B: "Career Alignment Breakdown"**
   - Use `summary.byCareerAlignment` (already populated by server)
   - Render 4 horizontal bars (Core ICT / ICT-Adjacent / Non-ICT but Adjacent / Non-ICT) with counts and percentage of total
   - Below the 4 bars, a small caption block showing the binary collapse rule:
     `Core ICT + ICT-Adjacent → ICT-Aligned (155 students, 51.5%)`
     `Non-ICT but Adjacent + Non-ICT → Non-ICT-Aligned (146 students, 48.5%)`

   **Section C: "RIASEC × Industry Cross-tab" (sanity check)**
   - Use `summary.byFieldIndustryByRiasec`
   - Render as a `MudTable` with rows = RIASEC dimensions (Conventional / Investigative / etc.), columns = top 6–8 most common Field_Industry values across the whole dataset. Cells = counts.
   - Goal: show that Investigative/Realistic alumni cluster in Technology/Engineering, Conventional clusters in Government/Admin, etc. — visually validating that RIASEC dimensions map to industries the dataset reflects.
   - Add a one-sentence intro: "This cross-tab confirms that the RIASEC dimensions in the assessment correspond to actual industry placements observed in the dataset."

2. **Update `Pages/Dataset.razor`** — add a `CareerAlignment` column to the `MudTable` (4-class) immediately to the LEFT of the existing `CareerOutcome` column (2-class). Use a `MudChip` with 4 distinct colors:
   - Core ICT → `Color.Success` (green)
   - ICT-Adjacent → `Color.Primary`
   - Non-ICT but Adjacent → `Color.Warning`
   - Non-ICT → `Color.Default` (grey)

3. **Build:** `dotnet build src/CareerPath.Client/CareerPath.Client.csproj` must succeed.

### Client-side rules

- Match the existing MudBlazor styling (NVSU green `#007F3B` headers, gold `#FFD700` accents, `Source Serif 4` headings)
- Do NOT add new pages or remove existing ones
- Do NOT add new NuGet packages
- Do NOT touch `Predict.razor`, `Home.razor`, `About.razor`, `RiasecAssessment.razor`
- Do NOT commit anything

### Report back

- File-by-file changes (one-line each)
- Build result
- Visual decisions for the 6 CV cards (especially the per-fold mini-bar visualization)

---

## 7. Agent H (Docs) — owns `MANUSCRIPT_EDITS.md` only

### Responsibilities

1. **Add Edit #10 to `MANUSCRIPT_EDITS.md`** (append after the existing Edit #9 — do not renumber existing edits, do not rewrite the file):

   - Topic: "10-fold and 20-fold cross-validation for stability validation"
   - Old wording: whatever the manuscript currently says about Objective 4 evaluation methodology (probably along the lines of "the model was evaluated on a held-out test set using accuracy, precision, recall, and F1-score")
   - New wording: append the canonical 80/20 split paragraph, then add: "In addition to the canonical 80/20 stratified split, the system reports 10-fold and 20-fold cross-validation metrics (mean ± standard deviation per fold) to validate that the reported accuracy is not an artifact of the test fold composition. Cross-validation results are persisted to the SQLite training history database and surfaced in the Insights dashboard. The 80/20 split remains the canonical model selection method; cross-validation is reported for stability evaluation only."
   - Why: "Defense panelists may ask whether 76% accuracy on n=50 test rows is robust. CV mean ± stddev across 10 (or 20) folds gives a quantitative answer that the single-split metric cannot."

2. **Optional — if you notice the manuscript's existing Edit #9 (Limitations) doesn't mention CV stddev, append a sentence to it referencing the new CV approach.** Use judgment.

### Docs-side rules

- ONLY modify `MANUSCRIPT_EDITS.md`
- Do NOT renumber existing edits
- Do NOT touch `CLAUDE.md`, `CLAUDE_CODE_HANDOFF.md`, `README.md`, `ARCHITECTURE.md`, `AGENT_CONTRACT_SPEC.md`, this file, or any code
- Do NOT commit anything

### Report back

- Confirm Edit #10 was appended (not inserted/renumbering)
- Whether you also touched Edit #9
- Any wording suggestions you'd flag for the researcher

---

## 8. Build verification (each agent)

Same as V1 — each agent builds its own project and confirms zero errors. Orchestrator runs the full solution build in Phase 3b.

## 9. Out of scope for V2

- New ML algorithms (still RF + DT + LR only)
- Hyperparameter tuning across folds (run with the same defaults as V1)
- Stratified k-fold (use simple k-fold; ML.NET's `CrossValidate` is fine)
- Showing CV per-fold metrics in the Dataset page (Insights only)
- Deleting old `TrainingRun` rows (they accumulate — that's the point of "training history")
- Multipage history view (one Insights section is enough; the `/api/training/history` endpoint is there for future expansion)

## 10. If you get stuck

Same as V1 — stop and report a contract conflict; don't silently change a contract.
