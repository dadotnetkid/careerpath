#!/usr/bin/env python3
"""Admin data views: alumni dataset paging and search, summary, model metrics."""

import sys

from common import Api, run_module, browser_page, goto, ui_login, ADMIN_EMAIL, ADMIN_PASSWORD

SUITE = "dataset"


def run(cfg, report, api):
    report.suite = SUITE

    admin = Api(cfg.base_url)
    status, body = admin.login_admin()
    if not report.check("admin signs in", status == 200 and body.get("role") == "Admin", f"{status} {body}"):
        report.skip("dataset checks", "admin sign-in failed")
        return

    report.section("Alumni dataset")
    status, page1 = admin.get("/api/dataset?page=1&pageSize=10")
    if not report.check("dataset returns 200", status == 200, str(page1)[:160]):
        return

    total = page1["total"]
    report.check("the dataset is not empty", total > 0, f"total={total}")
    report.check("page size is honoured", len(page1["items"]) == 10, str(len(page1["items"])))
    report.check("the page echoes its own number", page1["page"] == 1 and page1["pageSize"] == 10)

    status, page2 = admin.get("/api/dataset?page=2&pageSize=10")
    ids1 = [r.get("jobTitle", "") + r.get("interest", "") for r in page1["items"]]
    ids2 = [r.get("jobTitle", "") + r.get("interest", "") for r in page2["items"]]
    report.check("page 2 returns a different slice", ids1 != ids2)
    report.check("the total is stable across pages", page2["total"] == total)

    last_page = (total + 9) // 10
    status, beyond = admin.get(f"/api/dataset?page={last_page + 5}&pageSize=10")
    report.check("a page past the end returns no rows, not an error",
                 status == 200 and beyond["items"] == [], f"{status} {len(beyond.get('items', []))} rows")

    report.section("Records carry the training fields")
    row = page1["items"][0]
    for field in ("gwa", "honors", "competencies", "careerClarity", "interest", "jobTitle"):
        report.check(f"record has '{field}'", field in row, str(list(row.keys()))[:120] if field not in row else "")

    report.section("Filtering")
    status, filtered = admin.get("/api/dataset?page=1&pageSize=50&honors=No%20Honors")
    report.check("honors filter returns only that value",
                 status == 200 and all(r["honors"] == "No Honors" for r in filtered["items"]),
                 f"{status}")
    report.check("honors filter narrows the total", filtered["total"] <= total,
                 f"{filtered['total']} of {total}")

    search_term = row["jobTitle"][:6]
    status, searched = admin.get(f"/api/dataset?page=1&pageSize=50&search={search_term}")
    report.check(f"search for '{search_term}' returns matches", status == 200 and searched["total"] > 0,
                 f"{status} {searched.get('total')}")

    status, nothing = admin.get("/api/dataset?page=1&pageSize=10&search=zzzznotarealjobtitle")
    report.check("a search with no matches returns an empty page",
                 status == 200 and nothing["total"] == 0, str(nothing.get("total")))

    report.section("Dataset summary")
    status, summary = admin.get("/api/dataset/summary")
    if report.check("summary returns 200", status == 200, str(status)):
        report.check("summary total matches the dataset total", summary["totalRecords"] == total,
                     f"{summary['totalRecords']} vs {total}")
        for group in ("byGwa", "byHonors", "byCareerOutcome", "byCareerClarity", "byRiasecDominant", "byYear"):
            counts = summary.get(group) or {}
            report.check(f"{group} is populated", len(counts) > 0)
        report.check("the GWA breakdown sums to the total",
                     sum(summary["byGwa"].values()) == total,
                     f"{sum(summary['byGwa'].values())} vs {total}")
        report.check("RIASEC breakdown uses the six dimension names",
                     all(k in ("Realistic", "Investigative", "Artistic", "Social", "Enterprising", "Conventional")
                         for k in summary["byRiasecDominant"]),
                     str(list(summary["byRiasecDominant"].keys())))

    report.section("Model metrics")
    status, metrics = admin.get("/api/metrics")
    if report.check("metrics returns 200", status == 200, str(status)):
        for field in ("accuracy", "precision", "recall", "f1Score", "areaUnderRocCurve"):
            value = metrics.get(field)
            report.check(f"{field} is a valid rate", isinstance(value, (int, float)) and 0.0 <= value <= 1.0,
                         str(value))
        report.check("training and test row counts are recorded",
                     metrics["trainingRows"] > 0 and metrics["testRows"] > 0,
                     f"{metrics['trainingRows']} / {metrics['testRows']}")
        report.check("the confusion matrix is 2x2",
                     len(metrics["confusionMatrix"]) == 2 and all(len(r) == 2 for r in metrics["confusionMatrix"]),
                     str(metrics["confusionMatrix"]))
        report.check("the confusion matrix totals the test rows",
                     sum(sum(r) for r in metrics["confusionMatrix"]) == metrics["testRows"],
                     f"{sum(sum(r) for r in metrics['confusionMatrix'])} vs {metrics['testRows']}")

    status, comparison = admin.get("/api/metrics/comparison")
    if report.check("model comparison returns 200", status == 200, str(status)):
        report.check("comparison covers Random Forest", bool(comparison.get("randomForest")),
                     str(list(comparison.keys()))[:120])

    status, diagnostic = admin.get("/api/metrics/diagnostic")
    report.check("diagnostic metrics are served or reported missing", status in (200, 404), str(status))

    if cfg.api_only:
        return

    report.section("Admin pages")
    with browser_page(cfg, report, SUITE) as page:
        if not ui_login(page, cfg, ADMIN_EMAIL, ADMIN_PASSWORD, report):
            return

        goto(page, cfg, "/dataset")
        page.wait_for_timeout(2500)
        text = page.inner_text("body")
        report.check("dataset page renders rows", "Alumni" in text or "Job Title" in text, text[:80])
        report.check("dataset page shows the record count", str(total) in text.replace(",", ""), "")

        goto(page, cfg, "/insights")
        page.wait_for_timeout(3000)
        text = page.inner_text("body")
        report.check("insights page renders", "Insights" in text or "Accuracy" in text, text[:80])

        goto(page, cfg, "/train-test")
        page.wait_for_timeout(2500)
        text = page.inner_text("body")
        report.check("train & test page renders", "Train" in text, text[:80])


if __name__ == "__main__":
    sys.exit(run_module(run, __doc__))
