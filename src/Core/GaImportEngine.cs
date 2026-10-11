using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// RC_PRZYGOTUJ_GA — przygotowanie rysunku RC (plik „default”) z rysunku GA:
    ///   • płyty w pliku GA: opis „PLOT …” / „GARAGE …” (SD-Text, `PlotLabelRules.IsPlotLabel`) wewnątrz obrysu SD-PILED-RAFT,
    ///   • kopiowane tylko: obrys + linia uskoku (SD-PILED-RAFT, także door threshold i linie stopni),
    ///     pale (koła SD-Pile), teksty SD-Text poza „NIB TOC=…” (opisy belek itp. zostają),
    ///     podpisy pali (SD-Pile Text*) — TYLKO na rzucie górnym,
    ///   • ramka opisu płyty przebudowana: zostaje nr plotu, SSL i grubość płyty,
    ///   • dwa rzuty (dolny i górny) obok siebie; tytuły rzutów (S02_TEXT035) pod płytą, na środku, ~2400 mm
    ///     od rysunku płyty; ramki szablonów rebar_bottom / rebar_top po bokach (z zawartością),
    ///   • otwarte kawałki linii (np. uskok narysowany dwiema polilinami) łączone w zamkniętą polilinię,
    ///   • XData RC_GA [PLOT, B|T] — ponowne wywołanie zastępuje poprzednio wstawioną płytę,
    ///   • strefy pogrubień (SD-Thickening-Zone, zamknięte) → warstwa RC-BEAM + RC_BEAM (H z opisu „450THK”, kontrola
    ///     SSL − NIB TOC + 150); ponowne wywołanie usuwa też pręty belek starych stref (sekcja 4i);
    ///   • K8: GA bez SD-Thickening-Zone (C12) — strefy z opisów „xxxTHK” (MText / MLeader na SD-Text) przy krawędziach nibu
    ///     (BeamGeometry.ZonesFromThk: linia strefy z GA albo 600 od linii uskoku, odrzucenia z komunikatem).
    /// Wymiary, architektura, poziomy, kanalizacja itp. nie są kopiowane.
    /// </summary>
    public static class GaImportEngine
    {
        public const string XApp = "RC_GA";
        public const string SlabLayer = "SD-PILED-RAFT";
        public const double RegionMargin = 1500.0;   // wokół obrysu: door threshold, opisy przy krawędzi
        public const double TitleGap     = 2400.0;   // od rysunku płyty do tytułu rzutu
        public const double PlanGap      = 7500.0;   // między rzutami i między rzutem a ramką szablonów
        public const double LabelMargin  = 70.0;     // ramka opisu płyty wokół tekstu
        private const double FrameWidth  = 8065.0;   // ramka szablonów (gdy brak w rysunku)
        private const double FrameHeight = 20288.0;

        public sealed class GaPlot
        {
            public string Label;                  // „PLOT 9-10”
            public ObjectId LabelId;              // MText/DBText opisu płyty
            public ObjectId LabelBoxId;           // ramka opisu (SD-Text), może być Null
            public ObjectId OutlineId;            // obrys zewnętrzny
            public Extents3d Outline;
            public Extents3d Region;              // obrys + margines
            public override string ToString()
                => $"{Label}   —   {Outline.MaxPoint.X - Outline.MinPoint.X:F0} × {Outline.MaxPoint.Y - Outline.MinPoint.Y:F0} mm";
        }

        public sealed class Source : IDisposable
        {
            public Database Db;
            public List<GaPlot> Plots = new List<GaPlot>();
            public void Dispose() { Db?.Dispose(); Db = null; }
        }

        // ----------------------------------------------------------------
        // Odczyt GA
        // ----------------------------------------------------------------

        public static Source ReadSource(string path, List<string> warnings)
        {
            var src = new Source { Db = new Database(false, true) };
            try
            {
                if (Path.GetExtension(path).Equals(".dwg", StringComparison.OrdinalIgnoreCase))
                    src.Db.ReadDwgFile(path, FileShare.Read, true, null);
                else
                    src.Db.DxfIn(path, null);

                var outlines = new List<(ObjectId id, List<Point2d> v, Extents3d ext, double area)>();
                var labels = new List<(ObjectId id, string label, Point3d pt)>();
                var boxes = new List<(ObjectId id, Extents3d ext)>();
                using (var tr = src.Db.TransactionManager.StartOpenCloseTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(src.Db), OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                        string layer = e.Layer ?? "";
                        if (layer.Equals(SlabLayer, StringComparison.OrdinalIgnoreCase) && e is Polyline pl
                            && pl.NumberOfVertices >= 3 && GeometryHelper.IsEffectivelyClosed(pl))
                        {
                            var v = GeometryHelper.GetPolylineVertices(pl);
                            outlines.Add((id, v, Ext(v), Math.Abs(Area(v))));
                        }
                        else if (layer.Equals("SD-Text", StringComparison.OrdinalIgnoreCase))
                        {
                            if (e is MText mt && PlotLabelRules.IsPlotLabel(FirstLine(mt)))
                                labels.Add((id, FirstLine(mt), mt.Location));
                            else if (e is DBText dt && PlotLabelRules.IsPlotLabel(dt.TextString))
                                labels.Add((id, dt.TextString.Trim(), dt.Position));
                            else if (e is Polyline bp && bp.NumberOfVertices >= 4 && GeometryHelper.IsEffectivelyClosed(bp))
                                boxes.Add((id, Ext(GeometryHelper.GetPolylineVertices(bp))));
                        }
                    }
                    tr.Commit();
                }

                foreach (var (id, label, pt) in labels)
                {
                    var p2 = new Point2d(pt.X, pt.Y);
                    // największy obrys zawierający opis = obrys zewnętrzny (linia uskoku też go zawiera)
                    var hit = outlines.Where(o => GeometryHelper.IsPointInsidePolygon(o.v, p2))
                                      .OrderByDescending(o => o.area).FirstOrDefault();
                    if (hit.v == null)
                    {
                        warnings.Add($"{label}: opis poza obrysem {SlabLayer} — pominięto.");
                        continue;
                    }
                    if (src.Plots.Any(pp => string.Equals(pp.Label, label, StringComparison.OrdinalIgnoreCase)))
                    {
                        warnings.Add($"{label}: opis występuje więcej niż raz — użyto pierwszego.");
                        continue;
                    }
                    var box = boxes.Where(b => b.ext.MinPoint.X <= pt.X + 200 && b.ext.MaxPoint.X >= pt.X - 200
                                            && b.ext.MinPoint.Y <= pt.Y + 200 && b.ext.MaxPoint.Y >= pt.Y - 200)
                                   .OrderBy(b => (b.ext.MaxPoint.X - b.ext.MinPoint.X) * (b.ext.MaxPoint.Y - b.ext.MinPoint.Y))
                                   .FirstOrDefault();
                    var o = hit.ext;
                    src.Plots.Add(new GaPlot
                    {
                        Label = label, LabelId = id, LabelBoxId = box.id, OutlineId = hit.id, Outline = o,
                        Region = new Extents3d(new Point3d(o.MinPoint.X - RegionMargin, o.MinPoint.Y - RegionMargin, 0),
                                               new Point3d(o.MaxPoint.X + RegionMargin, o.MaxPoint.Y + RegionMargin, 0))
                    });
                }
                if (src.Plots.Count == 0)
                    throw new InvalidOperationException(
                        $"W pliku nie znaleziono płyt (opis „PLOT …” / „GARAGE …” na SD-Text wewnątrz zamkniętego obrysu {SlabLayer}).");
                // Najpierw płyty PLOT, potem garaże (domyślnie wybierany pierwszy PLOT, nie GARAGE)
                src.Plots = src.Plots.OrderBy(p => p.Label.TrimStart().StartsWith("GARAGE", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                                     .ThenBy(p => p.Label, StringComparer.OrdinalIgnoreCase).ToList();
                return src;
            }
            catch
            {
                src.Dispose();
                throw;
            }
        }

        // ----------------------------------------------------------------
        // Import
        // ----------------------------------------------------------------

        public sealed class Result
        {
            public int Bottom, Top, Joined;
            /// <summary>Rzuty, w których automat domknął otwartą linię uskoku (NibAutoClose).</summary>
            public int NibClosed;
            /// <summary>Strefy belek (SD-Thickening-Zone → RC-BEAM) na rzucie dolnym i ich wysokości H (0 = nieznana).</summary>
            public int Beams;
            public List<int> BeamHeights = new List<int>();
            /// <summary>Decyzja 2026-10-11 (punkt D): nowe strefy z polilinii ręcznych (RC-BEAM-MANUAL) — oba rzuty razem.</summary>
            public int ManualBeams;
            /// <summary>Obrysy płyty wstawione na rzutach (dolny / górny; Null — brak).</summary>
            public ObjectId OutlineBottom = ObjectId.Null, OutlineTop = ObjectId.Null;
        }

        /// <summary>Warstwa stref pogrubień (belek) w GA — zamknięta polilinia → strefa RC-BEAM (oba rzuty).</summary>
        public const string ThickeningLayer = "SD-Thickening-Zone";

        /// <summary>Dane GA do kontroli wysokości belki: SSL z opisu płyty i teksty „NIB TOC=…” (układ GA, obszar płyty).</summary>
        private sealed class BeamGaContext
        {
            public double? Ssl;
            /// <summary>K8: grubość płyty z opisu („225mm THK SLAB”) — opisy „xxxTHK” ≤ grubość pomijane.</summary>
            public double? SlabThickness;
            public List<(Point2d p, double toc)> Tocs = new List<(Point2d, double)>();
        }

        public static Result Import(Document doc, Source src, GaPlot plot, List<string> warnings)
        {
            var db = doc.Database;
            var res = new Result();

            // 1. Co kopiujemy (źródło) + zakres rysunku płyty
            var common = new List<ObjectId>();
            var pileLabels = new List<ObjectId>();
            var beamCtx = new BeamGaContext();
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            using (var tr = src.Db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(src.Db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    string layer = e.Layer ?? "";
                    Extents3d x;
                    try { x = e.GeometricExtents; }
                    catch { continue; }
                    var c = new Point3d((x.MinPoint.X + x.MaxPoint.X) / 2, (x.MinPoint.Y + x.MaxPoint.Y) / 2, 0);
                    if (!InsideXY(plot.Region, c)) continue;

                    // Belki: „NIB TOC=…” (nie kopiowane) do kontroli krzyżowej wysokości SSL − NIB TOC + 150
                    if (layer.Equals("SD-Text", StringComparison.OrdinalIgnoreCase))
                    {
                        string tt = e is MText tm ? string.Join(" ", TextLines(tm)) : e is DBText td ? td.TextString : null;
                        if (BeamGeometry.ParseNibToc(tt) is double toc)
                            beamCtx.Tocs.Add((new Point2d(c.X, c.Y), toc));
                        if (id == plot.LabelId && e is MText lm)
                        {
                            foreach (var l in TextLines(lm))
                                if (SectionPlanner.ParseSsl(l) is string ssl && NumberParser.TryParseDouble(ssl, out double sv)) { beamCtx.Ssl = sv; break; }
                            foreach (var l in TextLines(lm))
                                if (SectionPlanner.ParseThickness(l) is double st) { beamCtx.SlabThickness = st; break; }
                        }
                    }

                    int kind = Classify(e, layer, id, plot);
                    if (kind == 0) continue;
                    if (kind == 2) { pileLabels.Add(id); continue; }
                    common.Add(id);
                    x0 = Math.Min(x0, x.MinPoint.X); y0 = Math.Min(y0, x.MinPoint.Y);
                    x1 = Math.Max(x1, x.MaxPoint.X); y1 = Math.Max(y1, x.MaxPoint.Y);
                }
                tr.Commit();
            }
            if (common.Count == 0) throw new InvalidOperationException($"{plot.Label}: nic do skopiowania.");
            double w = x1 - x0;

            using (doc.LockDocument())
            {
                EraseOld(db);

                // 2. Tytuły i ramki szablonów w rysunku docelowym
                var (titleB, titleT, frameB, frameT) = FindTemplate(db);
                Extents3d? tbExt = ExtOf(db, titleB), ttExt = ExtOf(db, titleT);
                if (!tbExt.HasValue) warnings.Add("Brak tytułu rzutu dolnego (…BOTTOM LAYER) — rzut wstawiony w punkcie (0,0).");
                if (!ttExt.HasValue) warnings.Add("Brak tytułu rzutu górnego (…TOP LAYER) — bez tytułu.");

                // Rzut dolny: środek nad tytułem, dół rysunku płyty TitleGap nad tytułem
                double cxB, botB;
                if (tbExt.HasValue)
                {
                    cxB = (tbExt.Value.MinPoint.X + tbExt.Value.MaxPoint.X) / 2;
                    botB = tbExt.Value.MaxPoint.Y + TitleGap;
                }
                else { cxB = w / 2; botB = 0; }
                var dB = new Vector3d(cxB - (x0 + x1) / 2, botB - y0, 0);
                var dT = dB + new Vector3d(w + PlanGap, 0, 0);

                // 3. Kopie
                res.Bottom = CloneInto(db, src, common, dB, plot, "B", warnings, ref res.Joined, ref res.NibClosed, beamCtx, res);
                var topIds = new List<ObjectId>(common); topIds.AddRange(pileLabels);
                res.Top = CloneInto(db, src, topIds, dT, plot, "T", warnings, ref res.Joined, ref res.NibClosed, beamCtx, res);

                // 4. Tytuł górny pod rzutem górnym (ten sam poziom co dolny), ramki szablonów po bokach
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    double planB0 = x0 + dB.X, planT1 = x1 + dT.X;
                    if (ttExt.HasValue && tbExt.HasValue && tr.GetObject(titleT, OpenMode.ForWrite) is Entity tt)
                    {
                        double cxT = (x0 + x1) / 2 + dT.X;
                        tt.TransformBy(Matrix3d.Displacement(new Vector3d(
                            cxT - (ttExt.Value.MinPoint.X + ttExt.Value.MaxPoint.X) / 2,
                            tbExt.Value.MaxPoint.Y - ttExt.Value.MaxPoint.Y, 0)));
                    }
                    double planTop = y1 + dB.Y;
                    PlaceFrame(tr, db, frameB, "rebar_bottom", warnings, top: planTop, rightEdge: planB0 - PlanGap);
                    PlaceFrame(tr, db, frameT, "rebar_top", warnings, top: planTop, leftEdge: planT1 + PlanGap);
                    // decyzja 2026-10-11 (punkt D): warstwa do ręcznego rysowania belek
                    BeamStore.EnsureManualLayer(tr, db);
                    tr.Commit();
                }

                // 5. Polilinie ręczne belek (RC-BEAM-MANUAL) w obrysach wstawionej płyty → strefy RC-BEAM na obu rzutach (polilinie zostają)
                try
                {
                    var polys = new List<List<Point2d>>();
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        foreach (var oid in new[] { res.OutlineBottom, res.OutlineTop })
                            if (!oid.IsNull && !oid.IsErased && tr.GetObject(oid, OpenMode.ForRead) is Polyline opl)
                                polys.Add(GeometryHelper.GetPolylineVertices(opl));
                        tr.Commit();
                    }
                    if (polys.Count > 0)
                        res.ManualBeams = AutoRebarEngine.ConvertManualBeams(db, pt => polys.Any(pp => GeometryHelper.IsPointInsidePolygon(pp, pt)),
                                                                             m => warnings.Add("[RC BEAM] " + m));
                }
                catch (System.Exception ex)
                {
                    Log.Error("GaImport.Import.ManualBeams", ex);
                    warnings.Add($"[RC BEAM] Błąd polilinii ręcznych belek — {ex.Message}");
                }
            }
            return res;
        }

        /// <summary>0 = pomiń, 1 = oba rzuty, 2 = tylko rzut górny (podpisy pali).</summary>
        private static int Classify(Entity e, string layer, ObjectId id, GaPlot plot)
        {
            if (layer.Equals(SlabLayer, StringComparison.OrdinalIgnoreCase))
            {
                if (e is MText m && m.TextHeight >= 1000) return 0;   // notatki arkusza
                return e is Polyline || e is Line || e is Arc || e is MText || e is DBText ? 1 : 0;
            }
            if (layer.Equals("SD-Pile", StringComparison.OrdinalIgnoreCase))
                return e is Circle ? 1 : 0;
            if (layer.StartsWith("SD-Pile Text", StringComparison.OrdinalIgnoreCase))
                return e is DBText || e is MText ? 2 : 0;
            if (layer.Equals("SD-Text", StringComparison.OrdinalIgnoreCase))
            {
                if (id == plot.LabelId || id == plot.LabelBoxId) return 1;   // przebudowywane po skopiowaniu
                string t = e is MText mt ? string.Join(" ", TextLines(mt)) : e is DBText dt ? dt.TextString : null;
                if (t != null && Regex.IsMatch(t, @"NIB\s*TOC", RegexOptions.IgnoreCase)) return 0;
                // K8: odnośnik (MLeader) z opisem belki („525THK”, „GB-600x450mm deep”) — oba rzuty; inne odnośniki jak dotąd pomijane
                if (e is MLeader ml) return IsBeamText(MLeaderLines(ml)) ? 1 : 0;
                return e is MText || e is DBText || e is Polyline || e is Line ? 1 : 0;
            }
            // Strefa pogrubienia (belka): zamknięta polilinia → oba rzuty, w CloneInto warstwa RC-BEAM + RC_BEAM
            if (layer.Equals(ThickeningLayer, StringComparison.OrdinalIgnoreCase))
                return e is Polyline zp && zp.NumberOfVertices >= 3 && GeometryHelper.IsEffectivelyClosed(zp) ? 1 : 0;
            return 0;
        }

        private static int CloneInto(Database db, Source src, List<ObjectId> ids, Vector3d d, GaPlot plot,
                                     string view, List<string> warnings, ref int joined, ref int nibClosed,
                                     BeamGaContext beamCtx = null, Result result = null)
        {
            var col = new ObjectIdCollection(ids.ToArray());
            // using: mapa żyje do końca metody — bez tego JIT (Release) może ją sfinalizować w trakcie foreach po IdMappingEnumerator
            // (SEHException w MoveNext, test BRX G528S w pełnym przebiegu run-planc-beam)
            using var idMap = new IdMapping();
            var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
            src.Db.WblockCloneObjects(col, msId, idMap, DuplicateRecordCloning.Ignore, false);

            int n = 0;
            ObjectId label = ObjectId.Null, box = ObjectId.Null, outline = ObjectId.Null;
            var openPl = new List<ObjectId>();
            var nibPieces = new List<ObjectId>();   // kawałki linii uskoku (Polyline / Line / Arc na SlabLayer)
            var textClones = new List<ObjectId>();
            var zoneClones = new List<ObjectId>();  // strefy pogrubień (belki)
            var thkTexts = new List<(Point2d ins, Point2d mid, List<string> lines)>();   // teksty SD-Text (wysokość belki „450THK”)
            var thkCands = new List<BeamGeometry.ThkText>();   // K8: opisy belek (MText — środek obwiedni, MLeader — grot)
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureApp(tr, db);
                var move = Matrix3d.Displacement(d);
                foreach (IdPair pair in idMap)
                {
                    if (!pair.IsCloned || !pair.IsPrimary || !ids.Contains(pair.Key)) continue;
                    if (!(tr.GetObject(pair.Value, OpenMode.ForWrite, false, true) is Entity e)) continue;
                    if (e.OwnerId != msId) continue;
                    e.TransformBy(move);
                    Tag(e, plot.Label, view);
                    n++;
                    if ((e.Layer ?? "").Equals(ThickeningLayer, StringComparison.OrdinalIgnoreCase) && e is Polyline) zoneClones.Add(e.ObjectId);
                    if ((e.Layer ?? "").Equals("SD-Text", StringComparison.OrdinalIgnoreCase) && (e is DBText || e is MText))
                    {
                        var lines = e is MText tm ? TextLines(tm) : new List<string> { ((DBText)e).TextString ?? "" };
                        if (lines.Any(l => BeamGeometry.ParseThk(l) != null))
                        {
                            var ins = e is MText im ? im.Location : ((DBText)e).Position;
                            Point2d mid = new Point2d(ins.X, ins.Y);
                            try { var ex = e.GeometricExtents; mid = new Point2d((ex.MinPoint.X + ex.MaxPoint.X) / 2, (ex.MinPoint.Y + ex.MaxPoint.Y) / 2); }
                            catch (System.Exception gex) { Log.Error("GaImport.CloneInto.ThkText", gex); }
                            thkTexts.Add((new Point2d(ins.X, ins.Y), mid, lines));
                        }
                        if (IsBeamText(lines))
                        {
                            Point3d c3 = e is MText im2 ? MTextCenter(im2) : ((DBText)e).Position;
                            if (!(e is MText))
                                try { var ex = e.GeometricExtents; c3 = new Point3d((ex.MinPoint.X + ex.MaxPoint.X) / 2, (ex.MinPoint.Y + ex.MaxPoint.Y) / 2, 0); }
                                catch (System.Exception gex) { Log.Error("GaImport.CloneInto.ThkMid", gex); }
                            thkCands.Add(new BeamGeometry.ThkText { Text = string.Join("\n", lines), X = c3.X, Y = c3.Y });
                        }
                    }
                    if ((e.Layer ?? "").Equals("SD-Text", StringComparison.OrdinalIgnoreCase) && e is MLeader mle)
                    {
                        var lines = MLeaderLines(mle);
                        var tip = MLeaderTip(mle);
                        if (IsBeamText(lines) && tip.HasValue)
                            thkCands.Add(new BeamGeometry.ThkText { Text = string.Join("\n", lines), X = tip.Value.X, Y = tip.Value.Y, Leader = true });
                    }
                    if (pair.Key == plot.LabelId) label = e.ObjectId;
                    else if (e is MText && (e.Layer ?? "").Equals("SD-Text", StringComparison.OrdinalIgnoreCase))
                        textClones.Add(e.ObjectId);
                    else if (pair.Key == plot.LabelBoxId) box = e.ObjectId;
                    else if (pair.Key == plot.OutlineId)
                    {
                        outline = e.ObjectId;
                        if (result != null) { if (view == "B") result.OutlineBottom = outline; else result.OutlineTop = outline; }
                    }
                    else if ((e.Layer ?? "").Equals(SlabLayer, StringComparison.OrdinalIgnoreCase))
                    {
                        if (e is Polyline pl && !GeometryHelper.IsEffectivelyClosed(pl)) openPl.Add(e.ObjectId);
                        if (e is Polyline || e is Line || e is Arc) nibPieces.Add(e.ObjectId);
                    }
                }

                // Opis płyty: tylko nr plotu, SSL i grubość; ramka dopasowana do tekstu
                if (!label.IsNull) label = RebuildLabel(tr, msId, label, box, textClones, plot.Label, view, warnings);
                tr.Commit();
            }

            // Nib: NAJPIERW automat domykania otwartej linii uskoku krawędzią obrysu (NibAutoClose; tylko gdy płyta nie ma
            // zamkniętego nibu), DOPIERO POTEM JoinLoops na pozostałych kawałkach — inaczej JoinLoops skleja linię uskoku
            // z linią strefy 760 mm w zamknięty pas, którego NibDetector nie uznaje (PLOT 319; POPRAWKI_WZORCA 23).
            if (!outline.IsNull)
            {
                var msgs = new List<string>();
                try
                {
                    if (NibAutoClose.TryClose(db, outline, nibPieces, e => Tag(e, plot.Label, view), msgs)) nibClosed++;
                    else foreach (var m in msgs) warnings.Add($"{m} ({(view == "B" ? "rzut dolny" : "rzut górny")})");
                }
                catch (System.Exception ex)
                {
                    Log.Error("GaImport.CloneInto.Nib", ex);
                    warnings.Add($"Nib: błąd domykania linii uskoku — {ex.Message} ({(view == "B" ? "rzut dolny" : "rzut górny")})");
                }
            }

            // Belki (po domknięciu nibu, przed JoinLoops): strefa SD-Thickening-Zone → warstwa RC-BEAM + RC_BEAM (H z opisu)
            if (zoneClones.Count > 0)
            {
                try
                {
                    int nz = ProcessBeamZones(db, zoneClones, outline, thkTexts, beamCtx, d, view, warnings);
                    if (view == "B" && result != null) result.Beams += nz;
                }
                catch (System.Exception ex)
                {
                    Log.Error("GaImport.CloneInto.Beam", ex);
                    warnings.Add($"[RC BEAM] Błąd strefy belki — {ex.Message} ({(view == "B" ? "rzut dolny" : "rzut górny")})");
                }
                if (view == "B" && result != null && beamCtx != null)
                    foreach (var zid in zoneClones) result.BeamHeights.Add(HeightOf(db, zid));
            }

            // Otwarte kawałki obrysu/uskoku tworzące pętlę → jedna zamknięta polilinia (NibDetector)
            openPl.RemoveAll(id => id.IsErased);
            if (openPl.Count > 0)
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    joined += JoinLoops(tr, msId, openPl, plot.Label, view);
                    tr.Commit();
                }
            }

            // K8: GA bez stref SD-Thickening-Zone — strefy belek z opisów „xxxTHK” (po JoinLoops: obrys zewnętrzny z otwartych kawałków
            // jest już zamknięty — NibDetector widzi nib). Holbrook (strefy z GA) — bez zmian.
            if (zoneClones.Count == 0 && thkCands.Count > 0 && !outline.IsNull)
            {
                try
                {
                    var made = ProcessBeamTexts(db, outline, thkCands, nibPieces, beamCtx, d, plot, view, warnings);
                    if (view == "B" && result != null)
                    {
                        result.Beams += made.Count;
                        foreach (var zid in made) result.BeamHeights.Add(HeightOf(db, zid));
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Error("GaImport.CloneInto.BeamTexts", ex);
                    warnings.Add($"[RC BEAM] Błąd stref z opisów „…THK” — {ex.Message} ({(view == "B" ? "rzut dolny" : "rzut górny")})");
                }
            }
            return n;
        }

        /// <summary>
        /// Strefy pogrubień (belek) skopiowane z GA: warstwa RC-BEAM, rekord RC_BEAM ([6] = obrys płyty tego rzutu), wysokość H
        /// z opisu „450THK” w strefie (DBText / MText — linie TextLines). Kontrola krzyżowa: SSL − NIB TOC (tekst najbliżej
        /// strefy, z GA) + 150 — różnica → ostrzeżenie; brak opisu → H z kontroli (ostrzeżenie); brak obu → H = 0 (ustaw w RC_BEAM).
        /// H spoza siatki 75 → przyjęta z ostrzeżeniem (1b.10). Ostrzeżenia tylko dla rzutu dolnego (bez powtórzeń). Zwraca liczbę stref.
        /// </summary>
        private static int ProcessBeamZones(Database db, List<ObjectId> zones, ObjectId outlineId,
            List<(Point2d ins, Point2d mid, List<string> lines)> texts, BeamGaContext ctx, Vector3d d, string view, List<string> warnings)
        {
            bool warn = view == "B";
            int made = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                BeamStore.EnsureLayer(tr, db, ThickeningLayer);
                List<(double X, double Y)> outl = null;
                string slabHandle = "";
                if (!outlineId.IsNull && tr.GetObject(outlineId, OpenMode.ForRead) is Polyline opl)
                {
                    outl = GeometryHelper.SquaredOrSame(GeometryHelper.GetPolylineVertices(opl)).Select(p => (p.X, p.Y)).ToList();
                    slabHandle = opl.Handle.ToString();
                }
                else if (warn) warnings.Add("[RC BEAM] Brak obrysu płyty — strefa belki bez powiązania z płytą.");

                int k = 0;
                foreach (var zid in zones)
                {
                    k++;
                    if (!(tr.GetObject(zid, OpenMode.ForWrite) is Polyline pl)) continue;
                    pl.Layer = BeamStore.LayerName;
                    var verts = GeometryHelper.GetPolylineVertices(pl);
                    var vxy = verts.Select(p => (p.X, p.Y)).ToList();

                    // H z opisu w strefie (punkt wstawienia albo środek tekstu)
                    var hs = new List<int>();
                    foreach (var (ins, mid, lines) in texts)
                    {
                        if (!GeometryHelper.IsPointInsidePolygon(verts, ins) && !GeometryHelper.IsPointInsidePolygon(verts, mid)) continue;
                        foreach (var l in lines) if (BeamGeometry.ParseThk(l) is int h) hs.Add(h);
                    }
                    int? hText = hs.Count > 0 ? hs.GroupBy(h => h).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key : (int?)null;
                    string zName = $"strefa {k}";
                    if (warn && hs.Distinct().Count() > 1)
                        warnings.Add($"[RC BEAM] {zName}: różne wysokości w opisach ({string.Join("/", hs.Distinct())}) — przyjęto {hText}.");

                    // Kontrola krzyżowa: SSL − NIB TOC + 150 (NIB TOC najbliżej strefy; współrzędne GA = rzut − przesunięcie)
                    int? hCheck = null;
                    if (ctx != null && ctx.Ssl.HasValue && ctx.Tocs.Count > 0)
                    {
                        var srcPoly = verts.Select(p => new Point2d(p.X - d.X, p.Y - d.Y)).ToList();
                        var near = ctx.Tocs.OrderBy(t => DistToPoly(srcPoly, t.p)).First();
                        hCheck = BeamGeometry.HeightFromLevels(ctx.Ssl.Value, near.toc);
                    }

                    int H = hText ?? hCheck ?? 0;
                    if (warn)
                    {
                        if (hText.HasValue && hCheck.HasValue && Math.Abs(hText.Value - hCheck.Value) > 1)
                            warnings.Add($"[RC BEAM] {zName}: wysokość z opisu {hText} ≠ SSL − NIB TOC + 150 = {hCheck} — sprawdź (przyjęto {H}).");
                        else if (!hText.HasValue && hCheck.HasValue)
                            warnings.Add($"[RC BEAM] {zName}: brak opisu „…THK” w strefie — wysokość z SSL − NIB TOC + 150 = {hCheck}.");
                        else if (!hText.HasValue)
                            warnings.Add($"[RC BEAM] {zName}: brak wysokości (opis „…THK” ani NIB TOC) — ustaw H w RC_BEAM.");
                        if (H > 0 && H % 75 != 0)
                            warnings.Add($"[RC BEAM] {zName}: wysokość {H} spoza siatki 75 mm — przyjęta (do potwierdzenia).");
                    }

                    var an = BeamGeometry.Analyze(vxy, outl);
                    if (warn)
                        foreach (var m in an.Messages) warnings.Add($"[RC BEAM] {zName}: {m}");
                    var p = new BeamParams { Height = H };
                    if (an.TypicalWidth > 0) p.Width = an.TypicalWidth;
                    BeamStore.Write(tr, db, pl, p, slabHandle);
                    made++;
                }
                tr.Commit();
            }
            return made;
        }

        /// <summary>
        /// K8: środek tekstu MText z obwiedni rzeczywistego tekstu (ActualWidth / ActualHeight — nie szerokości ramki, która bywa duża),
        /// punktu zaczepienia (Attachment) i obrotu.
        /// </summary>
        private static Point3d MTextCenter(MText mt)
        {
            try
            {
                double w = mt.ActualWidth, h = mt.ActualHeight;
                string a = mt.Attachment.ToString();
                double ox = a.EndsWith("Left") ? 0 : a.EndsWith("Center") ? w / 2 : w;        // od lewej krawędzi do punktu zaczepienia
                double oy = a.StartsWith("Top") ? 0 : a.StartsWith("Middle") ? h / 2 : h;      // od górnej krawędzi do punktu zaczepienia
                double lx = w / 2 - ox, ly = -(h / 2 - oy);                                     // środek względem punktu zaczepienia
                double c = Math.Cos(mt.Rotation), sn = Math.Sin(mt.Rotation);
                return new Point3d(mt.Location.X + lx * c - ly * sn, mt.Location.Y + lx * sn + ly * c, 0);
            }
            catch (System.Exception ex) { Log.Error("GaImport.MTextCenter", ex); return mt.Location; }
        }

        /// <summary>K8: linie opisu belki („…THK” z H albo „GB-…”).</summary>
        private static bool IsBeamText(List<string> lines)
            => lines != null && lines.Any(l => BeamGeometry.ParseThk(l) != null || Regex.IsMatch(l, @"\bGB\s*-", RegexOptions.IgnoreCase));

        /// <summary>K8: linie tekstu odnośnika (MLeader z treścią MText); pusta lista gdy brak.</summary>
        private static List<string> MLeaderLines(MLeader ml)
        {
            try
            {
                if (ml.ContentType != ContentType.MTextContent) return new List<string>();
                using (var mt = ml.MText) return mt != null ? TextLines(mt) : new List<string>();
            }
            catch (System.Exception ex) { Log.Error("GaImport.MLeaderLines", ex); return new List<string>(); }
        }

        /// <summary>K8: grot pierwszego odnośnika (pierwszy wierzchołek pierwszej linii); null gdy brak.</summary>
        private static Point3d? MLeaderTip(MLeader ml)
        {
            try
            {
                var leaders = ml.GetLeaderIndexes();
                if (leaders == null || leaders.Count == 0) return null;
                var lines = ml.GetLeaderLineIndexes((int)leaders[0]);
                if (lines == null || lines.Count == 0) return null;
                return ml.GetFirstVertex((int)lines[0]);
            }
            catch (System.Exception ex) { Log.Error("GaImport.MLeaderTip", ex); return null; }
        }

        /// <summary>
        /// K8: strefy belek z opisów „xxxTHK” (GA bez SD-Thickening-Zone): krawędzie linii uskoku nibu płyty (NibDetector na obrysie
        /// po JoinLoops), linie strefy z otwartych kawałków SD-PILED-RAFT, BeamGeometry.ZonesFromThk → polilinie RC-BEAM + RC_BEAM
        /// ([6] = obrys zewnętrzny rzutu), znacznik RC_GA (ponowne Prepare GA je usuwa). Kontrola krzyżowa SSL − NIB TOC + 150 jak dla
        /// stref z GA; NIB TOC wskazujący pogrubienie bez opisu — tylko ostrzeżenie. Komunikaty tylko dla rzutu dolnego.
        /// </summary>
        private static List<ObjectId> ProcessBeamTexts(Database db, ObjectId outlineId, List<BeamGeometry.ThkText> texts, List<ObjectId> pieces,
                                                       BeamGaContext ctx, Vector3d d, GaPlot plot, string view, List<string> warnings)
        {
            bool warn = view == "B";
            var made = new List<ObjectId>();
            var nib = NibDetector.Detect(db, outlineId);
            var edges = new List<BeamGeometry.ThkStepEdge>();
            List<Point2d> outer;
            ObjectId slabId;
            var lines = new List<((double X, double Y) A, (double X, double Y) B)>();
            if (nib != null)
            {
                outer = GeometryHelper.SquaredOrSame(nib.Outer);
                slabId = nib.OuterId;
                var inner = GeometryHelper.SquaredOrSame(nib.Inner);
                foreach (var ne in nib.Edges)
                    foreach (var f in GeometryHelper.EnumerateAxisAlignedEdges(inner))
                    {
                        bool v = f.Orientation == GeometryHelper.EdgeOrientation.Vertical;
                        if (v != ne.Vertical || (!v && f.Orientation != GeometryHelper.EdgeOrientation.Horizontal)) continue;
                        double c = v ? f.Start.X : f.Start.Y;
                        if (Math.Abs(c - ne.InnerCoord) > 1.0) continue;
                        double lo = v ? Math.Min(f.Start.Y, f.End.Y) : Math.Min(f.Start.X, f.End.X);
                        double hi = v ? Math.Max(f.Start.Y, f.End.Y) : Math.Max(f.Start.X, f.End.X);
                        if (Math.Min(hi, ne.Hi) - Math.Max(lo, ne.Lo) <= 1.0) continue;
                        if (edges.Any(x => x.Vertical == v && Math.Abs(x.C - c) < 1.0 && Math.Abs(x.Lo - lo) < 1.0 && Math.Abs(x.Hi - hi) < 1.0)) continue;
                        edges.Add(new BeamGeometry.ThkStepEdge { Vertical = v, C = c, Inward = ne.InwardSign, Lo = lo, Hi = hi });
                    }
            }
            else
            {
                // GA C12: zamknięty obrys płyty = linia uskoku, krawędź zewnętrzna nibu — otwarte kawałki 50–255 mm na zewnątrz (nie
                // domknięte przez JoinLoops). Krawędź obrysu z równoległym kawałkiem na zewnątrz na ≥ 50 % długości = krawędź z nibem.
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    outer = tr.GetObject(outlineId, OpenMode.ForRead) is Polyline opl
                        ? GeometryHelper.SquaredOrSame(GeometryHelper.GetPolylineVertices(opl)) : new List<Point2d>();
                    tr.Commit();
                }
                slabId = outlineId;
                // kawałki otwarte i pętle domknięte przez JoinLoops (np. krawędź zewnętrzna razem z belką gruntową GB) tego rzutu
                var segs = PieceSegments(db, pieces);
                segs.AddRange(TaggedSlabSegments(db, outlineId, plot.Label, view));
                foreach (var f in GeometryHelper.EnumerateAxisAlignedEdges(outer.Concat(outer.Take(1)).ToList()))
                {
                    bool v = f.Orientation == GeometryHelper.EdgeOrientation.Vertical;
                    if (!v && f.Orientation != GeometryHelper.EdgeOrientation.Horizontal) continue;
                    double c = v ? f.Start.X : f.Start.Y;
                    double lo = v ? Math.Min(f.Start.Y, f.End.Y) : Math.Min(f.Start.X, f.End.X);
                    double hi = v ? Math.Max(f.Start.Y, f.End.Y) : Math.Max(f.Start.X, f.End.X);
                    double mid = (lo + hi) / 2;
                    int inward = GeometryHelper.IsPointInsidePolygon(outer, v ? new Point2d(c + 1, mid) : new Point2d(mid, c + 1)) ? 1 : -1;
                    double cover = 0;
                    foreach (var (a, b) in segs)
                    {
                        bool sv = Math.Abs(a.X - b.X) < 1.0, sh = Math.Abs(a.Y - b.Y) < 1.0;
                        if (v ? !sv : !sh) continue;
                        double off = ((v ? a.X : a.Y) - c) * -inward;
                        if (off < NibDetector.MinNibWidth || off > NibDetector.MaxNibWidth + 1.0) continue;
                        double l0 = v ? Math.Min(a.Y, b.Y) : Math.Min(a.X, b.X), l1 = v ? Math.Max(a.Y, b.Y) : Math.Max(a.X, b.X);
                        cover += Math.Max(0, Math.Min(l1, hi + 300) - Math.Max(l0, lo - 300));
                    }
                    if (cover >= 0.5 * (hi - lo))
                        edges.Add(new BeamGeometry.ThkStepEdge { Vertical = v, C = c, Inward = inward, Lo = lo, Hi = hi });
                }
                if (edges.Count == 0)
                {
                    if (warn) warnings.Add("[RC BEAM] Opisy „…THK” w GA, ale płyta bez nibu — strefy belek nie utworzone (RC_BEAM → Krawedz).");
                    return made;
                }
                if (warn) warnings.Add($"[RC BEAM] Obrys płyty z GA = linia uskoku (krawędź zewnętrzna nibu otwarta, nie domknięta) — krawędzie z nibem: {edges.Count}.");
            }
            lines.AddRange(PieceSegments(db, pieces));
            var msgs = new List<string>();
            double thk = ctx?.SlabThickness ?? 225.0;
            var zones = BeamGeometry.ZonesFromThk(edges, texts, lines, outer.Select(p => (p.X, p.Y)).ToList(), thk, msgs);
            if (warn) foreach (var m in msgs) warnings.Add("[RC BEAM] Opisy THK: " + m);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                BeamStore.EnsureLayer(tr, db, ThickeningLayer);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                string slabHandle = slabId.Handle.ToString();
                var outl = outer.Select(p => (p.X, p.Y)).ToList();
                int k = 0;
                foreach (var z in zones)
                {
                    k++;
                    var pl = new Polyline { Layer = BeamStore.LayerName };
                    for (int i = 0; i < z.Polygon.Count; i++) pl.AddVertexAt(i, new Point2d(z.Polygon[i].X, z.Polygon[i].Y), 0, 0, 0);
                    pl.Closed = true;
                    var id = ms.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                    Tag(pl, plot.Label, view);
                    var an = BeamGeometry.Analyze(z.Polygon, outl);
                    var p = new BeamParams { Height = z.Height };
                    if (an.TypicalWidth > 0) p.Width = an.TypicalWidth;
                    BeamStore.Write(tr, db, pl, p, slabHandle);
                    made.Add(id);
                    string zName = $"strefa z opisu {k} ({z.Height}THK{(z.Note != null ? ", " + z.Note : "")})";
                    if (warn)
                    {
                        warnings.Add($"[RC BEAM] {zName}: utworzona z opisu „…THK” (GA bez SD-Thickening-Zone) — sprawdź zasięg.");
                        foreach (var m in an.Messages) warnings.Add($"[RC BEAM] {zName}: {m}");
                        if (ctx != null && ctx.Ssl.HasValue && ctx.Tocs.Count > 0)
                        {
                            var srcPoly = z.Polygon.Select(q => new Point2d(q.X - d.X, q.Y - d.Y)).ToList();
                            var near = ctx.Tocs.OrderBy(t => DistToPoly(srcPoly, t.p)).First();
                            int hCheck = BeamGeometry.HeightFromLevels(ctx.Ssl.Value, near.toc);
                            if (Math.Abs(hCheck - z.Height) > 1)
                                warnings.Add($"[RC BEAM] {zName}: wysokość z opisu {z.Height} ≠ SSL − NIB TOC + 150 = {hCheck} — sprawdź.");
                        }
                    }
                }
                tr.Commit();
            }
            // NIB TOC wskazujący pogrubienie (H > grubość płyty + 50) bez strefy w pobliżu — tylko ostrzeżenie
            if (warn && ctx != null && ctx.Ssl.HasValue)
                foreach (var (tp, toc) in ctx.Tocs)
                {
                    int h = BeamGeometry.HeightFromLevels(ctx.Ssl.Value, toc);
                    if (h <= thk + 50) continue;
                    var pt = new Point2d(tp.X + d.X, tp.Y + d.Y);
                    if (!GeometryHelper.IsPointInsidePolygon(outer, pt) && outer.Min(q => q.GetDistanceTo(pt)) > 1500) continue;
                    bool covered = zones.Any(z => DistToPoly(z.Polygon.Select(q => new Point2d(q.X, q.Y)).ToList(), pt) < 1000);
                    if (!covered)
                        warnings.Add($"[RC BEAM] NIB TOC={toc.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)} wskazuje pogrubienie H {h} " +
                                     "bez opisu „…THK” — strefa nie utworzona (RC_BEAM, jeśli potrzebna).");
                }
            return made;
        }

        /// <summary>K8: odcinki otwartych kawałków SD-PILED-RAFT rzutu (Line, polilinie otwarte) — linie strefy / krawędzie nibu z GA.</summary>
        private static List<((double X, double Y) A, (double X, double Y) B)> PieceSegments(Database db, List<ObjectId> pieces)
        {
            var lines = new List<((double X, double Y) A, (double X, double Y) B)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (var id in pieces)
                {
                    if (id.IsErased) continue;
                    var o = tr.GetObject(id, OpenMode.ForRead);
                    if (o is Line ln) lines.Add(((ln.StartPoint.X, ln.StartPoint.Y), (ln.EndPoint.X, ln.EndPoint.Y)));
                    else if (o is Polyline pl && !GeometryHelper.IsEffectivelyClosed(pl))
                        for (int i = 0; i + 1 < pl.NumberOfVertices; i++)
                        {
                            var a = pl.GetPoint2dAt(i); var b = pl.GetPoint2dAt(i + 1);
                            lines.Add(((a.X, a.Y), (b.X, b.Y)));
                        }
                }
                tr.Commit();
            }
            return lines;
        }

        /// <summary>K8: odcinki polilinii SD-PILED-RAFT tego rzutu (znacznik RC_GA [płyta, rzut]) poza obrysem płyty.</summary>
        private static List<((double X, double Y) A, (double X, double Y) B)> TaggedSlabSegments(Database db, ObjectId outlineId, string plot, string view)
        {
            var res = new List<((double X, double Y) A, (double X, double Y) B)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || id == outlineId || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl)) continue;
                    if (!string.Equals(pl.Layer, SlabLayer, StringComparison.OrdinalIgnoreCase)) continue;
                    TypedValue[] v;
                    using (var rb = pl.GetXDataForApplication(XApp)) v = rb?.AsArray();
                    if (v == null || v.Length < 3 || Convert.ToString(v[1].Value) != plot || Convert.ToString(v[2].Value) != view) continue;
                    var pts = GeometryHelper.GetPolylineVertices(pl);
                    int n = GeometryHelper.IsEffectivelyClosed(pl) ? pts.Count : pts.Count - 1;
                    for (int i = 0; i < n; i++)
                    {
                        var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                        res.Add(((a.X, a.Y), (b.X, b.Y)));
                    }
                }
                tr.Commit();
            }
            return res;
        }

        private static int HeightOf(Database db, ObjectId zid)
        {
            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                var p = BeamStore.Read(tr.GetObject(zid, OpenMode.ForRead) as Entity);
                return p != null ? (int)Math.Round(p.Height) : 0;
            }
            catch (System.Exception ex) { Log.Error("GaImport.HeightOf", ex); return 0; }
        }

        private static double DistToPoly(List<Point2d> poly, Point2d p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                var ab = b - a; double len2 = ab.DotProduct(ab);
                double t = len2 > 1e-12 ? Math.Max(0, Math.Min(1, (p - a).DotProduct(ab) / len2)) : 0;
                best = Math.Min(best, (a + ab * t).GetDistanceTo(p));
            }
            return GeometryHelper.IsPointInsidePolygon(poly, p) ? 0 : best;
        }

        /// <summary>
        /// Opis płyty od nowa: czysty MText (te same warstwa, styl, wysokość, kolor, punkt i wyrównanie) z liniami
        /// nr plotu / SSL / grubość. Kopie tego samego opisu (duplikaty w GA) usuwane. Ramka dopasowana do tekstu.
        /// </summary>
        private static ObjectId RebuildLabel(Transaction tr, ObjectId msId, ObjectId labelId, ObjectId boxId,
            List<ObjectId> textClones, string plot, string view, List<string> warnings)
        {
            var ent = (Entity)tr.GetObject(labelId, OpenMode.ForWrite);
            List<string> lines = ent is MText m0 ? TextLines(m0)
                               : ent is DBText d0 ? new List<string> { d0.TextString.Trim() } : new List<string>();
            if (lines.Count == 0) return labelId;
            var keep = new List<string> { lines[0] };
            var ssl = lines.FirstOrDefault(l => l.StartsWith("SSL", StringComparison.OrdinalIgnoreCase));
            if (ssl != null) keep.Add(ssl);
            else warnings.Add($"{lines[0]}: w opisie brak SSL.");
            var thk = lines.FirstOrDefault(l => Regex.IsMatch(l, @"\bTHK\b", RegexOptions.IgnoreCase));
            if (thk != null) keep.Add(Regex.Replace(thk, @"\s+ON\s*$", "", RegexOptions.IgnoreCase).Trim());
            else warnings.Add($"{lines[0]}: w opisie brak grubości płyty (… THK SLAB).");

            // Duplikaty opisu (ta sama treść) — usuń
            string sig = string.Join("|", lines);
            foreach (var id in textClones)
                if (id != labelId && tr.GetObject(id, OpenMode.ForWrite) is MText dup && string.Join("|", TextLines(dup)) == sig)
                    dup.Erase();

            Entity fresh;
            if (ent is MText src)
            {
                var mt = new MText
                {
                    Layer = src.Layer, Color = src.Color, TextStyleId = src.TextStyleId, TextHeight = src.TextHeight,
                    Location = src.Location, Attachment = src.Attachment, Rotation = src.Rotation, Width = 0,
                    Contents = string.Join("\\P", keep)
                };
                var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForWrite);
                ms.AppendEntity(mt);
                tr.AddNewlyCreatedDBObject(mt, true);
                Tag(mt, plot, view);
                src.Erase();
                fresh = mt;
            }
            else
            {
                ((DBText)ent).TextString = string.Join(" ", keep);
                fresh = ent;
            }

            if (boxId.IsNull || !(tr.GetObject(boxId, OpenMode.ForWrite) is Polyline bx)) return fresh.ObjectId;
            Extents3d te;
            try { te = fresh.GeometricExtents; }
            catch (System.Exception ex) { Log.Error("GaImport.RebuildLabel", ex); return fresh.ObjectId; }
            double a = te.MinPoint.X - LabelMargin, b = te.MinPoint.Y - LabelMargin;
            double c = te.MaxPoint.X + LabelMargin, d = te.MaxPoint.Y + LabelMargin;
            while (bx.NumberOfVertices > 4) bx.RemoveVertexAt(bx.NumberOfVertices - 1);
            while (bx.NumberOfVertices < 4) bx.AddVertexAt(bx.NumberOfVertices, new Point2d(0, 0), 0, 0, 0);
            var pts = new[] { new Point2d(a, b), new Point2d(c, b), new Point2d(c, d), new Point2d(a, d) };
            for (int i = 0; i < 4; i++) { bx.SetPointAt(i, pts[i]); bx.SetBulgeAt(i, 0); }
            bx.Closed = true;
            return fresh.ObjectId;
        }

        /// <summary>
        /// Linie tekstu MText z surowej treści (Contents): \P = nowa linia, kody formatowania usunięte.
        /// (MText.Text w BricsCAD skleja linie bez separatora — „PLOT 30FFL=…”.)
        /// </summary>
        internal static List<string> TextLines(MText mt)
        {
            string c = mt.Contents ?? "";
            c = Regex.Replace(c, @"\\[ACcFfHhQqTtWwp][^;\\]*;", "");   // kody formatowania (\pxqc; to akapit, nie nowa linia)
            c = Regex.Replace(c, @"\\P", "\n");
            c = Regex.Replace(c, @"\\[LlOoKk]", "");
            c = c.Replace("\\~", " ").Replace("{", "").Replace("}", "");
            return c.Split('\n').Select(l => Regex.Replace(l, @"\s+", " ").Trim()).Where(l => l.Length > 0).ToList();
        }

        /// <summary>Łączy otwarte polilinie (bez łuków), których końce się stykają, w zamknięte pętle.</summary>
        private static int JoinLoops(Transaction tr, ObjectId msId, List<ObjectId> open, string plot, string view)
        {
            const double tol = 1.0;
            var parts = new List<(ObjectId id, List<Point2d> v)>();
            foreach (var id in open)
            {
                var pl = (Polyline)tr.GetObject(id, OpenMode.ForRead);
                bool arcs = false;
                for (int i = 0; i < pl.NumberOfVertices; i++) if (Math.Abs(pl.GetBulgeAt(i)) > 1e-9) arcs = true;
                if (!arcs) parts.Add((id, GeometryHelper.GetPolylineVertices(pl)));
            }
            int made = 0;
            var used = new HashSet<ObjectId>();
            foreach (var start in parts)
            {
                if (used.Contains(start.id)) continue;
                var chain = new List<Point2d>(start.v);
                var members = new List<ObjectId> { start.id };
                bool progress = true;
                while (progress && chain[0].GetDistanceTo(chain[chain.Count - 1]) > tol)
                {
                    progress = false;
                    foreach (var p in parts)
                    {
                        if (used.Contains(p.id) || members.Contains(p.id)) continue;
                        var end = chain[chain.Count - 1];
                        if (p.v[0].GetDistanceTo(end) <= tol) chain.AddRange(p.v.Skip(1));
                        else if (p.v[p.v.Count - 1].GetDistanceTo(end) <= tol) chain.AddRange(Enumerable.Reverse(p.v).Skip(1));
                        else continue;
                        members.Add(p.id);
                        progress = true;
                        break;
                    }
                }
                if (members.Count < 2 || chain[0].GetDistanceTo(chain[chain.Count - 1]) > tol) continue;
                chain.RemoveAt(chain.Count - 1);

                var first = (Polyline)tr.GetObject(members[0], OpenMode.ForRead);
                var pl = new Polyline { Layer = first.Layer, Color = first.Color, LinetypeId = first.LinetypeId,
                                        LineWeight = first.LineWeight };
                for (int i = 0; i < chain.Count; i++) pl.AddVertexAt(i, chain[i], 0, 0, 0);
                pl.Closed = true;
                var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForWrite);
                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                Tag(pl, plot, view);
                foreach (var id in members) { ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase(); used.Add(id); }
                made++;
            }
            return made;
        }

        // ----------------------------------------------------------------
        // Szablon rysunku RC: tytuły rzutów i ramki szablonów
        // ----------------------------------------------------------------

        private static (ObjectId titleB, ObjectId titleT, ObjectId frameB, ObjectId frameT) FindTemplate(Database db)
        {
            ObjectId tb = ObjectId.Null, tt = ObjectId.Null, fb = ObjectId.Null, ft = ObjectId.Null;
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                var o = tr.GetObject(id, OpenMode.ForRead);
                if (o is MText mt)
                {
                    string t = (mt.Text ?? "").ToUpperInvariant();
                    if (!t.Contains("REINFORCEMENT DETAILS")) continue;
                    if (t.Contains("BOTTOM LAYER") && tb.IsNull) tb = id;
                    else if (t.Contains("TOP LAYER") && tt.IsNull) tt = id;
                }
                else if (o is Polyline pl)
                {
                    if (pl.Layer.Equals("rebar_bottom", StringComparison.OrdinalIgnoreCase) && fb.IsNull) fb = id;
                    else if (pl.Layer.Equals("rebar_top", StringComparison.OrdinalIgnoreCase) && ft.IsNull) ft = id;
                }
            }
            tr.Commit();
            return (tb, tt, fb, ft);
        }

        /// <summary>Ramka szablonów (z zawartością — pręty-szablony i ich opisy) przesunięta obok rzutu; brak → nowa.</summary>
        private static void PlaceFrame(Transaction tr, Database db, ObjectId frameId, string layer, List<string> warnings,
                                       double top, double rightEdge = double.NaN, double leftEdge = double.NaN)
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            if (frameId.IsNull)
            {
                EnsureLayer(tr, db, layer);
                double l = double.IsNaN(leftEdge) ? rightEdge - FrameWidth : leftEdge;
                var pl = new Polyline { Layer = layer };
                pl.AddVertexAt(0, new Point2d(l, top), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(l + FrameWidth, top), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(l + FrameWidth, top - FrameHeight), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(l, top - FrameHeight), 0, 0, 0);
                pl.Closed = true;
                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                warnings.Add($"Brak ramki {layer} — utworzono nową.");
                return;
            }
            var frame = (Polyline)tr.GetObject(frameId, OpenMode.ForRead);
            var fe = Ext(GeometryHelper.GetPolylineVertices(frame));
            double dx = double.IsNaN(leftEdge) ? rightEdge - fe.MaxPoint.X : leftEdge - fe.MinPoint.X;
            double dy = top - fe.MaxPoint.Y;
            if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6) return;
            var move = Matrix3d.Displacement(new Vector3d(dx, dy, 0));
            foreach (ObjectId id in ms)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                if (id != frameId)
                {
                    Extents3d x;
                    try { x = e.GeometricExtents; } catch { continue; }
                    var c = new Point3d((x.MinPoint.X + x.MaxPoint.X) / 2, (x.MinPoint.Y + x.MaxPoint.Y) / 2, 0);
                    if (!InsideXY(fe, c)) continue;
                    if (e.GetXDataForApplication(XApp) != null) continue;
                }
                e.UpgradeOpen();
                e.TransformBy(move);
            }
        }

        // ----------------------------------------------------------------

        private static void EraseOld(Database db)
        {
            var old = new List<ObjectId>();
            var oldZones = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    using var rb = e.GetXDataForApplication(XApp);
                    if (rb != null)
                    {
                        old.Add(id);
                        if (BeamStore.IsZone(e)) oldZones.Add(id);
                    }
                }
                tr.Commit();
            }
            if (old.Count == 0) return;
            // Belki: pręty starych stref (znacznik = strefa albo bez znacznika w strefie) — razem ze strefami
            if (oldZones.Count > 0) BeamStore.EraseGeneratedForZones(db, oldZones);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in old)
                    if (tr.GetObject(id, OpenMode.ForWrite) is Entity e) e.Erase();
                tr.Commit();
            }
        }

        private static void Tag(Entity e, string plot, string view)
            => e.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, XApp),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, plot),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, view));

        private static void EnsureApp(Transaction tr, Database db)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(XApp)) return;
            rat.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = XApp };
            rat.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        private static void EnsureLayer(Transaction tr, Database db, string name)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord { Name = name };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        private static Extents3d? ExtOf(Database db, ObjectId id)
        {
            if (id.IsNull) return null;
            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                var x = e.GeometricExtents;
                tr.Commit();
                return x;
            }
            catch (System.Exception ex) { Log.Error("GaImport.ExtOf", ex); return null; }
        }

        private static string FirstLine(MText mt) => TextLines(mt).FirstOrDefault() ?? "";

        private static Extents3d Ext(List<Point2d> v)
            => new Extents3d(new Point3d(v.Min(p => p.X), v.Min(p => p.Y), 0), new Point3d(v.Max(p => p.X), v.Max(p => p.Y), 0));

        private static double Area(List<Point2d> v)
        {
            double a = 0;
            for (int i = 0; i < v.Count; i++) { var p = v[i]; var q = v[(i + 1) % v.Count]; a += p.X * q.Y - q.X * p.Y; }
            return a / 2;
        }

        private static bool InsideXY(Extents3d e, Point3d p)
            => p.X >= e.MinPoint.X && p.X <= e.MaxPoint.X && p.Y >= e.MinPoint.Y && p.Y <= e.MaxPoint.Y;
    }
}
