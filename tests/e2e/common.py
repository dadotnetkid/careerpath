"""Shared helpers for the Career Path end-to-end suites.

Every suite module exposes ``SUITE`` (its name) and ``run(cfg, report)``.
``run_all.py`` discovers them; each module is also runnable on its own.
"""

from __future__ import annotations

import argparse
import contextlib
import json
import os
import time
import uuid
from dataclasses import dataclass, field

import requests

DEFAULT_BASE_URL = "http://localhost:5001"

# Seeded from Auth:AdminEmail / Auth:AdminPassword in appsettings.json. Override
# with CAREERPATH_ADMIN_EMAIL / CAREERPATH_ADMIN_PASSWORD when they differ.
ADMIN_EMAIL = os.environ.get("CAREERPATH_ADMIN_EMAIL", "gj@nvsu.edu.ph")
ADMIN_PASSWORD = os.environ.get("CAREERPATH_ADMIN_PASSWORD", "ChangeMe1OnFirstRun!")

RIASEC_AREAS = ["R", "I", "A", "S", "E", "C"]
AREA_NAMES = {
    "R": "Realistic",
    "I": "Investigative",
    "A": "Artistic",
    "S": "Social",
    "E": "Enterprising",
    "C": "Conventional",
}
SCALE_LABELS = ["Strongly Dislike", "Dislike", "Unsure", "Like", "Strongly Like"]
ITEMS_PER_AREA = 5
MAX_AREA_SCORE = ITEMS_PER_AREA * 4
QUESTIONS_PER_PAGE = 6
EXPECTED_QUESTIONS = 30


# ── Config ─────────────────────────────────────────────────────────────────

@dataclass
class Config:
    base_url: str = DEFAULT_BASE_URL
    headed: bool = False
    slow_mo: int = 0
    hold: int = 0
    record: bool = False
    record_dir: str = ""
    api_only: bool = False
    full: bool = False
    train: bool = False


def build_parser(description: str) -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description=description)
    p.add_argument("--base-url", default=DEFAULT_BASE_URL)
    p.add_argument("--headed", action="store_true", help="show the browser")
    p.add_argument("--slow-mo", type=int, default=0, metavar="MS",
                   help="pause this long between UI actions so the run is watchable")
    p.add_argument("--hold", type=int, default=0, metavar="SECONDS",
                   help="with --headed, keep the window open this long at the end")
    p.add_argument("--record", action="store_true",
                   help="save a video of every browser leg under recordings/")
    p.add_argument("--record-dir", default="", metavar="DIR",
                   help="where recordings go (default: tests/e2e/recordings/<timestamp>)")
    p.add_argument("--api-only", action="store_true", help="skip the browser legs")
    p.add_argument("--full", action="store_true",
                   help="include the slow legs (assessment submit, results page)")
    p.add_argument("--train", action="store_true",
                   help="also kick a real training run (slow, retrains the model)")
    return p


def config_from_args(args) -> Config:
    record_dir = args.record_dir
    if args.record and not record_dir:
        stamp = time.strftime("%Y%m%d-%H%M%S")
        record_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "recordings", stamp)
    return Config(
        base_url=args.base_url.rstrip("/"),
        headed=args.headed,
        slow_mo=args.slow_mo,
        hold=args.hold,
        record=args.record,
        record_dir=record_dir,
        api_only=args.api_only,
        full=args.full,
        train=args.train,
    )


# ── Reporting ──────────────────────────────────────────────────────────────

