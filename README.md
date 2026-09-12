# Career Path Recommendation System

A web application that predicts whether a graduating CITE student's post-graduation career outcome will be **ICT-Aligned** or **Non-ICT-Aligned**, using a Random Forest model trained on 301 historical alumni records from a CHED Graduate Tracer Study.

Developed as a masteral thesis deliverable for the College of Information Technology Education (CITE), Nueva Vizcaya State University - Bayombong Campus.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022 / VS Code with C# extension / JetBrains Rider

## Quick Start

```bash
git clone <repo-url>
cd CiteTracker

# 1. Restore all packages
dotnet restore

# 2. Train the ML models (produces model.zip and metrics.json)
dotnet run --project src/CareerPath.ML

# 3. Run the web application
dotnet run --project src/CareerPath.Server
```

Then open **http://localhost:5001** in your browser.

| URL | Purpose |
|-----|---------|
| http://localhost:5001 | Main application |
| https://localhost:7001 | HTTPS (requires trusted dev cert) |
| http://localhost:5001/swagger | Swagger API documentation |

---

## Project Structure

```
CiteTracker/
├── CareerPath.sln                   Solution file (4 projects)
├── README.md                        This file
├── requirements/                    Project specs, handoff notes, architecture, manuscript edits
│   ├── AGENT_CONTRACT_SPEC*.md      Locked contracts (V1–V4) for the GTS 3 feature pipeline
│   ├── ARCHITECTURE.md              End-to-end prediction walkthrough
│   ├── CLAUDE_CODE_HANDOFF.md       Full project brief (gitignored)
│   └── MANUSCRIPT_EDITS.md          Required thesis manuscript changes (gitignored)
├── data/
│   ├── alumni_raw.csv               Legacy CHED GTS data (kept for traceability, not used by code)
│   ├── alumni.csv                   Legacy cleaned dataset (kept for traceability, not used by code)
│   └── alumni_gts3.csv              Canonical GTS 3 dataset (n=301, 14 columns) used by trainer + server
├── images/
│   └── nvsulogo.png                 NVSU official logo
└── src/
    ├── CareerPath.Shared/           Shared DTOs (referenced by all projects)
    │   └── Models.cs
    ├── CareerPath.ML/               ML training console app
    │   ├── AlumniData.cs            ML.NET input/output schemas
    │   ├── CareerPathTrainer.cs     RF + DT + LR training + PFI
    │   └── Program.cs              Entry point
    ├── CareerPath.Server/           ASP.NET Core API + Blazor host
    │   ├── Program.cs              Server startup and configuration
    │   ├── appsettings.json
    │   ├── Data/AppDbContext.cs     SQLite EF Core context
    │   ├── Services/
    │   │   ├── DatasetService.cs    CSV loading + filtering + summary
    │   │   └── PredictionService.cs Model loading + inference
    │   ├── Controllers/
    │   │   ├── PredictController.cs POST /api/predict + GET /api/predict/status
    │   │   └── DatasetController.cs GET /api/dataset, /summary, /metrics
    │   └── MLModel/                 Generated after training
    │       ├── model.zip            Serialized Random Forest model
    │       └── metrics.json         All three models' evaluation metrics
    └── CareerPath.Client/           Blazor WebAssembly frontend
        ├── Program.cs              WASM entry + MudBlazor services
        ├── App.razor               Theme (NVSU green/gold) + routing
        ├── Layout/
        │   ├── MainLayout.razor    App bar with NVSU logo + sidebar
        │   └── NavMenu.razor       Navigation links
        ├── Pages/
        │   ├── Home.razor          Landing page with system overview
        │   ├── Predict.razor       Student input form + result display
        │   ├── Dataset.razor       Paginated alumni table with filters
        │   ├── Insights.razor      Metrics dashboard + charts
        │   └── About.razor         Methodology and tech stack
        └── wwwroot/
            ├── index.html          HTML shell + MudBlazor CSS/JS
            ├── css/app.css         Minimal custom styles
            └── images/nvsulogo.png NVSU logo for the UI
```

---

## API Endpoints

| Method | Route | Request Body | Response |
|--------|-------|-------------|----------|
| POST | `/api/predict` | `StudentInput` JSON | `PredictionResult` with outcome, confidence, scores |
| GET | `/api/predict/status` | — | `{ ready: true/false }` |
| GET | `/api/dataset?page=1&pageSize=50&honors=Cum%20Laude&search=foo` | — | Paginated `AlumniRecord[]` |
| GET | `/api/dataset/summary` | — | `DatasetSummary` with counts and stats |
| GET | `/api/metrics` | — | `ModelMetrics` (Random Forest only) |
| GET | `/api/metrics/comparison` | — | `ModelComparison` (RF + DT + LR) |

