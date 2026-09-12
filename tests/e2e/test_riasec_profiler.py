#!/usr/bin/env python3
"""
End-to-end checks for the O*NET Interest Profiler RIASEC step.

Covers the API (question bank, scoring, validation) and the Blazor wizard UI
(guest -> consent -> 30 rated items -> Career Clarity) driven with Playwright.

Usage:
    python3 tests/e2e/test_riasec_profiler.py [--base-url http://localhost:5001]
                                              [--headed] [--full]

--full also walks Career Clarity + Academic Performance through to the Results
page, which calls OpenAI and the ML model, so it is slow and needs both
configured. Without it the run stops once the quiz hands off to Career Clarity.

Requires: pip install playwright && playwright install chromium
"""

import argparse
import json
import re
import sys
import urllib.error
import urllib.request

AREAS = ["R", "I", "A", "S", "E", "C"]
AREA_NAMES = {
    "R": "Realistic",
    "I": "Investigative",
    "A": "Artistic",
    "S": "Social",
    "E": "Enterprising",
    "C": "Conventional",
}
SCALE_LABELS = ["Strongly Dislike", "Dislike", "Unsure", "Like", "Strongly Like"]
EXPECTED_QUESTIONS = 30
ITEMS_PER_AREA = 5
QUESTIONS_PER_PAGE = 6
MAX_AREA_SCORE = ITEMS_PER_AREA * 4

# Rating given to every item of an area, so each area's expected score is
# deterministic: score = rating * ITEMS_PER_AREA.
ANSWER_PLAN = {"I": 4, "C": 3, "R": 2, "A": 1, "S": 1, "E": 0}

results = []


def check(name, condition, detail=""):
    results.append((name, bool(condition), detail))
    print(f"  {'PASS' if condition else 'FAIL'}  {name}" + (f"  — {detail}" if detail else ""))
    return bool(condition)


def get_json(url):
    with urllib.request.urlopen(url, timeout=30) as resp:
        return resp.status, json.loads(resp.read().decode())


