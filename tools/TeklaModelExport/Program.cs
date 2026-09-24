// TeklaModelExport
//
// Connects to a running Tekla Structures session via the Open API,
// exports every Beam-derived part (columns, beams, braces), compares it
// against the drawing-derived basis tables (basis/ROW_R1_basis.csv,
// ROW_R2_basis.csv), and HIGHLIGHTS the mismatched/unexpected parts by
// selecting them in the open Tekla view (Tekla draws selected parts in a
// distinct highlight color, so problems are visible immediately - no
// need to read a report to find them).
//
// It also writes:
//   - model_export.csv   every exported member (for the standalone
//                         Python compare script, or your own spreadsheet)
//   - report.csv          every discrepancy found (including "missing in
//                         model" rows, which can't be highlighted because
//                         there's no part to select)
//
// See README.md for the project-specific constants you must set below
// before this will produce meaningful Row/Grid/TOS values.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Tekla.Structures.Model;
using Tekla.Structures.Model.UI;
using Tekla.Structures.Geometry3d;

namespace TeklaModelExport
{
    internal static class Program
    {
        // ----------------------------------------------------------------
        // PROJECT-SPECIFIC CONSTANTS - set these from your model, see README.md
        // ----------------------------------------------------------------

        // Global X coordinate of Grid 1 (the row's start). All grid bucketing
        // below is relative to this.
        private const double GridOriginX_mm = 0.0;

        // Grid spacing along the row, must match basis/grid_geometry.csv.
        private static readonly double[] GridCumulativeX_mm =
            { 0, 6700, 13400, 20100, 26800, 33500 }; // grids 1..6

        // Global Y coordinate of each row's reference line. ORIENTATION:
        // if your model has the rows running along X instead of Y, swap
        // the X/Y reads in ClassifyRow()/ClassifyGrid() below.
        private static readonly Dictionary<string, double> RowYCoordinates =
            new Dictionary<string, double>
            {
                { "R1", 0.0 },
                { "R2", 6700.0 }, // example spacing between rows - VERIFY against your model
            };

        // A member is bucketed into a row only if its midpoint Y is within
        // this tolerance of that row's Y coordinate.
        private const double RowToleranceY_mm = 1000.0;

        // Model Z coordinate that corresponds to HPP+0.000 on the drawings.
        private const double HppZOffset_mm = 0.0;

        // How close (mm) a member's TOS must be to a basis elevation, and
        // how much its grid range must overlap the basis grid range, to
        // count as "the same location".
        private const double ElevationToleranceMm = 150.0;

        // Basis CSVs to compare against (relative to the working directory
        // this exe is run from - typically the repo root).
        private static readonly string[] BasisFiles =
        {
            @"..\..\..\..\..\basis\ROW_R1_basis.csv",
            @"..\..\..\..\..\basis\ROW_R2_basis.csv",
        };

        // ----------------------------------------------------------------

