#!/usr/bin/env python3
"""Public pages and navigation: what an anonymous visitor can see and reach."""

import sys

from common import Api, run_module, browser_page, goto, ADMIN_EMAIL, ADMIN_PASSWORD, ui_login

SUITE = "pages"

ADMIN_ONLY = ["/dataset", "/train-test", "/insights"]


def run(cfg, report, api):
    report.suite = SUITE

    report.section("Pages are served")
    for path in ["/", "/riasec", "/about", "/my-results"] + ADMIN_ONLY:
        status, _ = api.get(path)
        report.check(f"{path} is served", status == 200, str(status))

    if cfg.api_only:
        report.skip("page rendering checks", "--api-only")
        return

    with browser_page(cfg, report, SUITE) as page:
        report.section("Anonymous visitor")
        goto(page, cfg, "/")
        text = page.inner_text("body")
        report.check("home page renders the system name", "Career Path" in text, text[:80])
        report.check("home page offers the assessment", "Assessment" in text)
        report.check("nav shows Home, Assessment and About",
                     all(label in text for label in ("Home", "Career Path Assessment", "About")))
        report.check("nav hides the admin sections",
                     not any(label in text for label in ("Dataset", "Train & Test", "Insights")),
                     "")
        report.check("nav hides My Saved Results", "My Saved Results" not in text)

        goto(page, cfg, "/about")
        text = page.inner_text("body")
        report.check("about page renders", "About" in text, text[:80])
        report.check("about page describes the model", "RIASEC" in text or "Random Forest" in text)

        goto(page, cfg, "/riasec")
        text = page.inner_text("body")
        report.check("assessment opens on the welcome step", "Step 1 of 6" in text or "Continue as Guest" in text,
                     text[:100])
        report.check("welcome step offers all three entry paths",
                     all(label in text for label in ("Sign Up", "Sign In", "Continue as Guest")))

        report.section("Admin pages are gated in the UI")
        for path in ADMIN_ONLY:
            goto(page, cfg, path)
            page.wait_for_timeout(1200)
            text = page.inner_text("body")
            report.check(f"{path} asks an anonymous visitor to sign in",
                         "Sign-in Required" in text or "sign in" in text.lower(),
                         text[:80])

        report.section("Admin sees the full nav")
        if ui_login(page, cfg, ADMIN_EMAIL, ADMIN_PASSWORD, report):
            text = page.inner_text("body")
            report.check("nav reveals Dataset, Train & Test and Insights",
                         all(label in text for label in ("Dataset", "Train & Test", "Insights")),
                         "")
            report.check("admin does not get the student's saved-results link",
                         "My Saved Results" not in text)


if __name__ == "__main__":
    sys.exit(run_module(run, __doc__))
