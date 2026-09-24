// Comparer.cs
//
// Loads the basis CSVs (basis/ROW_R1_basis.csv, ROW_R2_basis.csv) and
// compares them against the members just read from the model, producing
// the same three discrepancy types as tools/compare/compare_model_to_basis.py:
//   - MissingInModel   basis level/grid has no nearby model member
//   - ExtraInModel     model member has no nearby basis level/grid
//   - ProfileMismatch  nearby, but profile differs from the basis Profile
//
// This lets Program.cs highlight ExtraInModel / ProfileMismatch parts
// directly in the open Tekla view (MissingInModel has no model object to
// select - it's reported instead, with its expected grid/elevation).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TeklaModelExport
{
    internal enum DiscrepancyType { MissingInModel, ExtraInModel, ProfileMismatch }

    internal sealed class BasisRow
    {
        public string Row;
        public string LevelType;
        public double ElevationMm;
        public int GridFrom;
        public int GridTo;
        public string Profile; // may be empty -> profile not checked for this row
        public string Notes;
    }

    internal sealed class ExportedMember
    {
        public int Id;
        public string Profile;
        public string AssemblyPos;
        public string Row;
        public int GridFrom;
        public int GridTo;
        public double TosMm;
    }

    internal sealed class Discrepancy
    {
        public DiscrepancyType Type;
        public string Row;
        public int GridFrom, GridTo;
        public double ElevationMm;
        public string ExpectedProfile;
        public string ModelProfile;
        public int? ModelId; // set when there's a real model part to highlight
        public string ModelAssemblyPos;
        public string Detail;

        public override string ToString()
        {
            string loc = $"Row {Row}, grid {GridFrom}-{GridTo}, EL {ElevationMm / 1000.0:0.000}m";
            switch (Type)
            {
                case DiscrepancyType.MissingInModel:
                    return $"MISSING  {loc}{(string.IsNullOrEmpty(ExpectedProfile) ? "" : $" ({ExpectedProfile})")} - {Detail}";
                case DiscrepancyType.ExtraInModel:
                    return $"EXTRA    {loc} model has {ModelProfile} (AssemblyPos {ModelAssemblyPos}) with no matching basis row";
                case DiscrepancyType.ProfileMismatch:
                    return $"MISMATCH {loc} expected {ExpectedProfile}, model has {ModelProfile} (AssemblyPos {ModelAssemblyPos})";
                default:
                    return base.ToString();
            }
        }
    }

    internal static class Comparer
    {
        public static List<BasisRow> LoadBasis(params string[] paths)
        {
            var result = new List<BasisRow>();
            foreach (var path in paths)
            {
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine($"WARNING: basis file not found: {path}");
                    continue;
                }
                var lines = File.ReadAllLines(path);
                if (lines.Length < 2) continue;
                var header = lines[0].Split(',');
                int idx(string name) => Array.IndexOf(header, name);
                int iRow = idx("Row"), iLevel = idx("LevelType"), iElev = idx("Elevation_m"),
                    iFrom = idx("GridFrom"), iTo = idx("GridTo"), iProfile = idx("Profile"),
                    iNotes = idx("Notes");

                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var cols = SplitCsvLine(lines[i]);
                    if (iElev < 0 || string.IsNullOrWhiteSpace(cols[iElev])) continue;
                    result.Add(new BasisRow
                    {
                        Row = cols[iRow],
                        LevelType = iLevel >= 0 ? cols[iLevel] : "",
                        ElevationMm = double.Parse(cols[iElev], CultureInfo.InvariantCulture) * 1000.0,
                        GridFrom = int.Parse(cols[iFrom], CultureInfo.InvariantCulture),
                        GridTo = int.Parse(cols[iTo], CultureInfo.InvariantCulture),
                        Profile = iProfile >= 0 ? cols[iProfile].Trim() : "",
                        Notes = iNotes >= 0 ? cols[iNotes] : "",
                    });
                }
            }
            return result;
        }

        public static List<Discrepancy> Compare(
            List<ExportedMember> model, List<BasisRow> basis, double toleranceMm = 150.0)
        {
            var report = new List<Discrepancy>();
            var matchedIds = new HashSet<int>();
            var byRow = model.GroupBy(m => m.Row).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var b in basis)
            {
                if (!byRow.TryGetValue(b.Row, out var candidates0))
                    candidates0 = new List<ExportedMember>();

                var candidates = candidates0.Where(m =>
                        Math.Abs(m.TosMm - b.ElevationMm) <= toleranceMm &&
                        GridsOverlap(b.GridFrom, b.GridTo, m.GridFrom, m.GridTo))
                    .ToList();

                if (candidates.Count == 0)
                {
                    report.Add(new Discrepancy
                    {
                        Type = DiscrepancyType.MissingInModel,
                        Row = b.Row,
                        GridFrom = b.GridFrom,
                        GridTo = b.GridTo,
                        ElevationMm = b.ElevationMm,
                        ExpectedProfile = b.Profile,
                        Detail = string.IsNullOrEmpty(b.Notes) ? "no model member found near this level/grid" : b.Notes,
                    });
                    continue;
                }

                foreach (var c in candidates) matchedIds.Add(c.Id);

                if (!string.IsNullOrEmpty(b.Profile))
                {
                    var mismatches = candidates.Where(c => c.Profile.Trim() != b.Profile).ToList();
                    if (mismatches.Count == candidates.Count)
                    {
                        foreach (var c in mismatches)
                        {
                            report.Add(new Discrepancy
                            {
                                Type = DiscrepancyType.ProfileMismatch,
                                Row = b.Row,
                                GridFrom = b.GridFrom,
                                GridTo = b.GridTo,
                                ElevationMm = b.ElevationMm,
                                ExpectedProfile = b.Profile,
                                ModelProfile = c.Profile,
                                ModelId = c.Id,
                                ModelAssemblyPos = c.AssemblyPos,
                                Detail = b.Notes,
                            });
                        }
                    }
                }
            }

            foreach (var m in model)
            {
                if (matchedIds.Contains(m.Id)) continue;
                report.Add(new Discrepancy
                {
                    Type = DiscrepancyType.ExtraInModel,
                    Row = m.Row,
                    GridFrom = m.GridFrom,
                    GridTo = m.GridTo,
                    ElevationMm = m.TosMm,
                    ModelProfile = m.Profile,
                    ModelId = m.Id,
                    ModelAssemblyPos = m.AssemblyPos,
                    Detail = "no basis level/grid found within tolerance",
                });
            }

            return report;
        }

        private static bool GridsOverlap(int aFrom, int aTo, int bFrom, int bTo)
        {
            int lo = Math.Max(Math.Min(aFrom, aTo), Math.Min(bFrom, bTo));
            int hi = Math.Min(Math.Max(aFrom, aTo), Math.Max(bFrom, bTo));
            return lo <= hi;
        }

        // Minimal CSV splitter handling quoted fields (matches the writer in Program.cs).
        private static string[] SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else current.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
                    else current.Append(c);
                }
            }
            fields.Add(current.ToString());
            return fields.ToArray();
        }
    }
}
