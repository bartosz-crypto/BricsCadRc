using System.Globalization;
using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// RC_SECTION z belką (plan belek E5, docs/PLAN_BELKI_2026-10-10.md): profil betonu (spód w pasie belki na −H, nib belki przy
    /// spodzie), pręty belki (kropki 4 H16, strzemię 51 w widoku; cięcie wzdłuż belki — pręty w widoku, strzemiona jako pionowe
    /// ramiona), wymiary belki, odcisk (składnik BEAM, „belka nieuwzględniona”), długości cięcia prętów belki (E6).
    /// </summary>
    [TestFixture]
    public class SectionBeamTests
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ── cięcie prostopadłe do belki (CaseBeam — szósty złoty przypadek) ──

        [Test]
        public void Perpendicular_Profile_BeamBottomAndNibAtEdge()
        {
            var r = SectionPlanner.Plan(SectionPlannerGoldenTests.CaseBeam());
            var sg = r.Segments.Single();
            Assert.That(sg.Prof, Is.Not.Null);
            // nib belki 0…160: −375…−225; trzon 160…760: −375…0; płyta 760…: −225…0
            Assert.That(sg.Prof[0], Is.EqualTo((0.0, 160.0, -375.0, -225.0)));
            Assert.That(sg.Prof[1], Is.EqualTo((160.0, 760.0, -375.0, 0.0)));
            Assert.That(sg.Prof[2], Is.EqualTo((760.0, 3000.0, -225.0, 0.0)));
            Assert.That(sg.Polygon.Take(4), Is.EqualTo(new[] { (0.0, -375.0), (760.0, -375.0), (760.0, -225.0), (3000.0, -225.0) }));
            Assert.That(sg.Polygon.Skip(sg.Polygon.Count - 4), Is.EqualTo(new[] { (160.0, 0.0), (160.0, -225.0), (0.0, -225.0) }.Prepend((3000.0, 0.0))));
            Assert.That(r.UsedBeams.Single().Handle, Is.EqualTo("000002AA"));
        }

        [Test]
        public void Perpendicular_Dots_4H16_BottomOnLink_TopUnderLinkTop()
        {
            var r = SectionPlanner.Plan(SectionPlannerGoldenTests.CaseBeam());
            var bb = r.Dots.Where(d => d.PosNr == "05").OrderBy(d => d.U).ToList();
            var bt = r.Dots.Where(d => d.PosNr == "103").OrderBy(d => d.U).ToList();
            Assert.That(bb.Count, Is.EqualTo(4));
            Assert.That(bt.Count, Is.EqualTo(4));
            // otuliny 2026-10-10: dół 50, boki 45 — dół: −375 + 50 + 10 + 8 = −307; góra: wierzch strzemienia −375 + 50 + 265 = −60,
            // pod nim −60 − 10 − 8 = −78; w poprzek 160 + 45 + 10 + 8 = 223 … 697 co 158
            Assert.That(bb.All(d => Math.Abs(d.V + 307) < 1e-9 && d.Beam && !d.Mesh && !d.Top), Is.True);
            Assert.That(bt.All(d => Math.Abs(d.V + 78) < 1e-9 && d.Beam && d.Top), Is.True);
            Assert.That(bb.Select(d => Math.Round(d.U, 1)), Is.EqualTo(new[] { 223.0, 381.0, 539.0, 697.0 }));
            // poziomy siatki płyty bez zmian (dB1 / dT1 tylko z B1 / T1)
            Assert.That(r.LevelDiameters, Is.EqualTo((10.0, 12.0)));
            Assert.That(r.Dots.Where(d => d.PosNr == "12").All(d => Math.Abs(d.V + 165) < 1e-9), Is.True);
        }

        [Test]
        public void Perpendicular_Link51_InView_HooksOnTop_OnCover()
        {
            var r = SectionPlanner.Plan(SectionPlannerGoldenTests.CaseBeam());
            var link = r.Bars.Single(b => b.PosNr == "07");
            Assert.That(link.Kind, Is.EqualTo(SectionPlanner.DistKind.Beam));
            // oś: lico zewnętrzne na otulinie 45 od lic belki (160 / 760) i 50 od spodu (−375); wysokość zewnętrzna A = 265, B = 510
            Assert.That(link.Path.Min(p => p.U), Is.EqualTo(210).Within(1e-6));
            Assert.That(link.Path.Max(p => p.U), Is.EqualTo(710).Within(1e-6));
            Assert.That(link.Path.Min(p => p.V), Is.EqualTo(-320).Within(1e-6));
            Assert.That(link.Path.Max(p => p.V), Is.EqualTo(-65).Within(1e-6));
            // haki u góry (koniec osi na górnym ramieniu)
            Assert.That(link.Path.Last().V, Is.EqualTo(-65).Within(1e-6));
            Assert.That(r.Labels.Any(l => l.Text == "07" && l.HasLeader && l.V < -375), Is.True);
        }

        [Test]
        public void Perpendicular_Pile_UnderBeamBottom_Dims_H_Width_Nib()
        {
            var r = SectionPlanner.Plan(SectionPlannerGoldenTests.CaseBeam());
            var pile = r.Piles.Single();
            Assert.That(pile.Base, Is.EqualTo(-375));
            Assert.That(pile.Top, Is.EqualTo(-340));
            // nib belki: uskok 225 (0 → −225) i wysokość 150; H = 375 (dalej od krawędzi), szerokość 600 pod belką; grubość płyty na prawym końcu
            Assert.That(r.Dims.Any(d => d.Vertical && d.V1 == 0 && d.V2 == -225 && d.LineU == -250), Is.True);
            Assert.That(r.Dims.Any(d => d.Vertical && d.V1 == -225 && d.V2 == -375 && d.LineU == -250), Is.True);
            Assert.That(r.Dims.Any(d => d.Vertical && d.V1 == 0 && d.V2 == -375 && d.LineU == -450), Is.True);
            Assert.That(r.Dims.Any(d => !d.Vertical && d.U1 == 160 && d.U2 == 760 && d.LineV == -725), Is.True);
            Assert.That(r.Dims.Any(d => d.Vertical && d.U1 == 3000 && d.V2 == -225), Is.True);
            Assert.That(r.SslMark!.Value.U, Is.EqualTo(-710));
        }

        [Test]
        public void Perpendicular_E4b_SideBars_AndNibBars()
        {
            var r = SectionPlanner.Plan(SectionPlannerGoldenTests.CaseBeam());
            // boczne: po 1 na bok, w połowie między osiami dołu (−307) i góry (−78): −192.5; przy ramionach strzemienia (220 / 700)
            var side = r.Dots.Where(d => d.PosNr == "20").OrderBy(d => d.U).ToList();
            Assert.That(side.Select(d => (d.U, d.V)), Is.EqualTo(new[] { (220.0, -192.5), (700.0, -192.5) }));
            // nib: w narożach spinki UB 03 (ramiona: dół −375 + 45 + 5 = −325, góra −325 + 60 = −265), wewnątrz U:
            // dołem H16 −325 + 5 + 8 = −312, górą H10 −265 − 5 − 5 = −275
            Assert.That(r.Dots.Single(d => d.PosNr == "22").V, Is.EqualTo(-312));
            Assert.That(r.Dots.Single(d => d.PosNr == "21").V, Is.EqualTo(-275));
            Assert.That(r.Dots.Single(d => d.PosNr == "21").U, Is.EqualTo(55));
            // UB 03 (element płyty) na otulinie dołem 45 < otulina dolna belki 50 — K2 (praktyka biura): bez ostrzeżenia, BN dołem od UB 03
            Assert.That(r.Warnings.Any(w => w.StartsWith("UB 03 płyty")), Is.False);
        }

        [Test]
        public void Perpendicular_E4b_NibBars_InUb03Corners_NoOverlap()
        {
            var r = SectionPlanner.Plan(SectionPlannerGoldenTests.CaseBeam());
            var ub = r.Bars.Single(b => b.PosNr == "03");
            Assert.That(ub.Kind, Is.EqualTo(SectionPlanner.DistKind.NibUB));
            foreach (var pos in new[] { "21", "22" })
            {
                var d = r.Dots.Single(x => x.PosNr == pos);
                // odstęp lic pręta BN i spinki ≥ 0 (styk w narożu); w pionie styk z ramieniem
                double gap = DistToPath(ub.Path, d.U, d.V) - (ub.Diameter + d.Diameter) / 2.0;
                Assert.That(gap, Is.GreaterThanOrEqualTo(-1e-6), pos);
                Assert.That(gap, Is.LessThan(10.0), pos);
            }
            // T IN NIB w nibie nie nachodzi na BN górą (odsunięty w poziomie do styku lic)
            var bnT = r.Dots.Single(x => x.PosNr == "21");
            var tin = r.Dots.Where(x => x.TinNib).OrderBy(x => x.U).ToList();
            Assert.That(tin.Count, Is.EqualTo(2));
            foreach (var t in tin)
            {
                double dist = Math.Sqrt((t.U - bnT.U) * (t.U - bnT.U) + (t.V - bnT.V) * (t.V - bnT.V));
                Assert.That(dist, Is.GreaterThanOrEqualTo((t.Diameter + bnT.Diameter) / 2.0 - 1e-6));
            }
            // drugi T IN NIB (190 od krawędzi — w trzonie belki 160…760): poziom jak w płycie (−75 − 30 − 6 = −111), nie pod ramieniem UB 03
            Assert.That(tin[1].V, Is.EqualTo(-111).Within(1e-9));
            Assert.That(tin[0].V, Is.EqualTo(-265 - 5 - 6).Within(1e-9));
        }

        [Test]
        public void Perpendicular_E4b_NibBars_WithoutUb03_FormulaLevels()
        {
            var inp = SectionPlannerGoldenTests.CaseBeam();
            inp.Dists.RemoveAll(d => d.Kind == SectionPlanner.DistKind.NibUB);
            var r = SectionPlanner.Plan(inp);
            // bez UB 03: dołem na otulinie dolnej belki + Ø spinki (−375 + 50 + 10 + 8 = −307), górą pod wierzchem nibu (−225 − 30 − 10 − 5 = −270)
            Assert.That(r.Dots.Single(d => d.PosNr == "22").V, Is.EqualTo(-307));
            Assert.That(r.Warnings.Any(w => w.StartsWith("UB 03 płyty")), Is.False);
            Assert.That(r.Dots.Single(d => d.PosNr == "21").V, Is.EqualTo(-270));
        }

        private static double DistToPath(List<(double U, double V)> p, double u, double v)
        {
            double best = double.MaxValue;
            for (int i = 0; i + 1 < p.Count; i++)
            {
                double du = p[i + 1].U - p[i].U, dv = p[i + 1].V - p[i].V, l2 = du * du + dv * dv;
                double t = l2 > 0 ? Math.Max(0, Math.Min(1, ((u - p[i].U) * du + (v - p[i].V) * dv) / l2)) : 0;
                double x = p[i].U + t * du - u, y = p[i].V + t * dv - v;
                best = Math.Min(best, Math.Sqrt(x * x + y * y));
            }
            return best;
        }

        [TestCase(500, 2)]   // H 600: osie 500 − 20 − 16 = 464 → 3 odstępy
        [TestCase(650, 3)]   // H 750: 614 → 4 odstępy (od światła 598 wychodziło 2 — rozstaw ~205)
        [TestCase(350, 1)]   // H 450: 314
        [TestCase(275, 1)]   // H 375
        [TestCase(200, 0)]   // H 300: 164
        public void E4b_SideBarCount(double stirrupA, int expected)
        {
            int n = BeamGeometry.SideBarCount(stirrupA, 10, 16, 16, 200);
            Assert.That(n, Is.EqualTo(expected));
            // rozstaw oś–oś (dół → boczne → góra) ≤ 200
            Assert.That((stirrupA - 20 - 16) / (n + 1), Is.LessThanOrEqualTo(200.0));
        }

        [Test]
        public void E4b_LapFor_SameMultipleOfDiameter()
        {
            Assert.That(BeamGeometry.LapFor(800, 16, 10), Is.EqualTo(500));
            Assert.That(BeamGeometry.LapFor(800, 16, 16), Is.EqualTo(800));
        }

        [Test]
        public void E4b_NibBarLine_EdgeEnds_ToSlabFaceWithCover()
        {
            // belka wzdłuż Y przy lewej krawędzi (nib 160), końce przy krawędziach płyty (nib zawinięty)
            var outline = new List<(double X, double Y)> { (0, 0), (6000, 0), (6000, 4000), (0, 4000) };
            var zone = new List<(double X, double Y)> { (160, 160), (760, 160), (760, 3840), (160, 3840) };
            var an = BeamGeometry.Analyze(zone, outline);
            var s = an.Segments.Single();
            // w poprzek — naroże spinki UB 03 (płyta, otulina 40 bez zmian): 40 + 10 + 8 = 58; wzdłuż — otulina czołowa 45
            var l16 = BeamGeometry.NibBarLine(s, outline, 16);
            Assert.That(l16, Is.EqualTo((58.0, 45.0, 3955.0)));
            Assert.That(BeamGeometry.NibBarLine(s, outline, 10)!.Value.Across, Is.EqualTo(55));
            var (c0, c1) = BeamGeometry.SideBarsAcross(s, 10);
            Assert.That((c0, c1), Is.EqualTo((220.0, 700.0)));          // 160 + 45 + 10 + 5
        }

        [Test]
        public void E4b_NibBarLine_NoEdge_Null()
        {
            var outline = new List<(double X, double Y)> { (0, 0), (6000, 0), (6000, 4000), (0, 4000) };
            var zone = new List<(double X, double Y)> { (2000, 1000), (2600, 1000), (2600, 3000), (2000, 3000) };   // belka wewnątrz płyty
            var s = BeamGeometry.Analyze(zone, outline).Segments.Single();
            Assert.That(BeamGeometry.NibBarLine(s, outline, 16), Is.Null);
        }

        // ── bez belek: wynik bit w bit ──

        [Test]
        public void NoBeams_OrBandOffCut_ResultBitIdentical()
        {
            string a = SectionPlannerGoldenTests.Dump(SectionPlanner.Plan(SectionPlannerGoldenTests.CaseNib()));
            var inp = SectionPlannerGoldenTests.CaseNib();
            inp.Beams.Add(new SectionPlanner.BeamBand { Handle = "1", S0 = 7000, S1 = 7600, Height = 450 });   // poza zakresem cięcia
            var r = SectionPlanner.Plan(inp);
            Assert.That(SectionPlannerGoldenTests.Dump(r), Is.EqualTo(a));
            Assert.That(r.UsedBeams, Is.Empty);
            Assert.That(r.Segments.All(s => s.Prof == null), Is.True);
        }

        [Test]
        public void BeamBars_WithoutBand_NotDrawn_NotUsed()
        {
            // pręty belki w rysunku, linia cięcia nie przecina strefy (brak pasa) — przekrój jak bez nich
            string a = SectionPlannerGoldenTests.Dump(SectionPlanner.Plan(SectionPlannerGoldenTests.CaseNib()));
            var inp = SectionPlannerGoldenTests.CaseNib();
            inp.Dists.Add(SectionPlannerGoldenTests.B("45", "BB", false, 218, 40, 3920, 484, 4, 161.3, 16));
            var r = SectionPlanner.Plan(inp);
            Assert.That(SectionPlannerGoldenTests.Dump(r), Is.EqualTo(a));
            Assert.That(r.UsedDists.Any(d => d.PosNr == "45"), Is.False);
        }

        // ── cięcie wzdłuż belki ──

        /// <summary>Belka wzdłuż X przy dolnej krawędzi (nib), H = 300 (jak 539), koniec lewy przy krawędzi (nib zawinięty), prawy wewnętrzny (x = 5000).</summary>
        private static SectionPlanner.Input AlongCase()
        {
            var inp = new SectionPlanner.Input
            {
                Outline = new List<(double X, double Y)> { (0, 0), (8000, 0), (8000, 4000), (0, 4000) },
                CutAlongX = true, CutCoord = 460, S0 = -500, S1 = 8500, LookSign = 1, Thickness = 225, NibHeight = 150
            };
            inp.Nibs.Add(new SectionPlanner.NibEdge { Vertical = false, OuterCoord = 0, InnerCoord = 160, Lo = 0, Hi = 8000 });
            inp.Dists.Add(new SectionPlanner.Dist { Mark = "H10-11-200 B1", PosNr = "11", LayerCode = "B1", AlongX = true, X0 = 40, Y0 = 40,
                                                    LengthA = 7920, BarsSpan = 3920, Count = 20, Spacing = 200, Diameter = 10 });
            inp.Dists.Add(new SectionPlanner.Dist { Mark = "H10-12-200 B2", PosNr = "12", LayerCode = "B2", AlongX = false, X0 = 40, Y0 = 40,
                                                    LengthA = 3920, BarsSpan = 7920, Count = 40, Spacing = 200, Diameter = 10 });
            double sp = 474.0 / 3;      // otulina boczna 45: 160 + 63 = 223 … 697 co 158
            inp.Dists.Add(SectionPlannerGoldenTests.B("05", "BB", true, 200, 223, 4750, 474, 4, sp, 16));
            inp.Dists.Add(SectionPlannerGoldenTests.B("103", "BT", true, 200, 223, 4750, 474, 4, sp, 16));
            var l51 = BarGeometryBuilder.GetLocalPoints("51", new double[] { 190, 510, 0, 0, 0 }, 10);
            inp.Dists.Add(SectionPlannerGoldenTests.B("07", "BL", false, 280, 205, 510, 4600, 24, 200, 10, "51", l51, new double[] { 190, 510, 0, 0, 0 }));
            inp.Beams.Add(new SectionPlanner.BeamBand { Handle = "000003BB", S0 = 160, S1 = 5000, Height = 300, NibHeight = 150, StirrupDia = 10, StirrupA = 190 });
            return inp;
        }

        [Test]
        public void Along_Profile_NibWrappedAtEdgeEnd_InternalEndStepsUp()
        {
            var r = SectionPlanner.Plan(AlongCase());
            var sg = r.Segments.Single();
            Assert.That(sg.Prof, Is.EqualTo(new[] { (0.0, 160.0, -300.0, -150.0), (160.0, 5000.0, -300.0, 0.0), (5000.0, 8000.0, -225.0, 0.0) }));
            // H 300 przy końcu z nibem (dalej od krawędzi), długość pasa pod belką
            Assert.That(r.Dims.Any(d => d.Vertical && d.V1 == 0 && d.V2 == -300 && d.LineU == -450), Is.True);
            Assert.That(r.Dims.Any(d => !d.Vertical && d.U1 == 160 && d.U2 == 5000 && d.LineV == -650), Is.True);
        }

        [Test]
        public void Along_LongitudinalBarsInView_LinksAsVerticalLegs()
        {
            var r = SectionPlanner.Plan(AlongCase());
            var bb = r.Bars.Single(b => b.PosNr == "05");
            var bt = r.Bars.Single(b => b.PosNr == "103");
            // dół: −300 + 50 + 10 + 8 = −232; góra: −300 + 50 + 190 − 10 − 8 = −78
            Assert.That(bb.Path, Is.EqualTo(new[] { (200.0, -232.0), (4950.0, -232.0) }));
            Assert.That(bt.Path, Is.EqualTo(new[] { (200.0, -78.0), (4950.0, -78.0) }));
            var links = r.Bars.Where(b => b.PosNr == "07").ToList();
            Assert.That(links.Count, Is.EqualTo(24));
            Assert.That(links.All(b => b.Path.Count == 2 && b.Path[0].U == b.Path[1].U && b.Path[0].V == -245 && b.Path[1].V == -65), Is.True);
            // jeden opis strzemion (ze strzałką do ramienia środkowego), pod spodem belki
            var lbl = r.Labels.Where(l => l.Text == "07").ToList();
            Assert.That(lbl.Count, Is.EqualTo(1));
            Assert.That(lbl[0].HasLeader && lbl[0].V < -300 && Math.Abs(lbl[0].LeaderV - (-245 - 5)) < 1e-9, Is.True);
            // siatka płyty: B1 w widoku na swoim poziomie (−225 + 40 + 5 + 5 — pręty proste, przez belkę), B2 — kropki
            Assert.That(r.Bars.Single(b => b.PosNr == "11").Path.All(p => p.V == -175), Is.True);
            Assert.That(r.Dots.Where(d => d.PosNr == "12").All(d => d.V == -165), Is.True);
        }

        [Test]
        public void UBar_PairOffset_SecondArm_And_BentDotOverStraight()
        {
            // cięcie prostopadłe do belki (wzdłuż Y) przez zakład U-bara z prętem prostym: kropki obu ramion U-bara, „piętrowo” nad prętem prostym
            var inp = SectionPlannerGoldenTests.CaseBeam();
            inp.Dists.Add(SectionPlannerGoldenTests.B("09", "BB", false, 223, 1500, 792, 0, 1, 200, 16, "21", null, new double[] { 800, 490, 800, 0, 0 }));
            inp.Dists[inp.Dists.Count - 1].PairOffset = 474;
            var r = SectionPlanner.Plan(inp);
            var u = r.Dots.Where(d => d.PosNr == "09").OrderBy(d => d.U).ToList();
            Assert.That(u.Select(d => Math.Round(d.U, 1)), Is.EqualTo(new[] { 223.0, 697.0 }));
            Assert.That(u.All(d => d.Bent && Math.Abs(d.V - (-307 + 16)) < 1e-9), Is.True);
        }

        [Test]
        public void CutIntervals_LShapedZone()
        {
            var l = new List<(double X, double Y)> { (0, 0), (3000, 0), (3000, 600), (600, 600), (600, 3000), (0, 3000) };
            Assert.That(SectionPlanner.CutIntervals(l, true, 300), Is.EqualTo(new[] { (0.0, 3000.0) }));
            Assert.That(SectionPlanner.CutIntervals(l, true, 1500), Is.EqualTo(new[] { (0.0, 600.0) }));
            Assert.That(SectionPlanner.CutIntervals(l, false, 300), Is.EqualTo(new[] { (0.0, 3000.0) }));
            Assert.That(SectionPlanner.CutIntervals(l, true, 3500), Is.Empty);
        }

        // ── odcisk (SectionFingerprint) ──

        private static SectionFingerprint.Source Src(SectionPlanner.Input inp)
            => new SectionFingerprint.Source
            {
                RefX = inp.CutAlongX ? inp.S0 : inp.CutCoord, RefY = inp.CutAlongX ? inp.CutCoord : inp.S0,
                Views = new List<(double X, double Y)> { (0, 0), (0, 12000) }, Letter = "A", DescThickness = inp.Thickness, DescSsl = inp.Ssl
            };

        private static SectionPlanner.Input Tagged(SectionPlanner.Input inp)
        {
            for (int i = 0; i < inp.Dists.Count; i++) inp.Dists[i].SourceHandle = (0x1A0 + i).ToString("X8");
            return inp;
        }

        private static SectionFingerprint.State StateOf(SectionPlanner.Input inp) => SectionFingerprint.Build(inp, SectionPlanner.Plan(inp), Src(inp));

        /// <summary>
        /// Skróty odcisków 5 złotych wejść (z handle rozkładów jak SectionFingerprintTests) policzone kodem HEAD ecaa083 (przed E5) —
        /// brak belek = ten sam odcisk (przekroje narysowane przed E5 na płytach bez belek zostają aktualne).
        /// </summary>
        private static readonly Dictionary<string, string> HeadHash = new Dictionary<string, string>
        {
            ["nib"]      = "679f6c622a3f04d86868e41ee77ad3cfc0417a9209b230b2e6ebad22ae93ea33", ["hole"] = "cab4c0365446a9552ebdbed9358b64d9113b100393eae47db93c45ea02d925d8", ["piles"] = "8add151f701826cd2ee78e3bdbf387c61475671e32001c48f8190e881354f0d0", ["vertical"] = "66942c97c0019cf9e66778ae4f0d2d16a958f2e9a9c9040fa1602be1152b2648", ["fitshape"] = "f2c82263885cdd96ea203fa98f2912e1146916eefb425681d991e84389b3fb5e",
        };

        [TestCase("nib")] [TestCase("hole")] [TestCase("piles")] [TestCase("vertical")] [TestCase("fitshape")]
        public void NoBeams_SameHashAsBeforeE5(string name)
        {
            var make = SectionPlannerGoldenTests.Cases().Single(c => (string)c.Arguments[0]! == name).Arguments[1] as Func<SectionPlanner.Input>;
            var st = StateOf(Tagged(make!()));
            // wersja 3 (zakończenia prętów Z1–Z6, 2026-10-11) zmienia tylko składnik META (Ver) — przy stylu None reszta odcisku
            // bez zmian: META z „Ver:1” = dawny skrót HEAD
            var old = new SortedDictionary<string, string>(st.Components);
            old["META"] = old["META"].Replace("Ver:" + SectionPlanner.Version, "Ver:1");
            Assert.That(old["META"], Does.Contain("Ver:1"));
            Assert.That(SectionFingerprint.Hash(old), Is.EqualTo(HeadHash[name]));
            Assert.That(st.Components.Keys.Any(k => k.StartsWith("BEAM:")), Is.False);
        }

        [Test]
        public void BeamComponent_OnlyWhenBeamCrossesCut_RelativeToP1()
        {
            var st = StateOf(Tagged(SectionPlannerGoldenTests.CaseBeam()));
            Assert.That(st.Components.Keys.Count(k => k.StartsWith("BEAM:")), Is.EqualTo(1));
            var f = SectionFingerprint.Fields(st.Components["BEAM:000002AA"]);
            Assert.That(f["S0"], Is.EqualTo("660.0"));      // 160 − P1 (−500)
            Assert.That(f["S1"], Is.EqualTo("1260.0"));
            Assert.That(f["H"], Is.EqualTo("375.0"));
            // pręty belki w odcisku (DIST, warstwa BB / BT / BL)
            Assert.That(st.Components.Values.Count(v => v.Contains("Kind:Beam")), Is.EqualTo(6));   // BB, BT, BL, BS, BN × 2
        }

        [Test]
        public void Compare_BeamHeightChanged_Stale_WithReason()
        {
            var a = StateOf(Tagged(SectionPlannerGoldenTests.CaseBeam()));
            var inp = Tagged(SectionPlannerGoldenTests.CaseBeam());
            inp.Beams[0].Height = 450; inp.Beams[0].StirrupA = 340;
            var v = SectionFingerprint.Compare(a, StateOf(inp));
            Assert.That(v.Stale, Is.True);
            Assert.That(v.Reasons, Has.Some.StartsWith("zmieniona belka 000002AA (").And.Contains("H 375 → 450"));
        }

        [Test]
        public void Compare_SectionBeforeE5_OnSlabWithBeam_Stale_BeamNotIncluded()
        {
            // przekrój sprzed E5: planer bez belek i bez prętów belki (PrepareSection je pomijał)
            var old = Tagged(SectionPlannerGoldenTests.CaseBeam());
            old.Beams.Clear();
            old.Dists.RemoveAll(d => d.Kind == SectionPlanner.DistKind.Beam);
            var stored = StateOf(old);
            var v = SectionFingerprint.Compare(stored, StateOf(Tagged(SectionPlannerGoldenTests.CaseBeam())));
            Assert.That(v.Stale, Is.True);
            Assert.That(v.Reasons, Has.Some.Contains("belka 000002AA (H 375) nieuwzględniona w przekroju"));
        }

        [Test]
        public void Compare_BeamParamsOnly_NoEffectOnDrawing_Current()
        {
            var a = StateOf(Tagged(SectionPlannerGoldenTests.CaseBeam()));
            var inp = Tagged(SectionPlannerGoldenTests.CaseBeam());
            inp.Beams[0].Params = "W600|inne";
            var b = StateOf(inp);
            Assert.That(b.Hash, Is.Not.EqualTo(a.Hash));
            Assert.That(b.Digest, Is.EqualTo(a.Digest));
            var v = SectionFingerprint.Compare(a, b);
            Assert.That(v.Stale, Is.False);
            Assert.That(v.Info, Has.Some.Contains("zmieniona belka 000002AA (parametry)"));
        }

        [Test]
        public void Compare_BeamRemoved_Stale()
        {
            var a = StateOf(Tagged(SectionPlannerGoldenTests.CaseBeam()));
            var inp = Tagged(SectionPlannerGoldenTests.CaseBeam());
            inp.Beams.Clear();
            var v = SectionFingerprint.Compare(a, StateOf(inp));
            Assert.That(v.Stale, Is.True);
            Assert.That(v.Reasons, Has.Some.StartsWith("usunięta belka 000002AA"));
        }

        // ── E6: długości cięcia prętów belki (BS 8666:2020, ShapeCodeLibrary) i masa jak RC_COUNT_BBS ──

        [TestCase("51", 10, 265, 510, 0, 1725)]    // 528: 2A + 2B + max(16d, 160) = 530 + 1020 + 160 = 1710 → 1725 (otuliny 45 / 50)
        [TestCase("51", 10, 190, 510, 0, 1575)]    // 539: 1560 → 1575
        [TestCase("51", 10, 340, 510, 0, 1875)]    // 319: 1860 → 1875
        [TestCase("51", 10, 275, 520, 0, 1750)]    // dawne otuliny 40 / 40
        [TestCase("11", 16, 1450, 1350, 0, 2775)]  // A + B − 0.5r − d = 2800 − 16 − 16 = 2768 → 2775
        [TestCase("11", 16, 1375, 1375, 0, 2725)]  // 2750 − 32 = 2718 → 2725
        [TestCase("21", 16, 800, 490, 800, 2050)]  // A + B + C − r − 2d = 2090 − 32 − 32 = 2026 → 2050 (528, otulina 45)
        [TestCase("21", 16, 775, 170, 775, 1675)]  // 1720 − 64 = 1656 → 1675
        [TestCase("21", 16, 800, 500, 800, 2050)]  // dawne: 2100 − 64 = 2036 → 2050
        [TestCase("21", 16, 775, 175, 775, 1675)]  // dawne: 1725 − 64 = 1661 → 1675
        [TestCase("00", 16, 5500, 0, 0, 5500)]
        public void BeamBars_CuttingLength_BS8666_2020(string shape, int d, double a, double b, double c, double expected)
        {
            Assert.That(BarLengthRules.CuttingLength(shape, d, a, b, c, 0, 0), Is.EqualTo(expected));
            Assert.That(BarLengthRules.TotalLengthFor(shape, new[] { a, b, c }, d), Is.EqualTo(expected));
        }

        [Test]
        public void BeamBars_CountRow_MassFromCuttingLength()
        {
            // RC_COUNT_BBS z wiersza RC_BBS: masa z długości cięcia (strzemię 51: 1750, nie A = 275); seria 101+ = TOP
            var links = BbsCountRules.FromBbs(7, 10, "51", false, 275, 1750, 51, null, 0.617);
            Assert.That(links.TotalMassKg, Is.EqualTo(51 * 1.75 * 0.617).Within(1e-9));
            Assert.That(links.Mark, Is.EqualTo("H10-07"));
            Assert.That(links.LayerCode, Is.EqualTo("BOTTOM"));
            var top = BbsCountRules.FromBbs(103, 16, "00", true, 5500, 5500, 8, null, 1.578);
            Assert.That(top.TotalMassKg, Is.EqualTo(8 * 5.5 * 1.578).Within(1e-9));
            Assert.That(top.LayerCode, Is.EqualTo("TOP"));
        }
    }
}
