#!/usr/bin/env python3
"""Assessment records: every completed assessment is recorded and the admin can export it."""

import csv
import io
import sys

from common import (Api, run_module, unique_email, strong_password, browser_page,
                    goto, ui_login, assessment_payload, ADMIN_EMAIL, ADMIN_PASSWORD)

SUITE = "assessment-records"

EXPORT_HEADERS = [
    "AssessedAt", "Respondent", "Gwa", "Honors", "Competencies",
    "CareerClarity", "ClarityLabel", "ClarityScore", "DesiredRoles",
    "RiasecDominant", "Interest",
    "RealisticScore", "InvestigativeScore", "ArtisticScore",
    "SocialScore", "EnterprisingScore", "ConventionalScore",
    "SuggestedCareerAlignment", "SuggestedFieldOfIndustry", "SuggestedCareer", "SuggestedCareers",
    "PredictedCareerOutcome", "PredictionConfidence",
]


def newest(admin):
    status, page = admin.get("/api/assessment-records?page=1&pageSize=1")
    return status, page


def run(cfg, report, api):
    report.suite = SUITE

    report.section("Access control")
    status, _ = api.get("/api/assessment-records")
    report.check("an anonymous caller cannot list records", status in (401, 403), str(status))
    resp = api.session.get(api._url("/api/assessment-records/export"), timeout=30)
    report.check("an anonymous caller cannot export records", resp.status_code in (401, 403), str(resp.status_code))

    student = Api(cfg.base_url)
    email, password = unique_email("records"), strong_password()
    status, reg = student.register(email, password, "E2E Records")
    if report.check("student account created", status == 200, str(reg)[:160]):
        status, _ = student.get("/api/assessment-records")
        report.check("a student cannot list records", status == 403, str(status))

    admin = Api(cfg.base_url)
    status, body = admin.login_admin()
    if not report.check("admin signs in", status == 200 and body.get("role") == "Admin", f"{status} {body}"):
        report.skip("record checks", "admin sign-in failed")
        return

    status, before = newest(admin)
    if not report.check("admin list returns 200", status == 200, str(before)[:160]):
        return
    total_before = before["total"]

    status, questions = api.get("/api/riasec/questions")
    if not report.check("questions load", status == 200 and len(questions) > 0, str(status)):
        return

    report.section("A guest's assessment is recorded")
    payload = assessment_payload(questions, include_prediction=True,
                                 skills=["Critical Thinking", "Information Technology Skills"])
    status, result = api.post("/api/riasec/assess", payload, timeout=240)
    if not report.check("guest assessment returns 200", status == 200, str(result)[:200]):
        return

    status, after = newest(admin)
    report.check("the total grows by one", after["total"] == total_before + 1,
                 f"{total_before} -> {after['total']}")
    row = after["items"][0]
    report.check("the newest row is the guest's", row["respondent"] == "Guest", row["respondent"])
    report.check("the academic profile is recorded",
                 row["gwa"] == payload["gwaCategory"] and row["honors"] == payload["honors"],
                 f"{row['gwa']} / {row['honors']}")
    report.check("the competencies are recorded",
                 row["competencies"] == "Critical Thinking, Information Technology Skills", row["competencies"])
    report.check("the RIASEC code matches the report", row["interest"] == result["dominantCode"],
                 f"{row['interest']} vs {result['dominantCode']}")
    report.check("the dominant RIASEC area is the full name",
                 row["riasecDominant"] == result["topDimensions"][0], row["riasecDominant"])
    report.check("the six area scores match the report",
                 row["riasecScores"] == result["riasecScores"], f"{row['riasecScores']}")
    clarity = result["clarity"]
    report.check("career clarity uses the alumni Low/Medium/High bucket",
                 row["careerClarity"] == clarity["clarityBucket"], row["careerClarity"])
    report.check("the clarity label and score are recorded",
                 row["clarityLabel"] == clarity["clarityLabel"]
                 and abs(row["clarityScore"] - clarity["totalScore"]) < 1e-6,
                 f"{row['clarityLabel']} {row['clarityScore']}")
    report.check("the desired roles are recorded",
                 row["desiredRoles"] == "; ".join(payload["careerClarity"]["desiredRoles"]), row["desiredRoles"])

    prediction = result.get("prediction")
    if prediction:
        report.check("the career alignment prediction is recorded",
                     row["predictedOutcome"] == prediction["predictedOutcome"]
                     and abs(row["predictionConfidence"] - prediction["confidence"]) < 1e-4,
                     f"{row['predictedOutcome']} {row['predictionConfidence']}")
    else:
        report.skip("career alignment prediction", "model not loaded on the server")

    matched = result.get("matchedJobs") or []
    if matched:
        report.check("the top suggested career is recorded", row["suggestedCareer"] == matched[0]["jobTitle"],
                     f"{row['suggestedCareer']} vs {matched[0]['jobTitle']}")
        report.check("the top career's alignment is recorded",
                     row["suggestedCareerAlignment"] == matched[0]["careerAlignment"], row["suggestedCareerAlignment"])
        report.check("up to five suggested careers are recorded",
                     row["suggestedCareers"] == "; ".join(j["jobTitle"] for j in matched[:5]),
                     row["suggestedCareers"][:120])
    else:
        report.skip("suggested careers", "no alumni matched this profile")

    report.section("A student's assessment is labelled as such")
    status, _ = student.post("/api/riasec/assess", assessment_payload(questions), timeout=240)
    if report.check("student assessment returns 200", status == 200, str(status)):
        status, latest = newest(admin)
        report.check("the newest row is labelled Student",
                     latest["items"][0]["respondent"] == "Student", latest["items"][0]["respondent"])

    report.section("CSV export")
    resp = admin.session.get(admin._url("/api/assessment-records/export"), timeout=60)
    if not report.check("export returns 200", resp.status_code == 200, str(resp.status_code)):
        return
    report.check("export is served as a CSV attachment",
                 resp.headers.get("Content-Type", "").startswith("text/csv")
                 and "attachment" in resp.headers.get("Content-Disposition", ""),
                 f"{resp.headers.get('Content-Type')} | {resp.headers.get('Content-Disposition')}")
    report.check("export starts with a UTF-8 BOM so Excel reads it", resp.content.startswith(b"\xef\xbb\xbf"))

    rows = list(csv.reader(io.StringIO(resp.content.decode("utf-8-sig"))))
    report.check("export has the expected header", rows and rows[0] == EXPORT_HEADERS, str(rows[0] if rows else []))
    status, now = newest(admin)
    report.check("export has one line per record", len(rows) - 1 == now["total"],
                 f"{len(rows) - 1} lines vs {now['total']} records")
    if len(rows) > 2:
        guest = dict(zip(rows[0], rows[-2]))
        report.check("the guest's row is in the export, oldest first",
                     guest["Respondent"] == "Guest" and guest["Interest"] == result["dominantCode"],
                     str(guest)[:160])

    if cfg.api_only:
        return

    report.section("Assessment Records page")
    with browser_page(cfg, report, SUITE) as page:
        if not ui_login(page, cfg, ADMIN_EMAIL, ADMIN_PASSWORD, report):
            return
        goto(page, cfg, "/assessment-records")
        page.wait_for_timeout(2500)
        text = page.inner_text("body")
        report.check("the page renders", "Assessment Records" in text, text[:80])
        report.check("the page shows the record count", f"Total Records: {now['total']}" in text, "")
        report.check("the page offers the CSV download", "Download CSV" in text.replace("DOWNLOAD", "Download"), "")
        report.check("the nav links to the page", page.locator("a[href='assessment-records']").count() > 0)


if __name__ == "__main__":
    sys.exit(run_module(run, __doc__))
