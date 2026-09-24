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

1. **Export + compare + highlight** (`tools/TeklaModelExport`) — a Tekla
   Open API C# console app. Run it **with the model open in Tekla**: it
   dumps every beam/column/brace to CSV (profile, position, nearest grid,
   top-of-steel elevation), compares them against the basis tables, and
   **selects/highlights the mismatched parts directly in the Tekla view**,
   so you see problems on the model itself, not just in a report.
2. **Basis reference tables** (`basis/`) — the grid spacing and TOS/BOBP/Splice
   elevation levels transcribed from the two drawings. These *are* reliably
   extractable (they're dimension strings, not scattered call-outs) and are
   the backbone for grouping model members. Profile-per-position still needs
   your eyes on the drawing once — see `basis/ROW_R1_basis.csv` /
   `ROW_R2_basis.csv`, which have the levels/grids pre-filled and a `Profile`
   column left for you to fill in (or leave blank to skip that check).
3. **Standalone compare script** (`tools/compare/compare_model_to_basis.py`)
   — optional. Does the same comparison in Python from `model_export.csv`
   alone (no Tekla connection, no highlighting) — useful if you just want
   the report.csv without opening Tekla again, or want to review/tweak the
   comparison logic outside Tekla.

## Quick start (highlighting workflow — recommended)

1. Fill in the `Profile` column in `basis/ROW_R1_basis.csv` and
   `basis/ROW_R2_basis.csv` for anything you want checked by exact profile
   (leave blank to only check that *something* was modeled at that level).
2. Open the model in Tekla Structures.
3. Build and run `tools/TeklaModelExport` (see its README for the one-time
   setup of grid/row/datum constants). With the model open, it will:
   - write `model_export.csv` and `report.csv`
   - **select the mismatched/unexpected parts in the Tekla view** — they'll
     show up highlighted the moment the tool finishes
4. Zoom to the highlighted parts in Tekla to fix them; `report.csv` has the
   full list (including "missing" levels/grids, which have nothing to
   select — check those against the drawing manually).

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
