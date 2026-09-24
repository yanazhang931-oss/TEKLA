# TEKLA Model-vs-Basis Checker

Tooling to check whether a Tekla Structures model matches the structural
basis (IFC steel elevation drawings) it was built from — currently set up
for:

**LP BUFFER VESSEL MODULE (71010RM0-R1)** — Venture Global NRU Phase 2
- `&AA-(71010RM0-R1)-N-ZD 1001.201` — ROW R1 elevation
- `&AA-(71010RM0-R1)-N-ZD 1001.202` — ROW R2 elevation

## Why this isn't a single "auto-check" script

The basis drawings are PDFs. Their steel call-outs (HEA300, HEB600, U140,
L90x90x9, ...) are plain text objects with **no coordinate/position data**
attached in the PDF — extracting the text gives you the set of profiles used
somewhere on the sheet, not which profile sits at which grid/elevation. So a
fully automatic "read the PDF, read the model, diff them" tool would silently
produce wrong matches.

Instead this repo splits the problem into three honest, verifiable steps:

1. **Export the model** (`tools/TeklaModelExport`) — a Tekla Open API C#
   console app that connects to your running Tekla session and dumps every
   beam/column/brace to CSV: profile, position, nearest grid, and top-of-steel
   (TOS) elevation.
2. **Basis reference tables** (`basis/`) — the grid spacing and TOS/BOBP/Splice
   elevation levels transcribed from the two drawings. These *are* reliably
   extractable (they're dimension strings, not scattered call-outs) and are
   the backbone for grouping model members. Profile-per-position still needs
   your eyes on the drawing once — see `basis/ROW_R1_basis.csv` /
   `ROW_R2_basis.csv`, which have the levels/grids pre-filled and a `Profile`
   column left for you to fill in (or leave blank to skip that check).
3. **Compare** (`tools/compare/compare_model_to_basis.py`) — groups the model
   export by row + grid bay + nearest basis elevation, and reports:
   - elevations/grids present in the model but not in the basis table
   - basis rows with no matching model member (missing in model)
   - profile mismatches where the basis `Profile` column is filled in

## Quick start

1. Open the Tekla model, then run the exporter (see
   `tools/TeklaModelExport/README.md`) to produce `model_export.csv`.
2. Fill in the `Profile` column in `basis/ROW_R1_basis.csv` and
   `basis/ROW_R2_basis.csv` from the drawings (or leave rows blank if you
   only want the grid/elevation completeness check).
3. Run:
   ```bash
   cd tools/compare
   pip install -r requirements.txt
   python compare_model_to_basis.py \
     --model ../../model_export.csv \
     --basis ../../basis/ROW_R1_basis.csv ../../basis/ROW_R2_basis.csv \
     --grid ../../basis/grid_geometry.csv \
     --out report.csv
   ```
4. Read `report.csv` — every row is one discrepancy, with a `Type` column
   (`missing_in_model`, `extra_in_model`, `profile_mismatch`) and enough
   context (row, grid, elevation, expected vs. actual profile) to jump
   straight to that spot in Tekla.

## Repo layout

```
basis/
  grid_geometry.csv       grid line spacing (shared by ROW R1 and ROW R2)
  ROW_R1_basis.csv         TOS/BOBP/Splice levels for ROW R1, from 1001.201
  ROW_R2_basis.csv         TOS/BOBP/Splice levels for ROW R2, from 1001.202
docs/
  ANALYSIS_NOTES.md         what was extracted from the PDFs, and confidence/caveats
tools/
  TeklaModelExport/         Open API C# exporter -> model_export.csv
  compare/                  Python comparison script
```

See `docs/ANALYSIS_NOTES.md` for exactly what was read off the two drawings
and what still needs your verification.
