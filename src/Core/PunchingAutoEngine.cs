using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// RC_PUNCHING_AUTO — przebicie z raportu xlsx (report_punching.xlsx):
    ///   1. raport → płyta (PLOT) wybrana z listy (podświetlona ta z największą liczbą pali na rysunku),
    ///   2. pal na rysunku (koło SD-Pile z podpisem P617 obok, cały model — bez obrysu) ↔ wiersz raportu,
    ///   3. PH = 3·(H12@200 | H16@200 | H16@100) + (Internal | Edge | Corner) + 1; MANUAL gdy Util &gt; 100,
    ///      Reentrant, FAIL / SHEAR RAILS albo nieznane zbrojenie,
    ///   4. na planie: tag PHn + kreskowanie pala (XData RC_PH — ponowne uruchomienie czyści poprzednie),
    ///   5. szablony detali (MText na AP-TEXT): „(nNo LOCATIONS)” i „APPLICABLE FOR PILES …”; nieużyte przekreślone,
    ///   6. liczby prętów do BBS: 501 = H12 L2250 × 14 (PH1–3), 502 = H16 L2500 × 14 (PH4–6) / × 28 (PH7–9).
    /// </summary>
    public static class PunchingAutoEngine
    {
        public const string XApp          = "RC_PH";
        public const string PileLayer     = "SD-Pile";
        public const string PileTextLayer = "SD-Pile Text";   // prefiks
        public const string TagLayer      = "AP rebar top";
        public const string HatchLayer    = "AP-Hatch";
        public const string TemplateLayer = "AP-TEXT";
        public const string NotUsedLayer  = "AP-NOTUSED";
        public const string TagStyle      = "WYG_0MS";

        public const int BarsPerDetail      = 14;   // 7 + 7 prętów (H12@200 / H16@200)
        public const int BarsPerDetailDense = 28;   // 14 + 14 prętów (H16@100)

        public sealed class Item
        {
            public PunchingReport.PileResult R;
            public string   Code;        // PH1..PH9 | MANUAL | NONE
            public string   Reason;      // dla MANUAL / NONE
            public ObjectId CircleId;
            public Point3d  Center;
            public double   Radius;
            public Point3d  Anchor;      // położenie tekstu ID
        }

        public sealed class Result
        {
            public string Plot;
            public List<Item> Items = new List<Item>();
            public List<string> Warnings = new List<string>();
            public int Count(string code) => Items.Count(i => i.Code == code);
            public int Bars501 => (Count("PH1") + Count("PH2") + Count("PH3")) * BarsPerDetail;
            public int Bars502 => (Count("PH4") + Count("PH5") + Count("PH6")) * BarsPerDetail
                                + (Count("PH7") + Count("PH8") + Count("PH9")) * BarsPerDetailDense;
        }

        // ----------------------------------------------------------------
        // Klasyfikacja
        // ----------------------------------------------------------------

        private static readonly Regex BarRx = new Regex(@"H\s*(\d+)\s*@\s*(\d+)", RegexOptions.IgnoreCase);

        public static (string code, string reason) Classify(PunchingReport.PileResult r)
        {
            string reinf = (r.Reinf ?? "").Trim();
            string up = reinf.ToUpperInvariant();
            if (!double.IsNaN(r.Util) && r.Util > 100.0) return ("MANUAL", $"Util {r.Util:F1}% > 100%");
            if ((r.Status ?? "").ToUpperInvariant().Contains("FAIL")) return ("MANUAL", "Status FAIL");
            if (up.Contains("SHEAR") || up.Contains("RAIL")) return ("MANUAL", reinf);
            if (r.Type == "Reentrant") return ("MANUAL", "naroże wklęsłe (reentrant)");
            if (!up.StartsWith("ADD")) return ("NONE", reinf.Length > 0 ? reinf : "brak");

            var m = BarRx.Match(up);
            int level = 0;
            if (m.Success)
            {
                string d = m.Groups[1].Value, s = m.Groups[2].Value;
                if (d == "12" && s == "200") level = 1;
                else if (d == "16" && s == "200") level = 2;
                else if (d == "16" && s == "100") level = 3;
            }
            if (level == 0) return ("MANUAL", $"nieznane zbrojenie '{reinf}'");
            int loc = r.Type == "Edge" ? 1 : r.Type == "Corner" ? 2 : 0;
            return ("PH" + ((level - 1) * 3 + loc + 1), null);
        }

        // ----------------------------------------------------------------
        // Analiza: raport + rysunek
        // ----------------------------------------------------------------

        private sealed class DwgPile { public ObjectId Id; public Point3d C; public double R; }
        private sealed class DwgText { public Point3d P; public string Text; public bool OnPileLayer; }

        /// <summary>Płyta do wyboru: etykieta, pale w raporcie, pale z podpisem na rysunku.</summary>
        public sealed class PlotChoice
        {
            public string Label; public int Piles; public int Hits;
            public override string ToString() => $"{Label}   —   {Piles} pali w raporcie, {Hits} znalezionych na rysunku";
        }

        /// <summary>
        /// Dopasowanie raportu do rysunku — bez wskazywania obrysu: pale to koła SD-Pile z podpisem
        /// (P617 …) obok, w całym modelu. Płyty (lista z trafieniami) wybiera użytkownik w <paramref name="choosePlot"/>
        /// (zwraca indeks albo -1); domyślnie podświetlona ta z największą liczbą trafień.
        /// </summary>
        public static Result Analyze(Database db, List<PunchingReport.Plot> plots,
                                     Func<List<PlotChoice>, int, int> choosePlot)
        {
            var res = new Result();
            var piles = new List<DwgPile>();
            var texts = new List<DwgText>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    string layer = ent.Layer ?? "";
                    bool pileLayer = layer.StartsWith(PileTextLayer, StringComparison.OrdinalIgnoreCase);
                    if (ent is Circle c && layer.Equals(PileLayer, StringComparison.OrdinalIgnoreCase))
                        piles.Add(new DwgPile { Id = id, C = c.Center, R = c.Radius });
                    else if (ent is DBText t)
                        texts.Add(new DwgText { P = t.Position, Text = (t.TextString ?? "").Trim(), OnPileLayer = pileLayer });
                    else if (ent is MText mt)
                        texts.Add(new DwgText { P = mt.Location, Text = (mt.Text ?? "").Trim(), OnPileLayer = pileLayer });
                }
                tr.Commit();
            }
            if (piles.Count == 0)
            {
                res.Warnings.Add($"Na rysunku nie ma pali (koła na warstwie {PileLayer}).");
                return res;
            }

            // Podpisy przy palach: tekst ≤ max(8R, 1500) od koła SD-Pile
            var near = new List<(DwgText t, DwgPile p, double d)>();
            foreach (var t in texts)
            {
                if (t.Text.Length == 0 || t.Text.Length > 12) continue;
                DwgPile best = null; double bd = double.MaxValue;
                foreach (var p in piles)
                {
                    double d = p.C.DistanceTo(t.P);
                    if (d < bd) { bd = d; best = p; }
                }
                if (best != null && bd <= Math.Max(8 * best.R, 1500)) near.Add((t, best, bd));
            }

            // Pal raportu → podpis: dokładne ID (dowolna warstwa), potem znormalizowane (tylko SD-Pile Text*,
            // żeby numer pręta „617” nie udawał pala P617); kilka podpisów — najbliższy pala
            (DwgText t, DwgPile p, double d) Find(string pileId)
            {
                var hits = near.Where(x => SameId(x.t.Text, pileId, exact: true)).ToList();
                if (hits.Count == 0) hits = near.Where(x => x.t.OnPileLayer && SameId(x.t.Text, pileId, exact: false)).ToList();
                return hits.OrderBy(x => x.d).FirstOrDefault();
            }

            var choices = plots.Select(p => new PlotChoice
            {
                Label = p.Label, Piles = p.Piles.Count,
                Hits = p.Piles.Count(r => Find(r.PileId).t != null)
            }).ToList();
            if (choices.Count == 0) { res.Warnings.Add("Raport nie zawiera płyt (PLOT …)."); return res; }
            int def = choices.IndexOf(choices.OrderByDescending(c => c.Hits).First());
            int k = choosePlot(choices, def);
            if (k < 0 || k >= plots.Count) return res;
            var plot = plots[k];
            res.Plot = plot.Label;
            if (choices[k].Hits == 0)
            {
                res.Warnings.Add($"{plot.Label}: żaden pal z raportu nie ma podpisu przy palu na rysunku.");
                return res;
            }

            var used = new HashSet<long>();
            foreach (var r in plot.Piles)
            {
                var (code, reason) = Classify(r);
                var hit = Find(r.PileId);
                if (hit.t == null)
                {
                    res.Warnings.Add($"{r.PileId}: brak podpisu przy palu na rysunku — {code}.");
                    continue;
                }
                if (!used.Add(hit.p.Id.Handle.Value))
                {
                    res.Warnings.Add($"{r.PileId}: pal przy podpisie jest już przypisany innemu palowi z raportu — pominięty.");
                    continue;
                }
                if (near.Count(x => SameId(x.t.Text, r.PileId, exact: true)) > 1)
                    res.Warnings.Add($"{r.PileId}: kilka podpisów przy palach — użyty najbliższy pala.");
                res.Items.Add(new Item { R = r, Code = code, Reason = reason, CircleId = hit.p.Id,
                                         Center = hit.p.C, Radius = hit.p.R, Anchor = hit.t.P });
            }
            return res;
        }

        /// <summary>P617 ≡ P617 (exact); znormalizowane: bez „P” i zer wiodących (617 ≡ P617 ≡ P0617).</summary>
        public static bool SameId(string a, string b, bool exact)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            a = a.Trim(); b = b.Trim();
            if (exact) return a.Equals(b, StringComparison.OrdinalIgnoreCase);
            string N(string s)
            {
                s = s.ToUpperInvariant();
                if (s.StartsWith("P")) s = s.Substring(1);
                s = s.TrimStart('0');
                return s;
            }
            return N(a) == N(b) && N(a).Length > 0;
        }

        // ----------------------------------------------------------------
        // Rysunek
        // ----------------------------------------------------------------

        public sealed class ApplyStats
        {
            public int Tagged, Manual, Cleaned, TemplatesUpdated, TemplatesCrossed;
            public List<string> Warnings = new List<string>();
        }

        public static ApplyStats Apply(Document doc, Result res)
        {
            var st = new ApplyStats();
            var db = doc.Database;
            string plotKey = res.Plot ?? "";
            var plotPiles = new HashSet<string>(res.Items.Select(i => i.R.PileId), StringComparer.OrdinalIgnoreCase);
            using (doc.LockDocument())
            {
                SingleBarEngine.EnsureLayer(db, TagLayer, 2);
                SingleBarEngine.EnsureLayer(db, HatchLayer, 7);
                SingleBarEngine.EnsureLayer(db, NotUsedLayer, 1);

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    EnsureApp(tr, db);
                    var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                    ObjectId styleId = tst.Has(TagStyle) ? tst[TagStyle] : db.Textstyle;
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                    // 1. Sprzątanie: nasze poprzednie wyniki tej płyty + stare tagi przy palach (stary RC_PUNCHING_TAG / ASD)
                    var phTagRx = new Regex(@"^\s*(\{[^;]*;)*\s*(PH\s*[1-9]|MANUAL)", RegexOptions.IgnoreCase);
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;
                        bool erase = false;
                        var rb = ent.GetXDataForApplication(XApp);
                        if (rb != null)
                        {
                            var v = rb.AsArray();
                            rb.Dispose();
                            // [1] kod, [2] pal, [3] płyta (PLOT); przekreślenia szablonów — zawsze od nowa
                            string code0 = v.Length > 1 ? v[1].Value as string : "";
                            string pile0 = v.Length > 2 ? v[2].Value as string : "";
                            string plot0 = v.Length > 3 ? v[3].Value as string : "";
                            erase = code0 == "NOTUSED"
                                 || string.Equals(plot0, plotKey, StringComparison.OrdinalIgnoreCase)
                                 || (!string.IsNullOrEmpty(pile0) && plotPiles.Contains(pile0))
                                 || code0 == "MANUAL-NOTE" && !(plot0 ?? "").StartsWith("PLOT", StringComparison.OrdinalIgnoreCase);
                        }
                        else if ((ent is MText mt && mt.Layer.Equals(TagLayer, StringComparison.OrdinalIgnoreCase)
                                  && phTagRx.IsMatch(mt.Contents ?? ""))
                                 || (ent is Hatch && ent.Layer.Equals(HatchLayer, StringComparison.OrdinalIgnoreCase)))
                        {
                            Point3d p = ent is MText m2 ? m2.Location : Center(ent);
                            erase = res.Items.Any(it => p.DistanceTo(it.Center) < Math.Max(3 * it.Radius, it.Center.DistanceTo(it.Anchor) + 300));
                        }
                        if (erase) { ent.UpgradeOpen(); ent.Erase(); st.Cleaned++; }
                    }

                    // 2. Tagi i kreskowanie
                    foreach (var it in res.Items.Where(i => i.Code != "NONE"))
                    {
                        bool manual = it.Code == "MANUAL";
                        var mt = new MText();
                        mt.SetDatabaseDefaults(db);
                        mt.Contents    = it.Code;
                        mt.Layer       = TagLayer;
                        mt.ColorIndex  = manual ? (short)1 : (short)4;
                        mt.TextHeight  = 120.0;
                        mt.Attachment  = AttachmentPoint.TopLeft;
                        mt.Location    = new Point3d(it.Anchor.X, it.Anchor.Y - 80.0, 0);
                        mt.TextStyleId = styleId;
                        ms.AppendEntity(mt);
                        tr.AddNewlyCreatedDBObject(mt, true);
                        Tag(mt, it.Code, it.R.PileId, plotKey);

                        if (!manual)
                        {
                            try
                            {
                                var h = new Hatch();
                                h.SetDatabaseDefaults(db);
                                h.Layer = HatchLayer;
                                h.ColorIndex = 1;
                                h.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31");
                                h.PatternScale = 10.0;
                                h.Associative = false;
                                ms.AppendEntity(h);
                                tr.AddNewlyCreatedDBObject(h, true);
                                h.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { it.CircleId });
                                h.EvaluateHatch(true);
                                Tag(h, it.Code, it.R.PileId, plotKey);
                            }
                            catch (System.Exception ex) { Log.Error("PunchingAuto.Hatch", ex); }
                            st.Tagged++;
                        }
                        else st.Manual++;
                    }

                    // 3. Notatka MANUAL nad płytą
                    var manualIds = res.Items.Where(i => i.Code == "MANUAL").Select(i => i.R.PileId).ToList();
                    if (manualIds.Count > 0)
                    {
                        var tagged = res.Items.ToList();
                        double nx = tagged.Min(i => i.Center.X), ny = tagged.Max(i => i.Center.Y);
                        var note = new MText();
                        note.SetDatabaseDefaults(db);
                        note.Contents   = (manualIds.Count == 1 ? "PILE: " : "PILES: ") + string.Join(", ", SortIds(manualIds)) + " — MANUAL DESIGN";
                        note.Layer      = TagLayer;
                        note.ColorIndex = 1;
                        note.TextHeight = 200.0;
                        note.Attachment = AttachmentPoint.BottomLeft;
                        note.Location   = new Point3d(nx - 600.0, ny + 1500.0, 0);   // nad palami płyty
                        note.TextStyleId = styleId;
                        ms.AppendEntity(note);
                        tr.AddNewlyCreatedDBObject(note, true);
                        Tag(note, "MANUAL-NOTE", "", plotKey);
                    }

                    // 4. Szablony detali PH1–PH9 (AP-TEXT)
                    UpdateTemplates(tr, db, ms, res, plotKey, st);

                    tr.Commit();
                }
            }
            return st;
        }

        private static void UpdateTemplates(Transaction tr, Database db, BlockTableRecord ms, Result res,
                                            string plotKey, ApplyStats st)
        {
            var phRx   = new Regex(@"PH\s*([1-9])(?!\d)");
            // „APPLICABLE FOR PILE/PILES/PILE/S …” do końca akapitu (\P), formatowania (\ lub }) — nie dalej
            var applRx = new Regex(@"APPLICABLE\s+FOR\s+PILE(?:/S|S)?[^\\}]*", RegexOptions.IgnoreCase);
            var locRx  = new Regex(@"\(\s*(?:\d+\s*No\.?\s*LOCATIONS?|N/A)\s*\)", RegexOptions.IgnoreCase);

            var byPh = res.Items.Where(i => i.Code.StartsWith("PH"))
                                .GroupBy(i => i.Code).ToDictionary(g => g.Key, g => SortIds(g.Select(i => i.R.PileId)));
            var seen = new HashSet<string>();
            var ids = new List<ObjectId>();
            foreach (ObjectId id in ms) ids.Add(id);   // kopia — w pętli dopisujemy linie przekreśleń
            foreach (ObjectId id in ids)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is MText mt)) continue;
                if (!mt.Layer.Equals(TemplateLayer, StringComparison.OrdinalIgnoreCase)) continue;
                string raw = mt.Contents ?? "";
                var pm = phRx.Match(mt.Text ?? raw);
                if (!pm.Success || !applRx.IsMatch(raw)) continue;
                string ph = "PH" + pm.Groups[1].Value;
                if (!seen.Add(ph)) st.Warnings.Add($"Szablon {ph} występuje na AP-TEXT więcej niż raz — zaktualizowano wszystkie.");
                byPh.TryGetValue(ph, out var list);
                int n = list?.Count ?? 0;

                string appl = n == 0 ? "APPLICABLE FOR PILES N/A"
                            : n == 1 ? "APPLICABLE FOR PILE " + list[0]
                                     : "APPLICABLE FOR PILES " + string.Join(", ", list);
                string loc = n == 0 ? "(N/A)" : n == 1 ? "(1No LOCATION)" : $"({n}No LOCATIONS)";
                string upd = applRx.Replace(raw, appl.Replace("$", "$$"), 1);
                if (locRx.IsMatch(upd)) upd = locRx.Replace(upd, loc, 1);
                else st.Warnings.Add($"Szablon {ph}: brak „(nNo LOCATIONS)” — liczba lokalizacji nie wpisana.");
                if (upd != raw) { mt.UpgradeOpen(); mt.Contents = upd; }
                st.TemplatesUpdated++;

                if (n == 0)
                {
                    // Nieużyty detal: przekreślenie (usuwane przy kolejnym uruchomieniu)
                    try
                    {
                        var e = mt.GeometricExtents;
                        foreach (var (a, b) in new[] { (e.MinPoint, e.MaxPoint),
                                                       (new Point3d(e.MinPoint.X, e.MaxPoint.Y, 0), new Point3d(e.MaxPoint.X, e.MinPoint.Y, 0)) })
                        {
                            var ln = new Line(new Point3d(a.X, a.Y, 0), new Point3d(b.X, b.Y, 0)) { Layer = NotUsedLayer, ColorIndex = 1 };
                            ms.AppendEntity(ln);
                            tr.AddNewlyCreatedDBObject(ln, true);
                            Tag(ln, "NOTUSED", ph, plotKey);
                        }
                        st.TemplatesCrossed++;
                    }
                    catch (System.Exception ex) { Log.Error("PunchingAuto.Cross", ex); }
                }
            }
            if (seen.Count == 0)
                st.Warnings.Add("Brak szablonów detali PH1–PH9 (MText na AP-TEXT z „APPLICABLE FOR PILES”) — opisy detali nie zaktualizowane.");
        }

        public static List<string> SortIds(IEnumerable<string> ids)
            => ids.Distinct(StringComparer.OrdinalIgnoreCase)
                  .OrderBy(s => { var m = Regex.Match(s, @"\d+"); return m.Success && long.TryParse(m.Value, out long v) ? v : long.MaxValue; })
                  .ThenBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

        private static Point3d Center(Entity e)
        {
            try
            {
                var x = e.GeometricExtents;
                return new Point3d((x.MinPoint.X + x.MaxPoint.X) / 2, (x.MinPoint.Y + x.MaxPoint.Y) / 2, 0);
            }
            catch { return new Point3d(double.MaxValue / 4, double.MaxValue / 4, 0); }   // brak zakresu — nie dopasuje się
        }

        private static void Tag(Entity e, string code, string pileId, string plotKey)
        {
            e.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, XApp),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, code ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, pileId ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, plotKey ?? ""));
        }

        private static void EnsureApp(Transaction tr, Database db)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(XApp)) return;
            rat.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = XApp };
            rat.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }
    }
}
