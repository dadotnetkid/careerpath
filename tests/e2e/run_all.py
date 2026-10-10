#!/usr/bin/env python3
"""Run every Career Path end-to-end suite against a live server.

    python3 tests/e2e/run_all.py                      # API + browser legs
    python3 tests/e2e/run_all.py --api-only           # no browser
    python3 tests/e2e/run_all.py --headed --slow-mo 300 --record
    python3 tests/e2e/run_all.py --only riasec,auth
    python3 tests/e2e/run_all.py --full --train       # everything, including a retrain
"""

import importlib
import sys
import traceback

import requests

from common import Api, Report, build_parser, config_from_args

# Order matters a little: sign-in and prediction first, so a broken server is
# obvious before the long legs run.
SUITE_MODULES = [
    "test_public_pages",
    "test_auth",
    "test_predict",
    "test_riasec_profiler",
    "test_saved_results",
    "test_admin_dataset",
    "test_assessment_records",
    "test_training",
]


def main():
    parser = build_parser(__doc__)
    parser.add_argument("--only", default="", metavar="NAMES",
                        help="comma-separated suite names to run (e.g. riasec,auth)")
    args = parser.parse_args()
    cfg = config_from_args(args)

    wanted = {n.strip().lower() for n in args.only.split(",") if n.strip()}

    print(f"Career Path end-to-end suites — {cfg.base_url}")
    if cfg.record:
        print(f"Recording browser legs to {cfg.record_dir}")

    try:
        requests.get(f"{cfg.base_url}/api/predict/status", timeout=10)
    except requests.RequestException as e:
        print(f"\nCannot reach {cfg.base_url} — start the server first "
              f"(dotnet run --project src/CareerPath.Server).\n  {e}")
        return 2

    report = Report()
    crashed = []

    for module_name in SUITE_MODULES:
        module = importlib.import_module(module_name)
        suite = getattr(module, "SUITE", module_name)
        if wanted and suite.lower() not in wanted and module_name.lower() not in wanted:
            continue

        print(f"\n{'─' * 64}\n{suite}\n{'─' * 64}")
        report.suite = suite
        try:
            module.run(cfg, report, Api(cfg.base_url))
        except Exception:
            crashed.append(suite)
            report.check(f"{suite} suite ran to completion", False, "raised an exception")
            traceback.print_exc()

    exit_code = report.summary()
    if crashed:
        print(f"\nSuites that raised: {', '.join(crashed)}")
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