def post_json(url, payload, timeout=180):
    req = urllib.request.Request(
        url,
        data=json.dumps(payload).encode(),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return resp.status, json.loads(resp.read().decode())
    except urllib.error.HTTPError as e:
        body = e.read().decode()
        try:
            return e.code, json.loads(body)
        except json.JSONDecodeError:
            return e.code, {"raw": body}


def clarity_payload():
    return {
        "desiredRoles": ["Data Analyst", "Software Developer", "QA Analyst"],
        "fitRatings": [5, 4, 3],
        "excitementRatings": [5, 4, 3],
        "genuineInterest": 5,
        "popularityMotivation": 3,
        "familyExpectation": 2,
        "salaryNeutralChoice": 0,
    }


# ── API tests ──────────────────────────────────────────────────────────────

def test_question_bank(base):
    print("\n[API] Question bank")
    status, questions = get_json(f"{base}/api/riasec/questions")
    check("questions endpoint returns 200", status == 200, f"status={status}")
    check(
        f"exactly {EXPECTED_QUESTIONS} items",
        len(questions) == EXPECTED_QUESTIONS,
        f"got {len(questions)}",
    )

    ids = [q["id"] for q in questions]
    check("ids are 1..30 with no gaps or duplicates", ids == list(range(1, EXPECTED_QUESTIONS + 1)))

    per_area = {a: sum(1 for q in questions if q["area"] == a) for a in AREAS}
    check(
        f"{ITEMS_PER_AREA} items per RIASEC area",
        all(per_area[a] == ITEMS_PER_AREA for a in AREAS),
        str(per_area),
    )
    check(
        "every item names its area correctly",
        all(q["areaName"] == AREA_NAMES.get(q["area"]) for q in questions),
    )
    check("no item text is blank", all(q["text"].strip() for q in questions))
    check(
        "old multiple-choice shape is gone (no scenario/options fields)",
        all("scenario" not in q and "options" not in q for q in questions),
    )

    # Spot-check against the published O*NET item bank.
    first = next(q for q in questions if q["id"] == 1)
    last = next(q for q in questions if q["id"] == EXPECTED_QUESTIONS)
    check(
        "item 1 matches O*NET wording",
        first["text"] == "Build kitchen cabinets" and first["area"] == "R",
        first["text"],
    )
    check(
        "item 30 matches O*NET wording",
        last["text"] == "Stamp, sort, and distribute mail for an organization" and last["area"] == "C",
        last["text"],
    )
    return questions


def test_scoring(base, questions):
    print("\n[API] Likert scoring")
    answers = {str(q["id"]): ANSWER_PLAN[q["area"]] for q in questions}
    status, result = post_json(
        f"{base}/api/riasec/assess",
        {
            "riasecAnswers": answers,
            "careerClarity": clarity_payload(),
            "gwaCategory": "Very Satisfactory",
            "honors": "No Honors",
            "skills": ["Critical Thinking"],
            "includePrediction": False,
        },
    )
    if not check("assess returns 200", status == 200, str(result)[:200]):
        return

    scores = result["riasecScores"]
    expected = {
        "realistic": ANSWER_PLAN["R"] * ITEMS_PER_AREA,
        "investigative": ANSWER_PLAN["I"] * ITEMS_PER_AREA,
        "artistic": ANSWER_PLAN["A"] * ITEMS_PER_AREA,
        "social": ANSWER_PLAN["S"] * ITEMS_PER_AREA,
        "enterprising": ANSWER_PLAN["E"] * ITEMS_PER_AREA,
        "conventional": ANSWER_PLAN["C"] * ITEMS_PER_AREA,
    }
    check(
        "ratings are summed per area, not counted",
        {k: scores[k] for k in expected} == expected,
        f"got {scores}, expected {expected}",
    )
    check(
        f"no area exceeds the {MAX_AREA_SCORE}-point maximum",
        all(v <= MAX_AREA_SCORE for v in scores.values()),
    )
    check(
        "dominant code is the top-3 areas by score",
        result["dominantCode"] == "ICR",
        result["dominantCode"],
    )
    check(
        "top dimensions match the dominant code",
        result["topDimensions"] == ["Investigative", "Conventional", "Realistic"],
        str(result["topDimensions"]),
    )

    # All "Strongly Like" — every area should max out.
    status, maxed = post_json(
        f"{base}/api/riasec/assess",
        {
            "riasecAnswers": {str(q["id"]): 4 for q in questions},
            "careerClarity": clarity_payload(),
            "gwaCategory": "Outstanding",
            "honors": "No Honors",
            "skills": ["Critical Thinking"],
            "includePrediction": False,
        },
    )
    check(
        "all Strongly Like gives every area the maximum score",
        status == 200 and all(v == MAX_AREA_SCORE for v in maxed["riasecScores"].values()),
        str(maxed.get("riasecScores")),
    )


def test_validation(base, questions):
    print("\n[API] Answer validation")
    complete = {str(q["id"]): 2 for q in questions}

    def assess(answers):
        return post_json(
            f"{base}/api/riasec/assess",
            {
                "riasecAnswers": answers,
                "careerClarity": clarity_payload(),
                "gwaCategory": "Satisfactory",
                "honors": "No Honors",
                "skills": ["Critical Thinking"],
                "includePrediction": False,
            },
            timeout=30,
        )

    status, body = assess({**complete, "1": 9})
    check("rating above the scale is rejected", status == 400, f"{status} {body}")

    status, body = assess({**complete, "1": -1})
    check("negative rating is rejected", status == 400, f"{status} {body}")

    status, body = assess({**complete, "999": 3})
    check("unknown question id is rejected", status == 400, f"{status} {body}")

    partial = dict(list(complete.items())[:10])
    status, body = assess(partial)
    check("incomplete answer set is rejected", status == 400, f"{status} {body}")

    status, body = assess({})
    check("empty answer set is rejected", status == 400, f"{status} {body}")

    status, body = assess({str(q["id"]): 0 for q in questions})
    check("all Strongly Dislike (rating 0) is accepted", status == 200, str(status))


# ── UI test ────────────────────────────────────────────────────────────────

def test_ui(base, questions, headed, full, slow_mo, hold):
    from playwright.sync_api import sync_playwright

    print("\n[UI] Wizard flow")
    page_count = (len(questions) + QUESTIONS_PER_PAGE - 1) // QUESTIONS_PER_PAGE

    with sync_playwright() as p:
        launch_args = ["--window-position=40,40", "--window-size=1400,1000"] if headed else []
        browser = p.chromium.launch(headless=not headed, slow_mo=slow_mo, args=launch_args)
        page = browser.new_page(no_viewport=True) if headed else browser.new_page(
            viewport={"width": 1280, "height": 1000}
        )
        try:
            page.goto(f"{base}/riasec", wait_until="networkidle")
            if headed:
                page.bring_to_front()
                page.wait_for_timeout(1500)  # give the window a moment to surface

            page.get_by_role("button", name="Continue as Guest").click()
            page.get_by_text("I have read and agree").click()
            page.get_by_role("button", name="Accept and Begin Assessment").click()
            page.wait_for_selector("label.mud-radio")

            body = page.inner_text("body")
            check("quiz intro names the O*NET Interest Profiler", "O*NET Interest Profiler" in body)
            check(f"intro states {len(questions)} work activities", f"{len(questions)} work activities" in body)
            check(f"quiz opens on page 1 of {page_count}", f"Page 1 of {page_count}" in body)
            check("scale labels are shown", all(lbl in body for lbl in SCALE_LABELS))

            for page_index in range(page_count):
                page_questions = questions[page_index * QUESTIONS_PER_PAGE:(page_index + 1) * QUESTIONS_PER_PAGE]
                radios = page.locator("label.mud-radio")
                radios.first.wait_for()

                check(
                    f"page {page_index + 1} shows {len(page_questions)} items with 5 options each",
                    radios.count() == len(page_questions) * len(SCALE_LABELS),
                    f"{radios.count()} radios",
                )

                # Page 1 also renders the intro card, so scope to the card holding the items.
                content = page.inner_text(".mud-card:has(label.mud-radio) .mud-card-content")
                for q in page_questions:
                    if q["text"] not in content:
                        check(f"item '{q['text'][:30]}' rendered", False)
                        break
                else:
                    check(f"page {page_index + 1} renders its 6 item texts", True)

                leaked = [n for n in AREA_NAMES.values() if n in content]
                check(
                    f"page {page_index + 1} hides the interest area from the student",
                    not leaked,
                    f"leaked: {leaked}",
                )

                for item_index, q in enumerate(page_questions):
                    rating = ANSWER_PLAN[q["area"]]
                    option = radios.nth(item_index * len(SCALE_LABELS) + rating)
                    option.scroll_into_view_if_needed()
                    option.click()

                answered = (page_index + 1) * len(page_questions)
                page.wait_for_function(
                    f"document.body.innerText.includes('{answered} / {len(questions)} answered')"
                )

                if page_index < page_count - 1:
                    page.get_by_role("button", name="Next").click()
                    page.wait_for_function(
                        f"document.body.innerText.includes('Page {page_index + 2} of {page_count}')"
                    )

            check(
                f"all {len(questions)} items answered",
                f"{len(questions)} / {len(questions)} answered" in page.inner_text("body"),
            )

            page.get_by_role("button", name="Continue to Career Clarity").click()
            page.wait_for_timeout(2000)
            check(
                "quiz hands off to the Career Clarity step",
                "Career Clarity" in page.inner_text("body")
                and "work activities" not in page.inner_text("body"),
            )

            if full:
                test_ui_results(page, questions)

            if headed and hold:
                print(f"  (holding the window open for {hold}s)")
                page.wait_for_timeout(hold * 1000)
        finally:
            browser.close()


def select_mud_option(page, label, option_index=0):
    """Open a MudSelect by its label and pick an item from the popover."""
    page.get_by_label(label, exact=False).first.click()
    page.wait_for_selector(".mud-popover .mud-list-item")
    page.locator(".mud-popover .mud-list-item").nth(option_index).click()
    page.wait_for_timeout(300)


def test_ui_results(page, questions):
    """Optional --full leg: finish the wizard and read the score bars."""
    print("\n[UI] Results page (--full)")

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
    check(f"score bars are out of {MAX_AREA_SCORE}", f"/ {MAX_AREA_SCORE}" in body)
    check("no score bar still reads out of 10", "/ 10" not in body)
    check(
        "all six dimensions are reported",
        all(f"{name} ({code})" in body for code, name in AREA_NAMES.items()),
    )

    # The answers were driven by ANSWER_PLAN, so every reported score is known.
    reported = {
        code: int(score)
        for code, score in re.findall(
            rf"\w+ \(([RIASEC])\)\s*\n\s*(\d+) / {MAX_AREA_SCORE}", body
        )
    }
    for code, name in AREA_NAMES.items():
        expected = ANSWER_PLAN[code] * ITEMS_PER_AREA
        check(
            f"{name} bar reads {expected} / {MAX_AREA_SCORE}",
            reported.get(code) == expected,
            f"UI shows {reported.get(code)}",
        )


# ── Runner ─────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="http://localhost:5001")
    parser.add_argument("--headed", action="store_true", help="show the browser")
    parser.add_argument("--hold", type=int, default=10, metavar="SECONDS",
                        help="with --headed, keep the window open this long at the end")
    parser.add_argument("--slow-mo", type=int, default=0, metavar="MS",
                        help="pause this many milliseconds between UI actions so the run is watchable")
    parser.add_argument("--full", action="store_true", help="also walk through to the Results page")
    parser.add_argument("--api-only", action="store_true", help="skip the Playwright leg")
    args = parser.parse_args()

    base = args.base_url.rstrip("/")
    print(f"Testing {base}")

    try:
        questions = test_question_bank(base)
    except urllib.error.URLError as e:
        print(f"\nCannot reach {base} — start the server first "
              f"(dotnet run --project src/CareerPath.Server). {e}")
        return 2

    test_scoring(base, questions)
    test_validation(base, questions)

    if not args.api_only:
        test_ui(base, questions, args.headed, args.full, args.slow_mo, args.hold)

    passed = sum(1 for _, ok, _ in results if ok)
    failed = len(results) - passed
    print(f"\n{'=' * 60}\n{passed} passed, {failed} failed")
    if failed:
        for name, ok, detail in results:
            if not ok:
                print(f"  FAIL  {name}  {detail}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