@dataclass
class Report:
    rows: list = field(default_factory=list)   # (suite, name, ok, detail)
    videos: list = field(default_factory=list)
    suite: str = ""

    def section(self, title: str):
        print(f"\n[{self.suite}] {title}")

    MAX_DETAIL = 160

    def check(self, name: str, condition, detail: str = "") -> bool:
        ok = bool(condition)
        if len(detail) > self.MAX_DETAIL:
            detail = detail[:self.MAX_DETAIL] + "…"
        self.rows.append((self.suite, name, ok, detail))
        print(f"  {'PASS' if ok else 'FAIL'}  {name}" + (f"  — {detail}" if detail else ""))
        return ok

    def skip(self, name: str, why: str):
        print(f"  SKIP  {name}  — {why}")

    @property
    def passed(self) -> int:
        return sum(1 for *_, ok, _ in self.rows if ok)

    @property
    def failed(self) -> int:
        return len(self.rows) - self.passed

    def summary(self) -> int:
        print("\n" + "=" * 64)
        print(f"{self.passed} passed, {self.failed} failed")
        if self.failed:
            for suite, name, ok, detail in self.rows:
                if not ok:
                    print(f"  FAIL  [{suite}] {name}  {detail}")
        if self.videos:
            print("\nRecordings:")
            for v in self.videos:
                print(f"  {v}")
        return 1 if self.failed else 0


# ── API client ─────────────────────────────────────────────────────────────

class Api:
    """Cookie-aware API client. One instance == one browser-less session."""

    def __init__(self, base_url: str):
        self.base_url = base_url.rstrip("/")
        self.session = requests.Session()

    def _url(self, path: str) -> str:
        return path if path.startswith("http") else f"{self.base_url}{path}"

    def request(self, method: str, path: str, json_body=None, timeout=60):
        resp = self.session.request(method, self._url(path), json=json_body, timeout=timeout)
        try:
            return resp.status_code, resp.json()
        except json.JSONDecodeError:
            return resp.status_code, resp.text

    def get(self, path, timeout=60):
        return self.request("GET", path, timeout=timeout)

    def post(self, path, body=None, timeout=60):
        return self.request("POST", path, json_body=body, timeout=timeout)

    def delete(self, path, timeout=60):
        return self.request("DELETE", path, timeout=timeout)

    # ── auth shortcuts ──
    def login(self, email: str, password: str):
        return self.post("/api/auth/login", {"email": email, "password": password, "rememberMe": False})

    def register(self, email: str, password: str, full_name: str = ""):
        return self.post("/api/auth/register", {"email": email, "password": password, "fullName": full_name})

    def logout(self):
        return self.post("/api/auth/logout")

    def me(self):
        return self.get("/api/auth/me")

    def login_admin(self):
        return self.login(ADMIN_EMAIL, ADMIN_PASSWORD)


def unique_email(prefix: str = "e2e") -> str:
    return f"{prefix}-{uuid.uuid4().hex[:10]}@nvsu.edu.ph"


def strong_password() -> str:
    return "E2eTest!" + uuid.uuid4().hex[:6].capitalize()


# ── Assessment payload helpers ─────────────────────────────────────────────

# Rating applied to every item of an area, so each area's expected score is
# deterministic: score = rating * ITEMS_PER_AREA.
ANSWER_PLAN = {"I": 4, "C": 3, "R": 2, "A": 1, "S": 1, "E": 0}


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


def assessment_payload(questions, answers=None, include_prediction=False,
                       gwa="Very Satisfactory", skills=None):
    return {
        # `answers` may legitimately be {} (the empty-answer-set check), so test
        # for None rather than truthiness.
        "riasecAnswers": {str(q["id"]): ANSWER_PLAN[q["area"]] for q in questions}
                         if answers is None else answers,
        "careerClarity": clarity_payload(),
        "gwaCategory": gwa,
        "honors": "No Honors",
        "skills": skills or ["Critical Thinking"],
        "includePrediction": include_prediction,
    }


def sample_student_input():
    return {
        "yearGraduated": 2023,
        "gwa": "Very Satisfactory",
        "honors": "No Honors",
        "competencies": "Information Technology Skills, Critical Thinking",
        "careerClarity": "High",
        "interest": "ICR",
    }


# ── Browser ────────────────────────────────────────────────────────────────

