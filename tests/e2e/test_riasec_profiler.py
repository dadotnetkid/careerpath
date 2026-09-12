#!/usr/bin/env python3
"""RIASEC step: the O*NET Interest Profiler question bank, scoring, and wizard."""

import re
import sys

from common import (ANSWER_PLAN, AREA_NAMES, EXPECTED_QUESTIONS, ITEMS_PER_AREA,
                    MAX_AREA_SCORE, QUESTIONS_PER_PAGE, RIASEC_AREAS, SCALE_LABELS,
                    assessment_payload, browser_page, goto, run_module)

SUITE = "riasec"

FALLBACK_MARKER = "AI-powered detailed analysis is currently unavailable"


def run(cfg, report, api):
    report.suite = SUITE
    questions = test_question_bank(cfg, report, api)
    if not questions:
        return
    test_scoring(cfg, report, api, questions)
    test_validation(cfg, report, api, questions)
    if cfg.full:
        test_report_generation(cfg, report, api, questions)
    if not cfg.api_only:
        test_ui(cfg, report, api, questions)


# ── API ────────────────────────────────────────────────────────────────────

def test_question_bank(cfg, report, api):
    report.section("Question bank")
    status, questions = api.get("/api/riasec/questions")
    if not report.check("questions endpoint returns 200", status == 200, str(status)):
        return None

    report.check(f"exactly {EXPECTED_QUESTIONS} items", len(questions) == EXPECTED_QUESTIONS,
                 f"got {len(questions)}")
    ids = [q["id"] for q in questions]
    report.check("ids are 1..30 with no gaps or duplicates", ids == list(range(1, EXPECTED_QUESTIONS + 1)))

    per_area = {a: sum(1 for q in questions if q["area"] == a) for a in RIASEC_AREAS}
    report.check(f"{ITEMS_PER_AREA} items per RIASEC area",
                 all(per_area[a] == ITEMS_PER_AREA for a in RIASEC_AREAS), str(per_area))
    report.check("every item names its area correctly",
                 all(q["areaName"] == AREA_NAMES.get(q["area"]) for q in questions))
    report.check("no item text is blank", all(q["text"].strip() for q in questions))
    report.check("item texts are unique", len({q["text"] for q in questions}) == EXPECTED_QUESTIONS)
    report.check("old multiple-choice shape is gone (no scenario/options fields)",
                 all("scenario" not in q and "options" not in q for q in questions))

    first = next(q for q in questions if q["id"] == 1)
    last = next(q for q in questions if q["id"] == EXPECTED_QUESTIONS)
    report.check("item 1 matches O*NET wording",
                 first["text"] == "Build kitchen cabinets" and first["area"] == "R", first["text"])
    report.check("item 30 matches O*NET wording",
                 last["text"] == "Stamp, sort, and distribute mail for an organization" and last["area"] == "C",
                 last["text"])

    report.section("Supporting lookups")
    status, skills = api.get("/api/riasec/skills")
    report.check("skills endpoint returns the seven GTS 3 competencies",
                 status == 200 and len(skills) == 7, f"{status} {len(skills) if status == 200 else ''}")

    status, roles = api.get("/api/riasec/roles/ICR")
    report.check("suggested roles are returned for a RIASEC code",
                 status == 200 and len(roles) > 0, f"{status} {len(roles) if status == 200 else ''}")
    return questions


