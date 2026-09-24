# What was extracted from the basis drawings

Source files (as uploaded):
- `&AA-(71010RM0-R1)-N-ZD 1001.201 (EN)` / `C2-117710-STR-LAY-LND-01001-201` — **ROW R1**, Rev 01, IFC (Issued for Construction), 03-21-2025
- `&AA-(71010RM0-R1)-N-ZD 1001.202 (EN)` / `C2-117710-STR-LAY-LND-01001-202` — **ROW R2**, Rev 01, IFC, 03-21-2025

Both: LP BUFFER VESSEL MODULE (71010RM0-R1), Venture Global NRU Phase 2,
scale 1:50, size A0. Datum: `HPP+0.000 = 100'-0"`.

## Reliable (used to build `basis/*.csv`)

**Grid spacing** — identical on both sheets: grids 1–6, five bays of
6700 mm (21'-11 3/4") each, total 33 500 mm along the row.

**Elevation levels** (TOS = top of steel, BOBP = bottom of base plate,
Splice = column splice), paired from the metric/imperial labels on each
sheet:

| Row | Levels (m above HPP) | Note |
|---|---|---|
| R1 | 0.914(BOBP), 2.415, 7.315, 10.965, 12.715(Splice), 14.615, 18.265, 21.915, 25.565, 26.115, 29.215 | 25.565 only labelled near grid 6 — local, not a full-row level |
| R2 | 0.914(BOBP), 2.415, 7.315, 10.965, 12.715(Splice), 14.615, 15.465, 18.265, 21.915, 23.965, 26.115, 29.215 | 15.465 and 23.965 only labelled near section X2-X2 — local |

These are in `basis/ROW_R1_basis.csv` and `basis/ROW_R2_basis.csv`, one row
per level, with a `Scope` column (`row` = spans the whole row, `local` =
only confirmed near one grid/section — verify extent against the drawing
before trusting a "missing in model" flag on those rows).

## Not reliable from text extraction alone (needs your eyes / the compare tool's "unmapped" output)

The profile call-outs (HEA300, HEB600, HEM800, HEA200, HEA160, HEA140,
HEB240, HEB300, HEB340, U140, U200, L90x90x9, PL20x400-390, etc.) appear on
both sheets **dozens of times each**, scattered across the plan view and six
cross-sections (X1-X1 … X6-X6 on R2; X11-X11 on R1). PDF text extraction
returns them without their (x, y) drawing position, so which profile belongs
to which member/grid/elevation cannot be reconstructed automatically.

Practical consequence: the `Profile` column in the basis CSVs is left blank
by default. You have two options:
1. Fill in `Profile` for the levels/grids you specifically want checked
   (e.g. the main HEA300 rafters at each TOS, or the HEM800 base beams) by
   reading it off the drawing once — the compare script will then flag any
   model member at that row/grid/elevation whose profile doesn't match.
2. Leave `Profile` blank and just run the grid/elevation completeness check
   — this alone catches the most common modeling mistakes (a level modeled
   at the wrong Z, a bay skipped, an extra member with no basis level nearby).

## Section cuts referenced on the sheets (for cross-checking specific joints)

- R2: X1-X1 (@109'-1/4"), X2-X2 (@110'-7/8"), X3-X3 (@121'-5/16"),
  X4-X4 (@115'-1/8"), X5-X5 (@121'-1/16"), X6-X6 (@145'-5/8")
- R1: X11-X11 (no elevation callout captured in text extraction — verify on
  sheet)

Use these where the compare report flags a discrepancy near grid line 6 /
one of these stations — the sheet has a detail section right there.
