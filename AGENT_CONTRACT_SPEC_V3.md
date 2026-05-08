# Agent Contract Spec V3 — Diagnostic 4×2 + 4×4 Confusion Matrices

**Read this entire document before writing any code.** Companion to V1 (`AGENT_CONTRACT_SPEC.md`) and V2 (`AGENT_CONTRACT_SPEC_V2.md`). All earlier contracts still apply — V3 only adds capability.

---

## 1. What V3 adds

Two diagnostic confusion matrices on the Insights page, **alongside the canonical 2×2 (which stays unchanged)**:

- **4×2** — rows = actual 4-class `CareerAlignment`, columns = predicted binary outcome from the canonical RF. Computed by running the binary model over all 301 alumni and tabulating by sub-class. Tells the panel: "of all Core ICT alumni, how many did the binary model correctly tag as ICT-Aligned?"

- **4×4** — rows = actual 4-class, columns = predicted 4-class. Requires a SEPARATE multiclass model (`model_4class.zip`) trained alongside the binary model. Used only for diagnostic display — not consumed by `/api/predict`.

The canonical binary model (`model.zip`) and binary metrics (`metrics.json`) are **unchanged**. V3 is purely additive.

### The n=6 caveat

The 4-class breakdown is severely imbalanced:
- Core ICT: 48 / ICT-Adjacent: 107 / Non-ICT but Adjacent: 140 / **Non-ICT: 6**

Six rows for "Non-ICT" means an 80/20 split leaves ~1 test row. The 4×4 will show "Non-ICT" predicted almost never — that's not a bug, it's the data. The Insights page must label this caveat clearly (see §6).

---

## 2. Locked contracts (DO NOT MODIFY)

These are already in place. Read but do not edit:

- `src/CareerPath.Shared/Models.cs` — new types: `DiagnosticConfusionMatrices`, `ConfusionMatrix4x2`, `ConfusionMatrix4x4`
- `src/CareerPath.ML/AlumniData.cs` — new types: `CareerAlignment4ClassFeatures`, `CareerAlignment4ClassPrediction`
- `src/CareerPath.Server/Data/AppDbContext.cs` — unchanged from V2 (no DB persistence for diagnostics — they're recomputed at training time)

---

## 3. The diagnostic_metrics.json contract

The trainer writes this file to `src/CareerPath.Server/MLModel/diagnostic_metrics.json`. The server reads it on demand via `GET /api/metrics/diagnostic` (no DB ingest, no startup hook — just file → DTO → response).

Shape (matches `DiagnosticConfusionMatrices`):

```json
{
  "computedAt": "2026-05-08T...",
  "binaryByActual4Class": {
    "actualLabels": ["Core ICT", "ICT-Adjacent", "Non-ICT but Adjacent", "Non-ICT"],
    "predictedLabels": ["ICT-Aligned", "Non-ICT-Aligned"],
    "counts": [[47,1],[98,9],[12,128],[0,6]],
    "rowTotals": [48, 107, 140, 6]
  },
  "multiclass4x4": {
    "labels": ["Core ICT", "ICT-Adjacent", "Non-ICT but Adjacent", "Non-ICT"],
    "counts": [[5,3,0,0],[3,15,3,0],[0,2,18,0],[0,0,1,0]],
    "rowTotals": [8, 21, 20, 1]
  },
  "multiclass4ClassAccuracy": 0.76,
  "multiclass4ClassMacroF1": 0.42,
  "multiclass4ClassTrainRows": 251,
  "multiclass4ClassTestRows": 50
}
```

Label ordering MUST be **exactly**: `["Core ICT", "ICT-Adjacent", "Non-ICT but Adjacent", "Non-ICT"]` for both matrices, in both their actual and predicted axes (4×4). The client renders by index, not by label-matching.

---

## 4. Agent I (ML) — owns `src/CareerPath.ML/**` AND `MLModel/diagnostic_metrics.json` + `MLModel/model_4class.zip`

### Responsibilities

1. **Add a `TrainDiagnostic(string csvPath, string outputDir)` method to `CareerPathTrainer`**. Steps:
   - Reload the data via the existing `LoadData(csvPath)`.
   - Compute the **4×2 matrix** (`BinaryByActual4Class`):
     - Load the just-saved `model.zip` (or hold a reference to the in-memory `rfModel` from `TrainAll`)
     - Run binary predictions for all 301 alumni (use `LoadFromEnumerable<StudentFeatures>(...)` + `model.Transform(...)`)
     - Tabulate: rows indexed by `CareerAlignment` (Core ICT / ICT-Adjacent / Non-ICT but Adjacent / Non-ICT), columns by predicted outcome (ICT-Aligned if `PredictedLabel==true`, else Non-ICT-Aligned)
   - Train a NEW **4-class multiclass model**:
     - Reuse the same feature pipeline used by binary (one-hot Gwa/Honors/CareerClarity, TF-IDF Competencies/Interest, concat YearGraduated, normalize)
     - Use `_mlContext.Transforms.Conversion.MapValueToKey("Label", "CareerAlignment")` to convert the 4-class string to a key
     - Trainer: `_mlContext.MulticlassClassification.Trainers.OneVersusAll(_mlContext.BinaryClassification.Trainers.FastForest(numberOfTrees: 100, numberOfLeaves: 20, minimumExampleCountPerLeaf: 5))` — One-vs-All wraps the same FastForest used by the binary model so the comparison is honest. Add `_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel")` at the end so the prediction emits the original string label.
     - 80/20 split with seed=42 (same as binary)
     - Save model to `<outputDir>/model_4class.zip`
   - Compute the **4×4 matrix** by evaluating the multiclass model on the test fold:
     - Use `_mlContext.MulticlassClassification.Evaluate(predictions)` to get `MicroAccuracy` and per-class metrics
     - Tabulate counts manually by zipping actual labels with predicted labels (canonical 4-element label order)
   - Compute `Multiclass4ClassMacroF1`: simple unweighted mean of per-class F1 scores (handle divide-by-zero — if precision+recall=0 for a class, F1=0 for that class)
   - Return a `DiagnosticConfusionMatrices` DTO and let `Program.cs` write the JSON

2. **Update `Program.cs`** in `CareerPath.ML/` so AFTER the existing `trainer.TrainAll(...)` and `trainer.CrossValidate(...)` calls, it ALSO runs `var diag = trainer.TrainDiagnostic(csvPath, outputDir);` and writes `diag` to `<outputDir>/diagnostic_metrics.json` with `WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase`.

3. **Build + run:**
   - `dotnet build src/CareerPath.ML/CareerPath.ML.csproj` — must succeed
   - `dotnet run --project src/CareerPath.ML` — must produce both `diagnostic_metrics.json` and `model_4class.zip` under `src/CareerPath.Server/MLModel/`

### Trainer-side rules

- The canonical `model.zip`, `metrics.json`, `cv_metrics.json`, `riasec_model.zip`, `riasec_metrics.json` are **untouched**. V3 only writes new files (`diagnostic_metrics.json`, `model_4class.zip`).
- Use the existing `MLContext(seed: 42)` for reproducibility
- Algorithm name in diagnostic JSON: doesn't matter, the server doesn't key off it — only the DTO field shapes matter
- Do NOT modify `Models.cs`, `AlumniData.cs`, or `AppDbContext.cs`
- Do NOT add new NuGet packages
- Do NOT commit anything

### Sanity check

After running, the 4×2 row totals MUST sum to 301 (`48+107+140+6`). The 4×4 row totals MUST sum to ~50 (the test fold). If either is off, debug.

### Report back

- Both confusion matrices in plain-text form (counts, with row totals)
- The multiclass test-fold accuracy + macro F1 (expected: ~50–60% accuracy, lower than binary 76% — that's the point of the n=6 caveat)
- Build result
- Any contract conflicts

---

## 5. Agent J (Server) — owns `src/CareerPath.Server/**` EXCEPT `Data/AppDbContext.cs`

### Responsibilities

1. **Extend the existing metrics endpoint** — add a new method to whichever controller currently hosts `/api/metrics` (probably `DatasetController` per the codebase):
   - `GET /api/metrics/diagnostic` → `DiagnosticConfusionMatrices`
   - Return 404 with `{ "error": "Diagnostic metrics not yet generated" }` if the file doesn't exist
   - Use `JsonSerializer.Deserialize<DiagnosticConfusionMatrices>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })`

2. **Update `Program.cs`** to resolve a `diagnosticMetricsPath` using the existing `ResolveContentPath` pattern and stash it in configuration (same approach as the existing `metricsPath`). Key name: `MLModel:DiagnosticMetricsPath`.

3. **Build:** `dotnet build src/CareerPath.Server/CareerPath.Server.csproj` — must succeed.

### Server-side rules

- Do NOT touch `Data/AppDbContext.cs`, `Services/PredictionService.cs`, `Services/RiasecService.cs`, `Services/OpenAiService.cs`, V1/V2 controllers (`PredictController`, `DatasetController`, `RiasecController`, `TrainingHistoryController`) — except for adding the diagnostic GET method to `DatasetController` if that's where `/api/metrics` lives.
- Do NOT add NuGet packages
- Do NOT commit anything
- Diagnostic file may not exist yet (Agent I may run after server boots) — endpoint must return 404 gracefully, not crash

### Report back

- File-by-file change list
- Build result
- Confirm the 404 path works

---

## 6. Agent K (Client) — owns `src/CareerPath.Client/**`

### Responsibilities

1. **Update `Pages/Insights.razor`** — DO NOT remove or move the existing 2×2 confusion matrix. Add **two NEW panels** immediately below the existing 2×2 block:

   ### Panel B — "Diagnostic: predictions per actual sub-class (4×2)"

   - Fetch `GET /api/metrics/diagnostic` on `OnInitializedAsync` (gracefully handle 404 — show a small info alert "Diagnostic metrics not yet generated. Run `dotnet run --project src/CareerPath.ML` to populate.")
   - Render as a 5-column × 5-row HTML table:
     - Header row: blank corner, "Predicted: ICT-Aligned", "Predicted: Non-ICT-Aligned", "Row total", "Correct?" (where "Correct?" shows what the binary collapse rule expects — for Core ICT and ICT-Adjacent, "ICT-Aligned" is correct; for the other two, "Non-ICT-Aligned" is correct)
     - 4 data rows, one per actual class in the canonical order
   - Cell coloring:
     - Cells matching the correct collapse: green tint, intensity proportional to count / rowTotal
     - Cells contradicting the collapse: red tint, same intensity scheme
   - Below the table, a one-sentence explainer: "This breaks down the canonical binary model's predictions by the original 4-class CareerAlignment label, showing how each sub-class is treated."

   ### Panel C — "Diagnostic: 4-class confusion matrix (4×4)"

   - Render as a 6-column × 5-row HTML table (4 actual rows + header, 4 prediction columns + axis label + row totals)
   - The diagonal (correct predictions) gets green tint, off-diagonal gets red
   - Below the table, the multiclass accuracy + macro-F1 as small chip-style stats: `Multiclass accuracy: NN.N% (test fold n=NN)` and `Macro-F1: NN.N%`
   - **Caveat callout**: a yellow `MudAlert Severity="Severity.Warning" Variant="Variant.Outlined"` containing: "The 'Non-ICT' class has only 6 alumni in the dataset — fewer than 2 land in the test fold. Predictions for this class are statistically unreliable. The 4×4 view is provided for transparency, not as a basis for the canonical binary metrics reported above."
   - Style the panel header with the existing NVSU green strip; smaller font than the 2×2 panel so visual hierarchy makes clear this is secondary

2. **Build:** `dotnet build src/CareerPath.Client/CareerPath.Client.csproj` — must succeed

### Client-side rules

- The existing 2×2 confusion matrix STAYS — do not delete, move, or restyle it
- Do NOT touch any other page
- Do NOT add new NuGet packages
- Do NOT commit anything
- If `/api/metrics/diagnostic` returns 404, show the info alert and skip rendering both panels

### Report back

- One-line summary per file edited
- Build result
- Visual decisions (especially the heatmap intensity formula and the caveat alert style)
- Any contract conflicts

---

## 7. Out of scope for V3

- DB persistence of diagnostic matrices (training overwrites the JSON each run — that's fine for now)
- 4-class CV (overkill given n=6 in one class)
- Per-class precision/recall in the 4×4 (just accuracy + macro-F1)
- Updating the binary model — the canonical model and metrics are frozen
- Manuscript edits — V3 is a defensive transparency feature; if the panel asks for the 4×4, the manuscript needs no edit. If you (the orchestrator) think it does, defer to the user

## 8. If you get stuck

Same as V1/V2 — stop and report; don't silently change a contract.