def test_scoring(cfg, report, api, questions):
    report.section("Likert scoring")
    status, result = api.post("/api/riasec/assess", assessment_payload(questions))
    if not report.check("assess returns 200", status == 200, str(result)[:200]):
        return

    scores = result["riasecScores"]
    expected = {AREA_NAMES[a].lower(): ANSWER_PLAN[a] * ITEMS_PER_AREA for a in RIASEC_AREAS}
    report.check("ratings are summed per area, not counted",
                 {k: scores[k] for k in expected} == expected, f"got {scores}")
    report.check(f"no area exceeds the {MAX_AREA_SCORE}-point maximum",
                 all(v <= MAX_AREA_SCORE for v in scores.values()))
    report.check("dominant code is the top-3 areas by score", result["dominantCode"] == "ICR",
                 result["dominantCode"])
    report.check("top dimensions match the dominant code",
                 result["topDimensions"] == ["Investigative", "Conventional", "Realistic"],
                 str(result["topDimensions"]))
    report.check("matched alumni jobs are returned", len(result["matchedJobs"]) > 0,
                 str(len(result["matchedJobs"])))
    report.check("match scores are normalised to 100",
                 result["matchedJobs"][0]["matchScore"] == 100.0,
                 str(result["matchedJobs"][0]["matchScore"]))
    report.check("career clarity is scored out of 25",
                 0 <= result["clarity"]["totalScore"] <= 25, str(result["clarity"]["totalScore"]))
    report.check("career clarity is bucketed for the model",
                 result["clarity"]["clarityBucket"] in ("Low", "Medium", "High"),
                 result["clarity"]["clarityBucket"])

    status, maxed = api.post("/api/riasec/assess",
                             assessment_payload(questions, {str(q["id"]): 4 for q in questions}))
    report.check("all Strongly Like gives every area the maximum score",
                 status == 200 and all(v == MAX_AREA_SCORE for v in maxed["riasecScores"].values()),
                 str(maxed.get("riasecScores")))

    status, zeroed = api.post("/api/riasec/assess",
                              assessment_payload(questions, {str(q["id"]): 0 for q in questions}))
    report.check("all Strongly Dislike scores every area zero",
                 status == 200 and all(v == 0 for v in zeroed["riasecScores"].values()),
                 str(zeroed.get("riasecScores")))


def test_validation(cfg, report, api, questions):
    report.section("Answer validation")
    complete = {str(q["id"]): 2 for q in questions}

    def assess(answers):
        return api.post("/api/riasec/assess", assessment_payload(questions, answers), timeout=30)

    status, body = assess({**complete, "1": 9})
    report.check("rating above the scale is rejected", status == 400, f"{status} {body}")
    status, body = assess({**complete, "1": -1})
    report.check("negative rating is rejected", status == 400, f"{status} {body}")
    status, body = assess({**complete, "999": 3})
    report.check("unknown question id is rejected", status == 400, f"{status} {body}")
    status, body = assess(dict(list(complete.items())[:10]))
    report.check("incomplete answer set is rejected", status == 400, f"{status} {body}")
    status, body = assess({})
    report.check("empty answer set is rejected", status == 400, f"{status} {body}")

    status, roles = api.get("/api/riasec/roles/Z")
    report.check("an unrecognised RIASEC letter returns no roles rather than an error",
                 status == 200 and roles == [], f"{status} {roles}")


def test_report_generation(cfg, report, api, questions):
    """--full: the whole pipeline, including the ML prediction and the AI narrative."""
    report.section("Full report (--full)")
    status, result = api.post("/api/riasec/assess",
                              assessment_payload(questions, include_prediction=True), timeout=240)
    if not report.check("assess with prediction returns 200", status == 200, str(result)[:200]):
        return

    prediction = result.get("prediction")
    if report.check("a binary prediction is attached", prediction is not None):
        report.check("the prediction names one of the two classes",
                     prediction["predictedOutcome"] in ("ICT-Aligned", "Non-ICT-Aligned"),
                     prediction["predictedOutcome"])
        report.check("similar alumni jobs come from the matched-jobs list",
                     set(prediction["similarAlumniJobs"]) <= {j["jobTitle"] for j in result["matchedJobs"]},
                     str(prediction["similarAlumniJobs"]))

    analysis = result.get("aiAnalysis", "")
    report.check("an analysis is generated", len(analysis.strip()) > 0, f"{len(analysis)} chars")
    if FALLBACK_MARKER in analysis:
        report.skip("AI narrative content checks",
                    "server returned the offline fallback — set OpenAI:ApiKey in user secrets")
    else:
        report.check("the AI narrative is substantial", len(analysis) > 800, f"{len(analysis)} chars")
        report.check("the narrative covers the expected sections",
                     all(section in analysis.upper()
                         for section in ("PERSONALITY PROFILE", "CAREER FIT", "SKILLS TO DEVELOP")),
                     "")
        report.check("the narrative quotes the student's RIASEC code",
                     result["dominantCode"] in analysis, result["dominantCode"])


