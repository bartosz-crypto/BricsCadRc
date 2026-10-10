using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// K4 (pręty boczne + UB SIDE) i K6 (przekrój: zakończenia prętów 45°, pionowe U-bary BU) planu
    /// docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md; wartości domyślne do potwierdzenia przez Bartka.
    /// </summary>
    public class BeamK4K6Tests
    {
        private static List<(double X, double Y)> Box(double x0, double y0, double x1, double y1)
            => new List<(double X, double Y)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };

        // ── K4 ──

        [Test]
        public void K4_SideBarCount_HMinus85()
        {
            // A = H − 85 (A1): 525 → 1, 600 → 2, 675 → 2, 750 → 3 (H12 strzemiona, H16 dół / góra, rozstaw ≤ 200)
            foreach (var (h, n) in new[] { (525.0, 1), (600.0, 2), (675.0, 2), (750.0, 3), (450.0, 1) })
                Assert.That(BeamGeometry.SideBarCount(BeamGeometry.StirrupHeightCovers(h), 12, 16, 16, 200), Is.EqualTo(n), h.ToString());
            var p = new BeamParams();
            Assert.That(p.SideBarsActive(450), Is.False, "B3: H ≤ 450 — brak");
            Assert.That(p.SideBarsActive(525), Is.True);
            Assert.That(p.SideDia, Is.EqualTo(12));
        }

        [Test]
        public void K4_UbSide_EdgeEnds_B485_A_StartPlusLap()
        {
            // belka 600 krawędź–krawędź (jak 528), boczne H12 od lica + 45 + 12 (koniec przy UB SIDE)
            var an = BeamGeometry.Analyze(Box(160, 160, 760, 10340), Box(0, 0, 10000, 10500));
            var s = an.Segments.Single();
            var (lo, hi) = BeamGeometry.StraightRange(s, 45, 57);
            Assert.That(lo, Is.EqualTo(160 + 57).Within(1e-9));
            var ranges = new Dictionary<BeamGeometry.Segment, (double Lo, double Hi)> { [s] = (lo, hi) };
            var msgs = new List<string>();
            var runs = BeamGeometry.PlanSideUBars(an, ranges, 2, 12, 12, 500, 45, msgs);
            Assert.That(runs.Count, Is.EqualTo(2));
            foreach (var r in runs)
            {
                Assert.That(r.B, Is.EqualTo(485), "B = 510 − 2·12 → 485 (C12: 480)");
                Assert.That(r.A, Is.EqualTo(525), "A = Ceil25((57 − 45) + 500)");
                Assert.That(r.C, Is.EqualTo(r.A));
                Assert.That(r.Levels, Is.EqualTo(2));
                Assert.That(r.Across1 - r.Across0, Is.EqualTo(600 - 2 * (45 + 12 + 6)).Within(1e-9));
            }
            var lowEnd = runs.Single(r => r.AtLo);
            Assert.That(lowEnd.Back, Is.EqualTo(160 + 45).Within(1e-9));
            Assert.That(lowEnd.LineStart, Is.EqualTo(lowEnd.Back).Within(1e-9));
            Assert.That(msgs, Is.Empty);
        }

        [Test]
        public void K4_UbSide_CornerLeg_LongArm()
        {
            // L (539: góra + lewa) — każda noga zamknięta UB SIDE z tyłem na licu zewnętrznym narożnika, ramię ≈ szerokość + zakład
            var zone = new List<(double X, double Y)> { (0, 5400), (4000, 5400), (4000, 4800), (600, 4800), (600, 0), (0, 0) };
            var an = BeamGeometry.Analyze(zone, Box(-160, -2000, 8000, 5560));
            var ranges = new Dictionary<BeamGeometry.Segment, (double Lo, double Hi)>();
            foreach (var s in an.Segments) ranges[s] = BeamGeometry.StraightRange(s, 45, 57);
            var runs = BeamGeometry.PlanSideUBars(an, ranges, 1, 12, 12, 500, 45, new List<string>());
            var corner = runs.Where(r => r.Kind == "L").ToList();
            Assert.That(corner.Count, Is.EqualTo(2));
            Assert.That(corner.All(r => r.A >= 600 - 45 + 500 - 1e-9), Is.True, string.Join(",", corner.Select(r => r.A)));
        }

        // ── K8: strefy z opisów „xxxTHK” (GA C12 bez SD-Thickening-Zone) ──

        private static List<BeamGeometry.ThkStepEdge> StepEdges(List<(double X, double Y)> step, List<(double X, double Y)> outer)
        {
            var res = new List<BeamGeometry.ThkStepEdge>();
            for (int i = 0; i < step.Count; i++)
            {
                var a = step[i]; var b = step[(i + 1) % step.Count];
                bool v = Math.Abs(a.X - b.X) < 1e-6;
                double c = v ? a.X : a.Y, lo = v ? Math.Min(a.Y, b.Y) : Math.Min(a.X, b.X), hi = v ? Math.Max(a.Y, b.Y) : Math.Max(a.X, b.X);
                double mid = (lo + hi) / 2;
                int inward = BeamGeometry.Inside(step, v ? (c + 1, mid) : (mid, c + 1)) ? 1 : -1;
                res.Add(new BeamGeometry.ThkStepEdge { Vertical = v, C = c, Lo = lo, Hi = hi, Inward = inward });
            }
            return res;
        }

        [Test]
        public void K8_Plot17_OneZone450_LeadersWithoutZoneLineRejected()
        {
            var step = new List<(double X, double Y)> { (3305530, -786061), (3311213, -786061), (3311213, -791011), (3314748, -791011),
                                                        (3314748, -798494), (3304630, -798494), (3304630, -791011), (3305530, -791011) };
            var outer = new List<(double X, double Y)> { (3305395, -785926), (3311348, -785926), (3311348, -790876), (3314148, -790876),
                                                         (3314148, -791352), (3314883, -791352), (3314883, -798629), (3304495, -798629),
                                                         (3304495, -790876), (3305395, -790876) };
            var lines = new List<((double X, double Y) A, (double X, double Y) B)>
            {
                ((3305530, -786661), (3311213, -786661)), ((3311348, -786661), (3311213, -786661)), ((3305530, -786661), (3305395, -786661)),
            };
            var texts = new List<BeamGeometry.ThkText>
            {
                new BeamGeometry.ThkText { Text = "450THK", X = 3307500, Y = -786402 },
                new BeamGeometry.ThkText { Text = "525THK", X = 3314466, Y = -791176, Leader = true },
                new BeamGeometry.ThkText { Text = "525THK", X = 3314531, Y = -798359, Leader = true },
                new BeamGeometry.ThkText { Text = "GB-600x450mm deep", X = 3316424, Y = -790866, Leader = true },
            };
            var msgs = new List<string>();
            var z = BeamGeometry.ZonesFromThk(StepEdges(step, outer), texts, lines, outer, 225, msgs);
            Assert.That(z.Count, Is.EqualTo(1), string.Join(" | ", msgs));
            Assert.That(z[0].Height, Is.EqualTo(450));
            Assert.That(z[0].Polygon.Min(p => p.X), Is.EqualTo(3305530).Within(1e-6));
            Assert.That(z[0].Polygon.Max(p => p.X), Is.EqualTo(3311213).Within(1e-6));
            Assert.That(z[0].Polygon.Min(p => p.Y), Is.EqualTo(-786661).Within(1e-6));
            Assert.That(z[0].Polygon.Max(p => p.Y), Is.EqualTo(-786061).Within(1e-6));
            Assert.That(msgs.Count(m => m.Contains("525THK")), Is.EqualTo(2), string.Join(" | ", msgs));   // pole styku GB: poza obrysem / bez linii strefy
            Assert.That(msgs.Any(m => m.Contains("GB-")), Is.True);
            // ta sama strefa z RC_BEAM → Krawedz (wspólna funkcja)
            var r = BeamGeometry.ZoneFromNibEdge(false, -786061, -1, 3305530, 3311213, 600);
            Assert.That(r.Min(p => p.Y), Is.EqualTo(-786661).Within(1e-6));
            Assert.That(RectilinearPolygons.SignedArea(r), Is.GreaterThan(0));
        }

        [Test]
        public void K8_Plot3_HeightsSplitByDividers_975Corner()
        {
            var step = new List<(double X, double Y)> { (3167850, -720007), (3182033, -720007), (3182033, -725689), (3178933, -725689),
                                                        (3178933, -733339), (3173250, -733339), (3173250, -725689), (3167850, -725689) };
            var lines = new List<((double X, double Y) A, (double X, double Y) B)>
            {
                ((3167715, -719872), (3181433, -719872)), ((3167850, -720607), (3182033, -720607)),
                ((3167850, -720607), (3167715, -720607)), ((3181433, -720607), (3181433, -719872)), ((3182033, -720607), (3182168, -720607)),
                ((3175355, -720607), (3175355, -720007)), ((3175355, -719872), (3175355, -720007)),
            };
            var texts = new List<BeamGeometry.ThkText>
            {
                new BeamGeometry.ThkText { Text = "600THK", X = 3173400, Y = -720349 },
                new BeamGeometry.ThkText { Text = "675THK", X = 3177100, Y = -720349 },
                new BeamGeometry.ThkText { Text = "975THK", X = 3181751, Y = -720171, Leader = true },
                new BeamGeometry.ThkText { Text = "525THK", X = 3181751, Y = -725494, Leader = true },
                new BeamGeometry.ThkText { Text = "450mm" + "\n" + "NIB STEP", X = 3182100, Y = -720607, Leader = true },
            };
            var msgs = new List<string>();
            var z = BeamGeometry.ZonesFromThk(StepEdges(step, null!), texts, lines, step, 225, msgs);
            Assert.That(z.Select(x => x.Height).OrderBy(x => x).ToArray(), Is.EqualTo(new[] { 600, 675, 975 }), string.Join(" | ", msgs));
            var z600 = z.Single(x => x.Height == 600);
            Assert.That(z600.Polygon.Max(p => p.X), Is.EqualTo(3175355).Within(1e-6), "podział linią w GA");
            var z675 = z.Single(x => x.Height == 675);
            Assert.That(z675.Polygon.Min(p => p.X), Is.EqualTo(3175355).Within(1e-6));
            Assert.That(z675.Polygon.Max(p => p.X), Is.EqualTo(3181433).Within(1e-6), "linia w GA na końcu krawędzi zewnętrznej (styk GB)");
            var z975 = z.Single(x => x.Height == 975);   // odnośnik z linią strefy: pole 600 × 600 w narożniku (węzeł 975THK)
            Assert.That(z975.Polygon.Min(p => p.X), Is.EqualTo(3181433).Within(1e-6));
            Assert.That(z975.Polygon.Max(p => p.X), Is.EqualTo(3182033).Within(1e-6));
            Assert.That(msgs.Any(m => m.Contains("525THK") && m.Contains("brak linii strefy")), Is.True);
        }

        [Test]
        public void K8_Rejections_SlabThicknessOutsideBand()
        {
            var step = new List<(double X, double Y)> { (0, 0), (10000, 0), (10000, 8000), (0, 8000) };
            var texts = new List<BeamGeometry.ThkText>
            {
                new BeamGeometry.ThkText { Text = "225THK", X = 5000, Y = 300 },
                new BeamGeometry.ThkText { Text = "450THK", X = 5000, Y = 3000 },
                new BeamGeometry.ThkText { Text = "450THK", X = 5000, Y = -500 },
                new BeamGeometry.ThkText { Text = "450THK", X = 5000, Y = 300 },
            };
            var msgs = new List<string>();
            var z = BeamGeometry.ZonesFromThk(StepEdges(step, null!), texts, new List<((double X, double Y), (double X, double Y))>(), step, 225, msgs);
            Assert.That(z.Count, Is.EqualTo(1), "tylko ostatni (w pasie, bez linii strefy — 600)");
            Assert.That(z[0].Polygon.Max(p => p.Y), Is.EqualTo(600).Within(1e-6));
            Assert.That(msgs.Any(m => m.Contains("≤ grubość płyty")), Is.True);
            Assert.That(msgs.Any(m => m.Contains("poza pasem")), Is.True);
            Assert.That(msgs.Any(m => m.Contains("poza obrysem")), Is.True);
        }

        // ── K6: przekrój ──

        [Test]
        public void K6_EndTicks_Slab_RealEndsOnly()
        {
            var inp = SectionPlannerGoldenTests.CaseNib();
            inp.BarEndTicks = true;
            var r = SectionPlanner.Plan(inp);
            var b1 = r.Bars.Where(b => b.PosNr == "05").ToList();
            Assert.That(b1.Count, Is.EqualTo(1));
            Assert.That(b1[0].Path.Count, Is.EqualTo(4), "oba końce B1 w przekroju — odgięcia");
            Assert.That(b1[0].Path[0].V - b1[0].Path[1].V, Is.EqualTo(SectionPlanner.BarEndTick).Within(1e-9), "dół — odgięcie w górę");
            var t1 = r.Bars.First(b => b.PosNr == "104");
            Assert.That(t1.Path[0].V - t1.Path[1].V, Is.EqualTo(-SectionPlanner.BarEndTick).Within(1e-9), "góra — odgięcie w dół");
            Assert.That(Math.Abs(t1.Path[0].U - t1.Path[1].U), Is.EqualTo(SectionPlanner.BarEndTick).Within(1e-9), "45°");

            // urwanie / koniec cięcia (przypadek hole: cięcie 1000…7000 w płycie) — bez odgięć na końcach cięcia
            var h = SectionPlannerGoldenTests.CaseHole();
            h.BarEndTicks = true;
            var rh = SectionPlanner.Plan(h);
            foreach (var b in rh.Bars.Where(b => b.Kind == SectionPlanner.DistKind.Main))
                Assert.That(b.Path.Count, Is.EqualTo(2), b.PosNr);
        }

        [Test]
        public void K6_EndTicks_Off_GoldenUnchanged()
        {
            var r = SectionPlanner.Plan(SectionPlannerGoldenTests.CaseNib());
            Assert.That(r.Bars.Where(b => b.PosNr == "05").All(b => b.Path.Count == 2), Is.True);
            Assert.That(SectionPlanner.Version, Is.EqualTo(2));
        }

        [Test]
        public void K6_VerticalUBar_CrossCut_DotsShiftedByDiameter()
        {
            var inp = SectionPlannerGoldenTests.CaseBeam();
            double sp = (600 - 2 * 63.0) / 3;
            var bu = SectionPlannerGoldenTests.B("09", "BU", false, 223, 1500, 750, 3 * sp, 4, sp, 16, "21", null, new double[] { 750, 245, 750, 0, 0 });
            bu.SymbolSide = "Left";
            inp.Dists.Add(bu);
            var r = SectionPlanner.Plan(inp);
            var dots = r.Dots.Where(d => d.PosNr == "09").ToList();
            Assert.That(dots.Count, Is.EqualTo(8), "4 kolumny × dół / góra");
            Assert.That(dots.Count(d => Math.Abs(d.U - (223 + 16)) < 1e-6), Is.EqualTo(2), "kolumna 223 → 239 (do środka o Ø)");
            Assert.That(dots.Count(d => Math.Abs(d.U - (697 - 16)) < 1e-6), Is.EqualTo(2), "kolumna 697 → 681");
            Assert.That(dots.Where(d => !d.Top).All(d => Math.Abs(d.V - (-375 + 50 + 10 + 8)) < 1e-6), Is.True);
            Assert.That(dots.Where(d => d.Top).All(d => Math.Abs(d.V - (-375 + 50 + 265 - 10 - 8)) < 1e-6), Is.True);
        }

        [Test]
        public void K6_VerticalUBar_AlongCut_ShapeABA_AndDrop()
        {
            var inp = new SectionPlanner.Input
            {
                Outline = Box(0, 0, 6000, 4000), CutAlongX = false, CutCoord = 460, S0 = -100, S1 = 4100, LookSign = 1, Thickness = 225
            };
            inp.Nibs.Add(new SectionPlanner.NibEdge { Vertical = true, OuterCoord = 0, InnerCoord = 160, Lo = 0, Hi = 4000 });
            double sp = (600 - 2 * 63.0) / 3;
            var bu = SectionPlannerGoldenTests.B("09", "BU", false, 223, 45, 750, 3 * sp, 4, sp, 16, "21", null, new double[] { 750, 245, 750, 0, 0 });
            bu.SymbolSide = "Left";
            inp.Dists.Add(bu);
            var dropped = SectionPlannerGoldenTests.B("10", "BU", false, 223, 3205, 750, 3 * sp, 4, sp, 16, "21", null, new double[] { 750, 210, 750, 0, 0 });
            dropped.SymbolSide = "Right";
            inp.Dists.Add(dropped);
            inp.Beams.Add(new SectionPlanner.BeamBand { Handle = "000002AB", S0 = 0, S1 = 4000, Height = 375, StirrupDia = 10, StirrupA = 265 });
            var r = SectionPlanner.Plan(inp);
            var u9 = r.Bars.Single(b => b.PosNr == "09");
            Assert.That(u9.Path.Count, Is.EqualTo(4), "A–B–A");
            var vs = u9.Path.Select(p => p.V).Distinct().OrderBy(v => v).ToList();
            Assert.That(vs, Is.EqualTo(new[] { -307.0, -78.0 }));
            Assert.That(u9.Path[1].U, Is.EqualTo(u9.Path[2].U).Within(1e-9), "tył U pionowo");
            var u10 = r.Bars.Single(b => b.PosNr == "10");
            var v10 = u10.Path.Select(p => p.V).Distinct().OrderBy(v => v).ToList();
            Assert.That(v10[0], Is.EqualTo(-307.0 + 17.5).Within(1e-9), "B − 35 — symetrycznie 17,5");
            Assert.That(v10[1], Is.EqualTo(-78.0 - 17.5).Within(1e-9));
        }
    }
}
