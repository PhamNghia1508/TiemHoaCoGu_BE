#!/usr/bin/env python3
"""Reject incomplete acceptance runs; no third-party dependencies required."""
import argparse
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


def verify(path: Path) -> str:
    root = ET.parse(path).getroot()
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    results = root.findall(".//t:UnitTestResult", ns)
    if not results:
        raise ValueError("No test results found")
    incomplete = [f"{r.get('testName')}: {r.get('outcome')}" for r in results if r.get("outcome") != "Passed"]
    if incomplete:
        raise ValueError("Acceptance requires every test to pass (no skipped SQL tests):\n" + "\n".join(incomplete))
    required = ("SqlServerBulkAuditTests", "SqlServerWorkshopConcurrencyTests", "SqlServerRecoveryTests")
    methods = root.findall(".//t:TestMethod", ns)
    for name in required:
        if not any(name in m.get("className", "") for m in methods):
            raise ValueError(f"Missing required SQL Server integration test class: {name}")
    return f"Acceptance complete: {len(results)} passed, 0 failed, 0 skipped; SQL Server suites present."


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("trx", type=Path)
    args = parser.parse_args()
    try:
        print(verify(args.trx))
    except (OSError, ET.ParseError, ValueError) as error:
        print(str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
