#!/usr/bin/env python3
"""Train & Test workflow: cross-validation history, status, and (with --train) a real run."""

import sys
import time

from common import Api, run_module

SUITE = "training"

ALGORITHMS = ("Random Forest", "Decision Tree", "Logistic Regression")


def run(cfg, report, api):
    report.suite = SUITE

    admin = Api(cfg.base_url)
    status, body = admin.login_admin()
    if not report.check("admin signs in", status == 200 and body.get("role") == "Admin", f"{status} {body}"):
        report.skip("training checks", "admin sign-in failed")
        return

    report.section("Training status")
    status, state = admin.get("/api/training/run/status")
    report.check("status returns 200", status == 200, str(status))
    report.check("status is one of the known states",
                 state.get("status") in ("Idle", "Running", "Completed", "Failed"),
                 str(state.get("status")))

    report.section("Cross-validation history")
    status, latest = admin.get("/api/training/latest")
    report.check("latest returns 200", status == 200, str(status))
    report.check("latest returns a list", isinstance(latest, list), type(latest).__name__)

    if not latest:
        report.skip("history content checks", "no training runs recorded yet — run Train & Test once")
    else:
        report.check("at most one row per algorithm and fold count",
                     len({(r["algorithm"], r["foldCount"]) for r in latest}) == len(latest),
                     f"{len(latest)} rows")
        report.check("every row uses a 10- or 20-fold split",
                     all(r["foldCount"] in (10, 20) for r in latest),
                     str(sorted({r["foldCount"] for r in latest})))
        report.check("mean accuracy is a valid rate",
                     all(0.0 <= r["meanAccuracy"] <= 1.0 for r in latest),
                     str([round(r["meanAccuracy"], 3) for r in latest]))
        report.check("standard deviations are non-negative",
                     all(r["stdAccuracy"] >= 0 and r["stdF1"] >= 0 for r in latest))
        report.check("the recorded algorithms are the three compared models",
                     {r["algorithm"] for r in latest} <= set(ALGORITHMS),
                     str(sorted({r["algorithm"] for r in latest})))

        sample = latest[0]
        status, history = admin.get(
            f"/api/training/history?algorithm={sample['algorithm']}&foldCount={sample['foldCount']}")
        report.check("history for a known run returns 200", status == 200, str(status))
        report.check("history rows all match the requested combination",
                     all(r["algorithm"] == sample["algorithm"] and r["foldCount"] == sample["foldCount"]
                         for r in history),
                     f"{len(history)} rows")

    report.section("History validation")
    status, body = admin.get("/api/training/history?algorithm=Random%20Forest&foldCount=7")
    report.check("a fold count other than 10 or 20 is rejected", status == 400, f"{status} {body}")

    status, body = admin.get("/api/training/history?algorithm=&foldCount=10")
    report.check("a missing algorithm is rejected", status == 400, f"{status} {body}")

    status, unknown = admin.get("/api/training/history?algorithm=NotAnAlgorithm&foldCount=10")
    report.check("an unknown algorithm returns an empty history, not an error",
                 status == 200 and unknown == [], f"{status} {unknown}")

    report.section("Access control")
    status, _ = api.get("/api/training/latest")
    report.check("anonymous caller cannot read training history", status in (401, 403), str(status))
    status, _ = api.post("/api/training/run")
    report.check("anonymous caller cannot start training", status in (401, 403), str(status))

    if not cfg.train:
        report.skip("real training run", "pass --train to retrain the model (slow)")
        return

    report.section("Training run (--train)")
    status, body = admin.post("/api/training/run")
    report.check("training starts and returns 202", status == 202, f"{status} {body}")

    status, body = admin.post("/api/training/run")
    report.check("a second start while running is refused", status == 409, f"{status} {body}")

    deadline = time.time() + 900
    final = None
    while time.time() < deadline:
        _, state = admin.get("/api/training/run/status")
        if state.get("status") in ("Completed", "Failed"):
            final = state
            break
        time.sleep(5)

    if report.check("training finishes within 15 minutes", final is not None):
        report.check("training completed without error", final["status"] == "Completed", str(final))
        report.check("the run is timestamped", bool(final.get("startedAt") and final.get("completedAt")))

        status, after = admin.get("/api/training/latest")
        report.check("the run is recorded in the history", status == 200 and len(after) > 0, str(len(after)))
        status, ready = admin.get("/api/predict/status")
        report.check("the model is still servable after retraining", ready.get("ready") is True, str(ready))


if __name__ == "__main__":
    sys.exit(run_module(run, __doc__))