@contextlib.contextmanager
def browser_page(cfg: Config, report: Report, name: str):
    """Chromium page, optionally visible and recorded. Video path lands in report.videos."""
    from playwright.sync_api import sync_playwright

    with sync_playwright() as p:
        launch_args = ["--window-position=40,40", "--window-size=1400,1000"] if cfg.headed else []
        browser = p.chromium.launch(headless=not cfg.headed, slow_mo=cfg.slow_mo, args=launch_args)

        context_args = {}
        if cfg.headed:
            context_args["no_viewport"] = True
        else:
            context_args["viewport"] = {"width": 1400, "height": 1000}
        if cfg.record:
            os.makedirs(cfg.record_dir, exist_ok=True)
            context_args["record_video_dir"] = cfg.record_dir
            context_args["record_video_size"] = {"width": 1400, "height": 1000}

        context = browser.new_context(**context_args)
        page = context.new_page()
        try:
            yield page
            if cfg.headed and cfg.hold:
                print(f"  (holding the window open for {cfg.hold}s)")
                page.wait_for_timeout(cfg.hold * 1000)
        finally:
            video = page.video
            context.close()          # video is only flushed once the context closes
            if cfg.record and video:
                target = os.path.join(cfg.record_dir, f"{name}.webm")
                with contextlib.suppress(Exception):
                    video.save_as(target)      # copies out under a readable name
                    video.delete()             # drop Playwright's page@<hash> original
                    report.videos.append(target)
            browser.close()


def goto(page, cfg: Config, path: str, wait="networkidle"):
    page.goto(f"{cfg.base_url}{path}", wait_until=wait)
    page.wait_for_timeout(400)


def ui_login(page, cfg: Config, email: str, password: str, report: Report | None = None):
    """Sign in through the top-bar dialog. Returns True when the bar shows the user."""
    goto(page, cfg, "/")
    page.get_by_role("button", name="Sign In").first.click()
    page.wait_for_selector(".mud-dialog")
    dialog = page.locator(".mud-dialog")
    # MudBlazor renders a "show or hide password" button that also carries the
    # Password label, so target the inputs by type rather than by label.
    dialog.locator("input[type='email']").first.fill(email)
    dialog.locator("input[type='password']").first.fill(password)
    dialog.get_by_role("button", name="Sign In").last.click()
    page.wait_for_timeout(2500)
    # The top bar swaps the Sign In / Sign Up buttons for an account menu whose
    # button shows the display name (full name when set, otherwise the email)
    # and the role.
    signed_in = page.locator(".mud-appbar .mud-menu").count() > 0
    if report:
        report.check(f"signed in through the UI as {email}", signed_in)
    return signed_in


def account_menu_text(page) -> str:
    return page.locator(".mud-appbar .mud-menu").inner_text()


def ui_logout(page):
    """Open the top-bar account menu and sign out. Returns True once the bar resets."""
    # MudMenu's activator opens on keyboard activation reliably; a synthetic
    # click on the button alone does not open the popover in Chromium.
    activator = page.locator(".mud-appbar .mud-menu button").first
    activator.focus()
    page.keyboard.press("Enter")
    sign_out = page.locator(".mud-popover").get_by_text("Sign Out").first
    sign_out.wait_for(state="visible", timeout=10_000)
    sign_out.click()
    page.wait_for_timeout(2500)
    return page.locator(".mud-appbar .mud-menu").count() == 0


def run_module(module_run, description: str):
    """Standalone entry point shared by every suite module."""
    args = build_parser(description).parse_args()
    cfg = config_from_args(args)
    report = Report()
    api = Api(cfg.base_url)
    try:
        api.get("/api/predict/status", timeout=10)
    except requests.RequestException as e:
        print(f"Cannot reach {cfg.base_url} — start the server first "
              f"(dotnet run --project src/CareerPath.Server). {e}")
        return 2
    module_run(cfg, report, api)
    return report.summary()
