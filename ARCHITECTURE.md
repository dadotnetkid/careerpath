# Architecture and Technical Documentation

This document explains every component of the Career Path Recommendation System in detail. Use this to understand the code before your thesis defense.

---

## Table of Contents

1. [System Overview](#1-system-overview)
2. [Data Pipeline](#2-data-pipeline)
3. [Machine Learning (CareerPath.ML)](#3-machine-learning)
4. [Backend API (CareerPath.Server)](#4-backend-api)
5. [Frontend (CareerPath.Client)](#5-frontend)
6. [Shared Models (CareerPath.Shared)](#6-shared-models)
7. [How a Prediction Works End-to-End](#7-how-a-prediction-works-end-to-end)
8. [Key Concepts for Defense Q&A](#8-key-concepts-for-defense-qa)

---

## 1. System Overview

The system has 4 projects in a single .NET solution:

```
┌─────────────────────────────────────────────────────────┐
│                    User's Browser                        │
│  ┌─────────────────────────────────────────────────┐    │
│  │     CareerPath.Client (Blazor WebAssembly)      │    │
│  │     Runs entirely in the browser via WASM        │    │
│  │     UI built with MudBlazor components           │    │
│  └──────────────────────┬──────────────────────────┘    │
│                         │ HTTP API calls                 │
└─────────────────────────┼───────────────────────────────┘
                          │
┌─────────────────────────┼───────────────────────────────┐
│  CareerPath.Server      │  (ASP.NET Core)               │
│  ┌──────────────────────▼──────────────────────────┐    │
│  │              Controllers                         │    │
│  │   PredictController    DatasetController          │    │
│  └──────┬───────────────────────┬──────────────────┘    │
│         │                       │                        │
│  ┌──────▼──────┐    ┌──────────▼─────────┐              │
│  │ Prediction  │    │  Dataset           │              │
│  │ Service     │    │  Service           │              │
│  │ (model.zip) │    │  (alumni.csv)      │              │
│  └─────────────┘    └────────────────────┘              │
│         │                                                │
│  ┌──────▼──────┐                                        │
│  │   SQLite    │  Prediction audit log                  │
│  │   (EF Core) │                                        │
│  └─────────────┘                                        │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│  CareerPath.ML  (Console app — run separately)          │
│  Reads alumni.csv → Trains 3 models → Saves model.zip   │
│  + metrics.json                                          │
└─────────────────────────────────────────────────────────┘
```

**Key flow:**
1. You run `CareerPath.ML` once to train the model (offline step)
2. You run `CareerPath.Server` which loads the trained model and serves both the API and the Blazor WASM client
3. The user opens the browser → the Blazor client downloads and runs in the browser → makes API calls to the server

---

## 2. Data Pipeline

### 2.1 Raw Data (`data/alumni_raw.csv`)

The original CHED Graduate Tracer Study data with columns:
- `ID`, `NAME`, `YEAR GRADUATED`, `COURSE`, `GWA`
- `JOB POSITION`, `FIELD OF INDUSTRY` (career outcomes — NOT used as features)
- `Competencies` (self-assessed skills like "Technical Skills", "Problem Solving")
- `Employment Status` (NOT used as feature — it's an outcome)
- `Career Alignment` (original 4-class label: "Core ICT-Related", "ICT-Adjascent", "Non-ICT but Adjascent", "Non-ICT")
- `Job Retention`, `Career Clarity` (Career Clarity is empty for all 301 rows)
- `Interest` (3-letter codes like "CSE", "RIC", "IRC" — these are RIASEC Holland codes)

### 2.2 What is RIASEC?

The **Interest** column contains RIASEC Holland codes. RIASEC stands for:

| Code | Type | Description | Example Careers |
|------|------|-------------|-----------------|
| **R** | Realistic | Hands-on, practical work | Engineer, Mechanic, Farmer |
| **I** | Investigative | Research, analytical thinking | Scientist, Programmer, Analyst |
| **A** | Artistic | Creative, expressive work | Designer, Writer, Artist |
| **S** | Social | Helping, teaching people | Teacher, Counselor, Nurse |
| **E** | Enterprising | Leading, persuading, selling | Manager, Entrepreneur, Sales |
| **C** | Conventional | Organizing, data management | Accountant, Administrator, Clerk |

Each student has a 3-letter code representing their top 3 interest types. For example:
- **IRC** = Investigative + Realistic + Conventional → likely aligned with ICT (analytical + hands-on + systematic)
- **ESC** = Enterprising + Social + Conventional → likely Non-ICT (business/management orientation)
- **CSE** = Conventional + Social + Enterprising → could go either way

The model treats these as text features (TF-IDF), so each unique letter/combination gets its own weight.

### 2.3 Cleaned Data (`data/alumni.csv`)

The cleaning script:
1. Renames columns to C#-friendly names (e.g., `YEAR GRADUATED` → `YearGraduated`)
2. Creates the binary `CareerOutcome` column:
   - `"Core ICT-Related"` OR `"ICT-Adjascent"` → **`"ICT-Aligned"`** (155 records)
   - `"Non-ICT but Adjascent"` OR `"Non-ICT"` → **`"Non-ICT-Aligned"`** (146 records)
3. Trims whitespace from all values

### 2.4 Data Leakage Prevention

These columns exist in the CSV but are **NEVER used as features** because they are consequences of the career outcome, not predictors:
- `JobPosition` — knowing someone is a "Software Developer" tells you they're ICT-Aligned
- `FieldOfIndustry` — knowing someone works in "Technology" leaks the answer
- `EmploymentStatus` — not a predictor of ICT vs Non-ICT
- `JobRetention` — post-employment metric
- `CareerAlignment` — the original 4-class label (directly leaks the answer)

**Only these 5 columns are used as features:** `Gwa`, `YearGraduated`, `Course`, `Competencies`, `Interest`

---

## 3. Machine Learning

### 3.1 File: `CareerPath.ML/AlumniData.cs`

Defines the ML.NET data schemas:

- **`AlumniCsvRow`** — maps to the CSV columns using `[LoadColumn(N)]` attributes. ML.NET reads the CSV into these objects.
- **`StudentFeatures`** — the input schema for predictions: `Gwa`, `YearGraduated`, `Course`, `Competencies`, `Interest`, `Label`
- **`CareerOutcomePrediction`** — the output schema: `PredictedLabel` (bool), `Score` (raw), `Probability` (0-1)

### 3.2 File: `CareerPath.ML/CareerPathTrainer.cs`

This is the core ML file. Here's what each method does:

#### `LoadData(csvPath)`
1. Reads the CSV using `LoadFromTextFile<AlumniCsvRow>`
2. Applies a `CustomMapping` to create the boolean `Label` column:
   - `CareerOutcome == "ICT-Aligned"` → `true`
   - `CareerOutcome == "Non-ICT-Aligned"` → `false`

#### `BuildFeaturePipeline()`
Creates the feature engineering pipeline (this is the same for all 3 models):

```
Step 1: OneHotEncoding("Course")
        → BSIT becomes [1,0,0], BSCS becomes [0,1,0], BSIS becomes [0,0,1]

Step 2: FeaturizeText("Competencies")
        → TF-IDF converts "Technical Skills, Problem Solving" into a numeric vector
        → Each unique word/n-gram gets a weight based on frequency

Step 3: FeaturizeText("Interest")
        → TF-IDF converts "CSE" or "IRC" into numeric vectors
        → This is why Interest has high importance — the RIASEC codes
          strongly correlate with career alignment

Step 4: Concatenate all into a single "Features" vector:
        [Gwa, YearGraduated, CourseEncoded..., CompetenciesFeaturized..., InterestFeaturized...]

Step 5: NormalizeMinMax("Features")
        → Scales all values to 0-1 range so no single feature dominates
```

#### `TrainAll(csvPath, modelOutputPath)`
1. Loads data and splits 80/20 (stratified, seed=42)
2. Trains **Random Forest** (FastForest):
   - 100 trees, 20 leaves per tree, min 5 samples per leaf
   - Adds a **Platt calibrator** so it outputs probabilities (FastForest natively only outputs scores)
   - This is the primary model — saved to `model.zip`
3. Trains **Decision Tree** (FastTree with 1 tree):
   - Uses FastTree (gradient boosted trees) but configured with just 1 tree to simulate a single decision tree
   - Learning rate = 1.0 (no boosting effect with 1 tree)
4. Trains **Logistic Regression** (LBFGS):
   - Classic linear classifier, good baseline
   - L-BFGS is the optimization algorithm (Limited-memory Broyden-Fletcher-Goldfarb-Shanno)
5. Saves `model.zip` (RF only) and `metrics.json` (all three)

#### `EvaluateModel()`
Runs each trained model on the test set and computes:
- **Accuracy** — % of correct predictions overall
- **Precision** — of those predicted ICT-Aligned, how many actually were? (fewer false positives)
- **Recall** — of those who are actually ICT-Aligned, how many did we catch? (fewer false negatives)
- **F1 Score** — harmonic mean of Precision and Recall (balances both)
- **AUC** — Area Under ROC Curve (ability to distinguish between classes across all thresholds)
- **Confusion Matrix** — 2x2 grid of TP, FN, FP, TN

#### `ComputeFeatureImportance()`
Manual implementation of **Permutation Feature Importance (PFI)**:

1. Start with the model's baseline accuracy on the test set
2. For each feature (Gwa, YearGraduated, Course, Competencies, Interest):
   a. Randomly shuffle that feature's values across all test rows (10 times)
   b. Re-run the model on the shuffled data
   c. Measure how much accuracy drops
3. The feature that causes the biggest drop when shuffled is the most important
4. Normalize to percentages (all importance values sum to 100%)

This is analogous to **Mean Decrease in Accuracy (MDA)** used with scikit-learn Random Forests.

### 3.3 File: `CareerPath.ML/Program.cs`

Simple console entry point:
1. Finds the solution root by walking up directories looking for `CareerPath.sln`
2. Calls `trainer.TrainAll(csvPath, modelPath)`
3. Prints summary metrics

### 3.4 Reproducibility

Both `MLContext(seed: 42)` and `TrainTestSplit(seed: 42)` use the same seed. This means:
- The same 80/20 split every time
- The same random forest trees every time
- If a panelist asks "will I get the same numbers if I rerun?" — **yes**

---

## 4. Backend API

### 4.1 File: `CareerPath.Server/Program.cs`

Server startup:
1. Finds the solution root (walks up from `bin/Debug/net10.0/` to find `CareerPath.sln`)
2. Registers services:
   - `DatasetService` (singleton) — loads CSV once into memory
   - `PredictionService` (singleton) — loads `model.zip` once
   - `AppDbContext` — SQLite database for prediction audit log
   - Swagger, Controllers
3. Configures middleware:
   - `UseBlazorFrameworkFiles()` — serves the WASM client files
   - `UseStaticFiles()` — serves CSS, images, etc.
   - `MapControllers()` — routes API calls
   - `MapFallbackToFile("index.html")` — any non-API route serves the Blazor app

### 4.2 File: `Services/DatasetService.cs`

- Loads `alumni.csv` into a `List<AlumniRecord>` at startup using CsvHelper
- `GetSummary()` — computes total records, counts by course/outcome/year, GWA stats
- `GetRecords(page, pageSize, course, search)` — filters and paginates the records
- `FindSimilarAlumniJobs()` — finds the top 5 most common job positions among alumni with the same predicted outcome and similar profile

### 4.3 File: `Services/PredictionService.cs`

- Loads the trained model from `model.zip` at startup
- `Predict(StudentInput)`:
  1. Converts `StudentInput` to `StudentFeatures`
  2. Calls `_engine.Predict(features)` (thread-safe via `lock`)
  3. Returns `PredictionResult` with outcome, confidence, probabilities, and interpretation text

### 4.4 File: `Controllers/PredictController.cs`

- `POST /api/predict` — accepts `StudentInput`, calls `PredictionService`, finds similar alumni, logs to SQLite, returns result
- `GET /api/predict/status` — returns whether the model is loaded

### 4.5 File: `Controllers/DatasetController.cs`

- `GET /api/dataset` — paginated alumni records with optional course/search filters
- `GET /api/dataset/summary` — summary statistics
- `GET /api/metrics` — Random Forest metrics only (reads `metrics.json`)
- `GET /api/metrics/comparison` — all three models (reads `metrics.json`)

### 4.6 File: `Data/AppDbContext.cs`

SQLite database with one table `PredictionLogs`:
- Records every prediction made through the API
- Fields: Course, YearGraduated, Gwa, Competencies, Interest, PredictedOutcome, Confidence, PredictedAt
- Database file: `careerpath.db` (auto-created on first run)

---

## 5. Frontend

### 5.1 Technology: Blazor WebAssembly + MudBlazor

**Blazor WebAssembly** means the entire C# frontend is compiled to WebAssembly and runs in the browser. No server-side rendering — the browser downloads the .NET runtime and runs C# code directly.

**MudBlazor** is a Material Design component library for Blazor. It provides pre-built components like `MudTextField`, `MudSelect`, `MudTable`, `MudCard`, `MudProgressLinear`, etc.

### 5.2 Theme (App.razor)

The NVSU official colors are set in `App.razor`:
- **Primary:** `#007F3B` (NVSU/Nueva Vizcaya green)
- **Secondary:** `#FFD701` (NVSU/Nueva Vizcaya gold)
- **Typography:** Source Serif 4 for headings, Inter/Roboto for body text

### 5.3 Layout (MainLayout.razor)

- **App Bar** — green gradient with NVSU logo, system title, "CITE, NVSU Bayombong" text
- **Side Drawer** — collapsible navigation with icons for Home, Predict, Dataset, Insights, About
- **Main Content** — max-width 1280px, padded

### 5.4 Pages

#### Home.razor (`/`)
- Green hero banner with NVSU logo
- Three cards linking to Predict, Dataset, Insights
- Timeline showing how the system works (3 steps)
- Disclaimer about predictions being suggestions, not guarantees

#### Predict.razor (`/predict`)
- **Left column:** Student input form with MudBlazor components
  - Course dropdown (BSIT/BSCS/BSIS)
  - Year graduated (numeric)
  - GWA (numeric, with helper text about PH grading system)
  - Competencies (multiline text)
  - Interest (multiline text)
  - Submit button with loading spinner
- **Right column:** Result display (appears after submission)
  - Large colored chip with "ICT-Aligned" or "Non-ICT-Aligned"
  - Confidence percentage
  - Two progress bars showing class probabilities
  - Interpretation text
  - Similar alumni job chips
  - Disclaimer

#### Dataset.razor (`/dataset`)
- Total record count badge
- Filter card: course dropdown + search textbox + filter button
- MudTable with columns: Name, Year, Course, GWA, Job Position, Field, Career Outcome
- Pagination controls at the bottom

#### Insights.razor (`/insights`)
- **Hero stats:** 4 cards showing RF's Accuracy, Precision, Recall, F1
- **Comparison table:** All three models side-by-side, best model highlighted green
- **Confusion matrix:** 2x2 HTML table with green (correct) and red (incorrect) cells
- **Feature importance:** Horizontal progress bars showing each feature's PFI percentage
- **Training info:** Row counts and training timestamp

#### About.razor (`/about`)
- Overview of the system
- Methodology (algorithm, comparison models, features, evaluation)
- Technology stack table
- Data source description
- Researcher info (placeholder)

---

## 6. Shared Models

### File: `CareerPath.Shared/Models.cs`

This file contains all DTOs (Data Transfer Objects) shared between the client and server:

| Class | Purpose |
|-------|---------|
| `StudentInput` | Form data sent from Predict page → POST /api/predict |
| `PredictionResult` | Prediction response with outcome, confidence, scores, interpretation |
| `ClassScore` | Single class probability (label + probability) |
| `ModelMetrics` | Evaluation metrics for one algorithm |
| `FeatureImportance` | One feature's name + importance percentage |
| `ModelComparison` | Container for all three models' metrics |
| `DatasetSummary` | Record counts, GWA stats, distributions |
| `AlumniRecord` | One alumni row from the dataset |
| `PagedResult<T>` | Generic paginated response |
| `ModelStatus` | Whether the model is loaded |

---

## 7. How a Prediction Works End-to-End

1. **User fills the form** on the Predict page and clicks "Predict My Career Path"

2. **Blazor serializes** the form into a `StudentInput` JSON object:
   ```json
   {
     "course": "BSIT",
     "yearGraduated": 2024,
     "gwa": 2.5,
     "competencies": "Technical Skills, Problem Solving",
     "interest": "Software Development"
   }
   ```

3. **HTTP POST** to `http://localhost:5001/api/predict`

4. **PredictController** receives the request:
   - Calls `PredictionService.Predict(input)`
   - Calls `DatasetService.FindSimilarAlumniJobs()` for similar alumni
   - Logs the prediction to SQLite
   - Returns `PredictionResult`

5. **PredictionService.Predict():**
   - Converts `StudentInput` → `StudentFeatures`
   - The ML.NET `PredictionEngine` runs the **full pipeline**:
     a. One-hot encodes "BSIT" → [1,0,0]
     b. TF-IDF featurizes "Technical Skills, Problem Solving" → numeric vector
     c. TF-IDF featurizes "Software Development" → numeric vector
     d. Concatenates all into one feature vector
     e. Normalizes to 0-1 range
     f. Runs through the 100-tree Random Forest
     g. Platt calibrator converts the score to a probability
   - Returns: `PredictedLabel=true`, `Probability=0.72`
   - Maps to: `PredictedOutcome="ICT-Aligned"`, `Confidence=0.72`

6. **Blazor receives** the JSON response and renders the result panel

---

## 8. Key Concepts for Defense Q&A

### "Why Random Forest?"
Random Forest is an **ensemble method** that builds many decision trees and votes on the outcome. It reduces overfitting compared to a single decision tree, handles both numeric and categorical features, and provides feature importance metrics. It's one of the most widely-used classifiers in applied ML.

### "Why binary classification instead of 4-class?"
The original 4 classes ("Core ICT-Related", "ICT-Adjacent", "Non-ICT but Adjacent", "Non-ICT") had very small per-class counts with n=301. Binary classification is more reliable with small datasets and produces more stable metrics. The distinction between "ICT-Adjacent" and "Non-ICT but Adjacent" was ambiguous even in the original labeling.

### "Why is Interest the most important feature?"
The RIASEC Interest codes directly encode career orientation. A student with code "IRC" (Investigative-Realistic-Conventional) has interests naturally aligned with ICT work. This makes theoretical sense — career interest is the strongest predictor of career choice.

### "Why ~76% accuracy and not higher?"
With only 301 records and 5 features (2 of which are short text), 76% is a strong result. The majority-class baseline is 51.5% (predicting "ICT-Aligned" for everyone). Our model improves by 24.5 percentage points over random guessing. Higher accuracy would require more data, more features (e.g., validated psychometric instruments), or both.

### "What is TF-IDF?"
**Term Frequency - Inverse Document Frequency**. It converts text into numbers:
- **TF:** How often a word appears in this record
- **IDF:** How rare that word is across all records
- Words that appear in many records get low weight; unique words get high weight
- So "Technical Skills" (common) gets less weight than a rare competency

### "What is Platt calibration?"
FastForest outputs raw scores (not probabilities). **Platt calibration** fits a logistic regression on top of the raw scores to convert them to calibrated probabilities between 0 and 1. This is needed so we can say "72% confidence" instead of just "positive prediction."

### "What is Permutation Feature Importance?"
For each feature, we randomly shuffle its values and see how much the model's accuracy drops. If shuffling "Interest" causes accuracy to drop from 76% to 50%, that means Interest is very important. If shuffling "Course" causes no drop, Course is unimportant. This is a model-agnostic technique that works with any classifier.

### "Is this reproducible?"
Yes. Both `MLContext(seed: 42)` and `TrainTestSplit(seed: 42)` use fixed seeds. Running the trainer again produces exactly the same numbers.

### "Why Blazor WebAssembly?"
The entire frontend runs in the browser via WebAssembly — no server-side rendering needed. This means the demo works even with a slow server, the UI is responsive, and the architecture cleanly separates frontend from backend. It's also all C#, matching the thesis's all-.NET stack.

### "What is MudBlazor?"
A Material Design component library for Blazor. It provides pre-built, professional-looking UI components (forms, tables, cards, charts) so the interface looks polished for the defense without requiring CSS frameworks like Tailwind or manual styling.
