// TeklaModelExport
//
// Connects to a running Tekla Structures session via the Open API and
// exports every Beam-derived part (columns, beams, braces) to a CSV that
// tools/compare/compare_model_to_basis.py can read.
//
// See README.md for the project-specific constants you must set below
// before this will produce meaningful Row/Grid/TOS values.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Tekla.Structures.Model;
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

            var rows = new List<string>
            {
                "Id,Class,Name,Profile,Material,AssemblyPos,Row,GridFrom,GridTo,TOS_mm,Length_mm"
            };

            ModelObjectEnumerator beams = model.GetModelObjectSelector()
                .GetAllObjectsWithType(ModelObject.ModelObjectEnum.BEAM);

            int count = 0;
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

                rows.Add(string.Join(",",
                    beam.Identifier.ID.ToString(CultureInfo.InvariantCulture),
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

                count++;
            }

            File.WriteAllLines("model_export.csv", rows);
            Console.WriteLine($"Exported {count} members to model_export.csv");
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
