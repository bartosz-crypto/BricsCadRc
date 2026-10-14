using System.Globalization;
using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// Decyzje użytkownika 2026-10-11 (moduł belki): A — listy średnic w oknie RC_BEAM (H12 … H32, strzemiona H10 / H12; wartość spoza
    /// listy jako „(nietypowa)”), zakład 40·Ø i promienie gięcia BS 8666:2020 dla H20–H32; B — strzemiona 4-cięte (dwa identyczne
    /// zachodzące strzemiona 51 jedną pozycją, „2x29 H12-nn-200 LINKS”, przekrój — 2 strzemiona, 1 opis); C — próg 4-ciętych wg
    /// EN 1992-1-1 §9.2.2(8) (s_t,max = 0,75·d ≤ 600) tylko dla belek krawędziowych, w płycie Auto = 2-cięte.
    /// </summary>
    [TestFixture]
    public class BeamLinkLegsTests
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly List<(int n, int d)> H16x4 = new List<(int n, int d)> { (4, 16) };

        // ── A: listy średnic ──

        [Test]
        public void DiameterItems_List_AndUnusualValueShown()
        {
            var main = BeamParams.DiameterItems(BeamParams.MainDiameters, 16);
            Assert.That(main.Select(x => x.Text), Is.EqualTo(new[] { "H12", "H16", "H20", "H22", "H25", "H32" }));
            var odd = BeamParams.DiameterItems(BeamParams.MainDiameters, 14);
            Assert.That(odd.Select(x => x.Text), Is.EqualTo(new[] { "H12", "H14 (nietypowa)", "H16", "H20", "H22", "H25", "H32" }));
            Assert.That(BeamParams.DiameterItems(BeamParams.LinkDiameters, 12).Select(x => x.Dia), Is.EqualTo(new[] { 10, 12 }));
            Assert.That(BeamParams.DiameterItems(BeamParams.LinkDiameters, 8).Select(x => x.Text), Is.EqualTo(new[] { "H8 (nietypowa)", "H10", "H12" }));
            Assert.That(new BeamParams().StirrupDia, Is.EqualTo(12));
            Assert.That(new BeamParams().BottomDia, Is.EqualTo(16));
            Assert.That(new BeamParams().StirrupLegs, Is.EqualTo(BeamParams.LegsAuto));
        }

        [TestCase(12, 500.0)]
        [TestCase(16, 650.0)]
        [TestCase(20, 800.0)]
        [TestCase(22, 900.0)]
        [TestCase(25, 1000.0)]
        [TestCase(32, 1300.0)]
        public void Lap_40d_Ceil25(int d, double lap) => Assert.That(new BeamParams().LapFor(d), Is.EqualTo(lap));

        [TestCase(20, 70.0)]
        [TestCase(25, 87.0)]
        [TestCase(32, 112.0)]
        [TestCase(22, 77.0)]   // H22 spoza Tabeli 2 BS 8666:2020 — 3,5·d
        public void BendRadius_BS8666(int d, double r) => Assert.That(BarShape.MinBendRadius(d), Is.EqualTo(r));

        [Test]
        public void UBar_H20_Lengths_BS8666()
        {
            // U-bar pionowy 21: A + B + C − r − 2d (r = 70 dla H20), w górę do 25
            double a = 900, b = 265, c = 900;
            Assert.That(BarLengthRules.CuttingLength("21", 20, a, b, c, 0, 0), Is.EqualTo(Math.Ceiling((a + b + c - 70 - 40) / 25.0) * 25.0));
            // strzemię 51 H12 4-cięte 290 × 355: 2A + 2B + max(16d, 160) = 580 + 710 + 192 = 1482 → 1500
            Assert.That(BarLengthRules.CuttingLength("51", 12, 290, 355, 0, 0, 0), Is.EqualTo(1500));
        }

        // ── B: geometria 4-ciętych ──

        [TestCase(2, 0)]
        [TestCase(3, 2)]
        [TestCase(4, 3)]
        [TestCase(5, 4)]
        [TestCase(6, 4)]
        [TestCase(7, 5)]
        [TestCase(8, 6)]
        public void FourLegK_Rule(int n, int k)
        {
            Assert.That(BeamGeometry.FourLegK(n), Is.EqualTo(k));
            if (k > 0) Assert.That(n - k + 1, Is.LessThanOrEqualTo(k), "strzemiona zachodzą co najmniej na 1 pręt");
        }

        [Test]
        public void FourLegWidth_And_LegSpacing_600()
        {
            // 600, 4 H16, H12, otulina 45: osie 65 … 535 co 156,7; B = 2·156,7 + 16 + 24 = 353,3 → 355
            Assert.That(BeamGeometry.FourLegWidth(600, 4, 16, 3, 12, 45), Is.EqualTo(355));
            Assert.That(BeamGeometry.FourLegWidth(450, 4, 16, 3, 12, 45), Is.EqualTo(255));
            Assert.That(BeamGeometry.LegSpacing(510, 0, 12), Is.EqualTo(498));
            Assert.That(BeamGeometry.LegSpacing(510, 355, 12), Is.EqualTo(188));
            Assert.That(BeamGeometry.LegSpacing(360, 255, 12), Is.EqualTo(138));
        }

        [Test]
        public void LinkLegLimit_600x600()
        {
            var (d, st) = BeamGeometry.LinkLegLimit(600, 50, 12, 16);
            Assert.That(d, Is.EqualTo(530));
            Assert.That(st, Is.EqualTo(397.5));
            Assert.That(BeamGeometry.LinkLegLimit(2000, 50, 12, 16).StMax, Is.EqualTo(600));
        }

        // ── C: próg normy (tabela raportu) ──

        // decyzja użytkownika 2026-10-12: Auto — 4-cięte (zachodzące, B = 370) tylko H ≤ 375; wyższe 2-cięte (przekroczenie — ostrzeżenie)
        [TestCase(600, 600, true, 2, 397.5, 498.0, true)]
        [TestCase(450, 450, true, 2, 285.0, 348.0, true)]
        [TestCase(450, 375, true, 4, 228.75, 348.0, false)]
        [TestCase(600, 375, true, 4, 228.75, 498.0, false)]   // Holbrook 528 — 2 × 370, rozstaw ramion 218
        [TestCase(600, 300, true, 4, 172.5, 498.0, true)]     // Holbrook 539 — 218 > 172,5
        [TestCase(600, 450, true, 2, 285.0, 498.0, true)]     // Holbrook 319 / C12 PLOT 17 — 2-cięte
        [TestCase(300, 750, true, 2, 510.0, 198.0, false)]
        [TestCase(600, 600, false, 2, 397.5, 498.0, false)]   // belka w płycie — Auto 2-cięte, bez ostrzeżenia
        [TestCase(600, 375, false, 4, 228.75, 498.0, false)]   // belka niska (H ≤ 375) — zawsze 2 zachodzące (2026-10-12)
        public void PlanLinkLegs_Auto(double width, double h, bool edge, int legs, double stMax, double sp2, bool exceeded)
        {
            var r = BeamGeometry.PlanLinkLegs(BeamParams.LegsAuto, h, width, 45, 50, 12, H16x4, edge);
            Assert.That(r.Legs, Is.EqualTo(legs));
            Assert.That(r.StMax, Is.EqualTo(stMax).Within(1e-9));
            Assert.That(r.Spacing2, Is.EqualTo(sp2));
            Assert.That(r.Exceeded, Is.EqualTo(exceeded));
            if (legs == 4) Assert.That(r.TemplateB, Is.EqualTo(width == 600 ? 370 : 255));
            if (!edge && legs == 2) Assert.That(r.Note, Does.Contain("belka w płycie — 2-cięte"));
            if (h <= BeamGeometry.ShallowBeamMaxH) Assert.That(r.Note, Does.Contain("belka niska"));
        }

        [Test]
        public void PlanLinkLegs_Message_600x600()
        {
            var r = BeamGeometry.PlanLinkLegs(BeamParams.LegsAuto, 600, 600, 45, 50, 12, H16x4);
            Assert.That(r.Note, Does.StartWith("600×600: d = 530, s_t,max = 0,75·d = 397.5"));
            Assert.That(r.Note, Does.Contain("rozstaw ramion 2-ciętych 498 > 397.5 → 2-cięte"));
            Assert.That(r.Note, Does.Contain("OSTRZEŻENIE"));
            var r4 = BeamGeometry.PlanLinkLegs(BeamParams.Legs4, 600, 600, 45, 50, 12, H16x4);
            Assert.That(r4.Note, Does.Contain("pręty 1–3 i 2–4"));
            Assert.That(r4.B4, Is.EqualTo(370));
        }

        [Test]
        public void PlanLinkLegs_Manual_OverridesAuto()
        {
            // ręczne 2-cięte przy przekroczeniu (krawędziowa) — ostrzeżenie, nie blokada
            var r2 = BeamGeometry.PlanLinkLegs(BeamParams.Legs2, 600, 600, 45, 50, 12, H16x4, true);
            Assert.That(r2.Legs, Is.EqualTo(2));
            Assert.That(r2.Exceeded, Is.True);
            Assert.That(r2.Note, Does.Contain("OSTRZEŻENIE"));
            // ręczne 2-cięte w płycie — bez ostrzeżenia
            var r2i = BeamGeometry.PlanLinkLegs(BeamParams.Legs2, 600, 600, 45, 50, 12, H16x4, false);
            Assert.That(r2i.Exceeded, Is.False);
            // ręczne 4-cięte w płycie i przy H 750 (Auto dałoby 2) — 4-cięte
            Assert.That(BeamGeometry.PlanLinkLegs(BeamParams.Legs4, 600, 600, 45, 50, 12, H16x4, false).Legs, Is.EqualTo(4));
            Assert.That(BeamGeometry.PlanLinkLegs(BeamParams.Legs4, 750, 300, 45, 50, 12, H16x4, true).Legs, Is.EqualTo(4));
            // 2 pręty w warstwie — 4-cięte niemożliwe
            var r22 = BeamGeometry.PlanLinkLegs(BeamParams.Legs4, 600, 600, 45, 50, 12, new List<(int n, int d)> { (2, 16) });
            Assert.That(r22.Legs, Is.EqualTo(2));
            Assert.That(r22.Note, Does.Contain("niemożliwe"));
        }

        [Test]
        public void PlanLinkLegs_BottomTopDifferent_WidestB()
        {
            // dół 4 H16, góra 5 H20: k = 3 i 4 → B = max(370, obejmujące) (belka niska — Auto 4-cięte)
            var r = BeamGeometry.PlanLinkLegs(BeamParams.LegsAuto, 375, 600, 45, 50, 12, new List<(int n, int d)> { (4, 16), (5, 20) });
            Assert.That(r.Legs, Is.EqualTo(4));
            Assert.That(r.B4, Is.EqualTo(Math.Max(370, BeamGeometry.FourLegWidth(600, 5, 20, 4, 12, 45))));
        }

        // ── B: opis i krotność ──

        [Test]
        public void Label_Links4Leg_2xN()
        {
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeLinks, 29, 58), Is.EqualTo("2x29"));
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeLinks, 29, null), Is.EqualTo("29"));   // 2-cięte bez zmian
            Assert.That(BarLabelRules.KeepsMultiplicity(BeamGeometry.CodeLinks), Is.True);
        }

        // ── B: przekrój — 2 strzemiona, 1 opis; 2-cięte bez zmian ──

        private static SectionPlanner.Input Case4Leg()
        {
            var inp = SectionPlannerGoldenTests.CaseBeam();
            var bl = inp.Dists.Single(d => d.PosNr == "07");
            bl.ShapeLocal = BarGeometryBuilder.GetLocalPoints("51", new double[] { 265, 355, 0, 0, 0 }, 10);
            bl.SourceDims = new double[] { 265, 355, 0, 0, 0 };
            bl.Layers = 2;
            return inp;
        }

        [Test]
        public void Section_FourLeg_TwoLinksInView_OneLabel()
        {
            var r = SectionPlanner.Plan(Case4Leg());
            var links = r.Bars.Where(b => b.PosNr == "07").OrderBy(b => b.Path.Min(p => p.U)).ToList();
            Assert.That(links.Count, Is.EqualTo(2));
            // pierwsze przy licu 160 (oś 210 … 210 + 345), drugie przy licu 760 (… 710), zachodzą
            Assert.That(links[0].Path.Min(p => p.U), Is.EqualTo(210).Within(1e-6));
            Assert.That(links[0].Path.Max(p => p.U), Is.EqualTo(555).Within(1e-6));
            Assert.That(links[1].Path.Min(p => p.U), Is.EqualTo(365).Within(1e-6));
            Assert.That(links[1].Path.Max(p => p.U), Is.EqualTo(710).Within(1e-6));
            Assert.That(links[1].Path.Min(p => p.V), Is.EqualTo(links[0].Path.Min(p => p.V)).Within(1e-9));
            // decyzja 2026-10-14: każde strzemię ma swój opis ze strzałką (screen Bartka)
            Assert.That(r.Labels.Count(l => l.Text == "07" && l.HasLeader), Is.EqualTo(2));
        }

        [Test]
        public void Section_FourLeg_PlanDistOnlyB4Wide_SecondLinkAtOtherFace_2026_10_14()
        {
            // w rzucie rozkład 4-ciętych ma szerokość jednego strzemienia (B4), nie całej belki — drugie strzemię i tak przy drugim licu pasa
            var inp = Case4Leg();
            inp.Dists.Single(d => d.PosNr == "07").LengthA = 355;
            var r = SectionPlanner.Plan(inp);
            var links = r.Bars.Where(b => b.PosNr == "07").OrderBy(b => b.Path.Min(p => p.U)).ToList();
            Assert.That(links.Count, Is.EqualTo(2));
            Assert.That(links[0].Path.Min(p => p.U), Is.EqualTo(210).Within(1e-6));
            Assert.That(links[1].Path.Max(p => p.U), Is.EqualTo(710).Within(1e-6));
        }

        [Test]
        public void Section_TwoLeg_Unchanged_LayersOne()
        {
            var a = SectionPlannerGoldenTests.Dump(SectionPlanner.Plan(SectionPlannerGoldenTests.CaseBeam()));
            var inp = SectionPlannerGoldenTests.CaseBeam();
            inp.Dists.Single(d => d.PosNr == "07").Layers = 1;
            Assert.That(SectionPlannerGoldenTests.Dump(SectionPlanner.Plan(inp)), Is.EqualTo(a));
            Assert.That(SectionPlanner.Version, Is.EqualTo(5));   // 2026-10-14: bez odsunięcia zakładów
        }
    }
}