        private static void Main()
        {
            var model = new Model();
            if (!model.GetConnectionStatus())
            {
                Console.Error.WriteLine(
                    "Could not connect to Tekla Structures. Make sure a model is open.");
                Environment.Exit(1);
                return;
            }

            var exportRows = new List<string>
            {
                "Id,Class,Name,Profile,Material,AssemblyPos,Row,GridFrom,GridTo,TOS_mm,Length_mm"
            };
            var members = new List<ExportedMember>();
            var beamById = new Dictionary<int, Beam>();

            ModelObjectEnumerator beams = model.GetModelObjectSelector()
                .GetAllObjectsWithType(ModelObject.ModelObjectEnum.BEAM);

            while (beams.MoveNext())
            {
                if (!(beams.Current is Beam beam)) continue;

                Point start = beam.StartPoint;
                Point end = beam.EndPoint;
                double midX = (start.X + end.X) / 2.0;
                double midY = (start.Y + end.Y) / 2.0;
                double topZ = Math.Max(start.Z, end.Z) - HppZOffset_mm;
                double length = Distance(start, end);

                string row = ClassifyRow(midY);
                (int gridFrom, int gridTo) = ClassifyGrid(midX - GridOriginX_mm);

                string assemblyPos = GetAssemblyPos(beam);
                string profile = SafeProfile(beam);
                string material = SafeMaterial(beam);
                int id = beam.Identifier.ID;

                exportRows.Add(string.Join(",",
                    id.ToString(CultureInfo.InvariantCulture),
                    beam.Class,
                    Csv(beam.Name),
                    Csv(profile),
                    Csv(material),
                    Csv(assemblyPos),
                    row,
                    gridFrom.ToString(CultureInfo.InvariantCulture),
                    gridTo.ToString(CultureInfo.InvariantCulture),
                    Math.Round(topZ, 1).ToString(CultureInfo.InvariantCulture),
                    Math.Round(length, 1).ToString(CultureInfo.InvariantCulture)));

                members.Add(new ExportedMember
                {
                    Id = id,
                    Profile = profile,
                    AssemblyPos = assemblyPos,
                    Row = row,
                    GridFrom = gridFrom,
                    GridTo = gridTo,
                    TosMm = topZ,
                });
                beamById[id] = beam;
            }

            File.WriteAllLines("model_export.csv", exportRows);
            Console.WriteLine($"Exported {members.Count} members to model_export.csv");

            var basis = Comparer.LoadBasis(BasisFiles);
            if (basis.Count == 0)
            {
                Console.Error.WriteLine(
                    "No basis rows loaded - check the BasisFiles paths at the top of Program.cs. " +
                    "Skipping comparison/highlighting.");
                return;
            }

            var discrepancies = Comparer.Compare(members, basis, ElevationToleranceMm);
            WriteReport(discrepancies);

            var toHighlight = new ArrayList();
            foreach (var d in discrepancies)
            {
                if (d.ModelId.HasValue && beamById.TryGetValue(d.ModelId.Value, out var beam))
                    toHighlight.Add(beam);
            }

            if (toHighlight.Count > 0)
            {
                new ModelObjectSelector().Select(toHighlight);
                model.GetViewHandler()?.RedrawAllViews();
            }

            int missing = discrepancies.Count(d => d.Type == DiscrepancyType.MissingInModel);
            int extra = discrepancies.Count(d => d.Type == DiscrepancyType.ExtraInModel);
            int mismatch = discrepancies.Count(d => d.Type == DiscrepancyType.ProfileMismatch);
            Console.WriteLine($"Comparison: {missing} missing in model, {extra} extra in model, " +
                               $"{mismatch} profile mismatches.");
            Console.WriteLine($"{toHighlight.Count} part(s) selected/highlighted in the Tekla view " +
                               "(extra + profile-mismatch parts). \"Missing\" rows have no part to " +
                               "select - see report.csv for their expected grid/elevation.");
            Console.WriteLine("Full details written to report.csv");
        }

        private static void WriteReport(List<Discrepancy> discrepancies)
        {
            var lines = new List<string>
            {
                "Type,Row,GridFrom,GridTo,Elevation_m,Expected_Profile,Model_Profile,Model_Id,Model_AssemblyPos,Detail"
            };
            foreach (var d in discrepancies)
            {
                lines.Add(string.Join(",",
                    d.Type.ToString(),
                    d.Row,
                    d.GridFrom.ToString(CultureInfo.InvariantCulture),
                    d.GridTo.ToString(CultureInfo.InvariantCulture),
                    (d.ElevationMm / 1000.0).ToString("0.000", CultureInfo.InvariantCulture),
                    Csv(d.ExpectedProfile),
                    Csv(d.ModelProfile),
                    d.ModelId?.ToString(CultureInfo.InvariantCulture) ?? "",
                    Csv(d.ModelAssemblyPos),
                    Csv(d.Detail)));
            }
            File.WriteAllLines("report.csv", lines);
        }

        private static string ClassifyRow(double midY)
        {
            foreach (var kv in RowYCoordinates)
            {
                if (Math.Abs(midY - kv.Value) <= RowToleranceY_mm)
                    return kv.Key;
            }
            return "UNKNOWN";
        }

        private static (int, int) ClassifyGrid(double relativeX)
        {
            for (int i = 0; i < GridCumulativeX_mm.Length - 1; i++)
            {
                if (relativeX >= GridCumulativeX_mm[i] - 1.0 &&
                    relativeX <= GridCumulativeX_mm[i + 1] + 1.0)
                {
                    return (i + 1, i + 2); // grid numbers are 1-based
                }
            }
            // outside the known bays - clamp to nearest edge for visibility
            return relativeX < GridCumulativeX_mm[0]
                ? (0, 1)
                : (GridCumulativeX_mm.Length, GridCumulativeX_mm.Length + 1);
        }

        private static double Distance(Point a, Point b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static string GetAssemblyPos(Beam beam)
        {
            try
            {
                var assembly = beam.GetAssembly();
                if (assembly == null) return "";
                string prefix = "", posNumber = "";
                assembly.GetReportProperty("ASSEMBLY_POS_PREFIX", ref prefix);
                assembly.GetReportProperty("ASSEMBLY_POS_NUMBER", ref posNumber);
                return $"{prefix}{posNumber}";
            }
            catch
            {
                return "";
            }
        }

        private static string SafeProfile(Beam beam)
        {
            try { return beam.Profile?.ProfileString ?? ""; }
            catch { return ""; }
        }

        private static string SafeMaterial(Beam beam)
        {
            try { return beam.Material?.MaterialString ?? ""; }
            catch { return ""; }
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Contains(",") || value.Contains("\"")
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }
    }
}
