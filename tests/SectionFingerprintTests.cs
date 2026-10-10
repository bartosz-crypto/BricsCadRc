using System.Globalization;
using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// SectionFingerprint — odcisk przekroju RC_SECTION (bezpiecznik aktualności przekrojów). Wejścia z SectionPlannerGoldenTests.
    /// </summary>
    [TestFixture]
    public class SectionFingerprintTests
    {
        // ── pomocnicze ──────────────────────────────────────────────────

        private static void Tag(SectionPlanner.Input inp)
        {
            // handle rozkładów jak z rysunku (klucz DIST:<handle>)
            for (int i = 0; i < inp.Dists.Count; i++) inp.Dists[i].SourceHandle = (0x1A0 + i).ToString("X8");
        }

        private static SectionFingerprint.Source Src(SectionPlanner.Input inp, string letter = "A")
            => new SectionFingerprint.Source
            {
                RefX = inp.CutAlongX ? inp.S0 : inp.CutCoord, RefY = inp.CutAlongX ? inp.CutCoord : inp.S0,   // P1 cięcia
                Views = new List<(double X, double Y)> { (0, 0), (0, 12000) }, Letter = letter,
                DescThickness = inp.Thickness, DescSsl = inp.Ssl
            };

        private static SectionFingerprint.State StateOf(SectionPlanner.Input inp, SectionFingerprint.Source? src = null)
            => SectionFingerprint.Build(inp, SectionPlanner.Plan(inp), src ?? Src(inp));

        private static SectionPlanner.Input Nib() { var i = SectionPlannerGoldenTests.CaseNib(); Tag(i); return i; }
        private static SectionPlanner.Input Hole() { var i = SectionPlannerGoldenTests.CaseHole(); Tag(i); return i; }
        private static SectionPlanner.Input Piles() { var i = SectionPlannerGoldenTests.CasePiles(); Tag(i); return i; }

        private static SectionFingerprint.Verdict Cmp(SectionFingerprint.State a, SectionFingerprint.State b)
            => SectionFingerprint.Compare(a, b);

        // ── liczby, serializacja ────────────────────────────────────────

        [Test]
        public void Num_RoundsToTenth_NormalizesMinusZero()
        {
            Assert.That(SectionFingerprint.Num(-0.0), Is.EqualTo("0.0"));
            Assert.That(SectionFingerprint.Num(-0.04), Is.EqualTo("0.0"));
            Assert.That(SectionFingerprint.Num(1234.56), Is.EqualTo("1234.6"));
            Assert.That(SectionFingerprint.Num(-12.34), Is.EqualTo("-12.3"));
            Assert.That(SectionFingerprint.Num(double.NaN), Is.EqualTo("nan"));
        }

        [Test]
        public void Esc_RoundTrip()
        {
            Assert.That(SectionFingerprint.Esc("a|b"), Does.Not.Contain("|"));
            foreach (var s in new[] { "", "a;b", "a|b|\\p", "x\\y", "2 H12-101-150 T IN NIB", "a\nb", "\\s;" })
                Assert.That(SectionFingerprint.Unesc(SectionFingerprint.Esc(s)), Is.EqualTo(s), s);
        }

        [Test]
        public void Hash_Deterministic_AndCultureIndependent()
        {
            var a = StateOf(Nib());
            var prev = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
                var b = StateOf(Nib());
                Assert.That(b.Hash, Is.EqualTo(a.Hash));
                Assert.That(b.Digest, Is.EqualTo(a.Digest));
                Assert.That(SectionFingerprint.Serialize(b.Components), Does.Not.Contain("225,0"));
            }
            finally { CultureInfo.CurrentCulture = prev; }
            Assert.That(a.Hash, Has.Length.EqualTo(64));
            Assert.That(Cmp(a, StateOf(Nib())).Stale, Is.False);
        }

        [Test]
        public void Sha_IsStable()
        {
            // SHA-256 UTF-8 (stała — zmiana algorytmu = wszystkie przekroje „nieaktualne”)
            Assert.That(SectionFingerprint.Sha("abc"), Is.EqualTo("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
            var c = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["B"] = "y:2.0", ["A"] = "x:1.0" };
            Assert.That(SectionFingerprint.Serialize(c), Is.EqualTo("A=x:1.0\nB=y:2.0"));
        }

        [Test]
        public void Components_ContainUsedOnly_AndRelativeCoordinates()
        {
            var inp = Hole();
            inp.Dists.Add(new SectionPlanner.Dist { Mark = "H10-30-200 B1", PosNr = "30", LayerCode = "B1", AlongX = true, X0 = 40, Y0 = 4500,
                                                     LengthA = 7920, BarsSpan = 200, Count = 2, Spacing = 200, SourceHandle = "000001FF" });
            var st = StateOf(inp);
            Assert.That(st.Components.Keys.Count(k => k.StartsWith("DIST:")), Is.EqualTo(4));
            Assert.That(st.Components.ContainsKey("DIST:000001FF"), Is.False);
            Assert.That(st.Components["CUT"], Does.Contain("Len:6000.0"));
            Assert.That(st.Components.ContainsKey("SEG#01"), Is.True);
        }

        [Test]
        public void Stretch_EdgeParallelToCut_FarFromIt_SameHash()
        {
            // odniesienie = P1 cięcia: STRETCH krawędzi równoległej do cięcia (także dolnej — zmiana MinPoint) bez alarmu
            var a = StateOf(Hole());
            foreach (var (iy, y) in new[] { (0, -500.0), (2, 5600.0) })
            {
                var inp = Hole();
                var o = inp.Outline;
                if (iy == 0) { o[0] = (o[0].X, y); o[1] = (o[1].X, y); }
                else { o[2] = (o[2].X, y); o[3] = (o[3].X, y); }
                var b = StateOf(inp);
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "krawędź y → " + y);
                Assert.That(Cmp(a, b).Stale, Is.False);
            }
        }

        [Test]
        public void ComponentsEqual_DigestDiffers_SameVersion_InfoRounding()
        {
            var a = StateOf(Nib());
            var stored = new SectionFingerprint.State { Components = a.Components, Hash = "inny", Digest = "inny", Version = SectionPlanner.Version };
            var v = Cmp(stored, a);
            Assert.That(v.Stale, Is.False);
            Assert.That(v.Info, Has.Some.EqualTo("wynik różni się w granicach zaokrągleń"));
        }

        [Test]
        public void Move_WholePlan_SameHash()
        {
            var a = StateOf(Nib());
            var moved = Nib();
            const double dx = 123456.78, dy = -98765.43;
            moved.Outline = moved.Outline.Select(p => (p.X + dx, p.Y + dy)).ToList();
            foreach (var n in moved.Nibs) { n.OuterCoord += dx; n.InnerCoord += dx; n.Lo += dy; n.Hi += dy; }
            foreach (var d in moved.Dists) { d.X0 += dx; d.Y0 += dy; }
            moved.CutCoord += dy; moved.S0 += dx; moved.S1 += dx;
            var b = StateOf(moved);
            Assert.That(b.Hash, Is.EqualTo(a.Hash));
            Assert.That(b.Digest, Is.EqualTo(a.Digest));
        }

        // ── werdykty ────────────────────────────────────────────────────

        [Test]
        public void Spacing_OfCutDistribution_Stale_WithReason()
        {
            var a = StateOf(Nib());
            var inp = Nib();
            var b2 = inp.Dists.Single(d => d.PosNr == "06");      // B2 — kropki w przekroju
            b2.Mark = "H10-06-150 B2"; b2.Spacing = 150; b2.Count = 40;
            var v = Cmp(a, StateOf(inp));
            Assert.That(v.Stale, Is.True);
            Assert.That(v.Reasons, Has.Some.Contains("zmieniony rozkład poz. 06 B2").And.Contains("rozstaw 200 → 150"));
        }

        [Test]
        public void FarDistribution_Changed_Current()
        {
            var inp0 = Hole();
            inp0.Dists.Add(new SectionPlanner.Dist { Mark = "H10-30-200 B1", PosNr = "30", LayerCode = "B1", AlongX = true, X0 = 40, Y0 = 4500,
                                                      LengthA = 7920, BarsSpan = 200, Count = 2, Spacing = 200, SourceHandle = "000001FF" });
            var a = StateOf(inp0);
            var inp1 = Hole();
            inp1.Dists.Add(new SectionPlanner.Dist { Mark = "H10-30-100 B1", PosNr = "30", LayerCode = "B1", AlongX = true, X0 = 40, Y0 = 4500,
                                                      LengthA = 7920, BarsSpan = 300, Count = 4, Spacing = 100, SourceHandle = "000001FF" });
            var b = StateOf(inp1);
            Assert.That(b.Hash, Is.EqualTo(a.Hash));
            Assert.That(Cmp(a, b).Stale, Is.False);
        }

        [Test]
        public void LevelDiameter_B1_AffectsB2Level_Stale()
        {
            var a = StateOf(Hole());
            var inp = Hole();
            // dodatkowy B1 Main Ø16 daleko od cięcia — zmienia dB1 (poziom B2) dla całego przekroju
            inp.Dists.Add(new SectionPlanner.Dist { Mark = "H16-31-200 B1", PosNr = "31", LayerCode = "B1", AlongX = true, X0 = 40, Y0 = 4500,
                                                     LengthA = 1000, BarsSpan = 200, Count = 2, Spacing = 200, Diameter = 16, SourceHandle = "000002FF" });
            var v = Cmp(a, StateOf(inp));
            Assert.That(v.Stale, Is.True);
            Assert.That(v.Reasons, Has.Some.Contains("dB1 10 → 16"));
        }

        [Test]
        public void Pile_Added_Moved_Removed()
        {
            var a = StateOf(Piles());
            var add = Piles(); add.Piles.Add((4200, 2500, 300));
            var v1 = Cmp(a, StateOf(add));
            Assert.That(v1.Stale, Is.True);
            Assert.That(v1.Reasons, Has.Some.StartsWith("nowy pal na linii cięcia (4300, 0 od punktu P1 cięcia)"));

            var mv = Piles(); mv.Piles[0] = (1600, 2500, 300);
            var v2 = Cmp(a, StateOf(mv));
            Assert.That(v2.Stale, Is.True);
            Assert.That(v2.Reasons, Has.Some.StartsWith("usunięty/przesunięty pal (1600, 0 od punktu P1 cięcia)"));

            var far = Piles(); far.Piles[2] = (2600, 4100, 300);    // pal daleko od cięcia — bez wpływu
            Assert.That(Cmp(a, StateOf(far)).Stale, Is.False);
        }

        [Test]
        public void Thickness_FromDescription_Stale_WithNumbers()
        {
            var a = StateOf(Nib());
            var inp = Nib(); inp.Thickness = 300;
            var v = Cmp(a, StateOf(inp, Src(inp)));
            Assert.That(v.Stale, Is.True);
            Assert.That(v.Reasons, Has.Some.EqualTo("zmieniona grubość 225 → 300"));
        }

        [Test]
        public void Thickness_Overridden_DescriptionChange_IsInfoOnly()
        {
            var inp0 = Nib(); inp0.Thickness = 250;
            var s0 = Src(inp0); s0.DescThickness = 225; s0.ThkOverridden = true;
            var a = StateOf(inp0, s0);
            var inp1 = Nib(); inp1.Thickness = 250;
            var s1 = Src(inp1); s1.DescThickness = 300; s1.ThkOverridden = true;
            var v = Cmp(a, StateOf(inp1, s1));
            Assert.That(v.Stale, Is.False);
            Assert.That(v.Info, Has.Some.Contains("opis płyty: grubość 225 → 300"));
        }

        [Test]
        public void Segment_OutlineOnCutLine_Stale()
        {
            var a = StateOf(Hole());
            var inp = Hole(); inp.Holes[0] = (3000, 2000, 4500, 3000);
            var v = Cmp(a, StateOf(inp));
            Assert.That(v.Stale, Is.True);
            Assert.That(v.Reasons, Has.Some.StartsWith("obrys/nib/otwór na linii cięcia"));
        }

        [Test]
        public void Regeneration_NewHandles_MatchedAsChanged_NotNewAndRemoved()
        {
            var a = StateOf(Nib());
            var inp = Nib();
            for (int i = 0; i < inp.Dists.Count; i++) inp.Dists[i].SourceHandle = (0x9000 + i).ToString("X8");   // nowe handle
            var same = Cmp(a, StateOf(inp));
            Assert.That(same.Stale, Is.False, "regeneracja bez zmian = aktualny");

            var b2 = inp.Dists.Single(d => d.PosNr == "06"); b2.Mark = "H10-06-150 B2"; b2.Spacing = 150; b2.Count = 40;
            var v = Cmp(a, StateOf(inp));
            Assert.That(v.Stale, Is.True);
            Assert.That(v.Reasons, Has.Some.StartsWith("zmieniony rozkład poz. 06 B2"));
            Assert.That(v.Reasons, Has.None.StartsWith("nowy rozkład"));
            Assert.That(v.Reasons, Has.None.StartsWith("usunięty rozkład"));
        }

        [Test]
        public void RoundingBoundary_WithinTolerance_Current()
        {
            var inp0 = Nib(); inp0.Dists[0].LengthA = 5920.04;   // 5920.0
            var a = StateOf(inp0);
            var inp1 = Nib(); inp1.Dists[0].LengthA = 5920.06;   // 5920.1 — inny skrót składników
            var b = StateOf(inp1);
            Assert.That(b.Hash, Is.Not.EqualTo(a.Hash));
            Assert.That(Cmp(a, b).Stale, Is.False);
        }

        [Test]
        public void Digest_IsArbiter_ChangeWithoutEffectOnDrawing_Current()
        {
            var a = StateOf(Nib());
            var inp = Nib();
            inp.Dists.Single(d => d.PosNr == "05").Mark = "H10-05-200 B1 ";   // inny opis, ten sam wynik
            var b = StateOf(inp);
            Assert.That(b.Hash, Is.Not.EqualTo(a.Hash));
            Assert.That(b.Digest, Is.EqualTo(a.Digest));
            var v = Cmp(a, b);
            Assert.That(v.Stale, Is.False);
            Assert.That(v.Info, Has.Some.StartsWith("zmiany bez wpływu na rysunek"));
        }

        [Test]
        public void Views_Cut_Letter_AlwaysStale_EvenWithSameDigest()
        {
            var inp = Nib();
            var a = StateOf(inp);
            var s1 = Src(inp); s1.Views.Add((20000, 0));
            Assert.That(Cmp(a, StateOf(inp, s1)).Reasons, Has.Some.StartsWith("nowy rzut płyty (przesunięcie 20000, 0)"));
            Assert.That(Cmp(a, StateOf(inp, Src(inp, "B"))).Reasons, Has.Some.EqualTo("litera przekroju A → B"));
            var cut = Nib(); cut.S0 = -600;     // cięcie dalej poza płytą — ten sam wynik, inne cięcie
            var vb = StateOf(cut);
            Assert.That(vb.Digest, Is.EqualTo(a.Digest));
            var v = Cmp(a, vb);
            Assert.That(v.Stale, Is.True);
            Assert.That(v.Reasons, Has.Some.EqualTo("długość linii cięcia 7000 → 7100"));
        }

        [Test]
        public void Version_Differs_SameInput_DifferentDigest_IsCurrentWithInfo()
        {
            var a = StateOf(Nib());
            var stored = new SectionFingerprint.State { Components = a.Components, Hash = a.Hash, Digest = "inny", Version = SectionPlanner.Version - 1 };
            var v = Cmp(stored, a);
            Assert.That(v.Stale, Is.False);
            Assert.That(v.NewVersionDiffers, Is.True);

            var sameVer = new SectionFingerprint.State { Components = a.Components, Hash = a.Hash, Digest = "inny", Version = SectionPlanner.Version };
            Assert.That(Cmp(sameVer, a).NewVersionDiffers, Is.False);
        }

        [Test]
        public void BarDimensions_Changed_Stale()
        {
            var inp0 = SectionPlannerGoldenTests.CaseFitShape(); Tag(inp0);
            inp0.Dists[0].SourceDims = new double[] { 1500, 200, 0, 0, 0 };
            var a = StateOf(inp0);
            var inp1 = SectionPlannerGoldenTests.CaseFitShape(); Tag(inp1);
            inp1.Dists[0].SourceDims = new double[] { 1500, 250, 0, 0, 0 };
            inp1.Dists[0].ShapeLocal = new List<(double X, double Y)> { (0, 0), (1500, 0), (1500, 250) };
            var v = Cmp(a, StateOf(inp1));
            Assert.That(v.Stale, Is.True);
            Assert.That(v.Reasons, Has.Some.EqualTo("wymiary pręta poz. 17 (B 200 → 250)"));
        }

        [Test]
        public void MatchDists_FallbackByPositionAndNearest()
        {
            string V(string pos, double x0) => $"Mark:x;Pos:{pos};Lay:B1;Kind:Main;AX:1;X0:{x0:0.0};Y0:0.0";
            var s = new Dictionary<string, string> { ["DIST:00000001"] = V("05", 0), ["DIST:00000002"] = V("05", 1000) };
            var c = new Dictionary<string, string> { ["DIST:000000A1"] = V("05", 990), ["DIST:000000A2"] = V("05", 10), ["DIST:000000A3"] = V("06", 0) };
            var (pairs, removed, added) = SectionFingerprint.MatchDists(s, c);
            Assert.That(pairs, Is.EquivalentTo(new[] { ("DIST:00000001", "DIST:000000A2"), ("DIST:00000002", "DIST:000000A1") }));
            Assert.That(removed, Is.Empty);
            Assert.That(added, Is.EqualTo(new[] { "DIST:000000A3" }));
        }
    }
}
