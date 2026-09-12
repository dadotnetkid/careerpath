#!/usr/bin/env python3
"""Authentication: registration, sign-in, sign-out, roles and role gates."""

import sys

from common import (ADMIN_EMAIL, ADMIN_PASSWORD, Api, run_module, strong_password,
                    unique_email, ui_login, ui_logout, account_menu_text, goto, browser_page)

SUITE = "auth"


def run(cfg, report, api):
    report.suite = SUITE

    report.section("Anonymous state")
    status, me = api.get("/api/auth/me")
    report.check("me returns 200 for an anonymous caller", status == 200, str(status))
    report.check("anonymous caller is not authenticated", me.get("isAuthenticated") is False, str(me))
    report.check("anonymous caller has no role", not me.get("role"), str(me.get("role")))

    report.section("Student registration")
    student = Api(cfg.base_url)
    email, password = unique_email("student"), strong_password()
    status, reg = student.register(email, password, "E2E Test Student")
    report.check("register returns 200", status == 200, str(reg)[:160])
    report.check("new account lands in the Student role", reg.get("role") == "Student", str(reg.get("role")))
    report.check("register signs the student in", reg.get("isAuthenticated") is True)
    report.check("register keeps the display name", reg.get("fullName") == "E2E Test Student", str(reg.get("fullName")))

    status, me = student.me()
    report.check("me reports the signed-in student", me.get("email") == email, str(me.get("email")))

    report.section("Registration is guarded")
    status, body = student.register(email, strong_password())
    report.check("duplicate email is rejected", status == 409, f"{status} {body}")

    status, body = api.register(unique_email("short"), "short")
    report.check("password under 8 characters is rejected", status == 400, f"{status} {body}")

    status, body = api.register("not-an-email", strong_password())
    report.check("malformed email is rejected", status == 400, f"{status} {body}")

    report.section("Sign in and sign out")
    status, body = student.logout()
    report.check("logout returns 204", status == 204, str(status))
    status, me = student.me()
    report.check("session is anonymous after logout", me.get("isAuthenticated") is False)

    status, body = student.login(email, "WrongPassword123!")
    report.check("wrong password is rejected", status == 401, f"{status} {body}")

    status, body = student.login(unique_email("ghost"), password)
    report.check("unknown email is rejected", status == 401, f"{status} {body}")

    status, body = student.login(email, password)
    report.check("correct credentials sign the student back in", status == 200 and body.get("role") == "Student",
                 f"{status} {body}")

    report.section("Admin account")
    admin = Api(cfg.base_url)
    status, body = admin.login_admin()
    signed_in = status == 200 and body.get("role") == "Admin"
    report.check(f"seeded admin {ADMIN_EMAIL} signs in with the Admin role", signed_in, f"{status} {body}")
    if not signed_in:
        report.skip("role gate checks", "admin sign-in failed; check Auth:AdminEmail / Auth:AdminPassword")
        return

    report.section("Role gates")
    status, _ = api.get("/api/dataset?page=1&pageSize=5")
    report.check("anonymous caller cannot read the dataset", status in (401, 403), str(status))

    status, _ = student.get("/api/dataset?page=1&pageSize=5")
    report.check("student cannot read the dataset", status == 403, str(status))

    status, _ = student.get("/api/training/latest")
    report.check("student cannot read training history", status == 403, str(status))

    status, _ = admin.get("/api/dataset?page=1&pageSize=5")
    report.check("admin can read the dataset", status == 200, str(status))

    status, _ = admin.get("/api/results")
    report.check("admin cannot read student saved results", status == 403, str(status))

    status, _ = api.get("/api/results")
    report.check("anonymous caller cannot read saved results", status in (401, 403), str(status))

    if cfg.api_only:
        return

    report.section("Sign-in through the UI")
    with browser_page(cfg, report, SUITE) as page:
        ui_login(page, cfg, email, password, report)
        bar = account_menu_text(page)
        report.check("top bar shows the student's display name", "E2E Test Student" in bar, bar[:60])
        report.check("top bar shows the Student role", "Student" in bar, bar[:60])
        report.check("student sees My Saved Results in the nav",
                     "My Saved Results" in page.inner_text("body"), "")
        report.check("sign out returns the bar to Sign In", ui_logout(page))
        report.check("the signed-out bar offers Sign In again", "Sign In" in page.inner_text("body"))


if __name__ == "__main__":
    sys.exit(run_module(run, __doc__))
