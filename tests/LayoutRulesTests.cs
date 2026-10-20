using System.Collections.Generic;
using System.Linq;
using BricsCadRc.Core;
using K = BricsCadRc.Core.LayoutRules.Kind;

namespace BricsCadRc.Tests
{
    /// <summary>LayoutRules — gotowe arkusze RC_LAYOUT (decyzja użytkownika 2026-10-19; wymiary z arkuszy wzorcowych PLOT 30 / PLOT 9-10).</summary>
    [TestFixture]
    public class LayoutRulesTests
    {
        private static LayoutRules.Item It(string key, K kind, double w, double h, bool nib = false)
            => new LayoutRules.Item { Key = key, Kind = kind, W = w, H = h, Nib = nib };

        private static void AssertNoOverlapInArea(LayoutRules.Sheet s)
        {
            var area = LayoutRules.Area;
            for (int i = 0; i < s.Placed.Count; i++)
            {
                Assert.That(area.Contains(s.Placed[i].Rect), Is.True, s.Placed[i].Item.Key + " poza polem: " + s.Placed[i].Rect);
                for (int j = i + 1; j < s.Placed.Count; j++)
                    Assert.That(s.Placed[i].Rect.Overlaps(s.Placed[j].Rect), Is.False, s.Placed[i].Item.Key + " / " + s.Placed[j].Item.Key);
            }
        }

        [Test]
        public void Plot30_AllOnOneSheet()
        {
            // PLOT 30 — zakresy jak w LayoutEngine na rysunku wzorcowym (symulacja ezdxf): rzuty, przekroje z notatką, 4 detale PH
            var items = new List<LayoutRules.Item>
            {
                It("B", K.PlanBottom, 325, 312), It("T", K.PlanTop, 326, 316),
                It("A-A", K.Section, 99, 58 + LayoutRules.NibNoteBox, nib: true), It("B-B", K.Section, 122, 58 + LayoutRules.NibNoteBox, nib: true),
                It("PH1", K.Ph, 153, 123), It("PH2", K.Ph, 148, 139), It("PH3", K.Ph, 160, 151), It("PH6", K.Ph, 173, 166),
            };
            var plan = LayoutRules.Arrange(items);
            Assert.That(plan.Sheets.Count, Is.EqualTo(1));
            var s = plan.Sheets[0];
            Assert.That(s.Placed.Count, Is.EqualTo(items.Count));
            AssertNoOverlapInArea(s);
            var b = s.Placed.First(p => p.Item.Key == "B").Rect;
            var t = s.Placed.First(p => p.Item.Key == "T").Rect;
            Assert.That(b.X0, Is.EqualTo(LayoutRules.AreaX0));
            Assert.That(b.Y1, Is.EqualTo(LayoutRules.AreaY1));
            Assert.That(t.X1, Is.EqualTo(LayoutRules.AreaX1));
            // PH w dolnym rzędzie
            Assert.That(s.Placed.First(p => p.Item.Key == "PH1").Rect.Y0, Is.EqualTo(LayoutRules.AreaY0));
            Assert.That(LayoutRules.Title3(s), Is.EqualTo("BOTTOM LAYER, TOP LAYER, PH DETAILS & SECTIONS."));
            Assert.That(LayoutRules.ScaleText(s.Placed.Select(p => LayoutRules.ScaleOf(p.Item.Kind))), Is.EqualTo("1:50, 1:25 & 1:20 @ A1"));
        }