# ── UI ─────────────────────────────────────────────────────────────────────

def test_ui(cfg, report, api, questions):
    report.section("Wizard flow")
    page_count = (len(questions) + QUESTIONS_PER_PAGE - 1) // QUESTIONS_PER_PAGE

    with browser_page(cfg, report, SUITE) as page:
        goto(page, cfg, "/riasec")
        page.get_by_role("button", name="Continue as Guest").click()
        page.get_by_text("I have read and agree").click()
        page.get_by_role("button", name="Accept and Begin Assessment").click()
        page.wait_for_selector("label.mud-radio")

        body = page.inner_text("body")
        report.check("quiz intro names the O*NET Interest Profiler", "O*NET Interest Profiler" in body)
        report.check(f"intro states {len(questions)} work activities", f"{len(questions)} work activities" in body)
        report.check(f"quiz opens on page 1 of {page_count}", f"Page 1 of {page_count}" in body)
        report.check("scale labels are shown", all(lbl in body for lbl in SCALE_LABELS))

        for page_index in range(page_count):
            page_questions = questions[page_index * QUESTIONS_PER_PAGE:(page_index + 1) * QUESTIONS_PER_PAGE]
            radios = page.locator("label.mud-radio")
            radios.first.wait_for()

            report.check(f"page {page_index + 1} shows {len(page_questions)} items with 5 options each",
                         radios.count() == len(page_questions) * len(SCALE_LABELS), f"{radios.count()} radios")

            # Page 1 also renders the intro card, so scope to the card holding the items.
            content = page.inner_text(".mud-card:has(label.mud-radio) .mud-card-content")
            missing = [q["text"] for q in page_questions if q["text"] not in content]
            report.check(f"page {page_index + 1} renders its item texts", not missing, str(missing)[:80])

            leaked = [n for n in AREA_NAMES.values() if n in content]
            report.check(f"page {page_index + 1} hides the interest area from the student",
                         not leaked, f"leaked: {leaked}")

            for item_index, q in enumerate(page_questions):
                option = radios.nth(item_index * len(SCALE_LABELS) + ANSWER_PLAN[q["area"]])
                option.scroll_into_view_if_needed()
                option.click()

            answered = (page_index + 1) * len(page_questions)
            page.wait_for_function(
                f"document.body.innerText.includes('{answered} / {len(questions)} answered')")

            if page_index < page_count - 1:
                page.get_by_role("button", name="Next").click()
                page.wait_for_function(
                    f"document.body.innerText.includes('Page {page_index + 2} of {page_count}')")

        report.check(f"all {len(questions)} items answered",
                     f"{len(questions)} / {len(questions)} answered" in page.inner_text("body"))

        page.get_by_role("button", name="Continue to Career Clarity").click()
        page.wait_for_timeout(2000)
        report.check("quiz hands off to the Career Clarity step",
                     "Career Clarity" in page.inner_text("body")
                     and "work activities" not in page.inner_text("body"))

        if cfg.full:
            test_ui_results(cfg, report, page)


def select_mud_option(page, label, option_index=0):
    """Open a MudSelect by its label and pick an item from the popover."""
    page.get_by_label(label, exact=False).first.click()
    page.wait_for_selector(".mud-popover .mud-list-item")
    page.locator(".mud-popover .mud-list-item").nth(option_index).click()
    page.wait_for_timeout(300)


