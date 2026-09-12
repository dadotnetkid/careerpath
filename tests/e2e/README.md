# End-to-end tests

Python + Playwright checks for the Career Path Recommendation System, run against
a live server. Every suite exercises the HTTP API and, where there is something
to see, drives the Blazor UI in a real browser.

## Setup

```bash
pip install -r tests/e2e/requirements.txt
playwright install chromium
```

## Run

Start the app first, then point the runner at it:

```bash
dotnet run --project src/CareerPath.Server     # serves on http://localhost:5001
python3 tests/e2e/run_all.py
```

Each suite is also runnable on its own, with the same flags:

```bash
python3 tests/e2e/test_riasec_profiler.py --headed --slow-mo 300
```

| Flag | Effect |
| --- | --- |
| `--base-url URL` | Target a different host (default `http://localhost:5001`) |
| `--api-only` | Skip every browser leg |
| `--headed` | Watch the browser drive the app |
| `--slow-mo MS` | Pause between UI actions so the run is watchable, e.g. `--slow-mo 300` |
| `--hold SECONDS` | With `--headed`, keep the window open at the end of a browser leg |
| `--record` | Save a video of every browser leg under `tests/e2e/recordings/<timestamp>/` |
| `--record-dir DIR` | Put the recordings somewhere else |
| `--full` | Include the slow legs: the AI narrative and the wizard's Results page |
| `--train` | Also kick a real training run and wait for it (retrains the model) |
| `--only NAMES` | `run_all.py` only: comma-separated suites, e.g. `--only riasec,auth` |

Watch a full run and keep the video:

```bash
python3 tests/e2e/run_all.py --headed --slow-mo 300 --hold 15 --record --full
```

## Suites

| Suite | File | Covers |
| --- | --- | --- |
| `pages` | `test_public_pages.py` | Public routes render; nav hides admin sections from anonymous visitors; admin pages show the sign-in gate; the nav opens up after an admin signs in |
| `auth` | `test_auth.py` | Register, sign in, sign out, roles, duplicate/weak/malformed credentials, every role gate on the API, and sign-in through the top-bar dialog |
| `predict` | `test_predict.py` | Model status, prediction shape, probabilities summing to 1, determinism, sensitivity to the RIASEC code, input validation |
| `riasec` | `test_riasec_profiler.py` | The O*NET Interest Profiler bank (30 items, 5 per area, published wording), summed 0-20 scoring, answer validation, the six-items-per-page wizard, and with `--full` the prediction plus the AI narrative and the Results page score bars |
| `saved-results` | `test_saved_results.py` | A student saves, lists, reads and deletes results; rows stay private to their owner; the My Saved Results page renders them |
| `dataset` | `test_admin_dataset.py` | Dataset paging, filtering and search; the summary totals; model metrics and the confusion matrix; the Dataset, Insights and Train & Test pages |
| `training` | `test_training.py` | Cross-validation history and its validation, training status, access control, and with `--train` a real run watched to completion |

## Notes

- The suites talk to the real database and the real model. They create student
  accounts with unique `e2e-*@nvsu.edu.ph` addresses (there is no delete-user
  endpoint, so those rows accumulate) and clean up the saved results they write.
- The admin account comes from `Auth:AdminEmail` / `Auth:AdminPassword`. Override
  with `CAREERPATH_ADMIN_EMAIL` / `CAREERPATH_ADMIN_PASSWORD` when they differ.
- `POST /api/riasec/assess` always generates the narrative, so each assessment
  check costs one OpenAI call when `OpenAI:ApiKey` is configured. Without a key
  the server returns its offline fallback and the narrative checks are skipped
  rather than failed.
- Recordings are one `.webm` per browser leg, written when the browser closes.
  They are gitignored.