        [Test]
        public void Plot910_PlansTooWide_TwoSheets()
        {
            // PLOT 9-10: rzuty 347×341 i 338×330 — razem szersze niż pole; 6 detali PH
            var items = new List<LayoutRules.Item>
            {
                It("B", K.PlanBottom, 347, 341), It("T", K.PlanTop, 338, 330),
                It("A-A", K.Section, 100, 84 + LayoutRules.NibNoteBox, nib: true), It("B-B", K.Section, 110, 84 + LayoutRules.NibNoteBox, nib: true),
                It("PH1", K.Ph, 153, 147), It("PH2", K.Ph, 151, 161), It("PH3", K.Ph, 161, 173),
                It("PH6", K.Ph, 180, 185), It("PH7", K.Ph, 169, 167), It("PH8", K.Ph, 162, 174),
            };
            var plan = LayoutRules.Arrange(items);
            Assert.That(plan.Sheets.Count, Is.EqualTo(2));
            foreach (var s in plan.Sheets) AssertNoOverlapInArea(s);
            Assert.That(plan.Sheets[0].Placed.Select(p => p.Item.Key), Is.EquivalentTo(new[] { "B", "A-A", "B-B" }));
            Assert.That(plan.Sheets[1].Placed.Count, Is.EqualTo(7));
            Assert.That(LayoutRules.Title3(plan.Sheets[0]), Is.EqualTo("BOTTOM LAYER & SECTIONS."));
            Assert.That(LayoutRules.Title3(plan.Sheets[1]), Is.EqualTo("TOP LAYER & PH DETAILS."));
            Assert.That(LayoutRules.ScaleText(plan.Sheets[1].Placed.Select(p => LayoutRules.ScaleOf(p.Item.Kind))), Is.EqualTo("1:50 & 1:25 @ A1"));
        }

        [Test]
        public void ManyDetails_SplitBottomDetailsTopPh()
        {
            var items = new List<LayoutRules.Item> { It("B", K.PlanBottom, 400, 420), It("T", K.PlanTop, 400, 420) };
            items.Add(It("A-A", K.Section, 120, 70 + LayoutRules.NibNoteBox, nib: true));
            for (int i = 0; i < 6; i++) items.Add(It("S" + i, K.Section, 200, 80));
            for (int i = 0; i < 6; i++) items.Add(It("D" + i, K.Detail, 200, 200));
            for (int i = 0; i < 12; i++) items.Add(It("PH" + i, K.Ph, 160, 170));
            var plan = LayoutRules.Arrange(items);
            foreach (var s in plan.Sheets) AssertNoOverlapInArea(s);
            Assert.That(plan.Sheets.Sum(s => s.Placed.Count), Is.EqualTo(items.Count));
            Assert.That(plan.Sheets[0].Has(K.PlanBottom), Is.True);
            Assert.That(plan.Sheets[0].Placed.Any(p => p.Item.Key == "A-A"), Is.True, "przekrój z nibem na pierwszym arkuszu");
            int iT = plan.Sheets.FindIndex(s => s.Has(K.PlanTop));
            Assert.That(iT, Is.GreaterThan(1), "arkusz detali przed rzutem górnym");
            Assert.That(plan.Sheets[1].Has(K.Detail), Is.True);
            Assert.That(plan.Sheets.Skip(iT + 1).All(s => s.Placed.All(p => p.Item.Kind == K.Ph)), Is.True, "nadmiar PH — osobne arkusze");
            Assert.That(plan.Sheets.Count, Is.GreaterThan(iT + 1));
            Assert.That(LayoutRules.Title3(plan.Sheets.Last()), Is.EqualTo("PH DETAILS."));
        }

        [Test]
        public void SectionOfDetail_OnSameSheet()
        {
            // rzut dolny duży — detal i jego przekrój nie mieszczą się na arkuszu 1; przekrój ma zostać przy detalu
            var items = new List<LayoutRules.Item>
            {
                It("B", K.PlanBottom, 640, 400), It("T", K.PlanTop, 640, 400),
                It("SECTION A-A", K.Section, 300, 80),
                It("SECTION C-C", K.Section, 200, 100), It("DETAIL '1'", K.Detail, 400, 300),
            };
            items[3].Group = "DETAIL '1'";
            var plan = LayoutRules.Arrange(items);
            var sd = plan.Sheets.First(s => s.Placed.Any(p => p.Item.Key == "DETAIL '1'"));
            Assert.That(sd.Placed.Any(p => p.Item.Key == "SECTION C-C"), Is.True, "przekrój C-C na arkuszu detalu");
            Assert.That(plan.Sheets.Sum(s => s.Placed.Count), Is.EqualTo(items.Count));
            foreach (var s in plan.Sheets) AssertNoOverlapInArea(s);
        }