def test_ui_results(cfg, report, page):
    """--full: finish the wizard in the browser and read the score bars."""
    report.section("Results page (--full)")

    for role_index in range(3):
        select_mud_option(page, f"Role {role_index + 1}", role_index)

    # Ratings only render once all three roles are chosen: A and B (3 each),
    # then C1-C3. Click the 5th star of every rating.
    page.wait_for_selector(".mud-rating-item")
    ratings = page.locator(".mud-rating-root")
    for r in range(ratings.count()):
        ratings.nth(r).locator(".mud-rating-item").nth(4).click()
    page.wait_for_timeout(300)

    page.get_by_role("button", name="Continue to Academic Performance").click()
    page.wait_for_selector("text=Academic Performance")

    select_mud_option(page, "Select your GWA range", 1)  # Very Satisfactory
    page.get_by_text("Critical Thinking", exact=True).first.click()

    page.get_by_role("button", name="Get My Career Report").click()
    page.wait_for_selector("text=RIASEC Dimension Scores", timeout=240_000)
    page.get_by_text("RIASEC Dimension Scores").scroll_into_view_if_needed()

    body = page.inner_text("body")
    report.check(f"score bars are out of {MAX_AREA_SCORE}", f"/ {MAX_AREA_SCORE}" in body)
    report.check("no score bar still reads out of 10", "/ 10" not in body)
    report.check("all six dimensions are reported",
                 all(f"{name} ({code})" in body for code, name in AREA_NAMES.items()))

    reported = {code: int(score) for code, score in
                re.findall(rf"\w+ \(([RIASEC])\)\s*\n\s*(\d+) / {MAX_AREA_SCORE}", body)}
    for code, name in AREA_NAMES.items():
        expected = ANSWER_PLAN[code] * ITEMS_PER_AREA
        report.check(f"{name} bar reads {expected} / {MAX_AREA_SCORE}",
                     reported.get(code) == expected, f"UI shows {reported.get(code)}")

    report.check("the report names the matched job roles", "Matched Job Roles" in body or "Match" in body)
    report.check("the report shows the ML prediction",
                 "ICT-Aligned" in body or "Non-ICT-Aligned" in body)

    test_ui_analysis_placement(report, page, body)


def card_text(page, heading: str) -> str:
    """Lower-cased text of the results card whose header contains `heading`.

    Note headings are uppercased with CSS, and inner_text() returns rendered
    text, so every comparison here is case-insensitive.
    """
    return page.locator(f".mud-card:has-text('{heading}')").first.inner_text().lower()


def test_ui_analysis_placement(report, page, body):
    """The AI narrative is split across the cards it explains, not dumped at the end."""
    report.section("AI narrative placement (--full)")

    if "currently unavailable" in body:
        report.skip("narrative placement checks",
                    "server returned the offline fallback — set OpenAI:ApiKey in user secrets")
        return

    report.check("the old single wall of analysis is gone",
                 "Detailed AI Career Analysis" not in body)

    placements = [
        ("RIASEC Dimension Scores", "What your interests say"),
        ("Career Clarity Breakdown", "What your clarity score means"),
        ("Top Matched Job Roles", "Why these roles fit your profile"),
        ("Predicted Career Outcome", "What this prediction means"),
    ]
    for card, note in placements:
        report.check(f"'{note}' sits in the {card} card", note.lower() in card_text(page, card))

    closing = card_text(page, "Where to Go From Here")
    report.check("the closing card carries the skills advice", "skills to develop" in closing)
    report.check("the closing card carries the next steps", "your next steps" in closing)
    report.check("the closing card carries the encouragement", "encouragement" in closing)
    report.check("the closing card is not repeating the whole narrative",
                 "what your interests say" not in closing and "what your clarity score means" not in closing)


if __name__ == "__main__":
    sys.exit(run_module(run, __doc__))
