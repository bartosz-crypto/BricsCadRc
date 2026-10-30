using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Pile cap (uwaga Bartka 2026-10-30, APARTMENT 2 — „je zbroimy ZAWSZE tak samo”, wzór pilecap.dwg + dlugosci.xls): oczepy RC_PILECAP
    /// przy płycie rzutu dolnego — identyczne (wymiary) jednym detalem 1:25 „DETAIL 'n' / NNo.” (ta sama ścieżka co belki / wsporniki:
    /// PrepareBeamDetail / FinishBeamDetail, szablony obok detalu, ramka i odnośnik przy każdym oczepie na rzucie). Rozkłady RC_BAR
    /// (BBS z rysunku): U-bary H12 21 A480 / B500 / C480 — po 5 dołem (BB) i górą (BT) w obu kierunkach, U-bary H12 21 A480 / B270 / C480
    /// przy osi pala — 2 dołem i 2 górą (kierunek X, jak przekrój D-D wzoru). Liczba w opisie „Nx5” (CountMult = N oczepów).
    /// Pod tytułem przekroje C-C (cięcie wzdłuż X) i D-D (wzdłuż Y) — rysunek (rola SECTION), bez rozkładów.
    /// </summary>
    public static partial class AutoRebarEngine
    {
        private const string PileCapTag = "[RC PILECAP]";
        /// <summary>Rola obiektów przekrojów oczepu w detalu (RC_BEAM_DETAIL [4]).</summary>
        public const string PileCapSectionRole = "SECTION";

        public sealed class PileCapRunResult
        {
            public int Caps, Details, Dists;
            public List<string> Messages = new List<string>();
        }

        /// <summary>RC_GENERATE → PileCap: oczepy przy obrysie rzutu dolnego <paramref name="bottomSlabId"/> (środek w obwiedni + 1500).</summary>
        public static PileCapRunResult GeneratePileCaps(Document doc, ObjectId bottomSlabId)
        {
            var db = doc.Database;
            var ed = doc.Editor;
            var res = new PileCapRunResult();
            void Msg(string m) { res.Messages.Add(m); ed.WriteMessage($"\n{PileCapTag} {m}"); }

            var bottom = ReadBeamView(db, bottomSlabId);
            if (bottom == null) { Msg("Obrys rzutu dolnego nie jest zamkniętą polilinią — przerwano."); return res; }
            var caps = PileCapEngine.CapsNear(db, bottom.Box, GaImportEngine.RegionMargin);
            if (caps.Count == 0)
            {
                Msg($"Przy płycie nie ma pile cap (warstwa {PileCapEngine.LayerName}) — ponowne Prepare GA (opis „…SQx…DP / PILE CAP”).");
                return res;
            }
            string F(double x) => Math.Round(x).ToString("0", CultureInfo.InvariantCulture);
            var groups = caps.GroupBy(c => $"{F(Math.Min(c.box.X1 - c.box.X0, c.box.Y1 - c.box.Y0))}x{F(Math.Max(c.box.X1 - c.box.X0, c.box.Y1 - c.box.Y0))}x" +
                                           $"{F(c.data?.H > 0 ? c.data.H : c.box.X1 - c.box.X0)}|{((c.box.X1 - c.box.X0) <= (c.box.Y1 - c.box.Y0) ? "V" : "H")}")
                             .ToList();

            var saveSlab = _currentSlabHandle;
            var saveHoles = _currentHoles;
            try
            {
                using (doc.LockDocument())
                {
                    _currentSlabHandle = null;
                    _currentHoles = new List<HoleBox>();
                    InitLabelOccupancy(db);
                    foreach (var g in groups)
                    {
                        var list = g.OrderBy(c => c.box.X0).ThenBy(c => c.box.Y0).ToList();
                        try { GeneratePileCapGroup(doc, bottom, list, "PC|" + g.Key, res, Msg); }
                        catch (System.Exception ex)
                        {
                            Log.Error("AutoRebar.GeneratePileCaps", ex);
                            Msg($"pile cap {list[0].handle}: błąd — {ex.Message}");
                        }
                        finally { ResetBeamDetailOverrides(); }
                    }
                }
            }
            finally
            {
                _currentSlabHandle = saveSlab;
                _currentHoles = saveHoles ?? new List<HoleBox>();
                ResetBeamDetailOverrides();
            }
            Msg($"Pile cap: {res.Caps} w {res.Details} detal(ach), rozkładów: {res.Dists}.");
            return res;
        }

        private static List<Point2d> RectPts((double X0, double Y0, double X1, double Y1) b)
            => new List<Point2d> { new Point2d(b.X0, b.Y0), new Point2d(b.X1, b.Y0), new Point2d(b.X1, b.Y1), new Point2d(b.X0, b.Y1) };

        /// <summary>
        /// Grupa identycznych oczepów (uwaga Bartka 2026-10-30: „kopie dla każdego oczepu”): jak identyczne belki / wsporniki — pierwszy
        /// oczep ma detal główny („DETAIL 'n' / NNo.”), kolejne — kopie rozkładów nad detalem (podpis <paramref name="signature"/>,
        /// PrepareBeamDetail / FindDetailMaster, ramka z przekątnymi), każdy oczep osobno w BBS; ramka i odnośnik przy każdym na rzucie.
        /// </summary>
        private static void GeneratePileCapGroup(Document doc, BeamView bottom,
                                                 List<(ObjectId id, string handle, (double X0, double Y0, double X1, double Y1) box, PileCapEngine.Data data)> caps,
                                                 string signature, PileCapRunResult res, Action<string> msg)
        {
            int n = caps.Count;
            foreach (var c in caps)
            {
                try { GenerateOnePileCap(doc, bottom, c, signature, n, res, msg); }
                catch (System.Exception ex)
                {
                    Log.Error("AutoRebar.GenerateOnePileCap", ex);
                    msg($"pile cap {c.handle}: błąd — {ex.Message}");
                }
                finally { ResetBeamDetailOverrides(); }
            }
        }

        private static void GenerateOnePileCap(Document doc, BeamView bottom,
                                               (ObjectId id, string handle, (double X0, double Y0, double X1, double Y1) box, PileCapEngine.Data data) m,
                                               string signature, int n, PileCapRunResult res, Action<string> msg)
        {
            var db = doc.Database;
            string name = $"pile cap {m.handle}";
            var b = m.box;
            double bx = b.X1 - b.X0, by = b.Y1 - b.Y0;
            double h = m.data?.H > 0 ? m.data.H : Math.Min(bx, by);
            double cx = (b.X0 + b.X1) / 2, cy = (b.Y0 + b.Y1) / 2;
            var poly = RectPts(b);

            var old = new List<(ObjectId, ObjectId)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                old.AddRange(BeamStore.FindGenerated(tr, db, m.handle, poly));
                tr.Commit();
            }
            EraseOldDistributions(db, old);

            var dv = new BeamView { OuterId = bottom.OuterId, Verts = poly,
                                    Box = new Extents3d(new Point3d(b.X0, b.Y0, 0), new Point3d(b.X1, b.Y1, 0)) };
            var tg = PrepareBeamDetail(doc, dv, null, m.handle, poly, s => msg(s.Replace("belka", "pile cap")), offsetFrame: true,
                                       allPilesInFrame: true, signature: signature);
            if (tg == null) return;
            SetBeamDetailOverrides(tg);
            res.Caps++;
            if (tg.CopyOf == null) res.Details++;

            const int d = PileCapEngine.Dia;
            // opisy jak w innych detalach (domyślne linie opisu prostopadle do prętów), każda linia w innym miejscu wzdłuż prętów (alongAt);
            // widoczne pręty (wygaszanie) — każdy rozkład inny pręt, bez nakładania górnego na dolny (T — za osią, B — przed osią)
            const double lane = 150.0;
            int k = 0;
            // wzór Bartka (TEST2.dwg, 2026-10-30): zakończenia — hak w głąb oczepu na OBU końcach (BAR_END Hook / Both / Down), opisy
            // prętów wzdłuż X w dół pod oczep, wzdłuż Y w lewo (≈ 1650 za lico), T1+B1 w górę
            void Ends(BarData bar) { bar.SymbolType = "Hook"; bar.SymbolSide = "Both"; bar.SymbolDirection = "Down"; }
            double dx0 = b.X0 + tg.Shift.X, dy0 = b.Y0 + tg.Shift.Y;
            const double labelOut = 1650.0;
            Func<Point3d, List<List<Point3d>>> Down() => ip => new List<List<Point3d>> { new List<Point3d> { ip, new Point3d(ip.X, dy0 - labelOut, 0) } };
            Func<Point3d, List<List<Point3d>>> Left() => ip => new List<List<Point3d>> { new List<Point3d> { ip, new Point3d(dx0 - labelOut, ip.Y, 0) } };
            void Visible(int idx)
            {
                if (_lastDistId.IsNull) return;
                ApplyVisibleIndices(db, _lastDistId, idx.ToString(CultureInfo.InvariantCulture));
            }

            // ── U-bary główne: wzdłuż X — 1 (T1 / B1), wzdłuż Y — 2 (T2 / B2); 5 co 120 symetrycznie (otulina 50) ──
            foreach (bool barsX in new[] { true, false })
            {
                double len = barsX ? bx : by, across = barsX ? by : bx;
                var (ua, ub) = PileCapEngine.MainU(len, h);
                var (cnt, sp) = PileCapEngine.MainLayout(across);
                var tpl = FindOrCreateBeamTemplate(db, doc.Editor, bottom.Box, top: false, d: d, shape: PileCapEngine.Shape, a: ua, b: ub,
                                                   code: BeamGeometry.CodeBottom, c: ua);
                if (tpl.id.IsNull) continue;
                double mid = barsX ? cy : cx, along0 = barsX ? cx : cy;
                double k0 = (barsX ? b.X0 : b.Y0) + PileCapEngine.Cover, k1 = k0 + ub;
                double s0 = mid - sp * (cnt - 1) / 2.0, s1 = mid + sp * (cnt - 1) / 2.0;
                string dir = barsX ? "1" : "2";
                foreach (bool top in new[] { true, false })
                {
                    string code = top ? BeamGeometry.CodeTop : BeamGeometry.CodeBottom;
                    string suffix = (top ? "T" : "B") + dir + " U-BARS";
                    double along = along0 + (top ? -lane : lane);
                    double x0 = barsX ? k0 : s0, x1 = barsX ? k1 : s1, y0 = barsX ? s0 : k0, y1 = barsX ? s1 : k1;
                    if (BeamDistribution(db, dv, tpl, x0, y0, x1, y1, barsX, d, ub, sp, sp, code, suffix, noSpacing: cnt <= 1,
                                         shape: PileCapEngine.Shape, repr: k++, zoneHandle: m.handle, customize: Ends,
                                         leaderFor: barsX ? Down() : Left(), tg: tg, alongAt: along))
                    {
                        res.Dists++;
                        // wygaszanie jak we wzorze: górny i dolny U-bar tym samym prętem (jeden na drugim) — wzdłuż X środkowy, wzdłuż Y drugi
                        Visible(barsX ? cnt / 2 : Math.Min(1, cnt - 1));
                    }
                }
                if (tg.CopyOf == null)
                    msg($"{name}: U-bary H{d} 21 A {ua:F0} / B {ub:F0} / C {ua:F0} — {dir}: {cnt} górą (T{dir}) + {cnt} dołem (B{dir}) co {sp:F0}.");
            }

            // ── U-bary przy osi pala: wzdłuż X, 2 co 150, dołem i górą jedną pozycją „2x2 H12-18-150 T1+B1 U-BARS” (wzór biura) ──
            {
                var (ua, _) = PileCapEngine.MainU(bx, h);
                double ub = PileCapEngine.InnerB, sp = PileCapEngine.InnerSpacing;
                var tpl = FindOrCreateBeamTemplate(db, doc.Editor, bottom.Box, top: false, d: d, shape: PileCapEngine.Shape, a: ua, b: ub,
                                                   code: BeamGeometry.CodeBottom, c: ua);
                if (!tpl.id.IsNull)
                {
                    int cnt = PileCapEngine.InnerPerDir;
                    double x0 = cx - ub / 2, x1 = cx + ub / 2, y0 = cy - sp * (cnt - 1) / 2.0, y1 = cy + sp * (cnt - 1) / 2.0;
                    if (BeamDistribution(db, dv, tpl, x0, y0, x1, y1, true, d, ub, sp, sp, BeamGeometry.CodeBoth, "T1+B1 U-BARS", noSpacing: false,
                                         shape: PileCapEngine.Shape, repr: k++, zoneHandle: m.handle,
                                         customize: bar => { Ends(bar); bar.CountDisplay = 2 * cnt; }, tg: tg, alongAt: cx))
                    {
                        res.Dists++;
                        Visible(Math.Min(1, cnt - 1));
                    }
                }
            }

            FinishBeamDetail(doc, tg, m.id, ObjectId.Null, s => msg(s.Replace("belka", "pile cap")), dv.Verts, new Vector2d(),
                             titleCount: n, resolveCollisions: false);
            msg($"{name}: {F0(bx)} × {F0(by)} × {F0(h)}" + (m.data != null && m.data.Toc != 0 ? $", TOC={m.data.Toc:0.000}" : "") +
                $" — DETAIL '{tg.Number}'" + (tg.CopyOf != null ? " (kopia rozkładów nad detalem)." : n > 1 ? $" ({n}No.)." : "."));
        }

        private static string F0(double x) => x.ToString("0", CultureInfo.InvariantCulture);
    }
}