        [Test]
        public void CrossRefBlocks()
        {
            Assert.That(LayoutRules.CrossRefBlock("BOTTOM LAYER & SECTIONS.", "CV129QD001-RC0090"),
                        Is.EqualTo(new[] { "BOTTOM LAYER & SECTIONS SEE", "DRG. CV129QD001-RC0090" }));
            var l = LayoutRules.CrossRefBlock("BOTTOM LAYER, TOP LAYER, DETAILS & SECTIONS.", "X-RC0001");
            Assert.That(l.Count, Is.EqualTo(3));
            Assert.That(l.All(x => x.Length <= LayoutRules.CrossRefChars), Is.True);
            Assert.That(LayoutRules.CrossRefBoxHeight(2), Is.EqualTo(16.7).Within(0.01));
        }

        [Test]
        public void DrawingNumbers()
        {
            Assert.That(LayoutRules.IncrementDrawingNumber("CV129QD001-RC0090", 1), Is.EqualTo("CV129QD001-RC0091"));
            Assert.That(LayoutRules.IncrementDrawingNumber("CV129QD001-RC0099", 2), Is.EqualTo("CV129QD001-RC0101"));
            Assert.That(LayoutRules.IncrementDrawingNumber("CO59AU001-DR-RC2160", 1), Is.EqualTo("CO59AU001-DR-RC2161"));
            Assert.That(LayoutRules.DrawingSuffix("CV129QD001-RC0091"), Is.EqualTo("RC0091"));
        }

        [Test]
        public void Texts()
        {
            Assert.That(LayoutRules.Title3(false, false, true, true, false), Is.EqualTo("DETAILS & SECTIONS."));
            Assert.That(LayoutRules.Title3(true, true, false, false, true), Is.EqualTo("BOTTOM LAYER, TOP LAYER & PH DETAILS."));
            Assert.That(LayoutRules.Title3(true, true, false, true, true), Is.EqualTo("BOTTOM LAYER, TOP LAYER & DETAILS."));
            Assert.That(LayoutRules.CrossRefLines(new[] { ("TOP LAYER & PH DETAILS.", "CV129QD001-RC0091") }),
                        Is.EqualTo(new[] { "TOP LAYER & PH DETAILS SEE DRG. CV129QD001-RC0091" }));
            Assert.That(LayoutRules.NibNote("A", 1), Does.Contain("fixed to the bottom of the"));
            Assert.That(LayoutRules.NibNote("B", 2), Does.Contain("Section B-B").And.Contain("to the underside of the"));
        }

        [Test]
        public void SectionDirection_AndNib_FromComponents()
        {
            var a = new Dictionary<string, string>
            {
                ["SEG#00"] = "U0:0;U1:6000;E0:Free;E1:Free;N0:0;N1:150",
                ["DIST:1A2"] = "Mark:01;Pos:01;Lay:B1;Kind:Main;AX:1;X0:0",
                ["DIST:1A3"] = "Mark:02;Pos:02;Lay:B2;Kind:Main;AX:0;X0:0",
            };
            Assert.That(LayoutRules.IsNibSection(a), Is.True);
            Assert.That(LayoutRules.SectionDir(a, cutAlongX: true), Is.EqualTo(1));
            Assert.That(LayoutRules.SectionDir(a, cutAlongX: false), Is.EqualTo(2));
            // tylko pręty prostopadłe (kropki) — kierunek przeciwny
            var b = new Dictionary<string, string> { ["DIST:1"] = "Lay:T2;Kind:Main;AX:0", ["SEG#00"] = "N0:0;N1:0" };
            Assert.That(LayoutRules.IsNibSection(b), Is.False);
            Assert.That(LayoutRules.IsBeamSection(a), Is.False);
            Assert.That(LayoutRules.IsBeamSection(new Dictionary<string, string> { ["BEAM:2A1"] = "S0:0;S1:600;H:525" }), Is.True);
            Assert.That(LayoutRules.SectionDir(b, cutAlongX: true), Is.EqualTo(1));
            Assert.That(LayoutRules.SectionDir(new Dictionary<string, string>(), false), Is.EqualTo(2));
        }
    }
}
