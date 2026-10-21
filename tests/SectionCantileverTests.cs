using System.Collections.Generic;
using System.Linq;
using BricsCadRc.Core;
using NUnit.Framework;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// RC_SECTION — wsporniki (prośba Bartka 2026-10-22: przekrój rysuje wszystko na płycie). Wzór PLOT 28 D-D: wspornik 923 × 300
    /// z wierzchem 75 poniżej płyty (SSL 8.600, TOC 8.525), strefa przy palu 600 × 375 (wierzch płyty), płyta 225.
    /// </summary>
    [TestFixture]
    public class SectionCantileverTests
    {
        /// <summary>Płyta 8000 × 4000 z języczkiem wspornika 600 × 788 na dolnej krawędzi (x 4000…4600), nib 135 wzdłuż krawędzi,
        /// cięcie pionowe x = 4300 wzdłuż wspornika.</summary>
        private static SectionPlanner.Input DDCase()
        {
            var inp = new SectionPlanner.Input
            {
                Outline = new List<(double X, double Y)> { (0, 0), (4000, 0), (4000, -788), (4600, -788), (4600, 0), (8000, 0), (8000, 4000), (0, 4000) },
                CutAlongX = false, CutCoord = 4300, S0 = -1200, S1 = 2000, LookSign = 1, Thickness = 225, NibHeight = 150, Ssl = "8.600"
            };
            inp.Nibs.Add(new SectionPlanner.NibEdge { Vertical = false, OuterCoord = 0, InnerCoord = 135, Lo = 0, Hi = 8000 });
            // wspornik: od końca (−788) do uskoku (135); strefa przy palu 135…735
            inp.Beams.Add(new SectionPlanner.BeamBand { Handle = "187", S0 = -788, S1 = 135, Height = 375, TopDrop = 75, Cantilever = true,
                                                     NibWidth = 0, NibHeight = 0, StirrupDia = 12, StirrupA = 215 });
            inp.Beams.Add(new SectionPlanner.BeamBand { Handle = "187", S0 = 135, S1 = 735, Height = 375, TopDrop = 0, Cantilever = true,
                                                     NibWidth = 0, NibHeight = 0, StirrupDia = 12, StirrupA = 290 });
            // U-bar H16 (21) wzdłuż Y w 4 kolumnach (4063 … 4537 co 158), tył na końcu wspornika (−743), ramię 1050
            double sp = 474.0 / 3;
            var ub = SectionPlannerGoldenTests.B("07", "BU", false, 4063, -743, 1050, 474, 4, sp, 16, "21", null, new double[] { 1050, 190, 1050, 0, 0 });
            ub.SymbolSide = "Left";
            inp.Dists.Add(ub);
            return inp;
        }

        [Test]
        public void DD_Profile_CantileverTopBelowSlab_PileCapDeep()
        {
            var r = SectionPlanner.Plan(DDCase());
            var sg = r.Segments.Single();
            Assert.That(sg.Prof, Is.Not.Null);
            // wspornik 923: wierzch −75, spód −375; strefa przy palu 600: wierzch 0, spód −375; dalej płyta 225
            Assert.That(sg.Prof.Any(p => System.Math.Abs(p.U1 - p.U0 - 923) < 1e-6 && p.Top == -75 && p.Bot == -375), Is.True);
            Assert.That(sg.Prof.Any(p => System.Math.Abs(p.U1 - p.U0 - 600) < 1e-6 && p.Top == 0 && p.Bot == -375), Is.True);
            Assert.That(sg.Prof.Any(p => p.Top == 0 && p.Bot == -225), Is.True);
            // wymiar obniżenia wierzchu (75) i wysokości wspornika (300) przy końcu
            Assert.That(r.Dims.Any(d => d.Vertical && d.V1 == 0 && d.V2 == -75), Is.True);
            Assert.That(r.Dims.Any(d => d.Vertical && d.V1 == -75 && d.V2 == -375), Is.True);
        }

        [Test]
        public void DD_UBarInView_UShapeWithBack()
        {
            var r = SectionPlanner.Plan(DDCase());
            var u = r.Bars.Single(b => b.PosNr == "07");
            Assert.That(u.Path.Count, Is.EqualTo(4), "ramię dołem, tył, ramię górą");
            // ramiona: dół na strzemieniu −375 + 45 + 12 + 8 = −310, góra o własne B − Ø wyżej (190 − 16 = 174 → −136)
            Assert.That(u.Path.Select(p => p.V).Distinct().OrderBy(v => v), Is.EqualTo(new[] { -310.0, -136.0 }));
        }

        [Test]
        public void CC_Across_UBarDots_TopAndBottom_NoDuplicates()
        {
            var inp = DDCase();
            inp.CutAlongX = true; inp.CutCoord = -400; inp.S0 = 3000; inp.S1 = 5600;
            inp.Beams.Clear();
            inp.Beams.Add(new SectionPlanner.BeamBand { Handle = "187", S0 = 4000, S1 = 4600, Height = 375, TopDrop = 75, Cantilever = true,
                                                     NibWidth = 0, NibHeight = 0, StirrupDia = 12, StirrupA = 215 });
            // drugi U-bar od strony płyty (ta sama pozycja) — zakład w miejscu cięcia: jedna kropka na kolumnę i poziom
            var ub2 = SectionPlannerGoldenTests.B("07", "BU", false, 4063, -743, 1050, 474, 4, 474.0 / 3, 16, "21", null, new double[] { 1050, 190, 1050, 0, 0 });
            inp.Dists.Add(ub2);
            var r = SectionPlanner.Plan(inp);
            var sg = r.Segments.Single();
            Assert.That(sg.U1 - sg.U0, Is.EqualTo(600).Within(1e-6), "przekrój tylko przez wspornik (języczek obrysu)");
            var dots = r.Dots.Where(d => d.PosNr == "07").ToList();
            Assert.That(dots.Count, Is.EqualTo(8), "4 kolumny × dół / góra");
            Assert.That(dots.Count(d => d.Top), Is.EqualTo(4));
        }

        [Test]
        public void NestedLink_SecondDirectionInside()
        {
            // strefa przy palu, cięcie wzdłuż wspornika: strzemię 290 (w poprzek — kropki) i 265 (wzdłuż — w widoku kształt)
            var inp = DDCase();
            inp.Dists.Clear();
            var l290 = BarGeometryBuilder.GetLocalPoints("51", new double[] { 290, 370, 0, 0, 0 }, 12);
            var l265 = BarGeometryBuilder.GetLocalPoints("51", new double[] { 265, 370, 0, 0, 0 }, 12);
            inp.Dists.Add(SectionPlannerGoldenTests.B("12", "BL", true, 4045, 200, 370, 450, 4, 150, 12, "51", l290, new double[] { 290, 370, 0, 0, 0 }));
            inp.Dists.Add(SectionPlannerGoldenTests.B("14", "BL", false, 4100, 180, 370, 400, 4, 133, 12, "51", l265, new double[] { 265, 370, 0, 0, 0 }));
            var r = SectionPlanner.Plan(inp);
            // strzemię w poprzek osi przecięte przez dolne i górne ramię — kropki (uwaga Bartka 2026-10-22), wysokość z pręta (A 290)
            var outer = r.Dots.Where(d => d.PosNr == "12").ToList();
            var inner = r.Bars.Where(b => b.PosNr == "14").ToList();
            Assert.That(outer.Count, Is.GreaterThan(0));
            Assert.That(r.Bars.Any(b => b.PosNr == "12"), Is.False, "bez pionowych ramion");
            Assert.That(inner.Count, Is.GreaterThan(0));
            double oBot = outer.Min(d => d.V) - 6, oTop = outer.Max(d => d.V) + 6;
            Assert.That(oTop - oBot, Is.EqualTo(290).Within(1e-6), "kropki na lico zewnętrzne = A pręta");
            double iBot = inner.Min(b => b.Path.Min(p => p.V)), iTop = inner.Max(b => b.Path.Max(p => p.V));
            Assert.That(iBot, Is.GreaterThan(oBot), "wewnętrzne wyżej od dołu");
            Assert.That(iTop, Is.LessThan(oTop), "wewnętrzne niżej od góry");
        }

        private static SectionPlanner.Dist SideU()
        {
            var d = SectionPlannerGoldenTests.B("08", "BC", false, 4063, -743, 850, 0, 1, 150, 12, "21", null, new double[] { 850, 485, 850, 0, 0 });
            d.SymbolSide = "Left"; d.PairOffset = 473; d.Layers = 1;
            return d;
        }

        [Test]
        public void SideU_DotsAtBack_AlongCut_AndArms_AcrossCut()
        {
            var inp = DDCase();
            inp.Dists.Add(SideU());
            var r = SectionPlanner.Plan(inp);
            var side = r.Dots.Where(d => d.PosNr == "08").ToList();
            Assert.That(side.Count, Is.EqualTo(1), "D-D: tył UB SIDE przecina płaszczyznę cięcia — kropka na poziomie pręta bocznego");
            Assert.That(side[0].Side, Is.True);
            // C-C (w poprzek, y = −400): kropki obu ramion
            var cc = DDCase();
            cc.CutAlongX = true; cc.CutCoord = -400; cc.S0 = 3000; cc.S1 = 5600;
            cc.Beams.Clear();
            cc.Beams.Add(new SectionPlanner.BeamBand { Handle = "187", S0 = 4000, S1 = 4600, Height = 375, TopDrop = 75, Cantilever = true,
                                                    NibWidth = 0, NibHeight = 0, StirrupDia = 12, StirrupA = 215 });
            cc.Dists.Add(SideU());
            var r2 = SectionPlanner.Plan(cc);
            Assert.That(r2.Dots.Count(d => d.PosNr == "08"), Is.EqualTo(2));
        }

        [Test]
        public void UBar_FreeEnds_AreRealEnds()
        {
            var r = SectionPlanner.Plan(DDCase());
            var u = r.Bars.Single(b => b.PosNr == "07");
            Assert.That(u.End0 && u.End1, Is.True, "wolne końce ramion — symbol zakończenia jak w pozostałych przekrojach");
        }

        [Test]
        public void Mesh_TopAndBottomInLine_WithBarInCantileverRoot()
        {
            var inp = DDCase();
            inp.Beams[1].PileCap = true;
            inp.Dists.Add(new SectionPlanner.Dist { Mark = "H10-10-200 B2", PosNr = "10", LayerCode = "B2", AlongX = true, X0 = 40, Y0 = 30,
                                                    LengthA = 7920, BarsSpan = 3800, Count = 20, Spacing = 200, Diameter = 10 });
            inp.Dists.Add(new SectionPlanner.Dist { Mark = "H10-103-200 T2", PosNr = "103", LayerCode = "T2", AlongX = true, X0 = 40, Y0 = 40,
                                                    LengthA = 7920, BarsSpan = 3800, Count = 20, Spacing = 200, Diameter = 10 });
            var r = SectionPlanner.Plan(inp);
            var bot = r.Dots.Where(d => d.PosNr == "10" && d.V < -100).Select(d => d.U).ToList();
            var top = r.Dots.Where(d => d.PosNr == "103").Select(d => d.U).ToList();
            Assert.That(top.Count, Is.GreaterThan(3));
            // każda kropka góry ma kropkę dołu w tym samym miejscu (poza nasadą wspornika)
            Assert.That(top.All(t => bot.Any(b => System.Math.Abs(b - t) < 1.0)), Is.True);
        }
    }
}
