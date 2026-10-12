using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// K4 (pręty boczne + UB SIDE) i K8 planu (K6 — przekrój — cofnięte 2026-10-11)
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
            // 2026-10-12: noga Y w kwadrat narożnika — jej UB SIDE z samym zakładem; noga X — ramię przez kwadrat
            Assert.That(corner.Any(r => r.A >= 600 - 45 + 500 - 1e-9), Is.True, string.Join(",", corner.Select(r => r.A)));
            Assert.That(corner.All(r => r.A >= 500 - 1e-9), Is.True, string.Join(",", corner.Select(r => r.A)));
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

        // ── przekrój (K6 cofnięte 2026-10-11): UB SIDE (BC) i pionowe U-bary (BU) pominięte w przekroju ──

        [TestCase(false)]
        [TestCase(true)]
        public void Section_BcAndBu_Skipped(bool alongX)
        {
            var inp = SectionPlannerGoldenTests.CaseBeam();
            var bc = SectionPlannerGoldenTests.B("09", "BC", alongX, 223, 205, 525, 0, 1, 200, 12, "21", null, new double[] { 525, 485, 525, 0, 0 });
            bc.PairOffset = 474; inp.Dists.Add(bc);
            double sp = (600 - 2 * 63.0) / 3;
            var bu = SectionPlannerGoldenTests.B("10", "BU", alongX, 223, 205, 750, 3 * sp, 4, sp, 16, "21", null, new double[] { 750, 245, 750, 0, 0 });
            bu.SymbolSide = "Left"; inp.Dists.Add(bu);
            var r = SectionPlanner.Plan(inp);
            Assert.That(r.Dots.Any(d => d.PosNr == "09" || d.PosNr == "10"), Is.False);
            Assert.That(r.Bars.Any(b => b.PosNr == "09" || b.PosNr == "10"), Is.False);
            // wersja 4 (2026-10-11: siatka równoległa do belki nie wchodzi w belkę — bez kropek w trzonie; decyzja „do 40mm side cover”
            // zmienia tylko wejście — wersja bez zmian); BC / BU nadal pominięte
            Assert.That(SectionPlanner.Version, Is.EqualTo(4));
        }

        // ── decyzje użytkownika 2026-10-11: pręty płyty RÓWNOLEGŁE do belki nie wchodzą w belkę (E2), PROSTOPADŁE wchodzą w belkę
        //    „jak normalnie do 40mm side cover” (40 od lica zewnętrznego); w nibie jedna pozycja — 3 pręty: 2 górą i 1 dołem
        //    („jedna pozycja bottom in nib i top”, BN „2+1 H10-nn T+B IN NIB”) ──

        /// <summary>
        /// Wejście jak złoty „beam” (belka 160…760, nib 160), ale siatka wg decyzji 2026-10-11: B1 / T1 (prostopadłe do belki) przez
        /// belkę od 200 (lico zewnętrzne 160 + 40), B2 / T2 (równoległe) od 810 / 860; w nibie bez T IN NIB i B IN NIB — jedna pozycja
        /// BN „H10-22-150 T+B IN NIB”: w rzucie 2 pręty (55 i 205 od krawędzi — BeamGeometry.NibPairAcross, co 150 jak T IN NIB — decyzja
        /// 2026-10-12), w przekroju 3 kropki; bez BN górą.
        /// </summary>
        internal static SectionPlanner.Input CaseBeamNoMeshInBeam()
        {
            var inp = SectionPlannerGoldenTests.CaseBeam();
            SectionPlanner.Dist P(string pos) => inp.Dists.First(d => d.PosNr == pos);
            var b1 = P("11"); b1.X0 = 160 + BeamGeometry.MeshBeamSideCover; b1.LengthA = 5960 - b1.X0;
            var t1 = P("111"); t1.X0 = 160 + BeamGeometry.MeshBeamSideCover; t1.LengthA = 5960 - t1.X0;
            var b2 = P("12"); b2.X0 = 810; b2.Count = 26; b2.BarsSpan = 5000;
            var t2 = P("112"); t2.X0 = 860; t2.Count = 25; t2.BarsSpan = 4800;
            inp.Dists.Remove(P("101"));
            inp.Dists.Remove(P("21"));
            var bn = P("22"); bn.Mark = "H12-22 " + BeamGeometry.SuffixNibBoth; bn.Diameter = 10; bn.X0 = 55;
            bn.Count = 2; bn.BarsSpan = BeamGeometry.NibPairSpacing; bn.Spacing = BeamGeometry.NibPairSpacing;
            return inp;
        }

        [TestCase(SectionPlanner.EndStyle.None)]
        [TestCase(SectionPlanner.EndStyle.Tick)]
        public void D20261011_NoMeshDotsInBeam_NibPair2T1B(SectionPlanner.EndStyle st)
        {
            var inp = CaseBeamNoMeshInBeam();
            inp.EndStyle = st;
            var r = SectionPlanner.Plan(inp);
            var mesh = r.Dots.Where(d => d.Mesh).ToList();
            Assert.That(mesh.Where(d => d.U <= 760 + 0.5), Is.Empty,
                        "kropki siatki w belce / nibie: " + string.Join(" ", mesh.Where(d => d.U <= 760.5).Select(d => d.PosNr + "@" + d.U)));
            var b2 = mesh.Where(d => d.PosNr == "12").OrderBy(d => d.U).ToList();
            var t2 = mesh.Where(d => d.PosNr == "112").OrderBy(d => d.U).ToList();
            Assert.That(b2[0].U, Is.EqualTo(810.0).Within(1e-9), "pierwszy B2 = rozkład");
            Assert.That(t2[0].U, Is.EqualTo(860.0).Within(1e-9), "pierwszy T2 = rozkład");
            // pręty prostopadłe do belki (B1 / T1 w widoku) wchodzą w belkę — koniec 40 od lica zewnętrznego (160 + 40 = 200)
            foreach (var pos in new[] { "11", "111" })
            {
                var bars = r.Bars.Where(b => b.PosNr == pos).ToList();
                Assert.That(bars, Is.Not.Empty, pos);
                Assert.That(bars.Min(b => b.Path.Min(q => q.U)), Is.EqualTo(200.0).Within(0.5), pos);
            }
            // bez T IN NIB płyty w nibie belki
            Assert.That(r.Dots.Any(d => d.TinNib), Is.False);
            // pręty w nibie 2T+1B: jedna pozycja (22), 3 kropki — 2 górą pod górnym ramieniem UB 03 (ten sam poziom; 55 w nibie
            // i 150 dalej w płytę — 205, jak T IN NIB, decyzja 2026-10-12), 1 dołem nad dolnym ramieniem UB 03, pod prętem górą przy krawędzi (55)
            var nib = r.Dots.Where(d => d.PosNr == "22").ToList();
            Assert.That(nib.Count, Is.EqualTo(3), string.Join(" ", nib.Select(d => d.U + "," + d.V)));
            Assert.That(nib.All(d => d.U > 0 && d.U < 760 && d.Beam), Is.True, string.Join(" ", nib.Select(d => d.U + "," + d.V)));
            var ub3 = r.Bars.First(b => b.Kind == SectionPlanner.DistKind.NibUB);
            var topN = nib.Where(d => d.Top).OrderBy(d => d.U).ToList();
            var botN = nib.Where(d => !d.Top).ToList();
            Assert.That(topN.Count, Is.EqualTo(2));
            Assert.That(botN.Count, Is.EqualTo(1));
            Assert.That(topN[0].U, Is.EqualTo(55.0).Within(0.5));
            Assert.That(topN[1].U, Is.EqualTo(205.0).Within(0.5));
            Assert.That(Math.Abs(topN[0].V - topN[1].V), Is.LessThan(1e-9));
            Assert.That(topN[0].V, Is.LessThan(ub3.Path.Max(q => q.V)), "pod górnym ramieniem UB 03");
            Assert.That(botN[0].U, Is.EqualTo(55.0).Within(0.5), "dołem pod prętem przy krawędzi");
            Assert.That(botN[0].V, Is.GreaterThan(ub3.Path.Min(q => q.V)), "nad dolnym ramieniem UB 03");
            Assert.That(botN[0].V, Is.LessThan(topN[0].V));
        }

        /// <summary>
        /// Przekrój WZDŁUŻ belki (cięcie x = 460 w trzonie) z siatką wg decyzji 2026-10-11: pręty B1 / T1 (prostopadłe do belki) wchodzą
        /// w belkę do 40 od lica zewnętrznego — ich kropki są w trzonie belki (schemat jak bez belki), B2 / T2 (równoległe, od 810 / 860)
        /// w trzonie nie występują.
        /// </summary>
        [Test]
        public void D20261011_AlongBeam_PerpendicularMeshDotsInTrunk()
        {
            var inp = CaseBeamNoMeshInBeam();
            inp.CutAlongX = false; inp.CutCoord = 460; inp.S0 = -200; inp.S1 = 2500; inp.LookSign = -1;
            inp.Beams[0].S0 = 0; inp.Beams[0].S1 = 4000;
            var r = SectionPlanner.Plan(inp);
            Assert.That(r.Dots.Count(d => d.Mesh && d.PosNr == "11"), Is.GreaterThan(5), "kropki B1 w trzonie belki");
            Assert.That(r.Dots.Count(d => d.Mesh && d.PosNr == "111"), Is.GreaterThan(5), "kropki T1 w trzonie belki");
            Assert.That(r.Bars.Any(b => b.PosNr == "12" || b.PosNr == "112"), Is.False, "równoległe do belki — nie w trzonie");
        }

        /// <summary>Siatka przechodząca przez belkę (złoty „beam”: B2 od 40 — rysunek sprzed decyzji) — schemat kropek także w belce (bez zmian).</summary>
        [Test]
        public void D20261011_MeshThroughBeam_SchemeInBeamUnchanged()
        {
            var r = SectionPlanner.Plan(SectionPlannerGoldenTests.CaseBeam());
            Assert.That(r.Dots.Any(d => d.Mesh && d.U > 160 && d.U < 760), Is.True);
        }
    }
}
