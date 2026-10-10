using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// Złoty test SectionPlanner (plan bezpiecznika przekrojów, etap 1 krok 1): pełny zrzut Result (wszystkie pola sprzed
    /// bezpiecznika, liczby w formacie „R” — bit w bit) dla 5 wejść, skrót SHA-256 zapisany na NIEZMIENIONYM kodzie planera.
    /// Pola dopisane później (UsedDists, UsedPiles, LevelDiameters, Source*) NIE wchodzą do zrzutu — nie mogą zmienić wyniku.
    /// Zmiana geometrii przekroju = zmiana wyniku względem wzorca aba0ed1 (wymaga zgody użytkownika) — wtedy też podbić
    /// <see cref="SectionPlanner.Version"/>.
    /// </summary>
    [TestFixture]
    public class SectionPlannerGoldenTests
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ── zrzut ───────────────────────────────────────────────────────

        private static string R(double v) => v.ToString("R", Inv);

        public static string Dump(SectionPlanner.Result r)
        {
            var sb = new StringBuilder();
            void P(IEnumerable<(double U, double V)>? pts, string name)
            {
                sb.Append(name).Append('=');
                if (pts == null) { sb.Append("null"); return; }
                foreach (var p in pts) sb.Append(R(p.U)).Append(',').Append(R(p.V)).Append(';');
            }
            sb.Append("UMin=").Append(R(r.UMin)).Append(" UMax=").Append(R(r.UMax)).Append(" NibTop=").Append(R(r.NibTop)).Append('\n');
            foreach (var s in r.Segments)
            {
                sb.Append("SEG ").Append(R(s.U0)).Append(' ').Append(R(s.U1)).Append(' ').Append(s.End0).Append(' ').Append(s.End1)
                  .Append(' ').Append(R(s.Nib0)).Append(' ').Append(R(s.Nib1)).Append(' ');
                P(s.Polygon, "poly");
                sb.Append('\n');
            }
            foreach (var d in r.Dots)
                sb.Append("DOT ").Append(R(d.U)).Append(' ').Append(R(d.V)).Append(' ').Append(d.Diameter).Append(' ').Append(d.PosNr)
                  .Append(' ').Append(d.Top).Append(' ').Append(d.Mesh).Append(' ').Append(d.TinNib).Append(' ').Append(R(d.NomSpacing)).Append('\n');
            foreach (var b in r.Bars)
            {
                sb.Append("BAR ").Append(b.Diameter).Append(' ').Append(b.PosNr).Append(' ').Append(b.Top).Append(' ').Append(b.Kind).Append(' ');
                P(b.Path, "path"); sb.Append(' ');
                P(b.Outline, "outline");
                sb.Append('\n');
            }
            foreach (var p in r.Piles)
                sb.Append("PILE ").Append(R(p.U)).Append(' ').Append(R(p.Width)).Append(' ').Append(R(p.Top)).Append(' ').Append(R(p.Bottom)).Append('\n');
            foreach (var m in r.Dims)
                sb.Append("DIM ").Append(R(m.U1)).Append(' ').Append(R(m.V1)).Append(' ').Append(R(m.U2)).Append(' ').Append(R(m.V2))
                  .Append(' ').Append(R(m.LineU)).Append(' ').Append(R(m.LineV)).Append(' ').Append(m.Vertical).Append('\n');
            foreach (var l in r.Labels)
                sb.Append("LBL ").Append(l.Text).Append(' ').Append(R(l.U)).Append(' ').Append(R(l.V)).Append(' ').Append(R(l.Height))
                  .Append(' ').Append(l.HasLeader).Append(' ').Append(R(l.LeaderU)).Append(' ').Append(R(l.LeaderV)).Append('\n');
            sb.Append("SSL ").Append(r.SslMark.HasValue ? R(r.SslMark.Value.U) + "," + R(r.SslMark.Value.V) : "null")
              .Append(' ').Append(r.SslText ?? "null").Append('\n');
            foreach (var w in r.Warnings) sb.Append("WARN ").Append(w).Append('\n');
            return sb.ToString();
        }

        private static string Sha(string s)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(s)).Select(b => b.ToString("x2")));
        }

        // ── wejścia ─────────────────────────────────────────────────────

        private static List<(double X, double Y)> Rect(double x0, double y0, double x1, double y1)
            => new List<(double X, double Y)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };

        private static SectionPlanner.Dist D(string mark, string layer, bool alongX, double x0, double y0, double lenA, double span,
                                             int count, double spacing, int dia, string shape = "00", string side = "",
                                             List<(double X, double Y)>? local = null)
        {
            string pos = SectionPlanner.PosNrOf(mark);
            return new SectionPlanner.Dist
            {
                Mark = mark, PosNr = pos, LayerCode = layer, Kind = SectionPlanner.KindOf(mark, pos, 3), AlongX = alongX,
                X0 = x0, Y0 = y0, LengthA = lenA, BarsSpan = span, Count = count, Spacing = spacing, Diameter = dia,
                ShapeCode = shape, SymbolSide = side, ShapeLocal = local!
            };
        }

        /// <summary>1. Nib z obu stron (krawędzie pionowe), UB 01 i UB 03 (kopia pręta wzorcowego), T IN NIB, SSL.</summary>
        public static SectionPlanner.Input CaseNib()
        {
            var inp = new SectionPlanner.Input
            {
                Outline = Rect(0, 0, 6000, 4000), CutAlongX = true, CutCoord = 2000, S0 = -500, S1 = 6500, LookSign = 1,
                Thickness = 225, NibHeight = 150, Ssl = "21.925"
            };
            inp.Nibs.Add(new SectionPlanner.NibEdge { Vertical = true, OuterCoord = 0, InnerCoord = 130, Lo = 0, Hi = 4000 });
            inp.Nibs.Add(new SectionPlanner.NibEdge { Vertical = true, OuterCoord = 6000, InnerCoord = 5870, Lo = 0, Hi = 4000 });
            inp.Dists.Add(D("H10-05-200 B1", "B1", true, 40, 40, 5920, 3920, 20, 200, 10));
            inp.Dists.Add(D("H10-06-200 B2", "B2", false, 40, 40, 3920, 5920, 30, 200, 10));
            inp.Dists.Add(D("H12-104-200 T1", "T1", true, 170, 40, 5660, 3920, 20, 200, 12));
            inp.Dists.Add(D("H12-105-200 T2", "T2", false, 40, 170, 3660, 5920, 30, 200, 12));
            inp.Dists.Add(D("20 H12-01-200 UB", "T1", true, 130, 100, 700, 3800, 20, 200, 12, "21", "Left",
                            new List<(double X, double Y)> { (700, 0), (0, 0), (0, 128), (700, 128) }));
            inp.Dists.Add(D("20 H10-03-200 UB", "B1", true, 40, 100, 610, 3800, 20, 200, 10, "21", "Left",
                            new List<(double X, double Y)> { (610, 0), (0, 0), (0, 60), (610, 60) }));
            inp.Dists.Add(D("2 H12-101-150 T IN NIB", "T1", false, 40, 40, 3920, 150, 2, 150, 12));
            return inp;
        }

        /// <summary>2. Otwór przecięty linią, końce cięcia w płycie (urwanie), bez SSL.</summary>
        public static SectionPlanner.Input CaseHole()
        {
            var inp = new SectionPlanner.Input
            {
                Outline = Rect(0, 0, 8000, 5000), CutAlongX = true, CutCoord = 2500, S0 = 1000, S1 = 7000, LookSign = 1, Thickness = 300
            };
            inp.Holes.Add((3000, 2000, 4000, 3000));
            inp.Dists.Add(D("H10-07-200 B1", "B1", false, 40, 40, 4920, 7920, 40, 200, 10));
            inp.Dists.Add(D("H10-08-200 B2", "B2", true, 40, 40, 7920, 4920, 25, 200, 10));
            inp.Dists.Add(D("H12-109-150 T1", "T1", false, 40, 40, 4920, 7920, 53, 150, 12));
            inp.Dists.Add(D("H12-110-200 T2", "T2", true, 40, 40, 7920, 4920, 25, 200, 12));
            return inp;
        }

        /// <summary>3. Pale: dwa przecięte, jeden daleko.</summary>
        public static SectionPlanner.Input CasePiles()
        {
            var inp = new SectionPlanner.Input
            {
                Outline = Rect(0, 0, 5000, 5000), CutAlongX = true, CutCoord = 2500, S0 = -100, S1 = 5100, LookSign = 1, Thickness = 225
            };
            inp.Piles.Add((1500, 2500, 300));
            inp.Piles.Add((3500, 2600, 300));
            inp.Piles.Add((2500, 4000, 300));
            inp.Dists.Add(D("H10-11-200 B1", "B1", false, 40, 40, 4920, 4920, 25, 200, 10));
            inp.Dists.Add(D("H10-12-200 B2", "B2", true, 40, 40, 4920, 4920, 25, 200, 10));
            return inp;
        }

        /// <summary>4. Cięcie pionowe, patrzenie w stronę malejącego X (LookSign −1), nib na krawędzi dolnej.</summary>
        public static SectionPlanner.Input CaseVertical()
        {
            var inp = new SectionPlanner.Input
            {
                Outline = Rect(0, 0, 6000, 4000), CutAlongX = false, CutCoord = 3000, S0 = -200, S1 = 4200, LookSign = -1,
                Thickness = 225, Ssl = "20.500"
            };
            inp.Nibs.Add(new SectionPlanner.NibEdge { Vertical = false, OuterCoord = 0, InnerCoord = 125, Lo = 0, Hi = 6000 });
            inp.Dists.Add(D("H10-13-200 B1", "B1", true, 40, 40, 5920, 3920, 20, 200, 10));
            inp.Dists.Add(D("H10-14-200 B2", "B2", false, 40, 40, 3920, 5920, 30, 200, 10));
            inp.Dists.Add(D("H12-115-200 T1", "T1", true, 40, 165, 5920, 3795, 19, 200, 12));
            inp.Dists.Add(D("H12-116-200 T2", "T2", false, 40, 165, 3795, 5920, 30, 200, 12));
            return inp;
        }

        /// <summary>5. Pręty kształtowe w widoku (FitShape): L dołem, U górą.</summary>
        public static SectionPlanner.Input CaseFitShape()
        {
            var inp = new SectionPlanner.Input
            {
                Outline = Rect(0, 0, 6000, 3000), CutAlongX = true, CutCoord = 1500, S0 = -100, S1 = 6100, LookSign = 1, Thickness = 250
            };
            inp.Dists.Add(D("H12-17-200 B1", "B1", true, 40, 40, 1500, 2900, 15, 200, 12, "11", "",
                            new List<(double X, double Y)> { (0, 0), (1500, 0), (1500, 200) }));
            inp.Dists.Add(D("H12-118-200 T1", "T1", true, 3000, 40, 2000, 2900, 15, 200, 12, "21", "",
                            new List<(double X, double Y)> { (0, -150), (0, 0), (2000, 0), (2000, -150) }));
            inp.Dists.Add(D("H10-19-200 B2", "B2", false, 40, 40, 2920, 5920, 30, 200, 10));
            return inp;
        }

        public static IEnumerable<TestCaseData> Cases()
        {
            yield return new TestCaseData("nib", (Func<SectionPlanner.Input>)CaseNib);
            yield return new TestCaseData("hole", (Func<SectionPlanner.Input>)CaseHole);
            yield return new TestCaseData("piles", (Func<SectionPlanner.Input>)CasePiles);
            yield return new TestCaseData("vertical", (Func<SectionPlanner.Input>)CaseVertical);
            yield return new TestCaseData("fitshape", (Func<SectionPlanner.Input>)CaseFitShape);
        }

        /// <summary>Skróty zapisane na niezmienionym kodzie SectionPlanner (przed polami Used* / Source*).</summary>
        private static readonly Dictionary<string, string> Golden = new Dictionary<string, string>
        {
            ["nib"]      = "6cdd4905a5b5a635902878c8fbae5178d8d96134e810c0d33ef692bf3f50d761",
            ["hole"]     = "cf9c88950c7911e95abdc2279fcd0bd737b7f83bf76131f6beccaccf1d420164",
            ["piles"]    = "e62159df1d8a97d4402e43e2746a34ddd7757689d0fa221837fb35744c928832",
            ["vertical"] = "b7d453c9093a86c4321faf50aeeec03595182eb84263d1da37ed2c37b8fb85d2",
            ["fitshape"] = "ba680f87ce1e77fab8b000de66b9268f110cc429365f5f9d13ec973a6fbe284f",
        };

        [TestCaseSource(nameof(Cases))]
        public void Plan_FullResult_IsBitIdentical(string name, Func<SectionPlanner.Input> make)
        {
            var dump = Dump(SectionPlanner.Plan(make()));
            string sha = Sha(dump);
            if (sha != Golden[name])
            {
                string path = Path.Combine(Path.GetTempPath(), "section-golden-" + name + ".txt");
                File.WriteAllText(path, dump);
                Assert.Fail($"{name}: SHA {sha} ≠ złoty {Golden[name]} (zrzut: {path})");
            }
        }

        [Test]
        public void Used_MarkedAtAddTime_ByReference_InInputOrder()
        {
            var inp = CaseNib();
            var r = SectionPlanner.Plan(inp);
            // wszystkie rozkłady przypadku nib dają kropki albo pręt w widoku
            Assert.That(r.UsedDists.Select(d => d.PosNr), Is.EqualTo(new[] { "05", "06", "104", "105", "01", "03", "101" }));
            Assert.That(r.UsedDists.All(u => inp.Dists.Any(d => ReferenceEquals(d, u))), Is.True);
            Assert.That(r.LevelDiameters, Is.EqualTo((10.0, 12.0)));
            Assert.That(r.UsedPiles, Is.Empty);

            var p = SectionPlanner.Plan(CasePiles());
            Assert.That(p.UsedPiles, Is.EqualTo(new[] { (1500.0, 2500.0, 300.0), (3500.0, 2600.0, 300.0) }));
        }

        [Test]
        public void Used_FarDistribution_NotUsed()
        {
            var inp = CaseHole();
            // rozkład daleko od linii cięcia (pręty X przy y = 4500, równoległe do cięcia y = 2500)
            inp.Dists.Add(D("H10-30-200 B1", "B1", true, 40, 4500, 7920, 200, 2, 200, 10));
            var r = SectionPlanner.Plan(inp);
            Assert.That(r.UsedDists.Any(d => d.PosNr == "30"), Is.False);
            Assert.That(r.UsedDists.Count, Is.EqualTo(4));
        }

        [TestCaseSource(nameof(Cases))]
        public void Plan_IsNotTrivial(string name, Func<SectionPlanner.Input> make)
        {
            var r = SectionPlanner.Plan(make());
            Assert.That(r.Segments, Is.Not.Empty, name);
            Assert.That(r.Dots.Count + r.Bars.Count, Is.GreaterThan(0), name);
        }
    }
}
