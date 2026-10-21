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
    ///     [7] kierunek wysięgu („+X” / „-X” / „+Y” / „-Y”). Bez powiązań handle (CloneRemap niepotrzebny); płyta = obrys, na którego
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
                              Dir = v.Length > 7 ? v[7].Value?.ToString() ?? "" : "" };
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
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, d.Dir ?? "")); // [7]
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
                if (taken.Contains(i) || CantileverRules.ParseGb(texts[i].Text) == null) continue;
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
                if (taken.Contains(i) || CantileverRules.ParseGb(texts[i].Text) != null) continue;
                if (!texts[i].Text.Split('\n').Any(l => BeamGeometry.ParseThk(l) != null)) continue;
                if (CantileverRules.InBox(box, texts[i].X, texts[i].Y, ThkSearch)) return i;
            }
            return -1;
        }

        private static Data Describe(CantileverRules.UShape stub, CantileverRules.UShape root, int gb, int thk, IList<Txt> texts)
        {
            var d = new Data { Width = stub.Width, Dir = CantileverRules.DirCode(stub.Out), RootLen = root?.Depth ?? 0 };
            if (gb >= 0)
            {
                var g = CantileverRules.ParseGb(texts[gb].Text).Value;
                d.Height = g.Height;
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

        private static List<(double X, double Y)> Pts(Polyline pl)
            => GeometryHelper.GetPolylineVertices(pl).Select(p => (p.X, p.Y)).ToList();

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
            var pts = new[] { u.A, u.C1, u.C2, u.B };
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
                    foreach (var t in tabs)
                    {
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

                    // obrys bez języczków — ta sama polilinia (handle — powiązania prętów i stref)
                    var clean = CantileverRules.RemoveTabs(pts, accepted.Select(a => a.t));
                    pl.UpgradeOpen();
                    int old = pl.NumberOfVertices;
                    for (int i = 0; i < clean.Count; i++) pl.AddVertexAt(old + i, new Point2d(clean[i].X, clean[i].Y), 0, 0, 0);
                    for (int i = 0; i < old; i++) pl.RemoveVertexAt(0);
                    pl.Closed = true;
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
