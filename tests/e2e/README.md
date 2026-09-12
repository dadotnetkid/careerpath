# End-to-end tests

Python + Playwright checks for the RIASEC step of the Career Path Assessment,
which uses the O*NET Interest Profiler 30-item short form.

## Setup

```bash
pip install -r tests/e2e/requirements.txt
playwright install chromium
```

## Run

Start the app first, then point the test at it:

```bash
dotnet run --project src/CareerPath.Server          # serves on http://localhost:5001
python3 tests/e2e/test_riasec_profiler.py
```

To watch it run:

```bash
python3 tests/e2e/test_riasec_profiler.py --headed --slow-mo 250 --full
```

Options:

| Flag | Effect |
| --- | --- |
| `--base-url URL` | Target a different host (default `http://localhost:5001`) |
| `--api-only` | Skip the browser leg, run the API checks only |
| `--headed` | Watch the browser drive the wizard |
| `--slow-mo MS` | Pause between UI actions so the run is watchable, e.g. `--slow-mo 250` |
| `--hold SECONDS` | With `--headed`, keep the window open at the end (default 10) |
| `--full` | Also walk Career Clarity + Academic Performance through to the Results page |

`--full` submits the assessment, so it needs the trained model and takes longer.
It still passes without an OpenAI key — the server falls back to a non-AI
narrative and the score bars, which is what the test asserts on.

## What is covered

**API**
- The bank serves 30 items, ids 1-30, 5 per RIASEC area, each with a matching area name.
- Items 1 and 30 match the published O*NET wording; the old scenario/options shape is gone.
- Ratings are summed per area rather than counted, so an area maxes out at 20.
- The dominant code and top dimensions follow the summed scores.
- Out-of-range ratings, unknown question ids, partial and empty answer sets are rejected with 400;
  an all-"Strongly Dislike" set is accepted.

**UI**
- Guest -> consent -> quiz, 6 items per page across 5 pages, 5 scale options per item.
- Every item text renders, and the item's interest area is never shown while answering,
  so the student cannot infer what an answer scores.
- The answered counter tracks every rating and the quiz hands off to Career Clarity.
- With `--full`, each dimension's bar on the Results page reads the score the answers imply, out of 20.
