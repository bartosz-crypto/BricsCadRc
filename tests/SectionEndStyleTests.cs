using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// Zakończenia prętów w przekroju (SectionPlanner wersja 3, zasady Z1–Z6, decyzja użytkownika 2026-10-11): symbol do wnętrza i cały
    /// w betonie, zasięg pręta bez zmian (kreska / hak nie wychodzi za koniec pręta), bez symbolu na urwaniu / końcu cięcia, zakład
    /// odsunięty o 25 mm, długość kreski na stałe 30 mm (DefaultEndSymbolLength, decyzja 2026-10-11); przekrój sprzed wersji 3 → P2 bez alarmu.
    /// </summary>
    [TestFixture]
    public class SectionEndStyleTests
    {
        private static readonly SectionPlanner.EndStyle[] Styles =
            { SectionPlanner.EndStyle.Tick };

        private static SectionPlanner.Result Plan(Func<SectionPlanner.Input> make, SectionPlanner.EndStyle st)
            => SectionPlanner.Plan(SectionPlannerGoldenTests.Styled(make, st)());

        private static bool InSegments(SectionPlanner.Result r, double u, double v, double T)
            => r.Segments.Any(s => u >= s.U0 - 0.5 && u <= s.U1 + 0.5 && v <= 0.5 && v >= -T - 0.5 - (s.Prof != null ? s.Prof.Max(p => -p.Bot) - T : 0));

        [Test]
        public void None_IsDefault_Length30()
        {
            Assert.That(new SectionPlanner.Input().EndStyle, Is.EqualTo(SectionPlanner.EndStyle.None));
            Assert.That(SectionPlanner.DefaultEndSymbolLength, Is.EqualTo(30.0));      // na stałe (decyzja użytkownika 2026-10-11)
        }

        [Test]
        public void Z4_BarsThroughHoleAndBreaks_NoSymbols_SameAsNone()
        {
            // przypadek „hole”: pręty w widoku przechodzą przez otwór i urwania — żaden koniec nie jest rzeczywistym końcem pręta
            string none = SectionPlannerGoldenTests.Dump(SectionPlanner.Plan(SectionPlannerGoldenTests.CaseHole()));
            foreach (var st in Styles)
                Assert.That(SectionPlannerGoldenTests.Dump(Plan(SectionPlannerGoldenTests.CaseHole, st)), Is.EqualTo(none), st.ToString());
        }

        [TestCase("nib")] [TestCase("fitshape")] [TestCase("beam")]
        public void Symbols_InConcrete_AndWithinBarExtent(string name)
        {
            var make = SectionPlannerGoldenTests.Cases().Single(c => (string)c.Arguments[0]! == name).Arguments[1] as Func<SectionPlanner.Input>;
            var baseRes = SectionPlanner.Plan(make!());
            double T = make!().Thickness;
            foreach (var st in Styles)
            {
                var r = Plan(make!, st);
                Assert.That(r.Bars.Count, Is.EqualTo(baseRes.Bars.Count));
                for (int i = 0; i < r.Bars.Count; i++)
                {
                    var a = baseRes.Bars[i]; var b = r.Bars[i];
                    // zasięg w u bez zmian (ramię skrócone o rzut kreski)
                    Assert.That(b.Path.Min(p => p.U), Is.GreaterThanOrEqualTo(a.Path.Min(p => p.U) - 1e-6), $"{name} {st} bar {b.PosNr}");
                    Assert.That(b.Path.Max(p => p.U), Is.LessThanOrEqualTo(a.Path.Max(p => p.U) + 1e-6), $"{name} {st} bar {b.PosNr}");
                    // punkty symboli (nowe względem drogi bez stylu) — w betonie
                    foreach (var p in b.Path.Where(q => !a.Path.Any(o => Math.Abs(o.U - q.U) < 1e-6 && Math.Abs(o.V - q.V) < 1e-6)))
                        Assert.That(InSegments(r, p.U, p.V, T), Is.True, $"{name} {st} bar {b.PosNr} końcówka {p}");
                }
            }
        }

        [Test]
        public void Z1_StraightBar_TickTowardsInterior_45deg_Length30()
        {
            // nib: pręt T1 104 (góra) — lewy koniec rzeczywisty (u = 170): kreska w dół, 45°, 30 mm, koniec kreski w u = 170
            var r = Plan(SectionPlannerGoldenTests.CaseNib, SectionPlanner.EndStyle.Tick);
            var b = r.Bars.Single(x => x.PosNr == "104");
            var tip = b.Path[0]; var root = b.Path[1];
            Assert.That(tip.U, Is.EqualTo(170).Within(1e-6));
            Assert.That(tip.V, Is.LessThan(root.V));                                   // górny pręt — w dół
            double len = Math.Sqrt((tip.U - root.U) * (tip.U - root.U) + (tip.V - root.V) * (tip.V - root.V));
            Assert.That(len, Is.EqualTo(30.0).Within(1e-6));
            Assert.That(Math.Abs(tip.U - root.U), Is.EqualTo(Math.Abs(tip.V - root.V)).Within(1e-6));   // 45°
            // prawy koniec za zakresem cięcia (S1 = 6500 > 5830? pręt do 5830 — koniec rzeczywisty) — symbol też
            Assert.That(b.Path.Count, Is.EqualTo(4));
        }

        [Test]
        public void MeshGap_DeletedDistribution_NoDots_Warning_2026_10_14()
        {
            // decyzja 2026-10-14: po usunięciu rozkładu (luka w zbrojeniu) schemat siatki nie wstawia kropek w luce i nie podpisuje ich
            // sąsiednim rozkładem; RC_SECTION / RC_SECTION_UPDATE dostaje komunikat o luce
            var inp = SectionPlannerGoldenTests.CasePiles();
            inp.Dists.RemoveAll(d => d.PosNr == "11");
            inp.Dists.Add(SectionPlannerGoldenTests.D("H10-11-200 B1", "B1", false, 40, 40, 4920, 1800, 10, 200, 10));    // x 40…1840
            inp.Dists.Add(SectionPlannerGoldenTests.D("H10-22-200 B1", "B1", false, 2960, 40, 4920, 2000, 11, 200, 10));  // x 2960…4960
            var r = SectionPlanner.Plan(inp);
            var b1 = r.Dots.Where(d => d.PosNr == "11" || d.PosNr == "22").ToList();
            Assert.That(b1.Count(d => d.PosNr == "11"), Is.EqualTo(10));
            Assert.That(b1.Count(d => d.PosNr == "22"), Is.EqualTo(11));
            // luka 1840…2960 (w u) — bez kropek
            var us = b1.Select(d => d.U).OrderBy(u => u).ToList();
            double maxStep = us.Zip(us.Skip(1), (a, b) => b - a).Max();
            Assert.That(maxStep, Is.GreaterThan(1000));
            Assert.That(r.Warnings.Any(w => w.StartsWith("Luka w zbrojeniu (dół)")), Is.True, string.Join(" | ", r.Warnings));
            // bez luki — bez komunikatu
            Assert.That(SectionPlanner.Plan(SectionPlannerGoldenTests.CasePiles()).Warnings.Any(w => w.StartsWith("Luka")), Is.False);
        }

        [Test]
        public void Z2_Lap_NoOffset_OneLayer_2026_10_14()
        {
            // decyzja 2026-10-14 („przekrój w 1 warstwie”): T1 104 i B1 05 na poziomie swojej warstwy, na jednej linii z ramionami
            // U-barów (bez odsunięcia 25 z wersji 3) — kreski zakończeń mogą tylko schodzić do wnętrza, linia pręta bez zmian
            var none = SectionPlanner.Plan(SectionPlannerGoldenTests.CaseNib());
            var r = Plan(SectionPlannerGoldenTests.CaseNib, SectionPlanner.EndStyle.Tick);
            double v0 = none.Bars.Single(x => x.PosNr == "104").Path[0].V;
            Assert.That(r.Bars.Single(x => x.PosNr == "104").Path.Max(p => p.V), Is.EqualTo(v0).Within(1e-6));
            double b0 = none.Bars.Single(x => x.PosNr == "05").Path[0].V;
            Assert.That(r.Bars.Single(x => x.PosNr == "05").Path.Min(p => p.V), Is.EqualTo(b0).Within(1e-6));
            Assert.That(r.Bars.Single(x => x.PosNr == "01").Path.Max(p => p.V), Is.EqualTo(none.Bars.Single(x => x.PosNr == "01").Path.Max(p => p.V)));
        }

        [Test]
        public void Z3_UBar_SymbolsOnlyOnFreeLegEnds()
        {
            var none = SectionPlanner.Plan(SectionPlannerGoldenTests.CaseNib());
            var r = Plan(SectionPlannerGoldenTests.CaseNib, SectionPlanner.EndStyle.Tick);
            var a = none.Bars.Single(x => x.PosNr == "03"); var b = r.Bars.Single(x => x.PosNr == "03");
            Assert.That(b.Path.Count, Is.EqualTo(a.Path.Count + 2));                 // dwa wolne końce, zagięcia bez symbolu
            // górne ramię — kreska w dół, dolne — w górę
            bool firstTop = b.Path[0].V > b.Path[b.Path.Count - 1].V;
            var top = firstTop ? b.Path[0] : b.Path[b.Path.Count - 1];
            var bot = firstTop ? b.Path[b.Path.Count - 1] : b.Path[0];
            Assert.That(top.V, Is.LessThan(a.Path.Max(p => p.V)));
            Assert.That(bot.V, Is.GreaterThan(a.Path.Min(p => p.V)));
        }

        [Test]
        public void Stirrups_NoSymbols()
        {
            var none = SectionPlanner.Plan(SectionPlannerGoldenTests.CaseBeam());
            foreach (var st in Styles)
            {
                var r = Plan(SectionPlannerGoldenTests.CaseBeam, st);
                Assert.That(r.Bars.Single(b => b.PosNr == "07").Path, Is.EqualTo(none.Bars.Single(b => b.PosNr == "07").Path));
            }
        }

        [Test]
        public void Labels_LeadersNotThroughSymbols_SameLabelSetAsNone()
        {
            foreach (var st in Styles)
            {
                var none = SectionPlanner.Plan(SectionPlannerGoldenTests.CaseNib());
                var r = Plan(SectionPlannerGoldenTests.CaseNib, st);
                Assert.That(r.Labels.Select(l => l.Text).OrderBy(t => t), Is.EqualTo(none.Labels.Select(l => l.Text).OrderBy(t => t)), st.ToString());
            }
        }

        // ── odcisk (styl nie jest zapisywany w przekroju — jeden styl w programie) ──

        private static SectionFingerprint.Source Src(SectionPlanner.Input inp)
            => new SectionFingerprint.Source { RefX = inp.S0, RefY = inp.CutCoord, Views = new List<(double X, double Y)> { (0, 0) }, Letter = "A" };

        private static SectionFingerprint.State StateOf(SectionPlanner.Input inp) => SectionFingerprint.Build(inp, SectionPlanner.Plan(inp), Src(inp));

        [Test]
        public void Fingerprint_NoStyleField_DigestReflectsSymbols()
        {
            var n = StateOf(SectionPlannerGoldenTests.CaseNib());
            var s = StateOf(SectionPlannerGoldenTests.Styled(SectionPlannerGoldenTests.CaseNib, SectionPlanner.DefaultEndStyle)());
            Assert.That(s.Components, Is.EqualTo(n.Components));              // wejście to samo — odcisk składników bez zmian
            Assert.That(s.Digest, Is.Not.EqualTo(n.Digest));                  // rysunek inny — skrót wyniku rozstrzyga (P2)
            Assert.That(SectionPlanner.DefaultEndStyle, Is.EqualTo(SectionPlanner.EndStyle.Tick));
        }

        [Test]
        public void Compare_OldSection_RecomputedWithStyle_P2_NotStale()
        {
            // przekrój sprzed wersji 3: odcisk bez pola End, META „Ver:1”, wynik bez symboli
            var stored = StateOf(SectionPlannerGoldenTests.CaseNib());
            stored.Components["META"] = stored.Components["META"].Replace("Ver:" + SectionPlanner.Version, "Ver:1");
            stored.Hash = SectionFingerprint.Hash(stored.Components);
            stored.Version = 1;
            var current = StateOf(SectionPlannerGoldenTests.Styled(SectionPlannerGoldenTests.CaseNib, SectionPlanner.EndStyle.Tick)());
            var v = SectionFingerprint.Compare(stored, current);
            Assert.That(v.Stale, Is.False, string.Join("; ", v.Reasons));
            Assert.That(v.NewVersionDiffers, Is.True);
        }

        [Test]
        public void Compare_OldSection_WithoutBarEnds_Current_NoP2()
        {
            // przekrój bez prętów kończących się w przekroju (hole) — wynik bez zmian, tylko wersja: aktualny bez informacji P2
            var stored = StateOf(SectionPlannerGoldenTests.CaseHole());
            stored.Components["META"] = stored.Components["META"].Replace("Ver:" + SectionPlanner.Version, "Ver:1");
            stored.Hash = SectionFingerprint.Hash(stored.Components);
            stored.Version = 1;
            var v = SectionFingerprint.Compare(stored, StateOf(SectionPlannerGoldenTests.Styled(SectionPlannerGoldenTests.CaseHole, SectionPlanner.EndStyle.Tick)()));
            Assert.That(v.Stale, Is.False);
            Assert.That(v.NewVersionDiffers, Is.False);
        }
    }
}
