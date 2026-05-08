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
├── MANUSCRIPT_EDITS.md              Required thesis manuscript changes
├── ARCHITECTURE.md                  Full technical documentation
├── data/
│   ├── alumni_raw.csv               Original CHED GTS data (untouched)
│   └── alumni.csv                   Cleaned data with binary CareerOutcome
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
| GET | `/api/dataset?page=1&pageSize=50&course=BSIT&search=foo` | — | Paginated `AlumniRecord[]` |
| GET | `/api/dataset/summary` | — | `DatasetSummary` with counts and stats |
| GET | `/api/metrics` | — | `ModelMetrics` (Random Forest only) |
| GET | `/api/metrics/comparison` | — | `ModelComparison` (RF + DT + LR) |

---

## Model Results

### Performance (80/20 stratified split, seed=42)

| Algorithm | Accuracy | Precision | Recall | F1 Score | AUC |
|-----------|----------|-----------|--------|----------|-----|
| **Random Forest (FastForest)** | **76.00%** | **77.42%** | **82.76%** | **80.00%** | **82.76%** |
| Logistic Regression (LBFGS) | 74.00% | 76.67% | 79.31% | 77.97% | 77.18% |
| Decision Tree (FastTree) | 70.00% | 73.33% | 75.86% | 74.58% | 76.19% |

### Feature Importance (Random Forest - PFI)

| Feature | Importance |
|---------|-----------|
| Interest | 57.59% |
| Competencies | 26.58% |
| GWA | 15.19% |
| Year Graduated | 0.63% |
| Course | 0.00% |

Key finding: **Interest is the strongest predictor** of career alignment, followed by Competencies, then GWA.

### Confusion Matrix (Random Forest)

|  | Predicted ICT | Predicted Non-ICT |
|--|--------------|-------------------|
| **Actual ICT** | 24 (TP) | 5 (FN) |
| **Actual Non-ICT** | 7 (FP) | 14 (TN) |

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

1. Update or replace `data/alumni.csv` with new records (keep the same column headers)
2. Run `dotnet run --project src/CareerPath.ML`
3. The trainer outputs `model.zip` and `metrics.json` to `src/CareerPath.Server/MLModel/`
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
- [ ] Sample input ready to type: Course=BSIT, Year=2024, GWA=2.5, Competencies="Technical Skills, Problem Solving", Interest="Software Development"

---

## Limitations

- **Small dataset (n=301):** Future work should expand across multiple HEIs
- **Class distribution:** 155 ICT-Aligned vs 146 Non-ICT-Aligned (nearly balanced, but small)
- **Model accuracy (76%):** Reflects feature limitations and dataset size, not bugs. Substantially outperforms the 51.5% majority-class baseline
- **Cross-school generalization:** Untested; trained exclusively on NVSU CITE alumni
- **Career Clarity:** Dropped because the column was empty (0/301 rows had values)
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
