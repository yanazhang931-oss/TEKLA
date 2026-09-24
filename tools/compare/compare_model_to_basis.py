#!/usr/bin/env python3
"""
Compare a Tekla model export against the drawing-derived basis tables.

Usage:
    python compare_model_to_basis.py \
        --model model_export.csv \
        --basis ROW_R1_basis.csv ROW_R2_basis.csv \
        --grid grid_geometry.csv \
        --out report.csv \
        [--elevation-tolerance-mm 150]

Input files:
    --model   CSV produced by tools/TeklaModelExport (Program.cs). Must have
              columns: Row, GridFrom, GridTo, TOS_mm, Profile, AssemblyPos, Id
    --basis   One or more basis CSVs (basis/ROW_R1_basis.csv, ROW_R2_basis.csv).
              Columns: Row, LevelType, Elevation_m, Scope, GridFrom, GridTo, Profile
    --grid    basis/grid_geometry.csv, used only to sanity-check grid numbers.

Output:
    A CSV report, one row per discrepancy, with a `Type` column:
      - missing_in_model   a basis row has no model member near it
      - extra_in_model     a model member has no nearby basis row
      - profile_mismatch   nearby, but the basis Profile (if set) differs
                            from every candidate model member's profile
    Also prints a one-line summary to stdout.
"""

import argparse
import csv
import sys
from collections import defaultdict


def read_csv(path):
    with open(path, newline="", encoding="utf-8-sig") as f:
        return list(csv.DictReader(f))


def load_basis(paths):
    rows = []
    for p in paths:
        rows.extend(read_csv(p))
    return rows


def grids_overlap(a_from, a_to, b_from, b_to):
    """True if grid ranges [a_from, a_to] and [b_from, b_to] overlap at all."""
    lo = max(min(a_from, a_to), min(b_from, b_to))
    hi = min(max(a_from, a_to), max(b_from, b_to))
    return lo <= hi


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                  formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--model", required=True)
    ap.add_argument("--basis", nargs="+", required=True)
    ap.add_argument("--grid", required=False)
    ap.add_argument("--out", default="report.csv")
    ap.add_argument("--elevation-tolerance-mm", type=float, default=150.0,
                     help="how close (mm) a model member's TOS must be to a "
                          "basis elevation to be considered a match")
    args = ap.parse_args()

    model_rows = read_csv(args.model)
    basis_rows = load_basis(args.basis)

    if not model_rows:
        print(f"WARNING: {args.model} has no rows", file=sys.stderr)
    if not basis_rows:
        print("ERROR: no basis rows loaded", file=sys.stderr)
        sys.exit(1)

    tol = args.elevation_tolerance_mm
    report = []

    # index model members by Row for faster scanning
    model_by_row = defaultdict(list)
    for m in model_rows:
        model_by_row[m.get("Row", "UNKNOWN")].append(m)

    matched_model_ids = set()

    # --- pass 1: for each basis row, find candidate model members ---
    for b in basis_rows:
        row = b["Row"]
        elev_mm = float(b["Elevation_m"]) * 1000.0
        g_from = int(b["GridFrom"])
        g_to = int(b["GridTo"])
        expected_profile = (b.get("Profile") or "").strip()

        candidates = []
        for m in model_by_row.get(row, []):
            try:
                m_tos = float(m["TOS_mm"])
                m_gfrom = int(m["GridFrom"])
                m_gto = int(m["GridTo"])
            except (ValueError, KeyError):
                continue
            if abs(m_tos - elev_mm) > tol:
                continue
            if not grids_overlap(g_from, g_to, m_gfrom, m_gto):
                continue
            candidates.append(m)

        if not candidates:
            report.append({
                "Type": "missing_in_model",
                "Row": row,
                "GridFrom": g_from, "GridTo": g_to,
                "Elevation_m": b["Elevation_m"],
                "LevelType": b.get("LevelType", ""),
                "Expected_Profile": expected_profile,
                "Model_Profile": "",
                "Model_Id": "",
                "Model_AssemblyPos": "",
                "Detail": b.get("Notes", ""),
            })
            continue

        for c in candidates:
            matched_model_ids.add(c.get("Id"))

        if expected_profile:
            mismatches = [c for c in candidates
                          if c.get("Profile", "").strip() != expected_profile]
            if mismatches and len(mismatches) == len(candidates):
                # none of the nearby members have the expected profile
                for c in mismatches:
                    report.append({
                        "Type": "profile_mismatch",
                        "Row": row,
                        "GridFrom": g_from, "GridTo": g_to,
                        "Elevation_m": b["Elevation_m"],
                        "LevelType": b.get("LevelType", ""),
                        "Expected_Profile": expected_profile,
                        "Model_Profile": c.get("Profile", ""),
                        "Model_Id": c.get("Id", ""),
                        "Model_AssemblyPos": c.get("AssemblyPos", ""),
                        "Detail": b.get("Notes", ""),
                    })

    # --- pass 2: model members not claimed by any basis row ---
    for m in model_rows:
        if m.get("Id") in matched_model_ids:
            continue
        report.append({
            "Type": "extra_in_model",
            "Row": m.get("Row", ""),
            "GridFrom": m.get("GridFrom", ""), "GridTo": m.get("GridTo", ""),
            "Elevation_m": round(float(m["TOS_mm"]) / 1000.0, 3) if m.get("TOS_mm") else "",
            "LevelType": "",
            "Expected_Profile": "",
            "Model_Profile": m.get("Profile", ""),
            "Model_Id": m.get("Id", ""),
            "Model_AssemblyPos": m.get("AssemblyPos", ""),
            "Detail": "no basis level/grid found within tolerance",
        })

    fieldnames = ["Type", "Row", "GridFrom", "GridTo", "Elevation_m", "LevelType",
                  "Expected_Profile", "Model_Profile", "Model_Id",
                  "Model_AssemblyPos", "Detail"]
    with open(args.out, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=fieldnames)
        w.writeheader()
        w.writerows(report)

    counts = defaultdict(int)
    for r in report:
        counts[r["Type"]] += 1

    print(f"Wrote {len(report)} discrepancies to {args.out}")
    for k in ("missing_in_model", "extra_in_model", "profile_mismatch"):
        print(f"  {k}: {counts.get(k, 0)}")


if __name__ == "__main__":
    main()
