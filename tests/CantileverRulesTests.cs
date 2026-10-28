using System.Collections.Generic;
using System.Linq;
using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>CantileverRules — wykrywanie wsporników (decyzja Bartka 2026-10-21, etap 1); dane z rysunków PLOT 28, PLOT 13-14, BLOCK C.</summary>
    [TestFixture]
    public class CantileverRulesTests
    {
        private static List<(double X, double Y)> P(params double[] xy)
        {
            var l = new List<(double X, double Y)>();
            for (int i = 0; i + 1 < xy.Length; i += 2) l.Add((xy[i], xy[i + 1]));
            return l;
        }

        [Test]
        public void Plot28_TabFoundAndRemoved()
        {
            // obrys zewnętrzny PLOT 28 (rzut dolny): języczek GB 600 × 788 na dolnej krawędzi y = 8415
            var poly = P(-40802, 8415, -40802, 7627, -40202, 7627, -40202, 8415, -38584, 8415, -38584, 7740, -35782, 7740, -35782, 8415,
                         -35107, 8415, -35107, 14817, -44322, 14817, -44322, 13017, -44997, 13017, -44997, 10215, -44322, 10215, -44322, 8415);
            var tabs = CantileverRules.FindTabs(poly);
            Assert.That(tabs.Count, Is.EqualTo(1), "wycięcie 2802 × 675 (występ) poza zakresem szerokości");
            var u = tabs[0].Shape;
            Assert.That(u.Width, Is.EqualTo(600).Within(1e-6));
            Assert.That(u.Depth, Is.EqualTo(788).Within(1e-6));
            Assert.That(CantileverRules.DirCode(u.Out), Is.EqualTo("-Y"));
            var clean = CantileverRules.RemoveTabs(poly, tabs);
            Assert.That(clean.Count, Is.EqualTo(12));
            Assert.That(clean.Any(q => q.Y < 7700), Is.False, "bez końca języczka");
            Assert.That(clean.Any(q => q.X == -40802 || q.X == -40202), Is.False, "nasady usunięte (krawędź prosta)");
        }

        [Test]
        public void Plot362_TabWithCollinearVertex()
        {
            // uwaga Bartka 2026-10-26: PLOT 362 (GA 357-362) — dolny języczek 600 × 275 z wierzchołkiem współliniowym (…677.437, −368522.199)
            var poly = P(5973805.175, -360072.148, 5973805.175, -369512.148, 5966952.675, -369512.148, 5966952.675, -369042.559,
                         5966677.437, -369042.559, 5966677.437, -368522.199, 5966677.437, -368442.559, 5966952.675, -368442.559,
                         5966952.675, -363182.225, 5966677.437, -363182.225, 5966677.437, -362582.225, 5966952.675, -362582.225,
                         5966952.675, -360072.148, 5973805.175, -360072.148);
            var tabs = CantileverRules.FindTabs(poly);
            Assert.That(tabs.Count, Is.EqualTo(2));
            Assert.That(tabs.All(t => Math.Abs(t.Shape.Width - 600) < 1 && Math.Abs(t.Shape.Depth - 275.238) < 1), Is.True);
            Assert.That(tabs.All(t => t.IdxE0 >= 0 && t.IdxE1 >= 0), Is.True);
            var clean = CantileverRules.RemoveTabs(poly, tabs);
            Assert.That(clean.Count, Is.EqualTo(4), "prostokąt bez języczków i bez punktu współliniowego");
            Assert.That(clean.Any(q => q.X < 5966900), Is.False);
            // rekord: te same języczki odnajdowane w obrysie (RecordedTabs → FindTabs)
            var eff = CantileverRules.EffectiveOutline(poly, tabs.Select(t => CantileverRules.TabRec.Of(t.Shape)));
            Assert.That(eff.Count, Is.EqualTo(4));
        }

        [Test]
        public void BayWindowAndSmallNotch_NotCantilevers()
        {
            // występ okna 2128 × 788 (PLOT 13-14) i wcięcie 645 × 100 (BLOCK D) — nie wsporniki
            var poly = P(0, 0, 10000, 0, 10000, 5000, 6000, 5000, 6000, 5788, 3872, 5788, 3872, 5000, 2000, 5000, 2000, 5100, 1355, 5100, 1355, 5000, 0, 5000);
            Assert.That(CantileverRules.FindTabs(poly), Is.Empty);
        }

        [Test]
        public void BlockC_ManyTabsInRow()
        {
            var pts = new List<(double X, double Y)> { (0, 0) };
            double x = 1000;
            for (int k = 0; k < 5; k++, x += 3150)
            {
                pts.Add((x, 0)); pts.Add((x, -1525)); pts.Add((x + 600, -1525)); pts.Add((x + 600, 0));
            }
            pts.Add((20000, 0)); pts.Add((20000, 8000)); pts.Add((0, 8000));
            var tabs = CantileverRules.FindTabs(pts);
            Assert.That(tabs.Count, Is.EqualTo(5));
            Assert.That(tabs.All(t => t.Shape.Depth == 1525 && t.Shape.Width == 600), Is.True);
            Assert.That(CantileverRules.RemoveTabs(pts, tabs).Count, Is.EqualTo(4));
        }

        [Test]
        public void Plot13_UPieces_StubAndRoot()
        {
            var stub = CantileverRules.TryU(P(13014, 4087, 13014, 3270, 13614, 3270, 13614, 4087));
            var root = CantileverRules.TryU(P(13014, 4087, 13014, 4822, 13614, 4822, 13614, 4087));
            Assert.That(CantileverRules.InRange(stub), Is.True);
            Assert.That(stub.Depth, Is.EqualTo(817).Within(1e-6));
            Assert.That(CantileverRules.IsRootOf(root, stub), Is.True);
            Assert.That(CantileverRules.IsRootOf(stub, stub), Is.False);
            Assert.That(CantileverRules.TryU(P(0, 0, 0, 100, 600, 100, 600, 50)), Is.Null, "ramiona różnej długości");
        }

        [Test]
        public void Texts()
        {
            Assert.That(CantileverRules.ParseGb("GB-600x300mm deep\nTOC=8.525"), Is.EqualTo((600.0, 300.0)));
            Assert.That(CantileverRules.ParseGb("GB-600x225mm deep"), Is.EqualTo((600.0, 225.0)));
            Assert.That(CantileverRules.ParseGb("375THK"), Is.Null);
            Assert.That(CantileverRules.ParseToc("GB-600x300mm deep TOC=8.525"), Is.EqualTo(8.525).Within(1e-9));
            Assert.That(CantileverRules.ParseToc("NIB TOC=8.575"), Is.Null);
        }

        [Test]
        public void Plan_Plot28_WithRoot()
        {
            // GB 600 × 300, wysięg 788, strefa przy palu 375THK × 735
            var p = CantileverRules.PlanBars(788, 600, 300, 735, 375);
            Assert.That(p.Columns, Is.EqualTo(4));
            Assert.That(p.Links.Count, Is.EqualTo(3));
            var ls = p.Links[0];
            Assert.That((ls.Dia, ls.Spacing, ls.A, ls.Legs, ls.B), Is.EqualTo((12, 150.0, 215.0, 4, 370.0)));
            var lr = p.Links[1];
            Assert.That((lr.Dia, lr.Spacing, lr.A), Is.EqualTo((12, 200.0, 290.0)));
            Assert.That(p.UBars.Count, Is.EqualTo(2));
            Assert.That(p.UBars.All(u => u.A == 1075 && u.Dia == 16), Is.True, "zakład ≥ 700 (2026-10-23), długość 2300 — moduł 50");
            Assert.That(p.UBars[0].Back, Is.EqualTo(738).Within(1e-9), "otulina wspornika 50 (uwaga Bartka 2026-10-28)");
            Assert.That(p.UBars[1].Back, Is.EqualTo(-678).Within(1e-9), "tył w środku strzemion strefy: −735 + 45 + 12");
            Assert.That(p.Straight, Is.Empty);
        }

        [Test]
        public void Plan_Plot28_FromStep_ShapesAndPileCapBothDirections()
        {
            // PLOT 28 od uskoku (linia nibu 135 od krawędzi): wspornik 923, strefa przy palu 600 (rysunek biura: 600 / 923)
            var p = CantileverRules.PlanBars(923, 600, 300, 600, 375);
            Assert.That(p.Links.Count, Is.EqualTo(3));
            Assert.That((p.Links[0].Shape, p.Links[0].AlongAxis), Is.EqualTo(("63", false)), "wspornik poza płytą — 63");
            Assert.That((p.Links[1].Shape, p.Links[1].AlongAxis), Is.EqualTo(("51", false)), "strefa przy palu — 51");
            var l2 = p.Links[2];
            Assert.That((l2.Shape, l2.AlongAxis, l2.L0, l2.L1), Is.EqualTo(("51", true, -600.0, 0.0)), "drugi kierunek w strefie przy palu");
            // drugi kierunek wewnątrz pierwszego (uwaga Bartka 2026-10-22: 290 − 2·12 = 266 → 265 — w dół, żeby wewnętrzne się zmieściło)
            Assert.That((l2.Dia, l2.A, l2.Legs, l2.B), Is.EqualTo((12, 265.0, 4, 370.0)));
            Assert.That(p.Links[1].A, Is.EqualTo(290.0));
            Assert.That(l2.S0, Is.EqualTo(0)); Assert.That(l2.S1, Is.EqualTo(600));
            Assert.That(l2.First, Is.GreaterThan(0)); Assert.That(l2.First + (l2.Count - 1) * l2.Step, Is.LessThan(600));
            Assert.That(p.UBars.All(u => u.A == 1075), Is.True);
            Assert.That(p.UBars.All(u => u.B == 190), Is.True, "U-bar w strzemieniu: 215 − 24 = 191 → 190 (w dół, jak rysunek biura)");
            Assert.That(CantileverRules.PlanBars(840, 450, 225, 0, 0).Links.Single().Shape, Is.EqualTo("63"));
        }

        [Test]
        public void Plan_Test4_Deep_TwoLegLinks_SideUBars()
        {
            // test4.dwg: GB 600 × 525, pile cap 600THK; od uskoku wspornik 652,5, strefa 600 — zasady belek (H > 375 → 2-cięte, boki)
            var p = CantileverRules.PlanBars(652.5, 600, 525, 600, 600);
            // uwaga Bartka 2026-10-28: wspornik — otulina boczna 50 (600 − 100 = 500), strefa przy palu — 45 (510)
            Assert.That((p.Links[0].Legs, p.Links[0].B, p.Links[0].A, p.Links[0].Shape), Is.EqualTo((2, 500.0, 440.0, "63")));
            Assert.That((p.Links[1].Legs, p.Links[1].B, p.Links[1].A, p.Links[1].Shape), Is.EqualTo((2, 510.0, 515.0, "51")));
            Assert.That((p.Links[2].Legs, p.Links[2].B, p.Links[2].A), Is.EqualTo((2, 510.0, 490.0)), "drugi kierunek: 515 − 24 → 490");
            Assert.That(p.SideLevels, Is.EqualTo(1));
            Assert.That(p.SideUBars.Count, Is.EqualTo(2));
            Assert.That(p.SideUBars.All(u => u.B == 475 && u.Dia == 12), Is.True, "B = 500 − 24 → 475 (otulina wspornika 50, 2026-10-28)");
            Assert.That(p.SideUBars[0].Back, Is.EqualTo(p.UBars[0].Back - 16).Within(1e-9));
            Assert.That(p.SideUBars[1].Back, Is.EqualTo(p.UBars[1].Back + 16).Within(1e-9));
            Assert.That(p.SideStraight, Is.Empty);
            Assert.That(CantileverRules.PlanBars(923, 600, 300, 600, 375).SideLevels, Is.EqualTo(0), "h 300 — bez prętów bocznych");
        }

        [Test]
        public void SecondDirLinkA_Minus2DiaDownTo5()
        {
            Assert.That(CantileverRules.SecondDirLinkA(290, 12), Is.EqualTo(265));
            Assert.That(CantileverRules.SecondDirLinkA(215, 12), Is.EqualTo(190));
            Assert.That(CantileverRules.SecondDirLinkA(140, 10), Is.EqualTo(120)); Assert.That(CantileverRules.SecondDirLinkA(665, 12), Is.EqualTo(640));
        }

        [Test]
        public void TabRecord_OutlineKeptOnDrawing_EffectiveWithoutTab()
        {
            // obrys z języczkiem jak w GA (PLOT 28) — rysunek bez zmian, obliczenia bez języczka
            var poly = P(0, 0, 4000, 0, 4000, -788, 4600, -788, 4600, 0, 10000, 0, 10000, 8000, 0, 8000);
            var u = CantileverRules.FindTabs(poly).Single().Shape;
            var rec = CantileverRules.TabRec.Parse(CantileverRules.TabRec.Of(u).ToString());
            Assert.That((rec.Dir, rec.Width, rec.Depth), Is.EqualTo(("-Y", 600.0, 788.0)));
            var eff = CantileverRules.EffectiveOutline(poly, new[] { rec });
            Assert.That(eff.Count, Is.EqualTo(4));
            Assert.That(CantileverRules.FindTabs(eff), Is.Empty);
            // przesunięty obrys (MOVE) — rekord bez współrzędnych nadal pasuje
            var moved = poly.Select(q => (q.X + 12345.0, q.Y - 777.0)).ToList();
            Assert.That(CantileverRules.EffectiveOutline(moved, new[] { rec }).Count, Is.EqualTo(4));
            // inny języczek (bez rekordu) zostaje; brak rekordu — null (obrys jak na rysunku)
            Assert.That(CantileverRules.EffectiveOutline(poly, new[] { new CantileverRules.TabRec { Dir = "+X", Width = 600, Depth = 788 } }), Is.Null);
            Assert.That(CantileverRules.EffectiveOutline(poly, null), Is.Null);
            // przywrócenie: obrys przecięty + InsertTab = języczek z rekordem
            var back = CantileverRules.InsertTab(eff, u);
            Assert.That(CantileverRules.RecordedTabs(back, new[] { rec }).Count, Is.EqualTo(1));
        }

        [Test]
        public void StepCut_NibLineNearEdge()
        {
            // krawędź y = 8415, koniec wspornika niżej (7627), linia uskoku 8550 (w płycie, 135) i dalsza krawędź 20000
            Assert.That(CantileverRules.StepCut(new[] { 8550.0, 20000.0, 7000.0 }, 8415, 7627), Is.EqualTo(8550));
            Assert.That(CantileverRules.StepCut(new[] { 9000.0 }, 8415, 7627), Is.Null, "dalej niż 300 — nie uskok");
            Assert.That(CantileverRules.StepCut(new[] { 8415.0 }, 8415, 7627), Is.Null, "na krawędzi");
            // wspornik w +X: koniec 5000, krawędź 4000, uskok 3865
            Assert.That(CantileverRules.StepCut(new[] { 3865.0, 4100.0 }, 4000, 5000), Is.EqualTo(3865));
            Assert.That(CantileverRules.StepCut(null, 4000, 5000), Is.Null);
        }

        [Test]
        public void Plan_BlockC_LongArm_StraightBars()
        {
            // GB 600 × 225, wysięg 1525, strefa 300THK × 735 → ramię 1425 > 1300: U-bary 750 + pręty proste H16
            var p = CantileverRules.PlanBars(1525, 600, 225, 735, 300);
            Assert.That(p.Links[0].Dia, Is.EqualTo(10));
            Assert.That(p.Links[0].Spacing, Is.EqualTo(100));
            Assert.That(p.UBars.All(u => u.A == 810), Is.True, "Ceil25(700 + 90) = 800 → 810 (długość 1700 — moduł 50)");
            Assert.That(p.Straight.Count, Is.EqualTo(1));
            var st = p.Straight[0];
            Assert.That(st.Dia, Is.EqualTo(16));
            // zakład z oboma U-barami ≥ 700 (2026-10-23)
            Assert.That(p.UBars[1].LineEnd - st.S0, Is.GreaterThanOrEqualTo(700 - 1e-6));
            Assert.That(st.S1 - p.UBars[0].LineStart, Is.GreaterThanOrEqualTo(700 - 1e-6));
        }

        [Test]
        public void Plan_NoRoot_StraightH12IntoSlab()
        {
            var p = CantileverRules.PlanBars(840, 450, 225, 0, 0);
            Assert.That(p.Columns, Is.EqualTo(3));
            Assert.That(p.Links.Count, Is.EqualTo(1));
            Assert.That(p.UBars.Count, Is.EqualTo(1));
            Assert.That(p.Straight.Count, Is.EqualTo(1));
            var st = p.Straight[0];
            Assert.That(st.Dia, Is.EqualTo(12));
            Assert.That(st.S0, Is.LessThanOrEqualTo(-1000 + 1e-6), "min. 1000 za krawędź");
            Assert.That(st.S1 - p.UBars[0].LineStart, Is.GreaterThanOrEqualTo(650 - 1e-6), "zakład z U-barem");
            Assert.That(CantileverRules.PlanBars(800, 800, 300, 0, 0).Columns, Is.EqualTo(5));
        }

        [Test]
        public void InsertTab_RestoresOutline()
        {
            var poly = P(0, 0, 10000, 0, 10000, 8000, 0, 8000);
            var u = CantileverRules.TryU(P(4000, 0, 4000, -788, 4600, -788, 4600, 0));
            var r = CantileverRules.InsertTab(poly, u);
            Assert.That(r.Count, Is.EqualTo(8));
            Assert.That(CantileverRules.FindTabs(r).Count, Is.EqualTo(1));
        }

        [Test]
        public void NestRootLinksGlobal_XAxisCantilever_XBarsOuter()
        {
            // test7: wspornik poziomy (+X), strefa 375 — pręty wzdłuż X (drugi kierunek, wzdłuż osi) zewnętrzne 290, wzdłuż Y wewnętrzne 265
            var p = CantileverRules.PlanBars(1243, 600, 300, 600, 375);
            CantileverRules.NestRootLinksGlobal(p, axisAlongX: true);
            Assert.That(p.Links[2].AlongAxis, Is.True);
            Assert.That(p.Links[2].A, Is.EqualTo(290.0));
            Assert.That(p.Links[1].A, Is.EqualTo(265.0));
            // wspornik w osi Y — bez zmian (w poprzek = pręty X — zewnętrzne)
            var q = CantileverRules.PlanBars(923, 600, 300, 600, 375);
            CantileverRules.NestRootLinksGlobal(q, axisAlongX: false);
            Assert.That((q.Links[1].A, q.Links[2].A), Is.EqualTo((290.0, 265.0)));
        }
    
        [Test]
        public void Test10_HorizontalCantilever_SingleRootLink_Lap700_Module50()
        {
            // test10-poprawiony (decyzja Bartka 2026-10-23): wspornik poziomy — strefa przy palu 375THK: jedna pozycja w obu kierunkach,
            // A = 290 − 15 = 275, co 150 (4 co 150 na 508, reszta po równo); U-bary zakład ≥ 700, długość całkowita co 50
            var p = CantileverRules.PlanBars(1243, 600, 300, 600, 375, singleRootLink: true);
            var root = p.Links.Where(l => l.Shape == "51").ToList();
            Assert.That(root.Count, Is.EqualTo(2));
            // uwaga Bartka 2026-10-28 (PLOT 344-346): rozłożone wzdłuż strefy — co 200 (3 na 508), w poprzek — co 150 (4)
            Assert.That(root.Where(l => !l.AlongAxis).Select(l => (l.A, l.B, l.Spacing, l.Step, l.Count)).Single(), Is.EqualTo((275.0, 370.0, 200.0, 200.0, 3)));
            Assert.That(root.Where(l => l.AlongAxis).Select(l => (l.A, l.B, l.Spacing, l.Step, l.Count)).Single(), Is.EqualTo((275.0, 370.0, 150.0, 150.0, 4)));
            Assert.That(p.SingleRootLink, Is.True);
            CantileverRules.NestRootLinksGlobal(p, axisAlongX: true);
            Assert.That(root.All(l => l.A == 275), Is.True, "bez zagnieżdżenia przy jednej pozycji");
            foreach (var u in p.UBars)
                Assert.That(BarLengthRules.TotalLengthFor("21", new[] { u.A, u.B, u.A }, u.Dia) % 50.0, Is.EqualTo(0).Within(1e-9));
            Assert.That(2 * p.UBars[0].A - (p.UBars[0].Back - p.UBars[1].Back), Is.GreaterThanOrEqualTo(700 - 1e-6), "zakład U-barów ≥ 700");
            Assert.That(p.UBars.All(u => u.B == 190), Is.True, "B U-bara bez −15 (decyzja 2026-10-23)");
            // wspornik pionowy — jak dotąd (zewnętrzne 290 + wewnętrzne 265)
            var q = CantileverRules.PlanBars(1243, 600, 300, 600, 375);
            Assert.That(q.Links.Where(l => l.Shape == "51").Select(l => l.A), Is.EqualTo(new[] { 290.0, 265.0 }));
        }

        [Test]
        public void UBarArmForModule_TotalLengthEvery50()
        {
            // 1275 / 175 / 1275 H16 → 2675; ramiona w górę: 1285 → 2700 (screen Bartka 2026-10-23)
            Assert.That(BarLengthRules.TotalLengthFor("21", new[] { 1275.0, 175, 1275 }, 16), Is.EqualTo(2675));
            Assert.That(BeamGeometry.UBarArmForModule(1275, 175, 16), Is.EqualTo(1285), "1285 → 2681 → 2700");
            Assert.That(BeamGeometry.UBarArmForModule(1275, 190, 16), Is.EqualTo(1275), "2700 — już w module");
            // ramię ograniczone — w dół
            double a = BeamGeometry.UBarArmForModule(1275, 175, 16, maxArm: 1275);
            Assert.That(a, Is.LessThan(1275));
            Assert.That(BarLengthRules.TotalLengthFor("21", new[] { a, 175, a }, 16) % 50.0, Is.EqualTo(0).Within(1e-9));
        }
        // uwagi Bartka 2026-10-26 (PLOT 357-362): krótki wspornik (wysięg 411 od uskoku, „300THK / TOC=21.250”, strefa 375THK × 600) —
        // zamiast U-barów H16 strzemiona wzdłuż osi na całej długości co 120; strzemiona strefy przy palu standardowo co 150
        [Test]
        public void ShortCantilever_LinksInsteadOfUBars()
        {
            var p = CantileverRules.PlanBars(411, 600, 300, 600, 375, singleRootLink: true);
            Assert.That(p.UBars, Is.Empty, "bez U-barów H16");
            Assert.That(p.ShortMainLinks, Is.True);
            var full = p.Links.Single(l => l.AlongAxis && l.L1 > 1);
            Assert.That(full.Step, Is.EqualTo(120).Within(1e-9));
            Assert.That(full.Count, Is.EqualTo(5));
            Assert.That(full.L0, Is.EqualTo(-600).Within(1e-9));
            Assert.That(full.L1, Is.EqualTo(411).Within(1e-9));
            Assert.That(full.Legs, Is.EqualTo(2));
            // otuliny końców: strefa przy palu 45, koniec wspornika 50 (2026-10-28)
            Assert.That(full.B, Is.EqualTo(Math.Floor((1011 - BeamGeometry.DefaultCoverSide - CantileverRules.OutsideCover) / 5.0) * 5.0).Within(1e-9));
            Assert.That((full.CoverL0, full.CoverL1), Is.EqualTo((45.0, 50.0)));
            var rootAlong = p.Links.Single(l => l.AlongAxis && l.L1 <= 1);
            Assert.That(rootAlong.Step, Is.EqualTo(150).Within(1e-9), "strefa przy palu — co 150");
            var longer = CantileverRules.PlanBars(923, 600, 300, 600, 375, singleRootLink: true);
            Assert.That(longer.UBars, Is.Not.Empty, "dłuższy wspornik bez zmian");
            Assert.That(longer.Links.Count(l => l.AlongAxis), Is.EqualTo(1));
        }

        // uwaga Bartka 2026-10-28: strzemiona strefy przy palu wzdłuż strefy co 200; gdy wejdą mniej niż 3 — co 150
        [Test]
        public void RootLinksAlongZone_200_UnlessFewerThan3()
        {
            var p = CantileverRules.PlanBars(411, 600, 300, 735, 375, singleRootLink: true);
            var r = p.Links.Single(l => l.Shape == "51" && !l.AlongAxis);
            Assert.That((r.Step, r.Count), Is.EqualTo((200.0, 4)));
            var q = CantileverRules.PlanBars(411, 600, 300, 400, 375, singleRootLink: true);
            var rq = q.Links.Single(l => l.Shape == "51" && !l.AlongAxis);
            Assert.That(rq.Step, Is.EqualTo(150.0), "400 − 2·46 = 308 → przy 200 tylko 2 → co 150");
            Assert.That(rq.CoverAcross, Is.EqualTo(45.0));
            Assert.That(p.Links.Single(l => l.Shape == "63").CoverAcross, Is.EqualTo(50.0));
        }

        [TestCase("300THK\nTOC=21.250", 300.0)]
        [TestCase("375THK", null)]
        [TestCase("GB-600x450mm deep\nTOC=21.250", null)]
        public void ParseThkToc(string t, double? h)
            => Assert.That(CantileverRules.ParseThkToc(t.Replace("\\n", "\n")), Is.EqualTo(h));
    }
}