---

## Model Results

The canonical metrics live in **`src/CareerPath.Server/MLModel/metrics.json`** and are regenerated every time the trainer runs. Always quote that file for the manuscript and the dashboard rather than the figures below — the snapshot here is illustrative and may lag a retrain.

### Performance (80/20 stratified split, seed=42, snapshot 2026-05-08)

| Algorithm | Accuracy | Precision | Recall | F1 Score | AUC |
|-----------|----------|-----------|--------|----------|-----|
| Decision Tree (FastTree) | 0.78 | 0.846 | 0.759 | 0.800 | 0.806 |
| **Random Forest (FastForest)** | **0.76** | **0.840** | **0.724** | **0.778** | **0.811** |
| Logistic Regression (LBFGS) | 0.74 | 0.750 | 0.828 | 0.787 | 0.790 |

Random Forest remains the primary model per Objective 2, while Decision Tree and Logistic Regression are reported for the Objective 6 comparison.

### Feature Importance (Random Forest - PFI, snapshot from `metrics.json`)

| Feature | Importance |
|---------|-----------|
| CareerClarity | 44.83% |
| Interest | 36.55% |
| Competencies | 18.62% |
| YearGraduated | 0.00% |
| Gwa | 0.00% |
| Honors | 0.00% |

Key finding: **Career Clarity and RIASEC Interest dominate the signal**, with Competencies a distant third. YearGraduated, Gwa, and Honors carry effectively zero permutation importance on this split — see the limitations section for the class-imbalance note that explains this. Always copy the live percentages from `metrics.json` for the manuscript rather than restating the snapshot above.

### Confusion Matrix (Random Forest, snapshot from `metrics.json`)

|  | Predicted ICT | Predicted Non-ICT |
|--|--------------|-------------------|
| **Actual ICT** | 21 (TP) | 8 (FN) |
| **Actual Non-ICT** | 4 (FP) | 17 (TN) |

---

## Manuscript Alignment

| Thesis Objective | Implementation | File |
|-----------------|----------------|------|
| Obj 2: Random Forest recommendations | `FastForestBinaryTrainer` (100 trees) | `CareerPathTrainer.cs` |
| Obj 3: Feature selection | TF-IDF + OneHotEncoding + MinMax normalization | `CareerPathTrainer.cs:BuildFeaturePipeline()` |
| Obj 4: Train and validate | 80/20 stratified split, seed=42 | `CareerPathTrainer.cs:TrainAll()` |
| Obj 5: Accuracy, F1, Recall, Precision | All four metrics computed and displayed | `Insights.razor` |
| Obj 6: Compare RF vs DT vs LR | Three trainers, same data, comparison table | `Insights.razor` |
| Obj 7: ISO 25010 usability | Collected via external Google Forms | N/A (survey-based) |

---

## How Retraining Works

1. Update or replace `data/alumni_gts3.csv` with new records (keep the same 14-column GTS 3 header)
2. Run `dotnet run --project src/CareerPath.ML`
3. The trainer outputs `model.zip`, `metrics.json`, `riasec_model.zip`, and `riasec_metrics.json` to `src/CareerPath.Server/MLModel/`
4. Restart the server: `dotnet run --project src/CareerPath.Server`

---

## Tech Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| Runtime | .NET | 10.0 |
| ML Framework | ML.NET | 3.0.1 |
| Frontend | Blazor WebAssembly | 10.0 |
| UI Library | MudBlazor | 9.3.0 |
| Backend | ASP.NET Core Web API | 10.0 |
| Database | SQLite + EF Core | 10.0 |
| CSV Parsing | CsvHelper | 33.1.0 |
| API Docs | Swashbuckle (Swagger) | 10.1.7 |

---

## Defense Day Checklist

- [ ] Run `dotnet run --project src/CareerPath.ML` to ensure `model.zip` and `metrics.json` exist
- [ ] Run `dotnet run --project src/CareerPath.Server` and verify http://localhost:5001 loads
- [ ] Test the Predict page: submit a sample student input, verify result appears
- [ ] Test the Dataset page: verify 301 records load, filters work
- [ ] Test the Insights page: verify metrics table, confusion matrix, feature importance bars appear
- [ ] Open Swagger at `/swagger` as backup proof the API works
- [ ] Have `metrics.json` open in notepad as backup if the dashboard fails
- [ ] Sample `StudentInput` ready to paste into Swagger or the Predict page:
  ```json
  {
    "yearGraduated": 2026,
    "gwa": "Very Satisfactory",
    "honors": "Cum Laude",
    "competencies": "Information Technology Skills, Problem-Solving Skills",
    "careerClarity": "High",
    "interest": "IRC"
  }
  ```

