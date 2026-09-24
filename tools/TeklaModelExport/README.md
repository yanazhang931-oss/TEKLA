# TeklaModelExport

A small Open API C# console app that connects to your **currently running**
Tekla Structures session, exports every beam/column/brace, compares it
against the drawing-derived basis tables, and **highlights the problem
parts directly in the open Tekla view** by selecting them (Tekla draws
selected parts in its highlight color — you'll see them the moment the
tool finishes, no report-reading required).

## What it does, in order

1. Reads every `Beam` (covers columns, beams, braces) from the open model.
2. Writes `model_export.csv` — one row per member.
3. Loads `basis/ROW_R1_basis.csv` and `basis/ROW_R2_basis.csv`.
4. Compares them (same logic as `tools/compare/compare_model_to_basis.py`,
   grouping by row + grid + nearest elevation) and writes `report.csv`.
5. **Selects (highlights) every part that is "extra" (no matching basis
   row nearby) or has a "profile mismatch"** (basis `Profile` column is
   filled in but the modeled part doesn't match) in the currently open
   Tekla view.
6. Prints a one-line summary to the console.

Parts flagged as **"missing in model"** (a basis level/grid with nothing
built near it) obviously can't be selected — there's no part there. Those
are only in `report.csv`, with their expected row/grid/elevation so you can
navigate there manually (e.g. via Tekla's "Go to" on a grid/elevation).

## Build & run

1. Open your Tekla model in Tekla Structures (this tool connects to the live
   session, it does not open the model file itself).
2. Requirements: .NET Framework matching your Tekla Structures version (most
   2020–2024 releases use .NET Framework 4.8), and the Tekla Open API
   assemblies from your install, typically:
   `C:\Program Files\Tekla Structures\<version>\nt\bin\Tekla.Structures*.dll`
3. Edit the constants at the top of `Program.cs`:
   - `GridOriginX_mm` / grid spacing — the model's global X coordinate that
     corresponds to Grid 1 (read this off your model, e.g. by clicking grid
     line 1 in Tekla and checking its reported coordinate).
   - `RowYCoordinates` — the global Y (or X, depending on model orientation)
     coordinate of ROW R1 and ROW R2, so members get bucketed into the
     right row.
   - `HppZOffset_mm` — the model Z coordinate that corresponds to
     `HPP+0.000` on the drawings (often 0 if the model origin was set at
     HPP, otherwise read it off a known TOS in the model).
   - `BasisFiles` — paths to the two basis CSVs, relative to wherever you
     run the exe from (defaults assume you run it from
     `tools/TeklaModelExport/bin/<config>/<tfm>/` inside the repo checkout;
     adjust if you copy the exe elsewhere, or just pass absolute paths).
   These are project-specific and can't be inferred from the drawings
   alone; get them once from the model (right-click a known grid/beam ->
   properties) and the tool is accurate from then on.
4. Build (`dotnet build -p:TeklaBin="C:\Program Files\Tekla Structures\2023.0\nt\bin"`
   or open in Visual Studio), then run the exe **with Tekla Structures open
   and the model active**. It writes `model_export.csv` and `report.csv`
   next to the exe, and highlights problem parts in the open view.

## Filling in the basis `Profile` column (optional but recommended)

`basis/ROW_R1_basis.csv` and `ROW_R2_basis.csv` ship with the elevation/grid
data pre-filled from the drawings but the `Profile` column blank (PDF text
extraction can't tell which profile call-out belongs to which position —
see `docs/ANALYSIS_NOTES.md`). Fill in `Profile` for the levels/grids you
want checked by profile, not just by "something exists here" — e.g. put
`HEA300` on the ROW R1, grid 1-6, TOS 7.315 row if that's what the drawing
shows there. Leave it blank to only check that *something* was modeled at
that level/grid (still catches skipped bays and wrong elevations).

## Notes / limitations

- Members that don't belong to either row's Y band come out with
  `Row=UNKNOWN` and won't match any basis row (they'll show as "extra") —
  check `RowToleranceY_mm` if too many end up there.
- `TOS_mm` is the higher end-point Z, which is the right convention for a
  sloped brace or a horizontal beam alike, but for a **column** it's the top
  of that column segment, not necessarily a drawing TOS callout — that's
  expected, the comparison buckets to the *nearest* basis level rather than
  requiring an exact hit (default tolerance 150 mm, `ElevationToleranceMm`).
- Re-running the tool re-selects the current problem set — the previous
  selection is simply replaced, nothing is written back into the model.
- If your model uses a different orientation (rows running along Y instead
  of X, say), swap the X/Y usage in `Program.cs` — it's marked with
  `// ORIENTATION`.
