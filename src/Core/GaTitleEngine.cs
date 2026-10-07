using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Teigha.DatabaseServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Teksty z layoutów GA → layouty RC (jak ASD-GAI w AsdRcSlab):
    ///   • blok A1-BL: CLIENT_1..3, PROJ_1..3, APPROVED kopiowane 1:1,
    ///     TITLE_1: prefiks GA („PLOT 9-10.” przed „GENERAL ARRANGEMENT”) w miejsce prefiksu RC (przed „REINFORCEMENT DETAILS”),
    ///     DRAWING_NUMBER: prefiks GA + „RC” + numer pierwszego GA (GA0090 → RC0090, RC0091 … wg kolejności zakładek),
    ///   • SLAB NOTES (MText z „SLAB AREA” w layoutach RC): ogony akapitów SLAB AREA / PERIMETER / THICKNESS /
    ///     CONCRETE VOLUME (po „=”) z GA, blok „CONCRETE TO BE DESIGNATED/DESIGNED … CERTIFICATE.”,
    ///     HYSTOOLS DK90/DK165 wg grubości (225 → DK90, 300 → DK165). Wartości w kolorze białym (warstwa SD-Text jest żółta).
    /// Względem ASD: wartości czytane także z nowego formatu GA (kody MText wewnątrz liczb, np. „= \C20;103.58\C7;”),
    /// przy wywołaniu z RC_PRZYGOTUJ_GA preferowany layout GA wybranej płyty.
    /// </summary>
    public static class GaTitleEngine
    {
        public const string TitleBlockName = "A1-BL";
        private static readonly string[] CopiedTags = { "CLIENT_1", "CLIENT_2", "CLIENT_3", "PROJ_1", "PROJ_2", "PROJ_3", "APPROVED" };
        private static readonly string[] NoteMarkers = { "SLAB AREA", "SLAB PERIMETER", "SLAB THICKNESS", "CONCRETE VOLUME" };

        public sealed class GaData
        {
            public string SourceLayout;
            public Dictionary<string, string> Attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string TitlePrefix;          // „PLOT 9-10.”
            public string DrawingPrefix;        // „CV129QD001”
            public int?   FirstGaNumber;        // 90
            public int    GaNumberWidth;        // 4
            public int?   PlotNumber;           // 9 (fallback numeracji)
            public Dictionary<string, string> Tails = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);  // do podglądu
            public string ConcreteBlock;
            public int?   ThicknessMm;
            public bool   HasTitleBlock => Attrs.Count > 0;
        }

        public sealed class ApplyResult
        {
            public int TitleLayouts;
            public int NotesLayouts;
            public bool NotesFound;
        }

        // ------------------------------------------------------------------
        //  Odczyt GA
        // ------------------------------------------------------------------

        private sealed class Lay
        {
            public string Name;
            public int Tab;
            public ObjectId BtrId;
            public Dictionary<string, string> Attrs;
        }

        private static List<Lay> Layouts(Transaction tr, Database db)
        {
            var list = new List<Lay>();
            var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry e in dict)
            {
                if (!(tr.GetObject(e.Value, OpenMode.ForRead) is Layout l) || l.ModelType) continue;
                list.Add(new Lay { Name = l.LayoutName, Tab = l.TabOrder, BtrId = l.BlockTableRecordId, Attrs = TitleAttrs(tr, l.BlockTableRecordId) });
            }
            list.Sort((a, b) => a.Tab.CompareTo(b.Tab));
            return list;
        }

        private static Dictionary<string, string> TitleAttrs(Transaction tr, ObjectId btrId)
        {
            foreach (var br in TitleBlocks(tr, btrId))
            {
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (ObjectId aid in br.AttributeCollection)
                    if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar && !d.ContainsKey(ar.Tag))
                        d[ar.Tag] = ar.TextString;
                if (d.Count > 0) return d;
            }
            return null;
        }

        private static IEnumerable<BlockReference> TitleBlocks(Transaction tr, ObjectId btrId)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                string name = br.Name;
                try
                {
                    if (br.IsDynamicBlock)
                        name = ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name;
                }
                catch (Exception ex) { Log.Error("GaTitleEngine.TitleBlocks", ex); }
                if (string.Equals(name, TitleBlockName, StringComparison.OrdinalIgnoreCase)) yield return br;
            }
        }

        private static string Norm(string s) => Regex.Replace((s ?? "").ToUpperInvariant(), @"\s+", " ").Trim();

        /// <summary>Czyta dane z GA. <paramref name="plotLabel"/> (np. „PLOT 9-10”) — preferuj layout tej płyty.</summary>
        public static GaData Read(Database ga, string plotLabel = null)
        {
            var d = new GaData();
            using (var tr = ga.TransactionManager.StartOpenCloseTransaction())
            {
                var lays = Layouts(tr, ga).Where(l => l.Attrs != null).ToList();
                if (lays.Count == 0) { tr.Commit(); return d; }

                // Layouty wybranej płyty (TITLE_1 zaczyna się od „PLOT 9-10”) — gdy GA ma kilka płyt
                var forPlot = lays;
                if (!string.IsNullOrWhiteSpace(plotLabel))
                {
                    string want = Norm(plotLabel);
                    var m = lays.Where(l => Norm(l.Attrs.TryGetValue("TITLE_1", out var t) ? t : "").StartsWith(want)
                                         && !Regex.IsMatch(Norm(l.Attrs["TITLE_1"]).Substring(want.Length), @"^[0-9]")).ToList();
                    if (m.Count > 0) forPlot = m;
                }
                var src = forPlot[0];
                d.SourceLayout = src.Name;
                d.Attrs = src.Attrs;

                if (d.Attrs.TryGetValue("TITLE_1", out var t1))
                {
                    var m = Regex.Match(t1 ?? "", @"^(.+?)\s+GENERAL\s+ARRANGEMENT", RegexOptions.IgnoreCase);
                    if (m.Success) d.TitlePrefix = m.Groups[1].Value.Trim();
                    var pm = Regex.Match(d.TitlePrefix ?? "", @"\bPLOT\s+(\d+)", RegexOptions.IgnoreCase);
                    if (pm.Success && int.TryParse(pm.Groups[1].Value, out int p)) d.PlotNumber = p;
                }
                if (d.Attrs.TryGetValue("DRAWING_NUMBER", out var drg) && !string.IsNullOrEmpty(drg))
                {
                    int dash = drg.IndexOf('-');
                    if (dash > 0) d.DrawingPrefix = drg.Substring(0, dash);
                    int last = drg.LastIndexOf('-');
                    var gm = Regex.Match(last >= 0 ? drg.Substring(last + 1).Trim() : "", @"^GA(\d+)$", RegexOptions.IgnoreCase);
                    if (gm.Success && int.TryParse(gm.Groups[1].Value, out int n)) { d.FirstGaNumber = n; d.GaNumberWidth = gm.Groups[1].Value.Length; }
                }

                // SLAB NOTES — najpierw layout źródłowy, potem pozostałe
                foreach (var l in new[] { src }.Concat(lays.Where(x => x != src)))
                {
                    var mt = NoteTexts(tr, l.BtrId).FirstOrDefault();
                    if (mt == null) continue;
                    ReadNotes(mt.Contents, d);
                    break;
                }
                tr.Commit();
            }
            return d;
        }

        private static IEnumerable<MText> NoteTexts(Transaction tr, ObjectId btrId)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            foreach (ObjectId id in btr)
                if (tr.GetObject(id, OpenMode.ForRead) is MText mt && GaNotesText.Clean(mt.Contents).IndexOf("SLAB AREA", StringComparison.OrdinalIgnoreCase) >= 0)
                    yield return mt;
        }

        private static void ReadNotes(string contents, GaData d)
        {
            foreach (var marker in NoteMarkers)
            {
                string tail = GaNotesText.ParagraphTail(contents, marker);
                if (tail == null) continue;
                d.Tails[marker] = tail;
                d.Values[marker] = GaNotesText.Clean(tail);
            }
            if (d.Values.TryGetValue("SLAB THICKNESS", out var th))
            {
                var m = Regex.Match(th, @"^\s*(\d+)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int mm)) d.ThicknessMm = mm;
            }
            var cb = GaNotesText.RxConcreteBlock.Match(contents);
            if (cb.Success) d.ConcreteBlock = cb.Value;
        }

        // ------------------------------------------------------------------
        //  Podgląd
        // ------------------------------------------------------------------

        public static string BuildDrawingSuffix(GaData d, int idx)
        {
            if (d.FirstGaNumber.HasValue)
            {
                int n = d.FirstGaNumber.Value + idx;
                return "RC" + (d.GaNumberWidth > 0 ? n.ToString("D" + d.GaNumberWidth, CultureInfo.InvariantCulture) : n.ToString(CultureInfo.InvariantCulture));
            }
            if (d.PlotNumber.HasValue)
            {
                int p = d.PlotNumber.Value;
                return p < 10 ? "RC" + p.ToString("D2") + idx : "RC" + p + (idx + 1);
            }
            return "RC" + (idx + 1).ToString("D3");
        }

        public static string Preview(GaData d, Database rc)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Źródło: layout GA „{d.SourceLayout}”.");
            sb.AppendLine();
            sb.AppendLine("BLOK TYTUŁOWY (A1-BL):");
            foreach (var tag in CopiedTags)
                sb.AppendLine($"  {tag,-10} = {(d.Attrs.TryGetValue(tag, out var v) && !string.IsNullOrEmpty(v) ? v : "(puste)")}");
            sb.AppendLine($"  TITLE_1    : prefiks „{d.TitlePrefix ?? "(brak — prefiks RC zostanie usunięty)"}” + „REINFORCEMENT DETAILS …”");

            using (var tr = rc.TransactionManager.StartOpenCloseTransaction())
            {
                var lays = Layouts(tr, rc);
                if (string.IsNullOrEmpty(d.DrawingPrefix))
                    sb.AppendLine("  DRAWING_NUMBER: bez zmian (brak prefiksu w GA)");
                else
                    for (int i = 0; i < lays.Count; i++)
                        sb.AppendLine($"  {lays[i].Name,-12} → {d.DrawingPrefix}-{BuildDrawingSuffix(d, i)}" + (lays[i].Attrs == null ? "   (brak A1-BL — pominięty)" : ""));
                tr.Commit();
            }

            sb.AppendLine();
            sb.AppendLine("SLAB NOTES:");
            foreach (var mk in NoteMarkers)
                sb.AppendLine($"  {mk,-16} = {(d.Values.TryGetValue(mk, out var v) ? v : "(brak w GA — bez zmian)")}");
            string dk = d.ThicknessMm == 225 ? "DK90" : d.ThicknessMm == 300 ? "DK165" : null;
            sb.AppendLine($"  HYSTOOLS         → {(dk ?? "bez zmian (grubość " + (d.ThicknessMm?.ToString() ?? "?") + ")")}");
            sb.AppendLine($"  CONCRETE TO BE … → {(d.ConcreteBlock != null ? Shorten(GaNotesText.Clean(d.ConcreteBlock), 110) : "(brak w GA — bez zmian)")}");
            return sb.ToString();
        }

        private static string Shorten(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 3) + "...";

        // ------------------------------------------------------------------
        //  Zapis do RC
        // ------------------------------------------------------------------

        public static ApplyResult Apply(GaData d, Database rc)
        {
            var res = new ApplyResult();
            var tags = new HashSet<string>(CopiedTags, StringComparer.OrdinalIgnoreCase);
            string dkNum = d.ThicknessMm == 225 ? "90" : d.ThicknessMm == 300 ? "165" : null;

            using (var tr = rc.TransactionManager.StartTransaction())
            {
                var lays = Layouts(tr, rc);
                for (int i = 0; i < lays.Count; i++)
                {
                    var l = lays[i];
                    bool touched = false;

                    // --- blok tytułowy
                    foreach (var br in TitleBlocks(tr, l.BtrId).ToList())
                    {
                        foreach (ObjectId aid in br.AttributeCollection)
                        {
                            if (!(tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar)) continue;
                            string nv = null;
                            if (ar.Tag.Equals("TITLE_1", StringComparison.OrdinalIgnoreCase))
                                nv = GaNotesText.ReplaceTitlePrefix(ar.TextString, d.TitlePrefix);
                            else if (ar.Tag.Equals("DRAWING_NUMBER", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(d.DrawingPrefix))
                                nv = d.DrawingPrefix + "-" + BuildDrawingSuffix(d, i);
                            else if (tags.Contains(ar.Tag) && d.Attrs.TryGetValue(ar.Tag, out var sv))
                                nv = sv ?? "";
                            if (nv == null || string.Equals(nv, ar.TextString, StringComparison.Ordinal)) continue;
                            ar.UpgradeOpen();
                            ar.TextString = nv;
                            touched = true;
                        }
                    }
                    if (touched) res.TitleLayouts++;

                    // --- SLAB NOTES
                    bool notesTouched = false;
                    foreach (var mt in NoteTexts(tr, l.BtrId).ToList())
                    {
                        res.NotesFound = true;
                        string c0 = mt.Contents, c = c0;
                        foreach (var kv in d.Tails) c = GaNotesText.ApplyTail(c, kv.Key, kv.Value);
                        if (d.ConcreteBlock != null)
                        {
                            var m = GaNotesText.RxConcreteBlock.Match(c);
                            if (m.Success)
                                c = c.Substring(0, m.Index) + GaNotesText.FlattenBlock(d.ConcreteBlock) + c.Substring(m.Index + m.Length);
                        }
                        if (dkNum != null) c = GaNotesText.RxHystools.Replace(c, "${1}" + dkNum + "${2}");
                        if (string.Equals(c, c0, StringComparison.Ordinal)) continue;
                        mt.UpgradeOpen();
                        mt.Contents = c;
                        mt.ColorIndex = 7;
                        notesTouched = true;
                    }
                    if (notesTouched) res.NotesLayouts++;
                }
                tr.Commit();
            }
            return res;
        }

    }
}
