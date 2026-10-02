using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Pręty zbiorcze przebicia (poz. 501/502): liczby PH z opisów detali (AP-TEXT) i usuwanie
    /// poprzednich prętów. Tagowanie pali z raportu — <see cref="PunchingAutoEngine"/> (RC_PUNCHING_AUTO).
    /// </summary>
    public static class PunchingTagEngine
    {
        public sealed class MappingWarning
        {
            public string Kind;
            public string Message;
        }

        public sealed class PhCountTotals
        {
            public Dictionary<string, int> PerPh    = new Dictionary<string, int>();
            public int                     TotalForPos501 = 0;
            public int                     TotalForPos502 = 0;
            public List<MappingWarning>    Warnings  = new List<MappingWarning>();
        }

        // ----------------------------------------------------------------
        // ReadPhCountsFromActiveDrawing — scan AP-TEXT MTEXTs and extract
        // pile counts per PH zone. Used by RC_PUNCHING_SUMMARY_BARS.
        // ----------------------------------------------------------------

        public static PhCountTotals ReadPhCountsFromActiveDrawing(Document doc)
        {
            var result = new PhCountTotals();
            var db     = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has("AP-TEXT"))
                    throw new InvalidOperationException(
                        "[RC_PUNCHING_SUMMARY_BARS] Layer 'AP-TEXT' missing — run RC_PUNCHING_AUTO first.");

                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                var phRegex    = new Regex(@"PH([1-9])", RegexOptions.None);
                var countRegex = new Regex(@"\((\d+)No LOCATION", RegexOptions.None);

                foreach (ObjectId oid in ms)
                {
                    if (oid.IsErased) continue;
                    var mt = tr.GetObject(oid, OpenMode.ForRead) as MText;
                    if (mt == null) continue;
                    if (!string.Equals(mt.Layer, "AP-TEXT",
                            StringComparison.OrdinalIgnoreCase)) continue;

                    string raw = mt.Contents ?? string.Empty;

                    // Identify PH zone
                    var phMatch = phRegex.Match(raw);
                    if (!phMatch.Success)
                    {
                        result.Warnings.Add(new MappingWarning {
                            Kind    = "no-ph-zone",
                            Message = "AP-TEXT MTEXT has no PH zone identifier — skipped"
                        });
                        continue;
                    }
                    string ph = "PH" + phMatch.Groups[1].Value;

                    // Extract pile count
                    int count;
                    var countMatch = countRegex.Match(raw);
                    if (countMatch.Success)
                    {
                        int.TryParse(countMatch.Groups[1].Value, out count);
                    }
                    else if (raw.Contains("(N/A)"))
                    {
                        count = 0;
                    }
                    else
                    {
                        result.Warnings.Add(new MappingWarning {
                            Kind    = "no-count-anchor",
                            Message = $"AP-TEXT MTEXT ({ph}) has no (NNo LOCATION) anchor " +
                                      "and no (N/A) — skipped"
                        });
                        continue;
                    }

                    result.PerPh[ph] = count;
                }

                tr.Commit();
            }

            // Pos 501: (PH1 + PH2 + PH3) × 14
            int sum501 = 0;
            foreach (var z in new[] { "PH1", "PH2", "PH3" })
                if (result.PerPh.TryGetValue(z, out int n)) sum501 += n;
            result.TotalForPos501 = sum501 * 14;

            // Pos 502: (PH4 + PH5 + PH6) × 14 + (PH7 + PH8 + PH9) × 28
            int sum502a = 0;
            foreach (var z in new[] { "PH4", "PH5", "PH6" })
                if (result.PerPh.TryGetValue(z, out int n)) sum502a += n;
            int sum502b = 0;
            foreach (var z in new[] { "PH7", "PH8", "PH9" })
                if (result.PerPh.TryGetValue(z, out int n)) sum502b += n;
            result.TotalForPos502 = sum502a * 14 + sum502b * 28;

            return result;
        }

        // ----------------------------------------------------------------
        // DeleteSummaryBars — erase all 501/502 summary entities.
        // Called at start of RC_PUNCHING_SUMMARY_BARS for idempotent re-runs.
        // ----------------------------------------------------------------

        public static int DeleteSummaryBars(Document doc)
        {
            var db = doc.Database;
            int erased = 0;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                // Phase 1a: find RC_SINGLE_BAR polylines with Mark = "H12-501" / "H16-502"
                var summaryPolylineHandles = new HashSet<long>();
                var toErase = new HashSet<long>();

                foreach (ObjectId oid in ms)
                {
                    if (oid.IsErased) continue;
                    var ent = tr.GetObject(oid, OpenMode.ForRead) as Entity;
                    if (!(ent is Polyline pl)) continue;

                    var sxd = SingleBarEngine.ReadBarXData(pl);
                    if (sxd == null || !IsSummaryMark(sxd.Mark)) continue;

                    summaryPolylineHandles.Add(pl.Handle.Value);
                    toErase.Add(pl.Handle.Value);

                    if (!string.IsNullOrEmpty(sxd.LabelHandle)
                        && long.TryParse(sxd.LabelHandle,
                            System.Globalization.NumberStyles.HexNumber, null, out long lblH))
                        toErase.Add(lblH);
                }

                // Phase 1b: find RC_BAR_BLOCK block refs whose SourceBarHandle points to
                // one of those polylines; collect them + their AnnotHandle
                foreach (ObjectId oid in ms)
                {
                    if (oid.IsErased) continue;
                    var ent = tr.GetObject(oid, OpenMode.ForRead) as Entity;
                    if (!(ent is BlockReference br)) continue;

                    var bxd = BarBlockEngine.ReadXData(br);
                    if (bxd == null) continue;

                    if (!string.IsNullOrEmpty(bxd.SourceBarHandle)
                        && long.TryParse(bxd.SourceBarHandle,
                            System.Globalization.NumberStyles.HexNumber, null, out long srcH)
                        && summaryPolylineHandles.Contains(srcH))
                    {
                        toErase.Add(br.Handle.Value);

                        if (!string.IsNullOrEmpty(bxd.AnnotHandle)
                            && long.TryParse(bxd.AnnotHandle,
                                System.Globalization.NumberStyles.HexNumber, null, out long annotH))
                            toErase.Add(annotH);
                    }
                }

                // Phase 2: resolve handles -> ObjectIds and erase
                foreach (var h in toErase)
                {
                    ObjectId oid;
                    if (db.TryGetObjectId(new Handle(h), out oid) && !oid.IsErased)
                    {
                        var ent = tr.GetObject(oid, OpenMode.ForWrite) as Entity;
                        if (ent != null) { ent.Erase(); erased++; }
                    }
                }

                tr.Commit();
            }

            return erased;
        }

        private static bool IsSummaryMark(string mark)
        {
            if (string.IsNullOrEmpty(mark)) return false;
            return mark.Equals("H12-501", StringComparison.OrdinalIgnoreCase)
                || mark.Equals("H16-502", StringComparison.OrdinalIgnoreCase);
        }
    }
}
