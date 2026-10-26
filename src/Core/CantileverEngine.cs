using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Wsporniki płyty na rysunku (decyzja Bartka 2026-10-21, etap 1 — wykrywanie i wyłączenie z obrysu siatki; zbrojenie — etap 2).
    ///   • Wspornik = zamknięta polilinia (prostokąt: nasada na krawędzi płyty → koniec) na warstwie <see cref="LayerName"/> z XData
    ///     <see cref="App"/>: [1] FormatVersion (Int16, 1) [2] szerokość [3] wysokość GB (0 = brak opisu) [4] TOC [m] (0 = brak)
    ///     [5] grubość strefy przy palu „xxxTHK” (0 = brak strefy / opisu) [6] długość strefy przy palu w płycie (0 = brak)
    ///     [7] kierunek wysięgu („+X” / „-X” / „+Y” / „-Y”) [8] wysięg poza krawędź płyty [9] długość strefy wspornika w płycie
    ///     (2026-10-21, uwaga Bartka „niech nakłada się na płytę jak BEAM”: prostokąt od końca wspornika do końca strefy przy palu,
    ///     bez strefy — CantileverRules.NoRootExtension). Bez powiązań handle (CloneRemap niepotrzebny); płyta = obrys, na którego
    ///     krawędzi leży nasada. Znacznik RC_GA skopiowany z obrysu / kawałka obrysu — ponowne Prepare GA usuwa wspornik razem z płytą.
    ///   • Strefa przy palu (przerywany U w płycie po drugiej stronie nasady) — przeniesiona na tę samą warstwę (bez rekordu).
    ///   • Obrys płyty: języczek usunięty (krawędź prosto przez nasadę) — Mesh / UB / nib / T IN NIB / B3 / Add jak bez wspornika.
    /// Wspornik potwierdzony = opis „GB-…” w pobliżu (≤ <see cref="GbSearch"/> od prostokąta) albo strefa przy palu; inne języczki
    /// w zakresach CantileverRules — tylko komunikat (RC_PREPARE_GA) / pytanie (RC_CANTILEVER).
    /// </summary>
    public static class CantileverEngine
    {
        public const string LayerName = "RC-CANTILEVER";
        public const string App = "RC_CANT";
        public const short FormatVersion = 1;
        public const string SlabLayer = "SD-PILED-RAFT";
        public const string GaApp = "RC_GA";
        /// <summary>Opis „GB-…” (punkt tekstu / grot odnośnika) najwyżej tyle od prostokąta wspornika [mm].</summary>
        public const double GbSearch = 2500.0;
        /// <summary>Opis „xxxTHK” strefy przy palu — w prostokącie strefy powiększonym o tyle [mm].</summary>
        public const double ThkSearch = 300.0;

        public static bool IsLayer(string layer) => string.Equals(layer, LayerName, StringComparison.OrdinalIgnoreCase);

        public sealed class Data
        {
            public double Width, Height, Toc, RootThk, RootLen;
            public string Dir = "";
            /// <summary>[8] wysięg poza krawędź płyty, [9] długość strefy wspornika w płycie (od krawędzi w głąb).</summary>
            public double Reach, Ext;
        }

        /// <summary>Opis z rysunku: treść, punkt (MText — środek, MLeader — grot, DBText — środek).</summary>
        public sealed class Txt
        {
            public string Text;
            public double X, Y;
        }

        /// <summary>Wynik przebiegu: liczba wsporników i komunikaty.</summary>
        public sealed class Report
        {
            public int Made;
            /// <summary>Ostrzeżenia (pominięte kandydaty, rozbieżności opisu).</summary>
            public List<string> Messages = new List<string>();
            /// <summary>Wsporniki wyłączone z obrysu (po jednym wierszu).</summary>
            public List<string> Info = new List<string>();
            /// <summary>Indeksy opisów wykorzystanych (opis GB i „xxxTHK” strefy przy palu) — do pominięcia przez K8 (belki z opisów).</summary>
            public HashSet<int> UsedTexts = new HashSet<int>();
            /// <summary>Kawałki przeniesione na RC-CANTILEVER (strefy przy palu) — do usunięcia z list kawałków obrysu / uskoku.</summary>
            public List<ObjectId> Moved = new List<ObjectId>();
        }

        // ── rekord ────────────────────────────────────────────────────────

        public static bool IsStub(Entity e)
        {
            if (!(e is Polyline)) return false;
            using (var rb = e.GetXDataForApplication(App)) return rb != null;
        }

        public static Data Read(Entity e)
        {
            TypedValue[] v;
            using (var rb = e?.GetXDataForApplication(App)) v = rb?.AsArray();
            if (v == null) return null;
            double R(int i) => v.Length > i && v[i].Value is double d ? d : 0.0;
            return new Data { Width = R(2), Height = R(3), Toc = R(4), RootThk = R(5), RootLen = R(6),
                              Dir = v.Length > 7 ? v[7].Value?.ToString() ?? "" : "", Reach = R(8), Ext = R(9) };
        }

        private static void Write(Transaction tr, Database db, Entity e, Data d)
        {
            BeamStore.EnsureApp(tr, db, App);
            if (!e.IsWriteEnabled) e.UpgradeOpen();
            e.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, App),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, FormatVersion),   // [1]
                new TypedValue((int)DxfCode.ExtendedDataReal, d.Width),             // [2]
                new TypedValue((int)DxfCode.ExtendedDataReal, d.Height),            // [3]
                new TypedValue((int)DxfCode.ExtendedDataReal, d.Toc),               // [4]
                new TypedValue((int)DxfCode.ExtendedDataReal, d.RootThk),           // [5]
                new TypedValue((int)DxfCode.ExtendedDataReal, d.RootLen),           // [6]
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, d.Dir ?? ""),  // [7]
                new TypedValue((int)DxfCode.ExtendedDataReal, d.Reach),             // [8]
                new TypedValue((int)DxfCode.ExtendedDataReal, d.Ext));              // [9]
        }

        /// <summary>Kolor warstwy RC-CANTILEVER — magenta jak strefy belek RC-BEAM (2026-10-21: wspornik ma być widoczny jak strefa belki).</summary>
        public const short LayerColor = 6;

        /// <summary>
        /// Warstwa RC-CANTILEVER (magenta, linia ciągła). Warstwa z pierwszej wersji (kolor obrysu płyty — wspornik niewidoczny na tle obrysu)
        /// dostaje kolor magenta.
        /// </summary>
        public static void EnsureLayer(Transaction tr, Database db)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(LayerName))
            {
                var ex = (LayerTableRecord)tr.GetObject(lt[LayerName], OpenMode.ForRead);
                if (ex.Color.ColorIndex != LayerColor && (ex.Description ?? "").StartsWith("NA Smart RC: wsporniki"))
                {
                    ex.UpgradeOpen();
                    ex.Color = Color.FromColorIndex(ColorMethod.ByAci, LayerColor);
                }
                return;
            }
            var rec = new LayerTableRecord { Name = LayerName, Color = Color.FromColorIndex(ColorMethod.ByAci, LayerColor) };
            lt.UpgradeOpen();
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            try { rec.Description = "NA Smart RC: wsporniki płyty (GB poza krawędzią) — wyłączone z obrysu siatki; rekord RC_CANT"; }
            catch (System.Exception ex) { Log.Error("CantileverEngine.EnsureLayer.Description", ex); }
        }

        // ── opisy przy wsporniku ───────────────────────────────────────────

        private static double DistToBox((double X0, double Y0, double X1, double Y1) b, double x, double y)
        {
            double dx = Math.Max(0, Math.Max(b.X0 - x, x - b.X1)), dy = Math.Max(0, Math.Max(b.Y0 - y, y - b.Y1));
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Najbliższy opis „GB-…” (≤ GbSearch od prostokąta wspornika), nieprzypisany innemu wspornikowi.</summary>
        private static int GbFor(CantileverRules.UShape stub, IList<Txt> texts, ISet<int> taken)
        {
            var box = CantileverRules.Box(stub);
            int best = -1; double bd = double.MaxValue;
            for (int i = 0; i < texts.Count; i++)
            {
                // „GB-…” — jeden wspornik; „300THK / TOC=…” (krótkie wsporniki, PLOT 357-362) — jeden opis z dwiema strzałkami do dwóch wsporników
                bool gbText = CantileverRules.ParseGb(texts[i].Text) != null;
                if (!gbText && CantileverRules.ParseThkToc(texts[i].Text) == null) continue;
                if (gbText && taken.Contains(i)) continue;
                double d = DistToBox(box, texts[i].X, texts[i].Y);
                if (d <= GbSearch && d < bd) { bd = d; best = i; }
            }
            return best;
        }

        private static int ThkFor(CantileverRules.UShape root, IList<Txt> texts, ISet<int> taken)
        {
            if (root == null) return -1;
            var box = CantileverRules.Box(root);
            for (int i = 0; i < texts.Count; i++)
            {
                if (taken.Contains(i) || CantileverRules.ParseGb(texts[i].Text) != null || CantileverRules.ParseThkToc(texts[i].Text) != null) continue;
                if (!texts[i].Text.Split('\n').Any(l => BeamGeometry.ParseThk(l) != null)) continue;
                if (CantileverRules.InBox(box, texts[i].X, texts[i].Y, ThkSearch)) return i;
            }
            return -1;
        }

        private static Data Describe(CantileverRules.UShape stub, CantileverRules.UShape root, int gb, int thk, IList<Txt> texts)
        {
            var d = new Data { Width = stub.Width, Dir = CantileverRules.DirCode(stub.Out), RootLen = root?.Depth ?? 0,
                               Reach = stub.Depth, Ext = root?.Depth ?? CantileverRules.NoRootExtension };
            if (gb >= 0)
            {
                var g = CantileverRules.ParseGb(texts[gb].Text);
                d.Height = g?.Height ?? CantileverRules.ParseThkToc(texts[gb].Text) ?? 0;
                d.Toc = CantileverRules.ParseToc(texts[gb].Text) ?? 0;
            }
            if (thk >= 0)
                foreach (var l in texts[thk].Text.Split('\n'))
                    if (BeamGeometry.ParseThk(l) is int t) { d.RootThk = t; break; }
            return d;
        }

        private static string Label(Data d)
            => $"GB {d.Width:F0} × {(d.Height > 0 ? d.Height.ToString("F0") : "?")}" +
               (d.Toc != 0 ? $", TOC={d.Toc:0.000}" : "") +
               (d.RootLen > 0 ? $", strefa przy palu {(d.RootThk > 0 ? d.RootThk + "THK" : "bez opisu")} × {d.RootLen:F0}" : ", bez strefy przy palu");

        // ── encje ─────────────────────────────────────────────────────────

        /// <summary>Wierzchołki jak na rysunku (z języczkami — wykrywanie wsporników).</summary>
        private static List<(double X, double Y)> Pts(Polyline pl)
            => GeometryHelper.GetRawPolylineVertices(pl).Select(p => (p.X, p.Y)).ToList();

        /// <summary>Dopisuje języczki do rekordu RC_CANT_TAB obrysu (obrys na rysunku bez zmian).</summary>
        private static void AddTabRecord(Transaction tr, Database db, Polyline pl, IEnumerable<CantileverRules.TabRec> add)
        {
            var all = GeometryHelper.CantileverTabRecord(pl) ?? new List<CantileverRules.TabRec>();
            all.AddRange(add);
            BeamStore.EnsureApp(tr, db, CantileverRules.TabApp);
            if (!pl.IsWriteEnabled) pl.UpgradeOpen();
            var tv = new List<TypedValue> { new TypedValue((int)DxfCode.ExtendedDataRegAppName, CantileverRules.TabApp) };
            foreach (var r in all) tv.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, r.ToString()));
            pl.XData = new ResultBuffer(tv.ToArray());
        }

        /// <summary>Kształt U języczka wspornika z prostokąta strefy RC_CANT (kierunek [7], wysięg od krawędzi [8]); null — za mało danych.</summary>
        private static CantileverRules.UShape StubShape(List<(double X, double Y)> zone, Data d)
        {
            if (d == null || d.Dir == null || d.Dir.Length != 2 || d.Reach <= 1.0 || zone.Count < 4) return null;
            bool ax = d.Dir[1] == 'X'; int sg = d.Dir[0] == '-' ? -1 : 1;
            double a0 = ax ? zone.Min(q => q.X) : zone.Min(q => q.Y), a1 = ax ? zone.Max(q => q.X) : zone.Max(q => q.Y);
            double c0 = ax ? zone.Min(q => q.Y) : zone.Min(q => q.X), c1 = ax ? zone.Max(q => q.Y) : zone.Max(q => q.X);
            double end = sg > 0 ? a1 : a0, root = end - sg * d.Reach;
            (double X, double Y) P(double al, double ac) => ax ? (al, ac) : (ac, al);
            return new CantileverRules.UShape
            {
                A = P(root, c0), C1 = P(end, c0), C2 = P(end, c1), B = P(root, c1),
                Width = c1 - c0, Depth = d.Reach, Out = ax ? (sg, 0.0) : (0.0, sg)
            };
        }

        /// <summary>
        /// Uwaga Bartka 2026-10-22 („Prepare GA zmienia faktyczny obrys płyty”): obrys na rysunku ma mieć języczek wspornika jak w GA.
        /// Dla każdego wspornika RC_CANT, którego nasada leży na krawędzi obrysu: obrys bez języczka (poprzednia wersja przecinała obrys,
        /// wspornik z osobnego kawałka U — JoinLoops skleił krawędź prosto) → języczek wstawiony z powrotem; języczek w rekordzie RC_CANT_TAB
        /// (obliczenia — obrys bez języczka). Liczba przywróconych / dopisanych.
        /// </summary>
        public static int RestoreOutlineTabs(Database db, IEnumerable<ObjectId> outlines, List<string> messages = null)
        {
            int n = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                var stubs = new List<CantileverRules.UShape>();
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline zp) || !IsStub(zp)) continue;
                    var u = StubShape(Pts(zp), Read(zp));
                    if (u != null) stubs.Add(u);
                }
                if (stubs.Count == 0) { tr.Commit(); return 0; }
                foreach (var oid in outlines.Distinct())
                {
                    if (oid.IsNull || oid.IsErased || !(tr.GetObject(oid, OpenMode.ForRead) is Polyline pl)) continue;
                    if (!GeometryHelper.IsEffectivelyClosed(pl) || HasArcs(pl) || IsStub(pl)) continue;
                    var raw = Pts(pl);
                    var rec = GeometryHelper.CantileverTabRecord(pl) ?? new List<CantileverRules.TabRec>();
                    var recorded = CantileverRules.RecordedTabs(raw, rec);
                    var add = new List<CantileverRules.TabRec>();
                    var cur = raw;
                    foreach (var u in stubs)
                    {
                        // już w rekordzie (ten sam języczek na rysunku)
                        if (recorded.Any(t => CantileverRules.Near(t.Shape.C1, u.C1) && CantileverRules.Near(t.Shape.C2, u.C2)
                                           || CantileverRules.Near(t.Shape.C1, u.C2) && CantileverRules.Near(t.Shape.C2, u.C1))) continue;
                        // języczek jest na rysunku, ale bez rekordu
                        var tabs = CantileverRules.FindTabs(cur);
                        if (tabs.Any(t => CantileverRules.Near(t.Shape.C1, u.C1) && CantileverRules.Near(t.Shape.C2, u.C2)
                                       || CantileverRules.Near(t.Shape.C1, u.C2) && CantileverRules.Near(t.Shape.C2, u.C1)))
                        {
                            add.Add(CantileverRules.TabRec.Of(u));
                            continue;
                        }
                        // obrys przecięty — wstawienie języczka (nasada na krawędzi obrysu)
                        var ins = CantileverRules.InsertTab(cur, u);
                        if (ins.Count <= cur.Count) continue;
                        if (!CantileverRules.FindTabs(ins).Any(t => CantileverRules.TabRec.Of(u).Matches(t.Shape))) continue;
                        cur = ins;
                        add.Add(CantileverRules.TabRec.Of(u));
                    }
                    if (add.Count == 0) continue;
                    if (!ReferenceEquals(cur, raw))
                    {
                        pl.UpgradeOpen();
                        int old = pl.NumberOfVertices;
                        for (int i = 0; i < cur.Count; i++) pl.AddVertexAt(old + i, new Point2d(cur[i].X, cur[i].Y), 0, 0, 0);
                        for (int i = 0; i < old; i++) pl.RemoveVertexAt(0);
                        pl.Closed = true;
                    }
                    AddTabRecord(tr, db, pl, add);
                    n += add.Count;
                }
                tr.Commit();
            }
            if (n > 0) messages?.Add($"[RC CANT] obrys płyty z języczkiem wspornika jak w GA: {n} (do obliczeń program bierze krawędź prosto przez nasadę).");
            return n;
        }

        private static bool HasArcs(Polyline pl)
        {
            for (int i = 0; i < pl.NumberOfVertices; i++) if (Math.Abs(pl.GetBulgeAt(i)) > 1e-9) return true;
            return false;
        }

        private static bool Dashed(Entity e)
        {
            string lt = (e.Linetype ?? "").ToUpperInvariant();
            return lt.Contains("DASH") || lt.Contains("HIDDEN");
        }

        private static void CopyGaTag(Entity from, Entity to)
        {
            using (var rb = from.GetXDataForApplication(GaApp))
                if (rb != null) to.XData = new ResultBuffer(rb.AsArray());
        }

        private static ObjectId AddRect(Transaction tr, Database db, CantileverRules.UShape u, Entity like, Data d)
        {
            EnsureLayer(tr, db);
            var pl = new Polyline { Layer = LayerName, Color = Color.FromColorIndex(ColorMethod.ByLayer, 256) };
            var pts = ZonePts(u, d.Ext);
            for (int i = 0; i < 4; i++) pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
            pl.Closed = true;
            if (like != null) { pl.LinetypeId = like.LinetypeId; pl.LineWeight = like.LineWeight; }
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            if (like != null) CopyGaTag(like, pl);
            Write(tr, db, pl, d);
            return pl.ObjectId;
        }

        /// <summary>Prostokąt strefy wspornika: nasady A / B przesunięte w głąb płyty o ext (A', C1, C2, B').</summary>
        private static (double X, double Y)[] ZonePts(CantileverRules.UShape u, double ext)
        {
            (double X, double Y) In((double X, double Y) q) => (q.X - u.Out.X * ext, q.Y - u.Out.Y * ext);
            return new[] { In(u.A), u.C1, u.C2, In(u.B) };
        }

        private static void MoveToLayer(Transaction tr, Database db, ObjectId id)
        {
            EnsureLayer(tr, db);
            if (tr.GetObject(id, OpenMode.ForWrite) is Entity e) e.Layer = LayerName;
        }

        /// <summary>Otwarte polilinie U (bez łuków) z listy — (id, kształt).</summary>
        private static List<(ObjectId id, CantileverRules.UShape u)> OpenUs(Transaction tr, IEnumerable<ObjectId> ids)
        {
            var res = new List<(ObjectId, CantileverRules.UShape)>();
            foreach (var id in ids)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl) || HasArcs(pl)) continue;
                if (GeometryHelper.IsEffectivelyClosed(pl)) continue;
                var u = CantileverRules.TryU(Pts(pl));
                if (u != null) res.Add((id, u));
            }
            return res;
        }

        // ── RC_PREPARE_GA: kawałki U obrysu (przed domknięciem nibu i JoinLoops) ──────────────────────

        /// <summary>
        /// Wspornik jako osobny kawałek obrysu w kształcie U (PLOT 13-14, PLOT 67-75): końce na końcach innych kawałków SD-PILED-RAFT
        /// albo na obrysie płyty, bok końcowy poza obrysem. Kawałek zastępowany prostym odcinkiem nasady (2-wierzchołkowa polilinia —
        /// JoinLoops skleja krawędź płyty prosto), U → prostokąt RC-CANTILEVER z rekordem; U strefy przy palu → RC-CANTILEVER.
        /// Listy openPl / nibPieces aktualizowane.
        /// </summary>
        public static Report ProcessOpenPieces(Database db, ObjectId outlineId, List<ObjectId> openPl, List<ObjectId> nibPieces,
                                               IList<Txt> texts, string where)
        {
            var rep = new Report();
            if (outlineId.IsNull || openPl.Count == 0) return rep;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (!(tr.GetObject(outlineId, OpenMode.ForRead) is Polyline opl)) return rep;
                var outline = GeometryHelper.GetPolylineVertices(opl);
                var us = OpenUs(tr, openPl);
                // końce pozostałych kawałków (nie-U): polilinie i linie
                var ends = new List<(ObjectId id, (double X, double Y) p)>();
                foreach (var id in openPl)
                {
                    if (id.IsErased || us.Any(x => x.id == id)) continue;
                    var ent = tr.GetObject(id, OpenMode.ForRead);
                    if (ent is Line ln) { ends.Add((id, (ln.StartPoint.X, ln.StartPoint.Y))); ends.Add((id, (ln.EndPoint.X, ln.EndPoint.Y))); }
                    else if (ent is Polyline pl && pl.NumberOfVertices >= 2)
                    {
                        ends.Add((id, (pl.GetPoint2dAt(0).X, pl.GetPoint2dAt(0).Y)));
                        var last = pl.GetPoint2dAt(pl.NumberOfVertices - 1); ends.Add((id, (last.X, last.Y)));
                    }
                }
                bool OnOutline((double X, double Y) q)
                {
                    var p = new Point2d(q.X, q.Y);
                    for (int i = 0; i < outline.Count; i++)
                    {
                        Point2d a = outline[i], b = outline[(i + 1) % outline.Count];
                        var ab = b - a; double L2 = ab.DotProduct(ab);
                        double t = L2 > 1e-9 ? Math.Max(0, Math.Min(1, (p - a).DotProduct(ab) / L2)) : 0;
                        if ((a + ab * t - p).Length <= CantileverRules.Tol) return true;
                    }
                    return false;
                }
                bool Connected((double X, double Y) q) => ends.Any(e => CantileverRules.Near(e.p, q)) || OnOutline(q);
                bool Inside(CantileverRules.UShape u)
                    => GeometryHelper.IsPointInsidePolygon(outline, new Point2d((u.C1.X + u.C2.X) / 2, (u.C1.Y + u.C2.Y) / 2));

                var taken = new HashSet<int>();
                foreach (var (sid, stub) in us)
                {
                    if (!CantileverRules.InRange(stub) || Inside(stub) || !Connected(stub.A) || !Connected(stub.B)) continue;
                    // ramka detalu / strefa (przerywana) — nie obrys wspornika
                    if (Dashed((Entity)tr.GetObject(sid, OpenMode.ForRead))) continue;
                    var root = us.FirstOrDefault(x => x.id != sid && CantileverRules.IsRootOf(x.u, stub));
                    int gb = GbFor(stub, texts, taken);
                    int thk = ThkFor(root.u, texts, taken);
                    var d = Describe(stub, root.u, gb, thk, texts);
                    if (gb < 0 && root.u == null)
                    {
                        rep.Messages.Add($"[RC CANT] {where}: możliwy wspornik {stub.Width:F0} × {stub.Depth:F0} bez opisu „GB-…” i bez strefy przy " +
                                         "palu — pominięty (sprawdź; RC_CANTILEVER).");
                        continue;
                    }
                    if (gb >= 0) { taken.Add(gb); rep.UsedTexts.Add(gb); }
                    if (thk >= 0) { taken.Add(thk); rep.UsedTexts.Add(thk); }

                    var se = (Entity)tr.GetObject(sid, OpenMode.ForWrite);
                    // nasada — prosty odcinek obrysu w miejscu U
                    var seg = new Polyline { Layer = se.Layer, Color = se.Color, LinetypeId = se.LinetypeId, LineWeight = se.LineWeight };
                    seg.AddVertexAt(0, new Point2d(stub.A.X, stub.A.Y), 0, 0, 0);
                    seg.AddVertexAt(1, new Point2d(stub.B.X, stub.B.Y), 0, 0, 0);
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    ms.AppendEntity(seg);
                    tr.AddNewlyCreatedDBObject(seg, true);
                    CopyGaTag(se, seg);
                    openPl.Add(seg.ObjectId); nibPieces.Add(seg.ObjectId);
                    // U → prostokąt wspornika
                    EnsureLayer(tr, db);
                    var spl = (Polyline)se;
                    spl.Layer = LayerName;
                    spl.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                    spl.Closed = true;
                    var zp = ZonePts(stub, d.Ext);
                    spl.SetPointAt(0, new Point2d(zp[0].X, zp[0].Y));
                    spl.SetPointAt(3, new Point2d(zp[3].X, zp[3].Y));
                    Write(tr, db, spl, d);
                    openPl.Remove(sid); nibPieces.Remove(sid);
                    if (root.u != null)
                    {
                        MoveToLayer(tr, db, root.id);
                        openPl.Remove(root.id); nibPieces.Remove(root.id);
                    }
                    rep.Made++;
                    rep.Info.Add($"[RC CANT] {where}: wspornik {Label(d)}, wysięg {stub.Depth:F0} — wyłączony z obrysu siatki.");
                    if (gb >= 0 && CantileverRules.ParseGb(texts[gb].Text) is var g && g.HasValue && Math.Abs(g.Value.Width - stub.Width) > 5.0)
                        rep.Messages.Add($"[RC CANT] {where}: opis „GB-{g.Value.Width:F0}x…” a szerokość na rysunku {stub.Width:F0} — przyjęta z rysunku.");
                }
                tr.Commit();
            }
            return rep;
        }

        // ── języczki zamkniętych obrysów (RC_PREPARE_GA po JoinLoops, RC_CANTILEVER) ─────────────────

        /// <summary>
        /// Języczki zamkniętych polilinii obrysu (CantileverRules.FindTabs): potwierdzone (opis GB albo strefa przy palu wśród
        /// <paramref name="openPieces"/>) — usunięte z obrysu (polilinia ta sama, wierzchołki na nowo), prostokąt RC-CANTILEVER
        /// z rekordem i znacznikiem RC_GA obrysu; strefa przy palu → RC-CANTILEVER (usunięta z <paramref name="openPieces"/>).
        /// Niepotwierdzone — <paramref name="ask"/> (null = tylko komunikat).
        /// </summary>
        public static Report ProcessClosedOutlines(Database db, IEnumerable<ObjectId> outlines, List<ObjectId> openPieces,
                                                   IList<Txt> texts, string where, Func<string, bool> ask = null,
                                                   IEnumerable<int> usedTexts = null)
        {
            var rep = new Report();
            var taken = new HashSet<int>(usedTexts ?? Enumerable.Empty<int>());
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var us = OpenUs(tr, openPieces ?? new List<ObjectId>());
                foreach (var oid in outlines.Distinct())
                {
                    if (oid.IsNull || oid.IsErased || !(tr.GetObject(oid, OpenMode.ForRead) is Polyline pl)) continue;
                    if (!GeometryHelper.IsEffectivelyClosed(pl)) continue;
                    var pts = Pts(pl);
                    var tabs = CantileverRules.FindTabs(pts);
                    if (tabs.Count == 0) continue;
                    if (HasArcs(pl))
                    {
                        rep.Messages.Add($"[RC CANT] {where}: obrys z łukami — wsporniki ({tabs.Count}) nie wyłączone; użyj RC_CANTILEVER po wyprostowaniu.");
                        continue;
                    }
                    var accepted = new List<(CantileverRules.Tab t, Data d, ObjectId rootId)>();
                    // języczki już będące wspornikami (rekord RC_CANT_TAB — ponowne RC_CANTILEVER / Prepare GA) — pominięte
                    var done = CantileverRules.RecordedTabs(pts, GeometryHelper.CantileverTabRecord(pl));
                    foreach (var t in tabs)
                    {
                        if (done.Any(x => x.IdxE0 == t.IdxE0 && x.IdxE1 == t.IdxE1)) continue;
                        var root = us.FirstOrDefault(x => CantileverRules.IsRootOf(x.u, t.Shape));
                        int gb = GbFor(t.Shape, texts, taken);
                        int thk = ThkFor(root.u, texts, taken);
                        var d = Describe(t.Shape, root.u, gb, thk, texts);
                        if (gb < 0 && root.u == null)
                        {
                            string q = $"możliwy wspornik {t.Shape.Width:F0} × {t.Shape.Depth:F0} bez opisu „GB-…” i bez strefy przy palu";
                            if (ask == null || !ask(q))
                            {
                                rep.Messages.Add($"[RC CANT] {where}: {q} — pominięty.");
                                continue;
                            }
                        }
                        if (gb >= 0) { taken.Add(gb); rep.UsedTexts.Add(gb); }
                        if (thk >= 0) { taken.Add(thk); rep.UsedTexts.Add(thk); }
                        accepted.Add((t, d, root.u != null ? root.id : ObjectId.Null));
                    }
                    if (accepted.Count == 0) continue;

                    // uwaga Bartka 2026-10-22: obrys na rysunku BEZ zmian (języczek jak w GA) — języczki w rekordzie RC_CANT_TAB,
                    // obliczenia (GeometryHelper.GetPolylineVertices) biorą obrys bez nich
                    AddTabRecord(tr, db, pl, accepted.Select(a => CantileverRules.TabRec.Of(a.t.Shape)));
                    foreach (var (t, d, rootId) in accepted)
                    {
                        AddRect(tr, db, t.Shape, pl, d);
                        if (!rootId.IsNull) { MoveToLayer(tr, db, rootId); openPieces?.Remove(rootId); rep.Moved.Add(rootId); }
                        rep.Made++;
                        rep.Info.Add($"[RC CANT] {where}: wspornik {Label(d)}, wysięg {t.Shape.Depth:F0} — wyłączony z obrysu siatki.");
                    }
                }
                tr.Commit();
            }
            return rep;
        }

        /// <summary>
        /// Wsporniki z pierwszej wersji (rekord bez [8] / [9] — prostokąt tylko poza płytą): prostokąt przedłużony w głąb płyty
        /// o strefę przy palu (rekord [6]) albo CantileverRules.NoRootExtension — nakłada się na płytę jak strefa belki (uwaga Bartka 2026-10-21).
        /// </summary>
        public static int UpgradeOldStubs(Database db, List<string> messages)
        {
            int n = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl) || !IsStub(pl)) continue;
                    var d = Read(pl);
                    if (d == null || d.Ext > 0 || pl.NumberOfVertices != 4 || d.Dir.Length != 2) continue;
                    var v = Pts(pl);
                    bool ax = d.Dir[1] == 'X'; int sg = d.Dir[0] == '-' ? -1 : 1;
                    double Along((double X, double Y) q) => ax ? q.X : q.Y;
                    double end = sg > 0 ? v.Max(Along) : v.Min(Along), rootC = sg > 0 ? v.Min(Along) : v.Max(Along);
                    double ext = d.RootLen > 1.0 ? d.RootLen : CantileverRules.NoRootExtension;
                    pl.UpgradeOpen();
                    for (int i = 0; i < 4; i++)
                    {
                        if (Math.Abs(Along(v[i]) - rootC) > CantileverRules.Tol) continue;
                        var q = ax ? (v[i].X - sg * ext, v[i].Y) : (v[i].X, v[i].Y - sg * ext);
                        pl.SetPointAt(i, new Point2d(q.Item1, q.Item2));
                    }
                    d.Reach = Math.Abs(end - rootC);
                    d.Ext = ext;
                    Write(tr, db, pl, d);
                    n++;
                }
                tr.Commit();
            }
            if (n > 0) messages?.Add($"[RC CANT] wsporniki z poprzedniej wersji: {n} — strefa przedłużona w głąb płyty (nakłada się na płytę).");
            return n;
        }

        // ── opisy z rysunku (RC_CANTILEVER) ─────────────────────────────────

        /// <summary>Opisy z modelu (MText / DBText / MLeader — dowolna warstwa) z „GB-” albo „THK”.</summary>
        public static List<Txt> ModelTexts(Database db)
        {
            var res = new List<Txt>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    try
                    {
                        var e = tr.GetObject(id, OpenMode.ForRead);
                        List<string> lines = null; Point3d? p = null;
                        if (e is MText mt) { lines = GaImportEngine.TextLines(mt); p = GaImportEngine.MTextCenter(mt); }
                        else if (e is DBText dt) { lines = new List<string> { dt.TextString ?? "" }; p = dt.Position; }
                        else if (e is MLeader ml) { lines = GaImportEngine.MLeaderLines(ml); p = GaImportEngine.MLeaderTip(ml); }
                        if (lines == null || !p.HasValue) continue;
                        string t = string.Join("\n", lines);
                        if (CantileverRules.ParseGb(t) == null && !lines.Any(l => BeamGeometry.ParseThk(l) != null)) continue;
                        res.Add(new Txt { Text = t, X = p.Value.X, Y = p.Value.Y });
                    }
                    catch (System.Exception ex) { Log.Error("CantileverEngine.ModelTexts", ex); }
                }
                tr.Commit();
            }
            return res;
        }

        /// <summary>Otwarte polilinie SD-PILED-RAFT w modelu (kandydaci na strefę przy palu).</summary>
        public static List<ObjectId> OpenSlabPieces(Database db)
        {
            var res = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                    if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is Polyline pl
                        && string.Equals(pl.Layer, SlabLayer, StringComparison.OrdinalIgnoreCase) && !GeometryHelper.IsEffectivelyClosed(pl))
                        res.Add(id);
                tr.Commit();
            }
            return res;
        }
    }
}
