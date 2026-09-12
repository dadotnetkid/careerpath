#!/usr/bin/env python3
"""Binary ICT-alignment prediction: model status, output shape, validation."""

import sys

from common import run_module, sample_student_input

SUITE = "predict"

OUTCOMES = {"ICT-Aligned", "Non-ICT-Aligned"}


def run(cfg, report, api):
    report.suite = SUITE

    report.section("Model status")
    status, body = api.get("/api/predict/status")
    ready = status == 200 and body.get("ready") is True
    report.check("status endpoint reports the model is ready", ready, f"{status} {body}")
    if not ready:
        report.skip("prediction checks", "no trained model loaded — run Train & Test first")
        return

    report.section("Prediction output")
    status, pred = api.post("/api/predict", sample_student_input())
    if not report.check("predict returns 200", status == 200, str(pred)[:200]):
        return

    report.check("outcome is one of the two classes", pred["predictedOutcome"] in OUTCOMES,
                 pred["predictedOutcome"])
    report.check("confidence is a probability", 0.0 <= pred["confidence"] <= 1.0, str(pred["confidence"]))
    report.check("both class scores are returned", len(pred["scores"]) == 2, str(len(pred["scores"])))
    report.check("class probabilities sum to 1",
                 abs(sum(s["probability"] for s in pred["scores"]) - 1.0) < 0.01,
                 str([round(s["probability"], 4) for s in pred["scores"]]))
    report.check("confidence matches the winning class probability",
                 abs(max(s["probability"] for s in pred["scores"]) - pred["confidence"]) < 0.01)
    report.check("the two class labels are the expected ones",
                 {s["label"] for s in pred["scores"]} == OUTCOMES,
                 str([s["label"] for s in pred["scores"]]))
    report.check("an interpretation is written", bool(pred.get("interpretation", "").strip()))
    report.check("prediction is timestamped", bool(pred.get("predictedAt")))

    report.section("Determinism")
    _, again = api.post("/api/predict", sample_student_input())
    report.check("the same input predicts the same outcome",
                 again["predictedOutcome"] == pred["predictedOutcome"])
    report.check("the same input predicts the same confidence",
                 abs(again["confidence"] - pred["confidence"]) < 1e-6)

    report.section("Input sensitivity")
    # The RIASEC interest code is a model feature, so changing it should be able
    # to move the probability. Equal probabilities for every code would mean the
    # feature is being ignored.
    confidences = []
    for code in ("ICR", "SEC", "RIA", "AES"):
        payload = {**sample_student_input(), "interest": code}
        _, p = api.post("/api/predict", payload)
        confidences.append(round(p["confidence"], 6))
    report.check("the interest code changes the prediction", len(set(confidences)) > 1, str(confidences))

    report.section("Validation")
    status, body = api.post("/api/predict", {})
    report.check("empty body is rejected", status == 400, f"{status} {str(body)[:120]}")

    bad_year = {**sample_student_input(), "yearGraduated": 1800}
    status, body = api.post("/api/predict", bad_year)
    report.check("out-of-range graduation year is rejected", status == 400, f"{status} {str(body)[:120]}")

    missing_gwa = {**sample_student_input(), "gwa": ""}
    status, body = api.post("/api/predict", missing_gwa)
    report.check("missing GWA is rejected", status == 400, f"{status} {str(body)[:120]}")


if __name__ == "__main__":
    sys.exit(run_module(run, __doc__))
