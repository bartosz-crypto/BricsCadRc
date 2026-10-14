using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>K7 (E2, docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md): wielokąty osiowe — różnica / suma prostokątów, odcinki na prostej;
    /// prostokąty odcięcia siatki przy belce (Holbrook 528 / 539 z GA P02).</summary>
    public class RectilinearPolygonsTests
    {
        private static List<(double X, double Y)> Box(double x0, double y0, double x1, double y1)
            => new List<(double X, double Y)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };

        [Test]
        public void Subtract_Nothing_SameVertices()
        {
            var r = RectilinearPolygons.Subtract(Box(0, 0, 100, 50), new RectilinearPolygons.Rect[0]);
            Assert.That(r.Count, Is.EqualTo(1));
            Assert.That(r[0].OrderBy(p => p.X).ThenBy(p => p.Y), Is.EqualTo(Box(0, 0, 100, 50).OrderBy(p => p.X).ThenBy(p => p.Y)));
            Assert.That(RectilinearPolygons.Area(r), Is.EqualTo(5000).Within(1e-9));
        }

        [Test]
        public void Subtract_CornerNotch_LShape()
        {
            var r = RectilinearPolygons.Subtract(Box(0, 0, 100, 100), new[] { new RectilinearPolygons.Rect(-10, 60, 40, 200) });
            Assert.That(r.Count, Is.EqualTo(1));
            Assert.That(r[0].Count, Is.EqualTo(6));
            Assert.That(RectilinearPolygons.Area(r), Is.EqualTo(10000 - 40 * 40).Within(1e-9));
            Assert.That(RectilinearPolygons.Inside(r, 20, 80), Is.False);
            Assert.That(RectilinearPolygons.Inside(r, 50, 80), Is.True);
        }

        [Test]
        public void Subtract_InteriorHole_TwoLoops()
        {
            var r = RectilinearPolygons.Subtract(Box(0, 0, 100, 100), new[] { new RectilinearPolygons.Rect(40, 40, 60, 60) });
            Assert.That(r.Count, Is.EqualTo(2));
            Assert.That(RectilinearPolygons.SignedArea(r[0]), Is.GreaterThan(0));
            Assert.That(RectilinearPolygons.SignedArea(r[1]), Is.LessThan(0));
            Assert.That(RectilinearPolygons.Area(r), Is.EqualTo(10000 - 400).Within(1e-9));
            Assert.That(RectilinearPolygons.Inside(r, 50, 50), Is.False);
        }

        [Test]
        public void Subtract_FullWidthBand_TwoSeparateParts()
        {
            var r = RectilinearPolygons.Subtract(Box(0, 0, 100, 100), new[] { new RectilinearPolygons.Rect(-5, 40, 105, 60) });
            Assert.That(r.Count, Is.EqualTo(2));
            Assert.That(r.All(l => RectilinearPolygons.SignedArea(l) > 0), Is.True);
            Assert.That(RectilinearPolygons.Area(r), Is.EqualTo(8000).Within(1e-9));
        }

        [Test]
        public void Union_Overlapping_And_DiagonalTouch()
        {
            var u = RectilinearPolygons.Union(new[] { new RectilinearPolygons.Rect(0, 0, 10, 10), new RectilinearPolygons.Rect(5, 5, 20, 10) });
            Assert.That(u.Count, Is.EqualTo(1));
            Assert.That(RectilinearPolygons.Area(u), Is.EqualTo(100 + 50).Within(1e-9));
            var d = RectilinearPolygons.Union(new[] { new RectilinearPolygons.Rect(0, 0, 10, 10), new RectilinearPolygons.Rect(10, 10, 20, 20) });
            Assert.That(d.Count, Is.EqualTo(2), "styk w narożniku — dwie pętle");
            Assert.That(RectilinearPolygons.Area(d), Is.EqualTo(200).Within(1e-9));
        }

        [Test]
        public void Intervals_OnLine_And_Difference()
        {
            var loops = RectilinearPolygons.Subtract(Box(0, 0, 100, 100), new[] { new RectilinearPolygons.Rect(40, 40, 60, 60) });
            var iv = RectilinearPolygons.IntervalsOnLine(loops, true, 50);
            Assert.That(iv, Is.EqualTo(new List<(double, double)> { (0, 40), (60, 100) }));
            var vv = RectilinearPolygons.IntervalsOnLine(loops, false, 10);
            Assert.That(vv, Is.EqualTo(new List<(double, double)> { (0, 100) }));
            var diff = RectilinearPolygons.SubtractIntervals(new List<(double, double)> { (0, 100) }, new List<(double, double)> { (20, 30), (90, 120) });
            Assert.That(diff, Is.EqualTo(new List<(double, double)> { (0, 20), (30, 90) }));
        }

        [Test]
        public void UncoveredParts_EdgeOnBoundaryIsCovered()
        {
            var cut = RectilinearPolygons.Union(new[] { new RectilinearPolygons.Rect(-265, 100, 600, 900) });
            // krawędź pionowa x = 0 (w obszarze), x = 600 (na boku — domknięty), x = 700 (poza)
            Assert.That(RectilinearPolygons.UncoveredParts(0, 1000, cut, false, 0), Is.EqualTo(new List<(double, double)> { (0, 100), (900, 1000) }));
            Assert.That(RectilinearPolygons.UncoveredParts(0, 1000, cut, false, 600), Is.EqualTo(new List<(double, double)> { (0, 100), (900, 1000) }));
            Assert.That(RectilinearPolygons.UncoveredParts(0, 1000, cut, false, 700), Is.EqualTo(new List<(double, double)> { (0, 1000) }));
            // krawędź pozioma y = 900 (na boku) → część nad belką usunięta
            Assert.That(RectilinearPolygons.UncoveredParts(-1000, 5000, cut, true, 900), Is.EqualTo(new List<(double, double)> { (-1000, -265), (600, 5000) }));
        }

        [Test]
        public void IsRectilinear_SkewDetected()
        {
            Assert.That(RectilinearPolygons.IsRectilinear(Box(0, 0, 10, 10)), Is.True);
            Assert.That(RectilinearPolygons.IsRectilinear(new List<(double X, double Y)> { (0, 0), (10, 0), (10, 10), (5, 15), (0, 10) }), Is.False);
        }

        // ── Holbrook 528 (GA P02): płyta 1326764..1337395 × 49647..60150, nib 160, strefa 1326924..1327524 × 49807..59990 ──

        private static BeamGeometry.Result An528()
        {
            var zone = Box(1326924, 49807, 1327524, 59990);
            var outl = Box(1326764, 49647, 1337395, 60150);
            return BeamGeometry.Analyze(zone, outl);
        }

        [Test]
        public void MeshCut_528_BottomParallel_FirstBar40PlusD()
        {
            // B2 (pręty Y, równoległe do belki pionowej): odcięcie od krawędzi do lica wewnętrznego + 10
            var rs = BeamGeometry.MeshCutRects(An528(), top: false, barsAlongX: false, d: 10);
            Assert.That(rs.Count, Is.EqualTo(1));
            Assert.That(rs[0].X1, Is.EqualTo(1327534).Within(1e-9));
            Assert.That(rs[0].X0, Is.EqualTo(1326924 - BeamGeometry.MeshCutReach).Within(1e-9));
            Assert.That(rs[0].Y0, Is.LessThan(49647));
            Assert.That(rs[0].Y1, Is.GreaterThan(60150));
            var reg = RectilinearPolygons.Subtract(Box(1326764, 49647, 1337395, 60150), rs);
            Assert.That(reg.Count, Is.EqualTo(1));
            Assert.That(reg[0].Min(p => p.X), Is.EqualTo(1327534).Within(1e-9), "pierwszy pręt B2 = 1327534 + 40 = lico + 40 + d");
        }

        [Test]
        public void MeshCut_528_BottomPerpendicular_IntoBeamTo40FromOuterFace()
        {
            // B1 (pręty X, prostopadłe; decyzja 2026-10-11 „wchodzą w belkę jak normalnie do 40mm side cover”): odjęty tylko nib przed
            // belką → granica pasu = lico zewnętrzne (= linia uskoku 1326924), pręt od granicy + 40 = 40 od lica zewnętrznego, w belce
            var rs = BeamGeometry.MeshCutRects(An528(), top: false, barsAlongX: true, d: 10);
            Assert.That(rs.Count, Is.EqualTo(1));
            Assert.That(rs[0].X0, Is.EqualTo(1326924 - BeamGeometry.MeshCutReach).Within(1e-9));
            Assert.That(rs[0].X1, Is.EqualTo(1326924).Within(1e-9));
            var reg = RectilinearPolygons.Subtract(Box(1326764, 49647, 1337395, 60150), rs);
            Assert.That(reg.Count, Is.EqualTo(1));
            Assert.That(reg[0].Min(p => p.X) + 40, Is.EqualTo(1326924 + BeamGeometry.MeshBeamSideCover).Within(1e-9),
                        "koniec pręta B1 40 od lica zewnętrznego belki");
            Assert.That(reg[0].Count, Is.EqualTo(4), "cały nib lewej krawędzi (z narożnikami) odcięty — prostokąt");
        }

        [Test]
        public void MeshCut_528_Perpendicular_MeshCoverOtherThan40_StillEnds40FromOuterFace()
        {
            // otulina siatki 30: granica pasu przesunięta o 40 − 30 w belkę — koniec pręta nadal 40 od lica zewnętrznego
            var rs = BeamGeometry.MeshCutRects(An528(), top: false, barsAlongX: true, d: 10, cover: 30);
            Assert.That(rs.Count, Is.EqualTo(1));
            Assert.That(rs[0].X1 + 30, Is.EqualTo(1326924 + 40).Within(1e-9));
        }

        [Test]
        public void MeshCut_528_Top_ParallelHalfSpacing_PerpendicularIntoBeam()
        {
            var t2 = BeamGeometry.MeshCutRects(An528(), top: true, barsAlongX: false, d: 12);
            Assert.That(t2.Count, Is.EqualTo(1));
            // decyzja 2026-10-14: góra jak dół — pierwszy pręt T2 = lico + 40 + d (dawniej lico + s/2)
            Assert.That(t2[0].X1, Is.EqualTo(1327524 + 12).Within(1e-9), "pierwszy pręt T2 = lico + 40 + d");
            // T1 prostopadłe (decyzja 2026-10-11 „do 40mm side cover”): przez belkę, 40 od lica zewnętrznego (= linia uskoku — jak dawniej)
            var t1 = BeamGeometry.MeshCutRects(An528(), top: true, barsAlongX: true, d: 12);
            Assert.That(t1.Count, Is.EqualTo(1));
            Assert.That(t1[0].X1 + 40, Is.EqualTo(1326924 + 40).Within(1e-9), "koniec pręta T1 40 od lica zewnętrznego");
            // wielokąt góry = linia uskoku (1326924): odcięcie leży w nibie — pas T1 bez zmian względem K7 sprzed 60e8a9c
            var reg = RectilinearPolygons.Subtract(Box(1326924, 49807, 1337235, 59990), t1);
            Assert.That(reg.Count, Is.EqualTo(1));
            Assert.That(reg[0].Min(p => p.X), Is.EqualTo(1326924).Within(1e-9));
        }

        [Test]
        public void UbExclusion_528_StepEdgeAndWrappedNibRemoved_TopEdgeKept()
        {
            var ub = RectilinearPolygons.Union(BeamGeometry.UbExclusionRects(An528()));
            // linia uskoku przed belką (x = 1326924, 49807..59990) — bez UB 01
            Assert.That(RectilinearPolygons.UncoveredParts(49807, 59990, ub, false, 1326924), Is.Empty);
            // nib zawinięty na górze (y = 59990, 1326924..1327524) — bez UB 02
            Assert.That(RectilinearPolygons.UncoveredParts(1326924, 1327524, ub, true, 59990), Is.Empty);
            // krawędź górna płyty (y = 60150) od 1327524 — zostaje
            Assert.That(RectilinearPolygons.UncoveredParts(1327524, 1337395, ub, true, 60150),
                        Is.EqualTo(new List<(double, double)> { (1327524, 1337395) }));
            // krawędź prawa — zostaje
            Assert.That(RectilinearPolygons.UncoveredParts(49647, 60150, ub, false, 1337395).Count, Is.EqualTo(1));
        }

        // ── Holbrook 539: U (góra — koniec wewnętrzny, lewa, dół — koniec przy krawędzi), płyta 1550118..1555611 × 47972..58475 ──

        [Test]
        public void MeshCut_539_TopLegInternalEnd_Notch()
        {
            var zone = new List<(double X, double Y)> { (1550278, 58315), (1553336, 58315), (1553336, 57715), (1550878, 57715), (1550878, 48732),
                                                        (1555451, 48732), (1555451, 48132), (1550278, 48132) };
            var outl = Box(1550118, 47972, 1555611, 58475);
            var an = BeamGeometry.Analyze(zone, outl);
            Assert.That(an.Segments.Count(s => !s.Manual), Is.EqualTo(3));
            // B1 (pręty X): górna noga równoległa — 2026-10-13: na końcu wewnętrznym (x = 1553336) pręty siatki kotwią się w belce
            // na 450 (H10) → odcięcie do x = 1553336 − 450 − 40 = 1552846, y od 57705
            var b1 = RectilinearPolygons.Union(BeamGeometry.MeshCutRects(an, top: false, barsAlongX: true, d: 10));
            Assert.That(BeamGeometry.MeshAnchorIntoBeam(10), Is.EqualTo(450));
            Assert.That(BeamGeometry.MeshAnchorIntoBeam(12), Is.EqualTo(550));
            Assert.That(RectilinearPolygons.Inside(b1, 1552800, 57800), Is.True);
            Assert.That(RectilinearPolygons.Inside(b1, 1552900, 57800), Is.False, "kotwienie w belce 450 za końcem wewnętrznym");
            Assert.That(RectilinearPolygons.Inside(b1, 1553400, 57800), Is.False, "za końcem wewnętrznym — siatka");
            Assert.That(RectilinearPolygons.Inside(b1, 1552000, 57700), Is.False, "pierwszy pręt 40 + d od lica 57715");
            Assert.That(RectilinearPolygons.Inside(b1, 1552000, 57706), Is.True);
            // dolna noga przy krawędzi (x 1555451): odcięcie wydłużone za koniec (nib zawinięty po prawej)
            Assert.That(RectilinearPolygons.Inside(b1, 1555550, 48300), Is.True);
            // lewa noga (pionowa, lico zewnętrzne 1550278): B1 prostopadłe przez belkę — odcięty tylko nib przed belką (decyzja 2026-10-11)
            Assert.That(RectilinearPolygons.Inside(b1, 1550200, 53000), Is.True, "nib przed belką odcięty");
            Assert.That(RectilinearPolygons.Inside(b1, 1550300, 53000), Is.False, "trzon belki — pręty B1 wchodzą do 40 od lica zewnętrznego");
        }
    }
}
