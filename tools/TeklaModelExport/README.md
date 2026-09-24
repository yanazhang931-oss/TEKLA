# TeklaModelExport

A small Open API C# console app that connects to your **currently running**
Tekla Structures session and exports every beam/column/brace/panel-leg to a
CSV that the compare script can consume.

## Build & run

1. Open your Tekla model in Tekla Structures (this tool connects to the live
   session, it does not open the model file itself).
2. Requirements: .NET Framework matching your Tekla Structures version (most
   2020–2024 releases use .NET Framework 4.8), and the Tekla Open API
   assemblies from your install, typically:
   `C:\Program Files\Tekla Structures\<version>\nt\bin\Tekla.Structures*.dll`
3. Edit the constants at the top of `Program.cs`:
   - `GridOriginX_mm` / `GridOriginY_mm` — the model's global X/Y coordinate
     that corresponds to Grid 1 / the row's reference line (read this off
     your model, e.g. by clicking on grid line 1 in Tekla and checking its
     reported coordinate).
   - `RowYCoordinates` — the global Y (or X, depending on how your model is
     oriented) coordinate of ROW R1 and ROW R2, so members get bucketed into
     the right row.
   - `HppZOffset_mm` — the model Z coordinate that corresponds to
     `HPP+0.000` on the drawings (often 0 if the model origin was set at
     HPP, otherwise read it off a known TOS in the model, e.g. a beam you
     already know sits at TOS+2.415 — its reported top Z minus 2415 gives
     the offset).
   These three are project-specific and can't be inferred from the drawings
   alone; get them once from the model (right-click a known grid/beam ->
   properties) and the export is accurate from then on.
4. Build (`dotnet build` or Visual Studio) referencing the DLLs above, then
   run the exe with Tekla Structures open. It writes `model_export.csv` next
   to the exe.

## What gets exported

One row per `Tekla.Structures.Model.Beam` (this covers columns, beams and
braces — Tekla doesn't distinguish them at the API level, `Profile`/`Class`
tell them apart):

| Column | Meaning |
|---|---|
| Id | Tekla part identifier |
| Class | Part class number |
| Name | Part name (e.g. COLUMN, BEAM, BRACE if named that way) |
| Profile | Profile string, e.g. `HEA300`, `U140`, `L90x90x9` |
| Material | Material string |
| AssemblyPos | Assembly position number, for jumping back into the model |
| Row | `R1` or `R2` (nearest `RowYCoordinates` entry), or `UNKNOWN` if farther than `RowToleranceY_mm` |
| GridFrom / GridTo | Bracketing grid numbers (1–6) by member midpoint X |
| TOS_mm | Top-of-steel elevation, in mm above `HppZOffset_mm` (max Z of the two end points) |
| Length_mm | Member length |

## Notes / limitations

- Members that don't belong to either row's Y band come out with
  `Row=UNKNOWN` — check `RowToleranceY_mm` if too many end up there.
- `TOS_mm` is the higher end-point Z, which is the right convention for a
  sloped brace or a horizontal beam alike, but for a **column** it's the top
  of that column segment, not necessarily a drawing TOS callout — that's
  expected, the compare script buckets to the *nearest* basis level rather
  than requiring an exact hit.
- If your model uses a different orientation (rows running along Y instead
  of X, say), swap the X/Y usage in `Program.cs` — it's marked with a
  `// ORIENTATION` comment.