---

## Limitations

- **Small dataset (n=301):** Future work should expand across multiple HEIs
- **Class distribution:** 155 ICT-Aligned vs 146 Non-ICT-Aligned (nearly balanced, but small)
- **Model accuracy:** See `src/CareerPath.Server/MLModel/metrics.json` for the live numbers. The classifier substantially outperforms the 51.5% majority-class baseline; remaining error reflects dataset size and the categorical nature of several features rather than bugs
- **Cross-school generalization:** Untested; trained exclusively on NVSU CITE alumni
- **Career Clarity is now a live feature.** Under the GTS 3 instrument the column is populated for all 301 records (Low 100 / Medium 164 / High 37) and the trained Random Forest assigns it the highest permutation importance (~44.83%). The earlier "Career Clarity dropped because the column was empty" caveat applied to the legacy `data/alumni.csv` and no longer holds
- **Class imbalance in categorical features:** Approximately 68% of respondents fall in the "Very Satisfactory" GWA band and approximately 83% report "No Honors". This compresses the predictive signal for those two columns and is the most likely reason their permutation importance reads as 0 on the current split
- **Text features:** Competencies and Interest are processed via TF-IDF, which may not capture semantic meaning as well as embeddings would

---

## Design Decisions

| Decision | Rationale |
|----------|-----------|
| ML.NET over scikit-learn | All-C# stack for unified development and deployment |
| FastForest for Random Forest | Microsoft's official RF implementation, inherits from `RandomForestTrainerBase` |
| Platt calibrator on FastForest | FastForest doesn't output probabilities natively; Platt scaling adds calibrated probability estimates |
| FastTree with 1 tree for DT | Simulates a single decision tree using ML.NET's boosted tree trainer |
| SQLite over SQL Server | Zero-configuration deployment suitable for thesis demonstration |
| Blazor WebAssembly | Client-side rendering, entire app runs in the browser via WebAssembly |
| MudBlazor for UI | Professional Material Design components without npm/Node.js build steps |
| Seed 42 everywhere | Fixed across MLContext and train-test split for reproducible results |
| Binary classification | Original 4-class labels collapsed to 2 (ICT-Aligned vs Non-ICT-Aligned) for reliability with small n |
| Manual PFI implementation | ML.NET's built-in PFI requires specific transformer types; manual permutation on raw features gives the same result |

---

## Build Pipeline

`azure-pipelines.yml` builds the solution and publishes a deployable artifact.
It runs on pushes to `master` and on pull requests into it; it builds and
packages only — nothing is deployed from here.

| Stage | What happens |
|-------|--------------|
| Build | Installs the .NET 10 SDK, restores (with a cached NuGet folder), builds `CareerPath.sln`, publishes `CareerPath.Server`, checks the runtime assets are present, zips the output and publishes it |

The artifact is named `careerpath-site` and holds one file,
`CareerPath.Server.zip`, laid out for IIS or Azure App Service:

```
CareerPath.Server.dll, web.config, appsettings.json
wwwroot/     published Blazor WebAssembly client
MLModel/     model.zip, model_4class.zip, metrics + cv metrics JSON
data/        alumni_gts3.csv
logs/        pre-created for AspNetCoreModuleV2 stdout logs
```

Publishing the host pulls in the client, the models and the CSV on its own, so
the zip runs as-is. The pipeline still verifies each of those paths before
packaging, so a missing model or CSV fails the build instead of producing an
artifact that starts and then 404s on `/api/metrics`.

### Before deploying the artifact

`appsettings.json` ships inside the zip with the development connection string,
the seeded admin password and a placeholder OpenAI key. Override them on the
target with environment variables or an `appsettings.Production.json` that is not
in source control:

| Setting | Environment variable |
|---------|----------------------|
| SQL Server connection | `ConnectionStrings__Default` |
| OpenAI key | `OpenAI__ApiKey` |
| Seeded admin | `Auth__AdminEmail`, `Auth__AdminPassword` |

The end-to-end suites under `tests/e2e` are not wired into the pipeline: they
need a running app, a reachable SQL Server and an OpenAI key. Run them against a
deployed environment with `python3 tests/e2e/run_all.py --base-url https://<host>`.
