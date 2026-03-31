using NUnit.Framework;
using BricsCadRc.Core;
using static BricsCadRc.Core.MLeaderSnapGeometry;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// Testy geometrii snap-back grotu MLeadera na pręt.
    ///
    /// Symulacja: "mock" MLeadera = punkt grotu (arrowPt), "mock" polilinii = lista odcinków.
    /// RcMLeaderOverrule deleguje do MLeaderSnapGeometry dla logiki odległości,
    /// a BRX Polyline.GetClosestPointTo() dla właściwego snapu na krzywą.
    ///
    /// Testujemy tutaj czystą geometrię (bez BRX API):
    ///   ClosestPointOnSegment, ClosestPointOnSegments, NeedsSnap, ComputeSnap.
    /// </summary>
    [TestFixture]
    public class MLeaderSnapGeometryTests
    {
        // Pręt poziomy: odcinek (0,0,0) → (1000,0,0)
        static readonly (Point3 A, Point3 B)[] HBar =
        {
            (new Point3(0, 0, 0), new Point3(1000, 0, 0))
        };

        // Pręt pionowy: odcinek (500,0,0) → (500,800,0)
        static readonly (Point3 A, Point3 B)[] VBar =
        {
            (new Point3(500, 0, 0), new Point3(500, 800, 0))
        };

        // Pręt L-kształtny: dwa odcinki
        static readonly (Point3 A, Point3 B)[] LBar =
        {
            (new Point3(0, 0, 0),   new Point3(600, 0, 0)),
            (new Point3(600, 0, 0), new Point3(600, 300, 0))
        };

        // ── ClosestPointOnSegment ─────────────────────────────────────────

        [Test]
        public void ClosestPointOnSegment_PerpendicularProjection()
        {
            var q  = new Point3(300, 80, 0);   // 80 mm nad środkiem odcinka poziomego
            var pt = ClosestPointOnSegment(q, new Point3(0, 0, 0), new Point3(1000, 0, 0));

            Assert.That(pt.X, Is.EqualTo(300).Within(0.001));
            Assert.That(pt.Y, Is.EqualTo(0).Within(0.001));
            Assert.That(pt.Z, Is.EqualTo(0).Within(0.001));
        }

        [Test]
        public void ClosestPointOnSegment_BeyondEnd_ClampsToEnd()
        {
            var q  = new Point3(1200, 0, 0);   // poza prawym końcem
            var pt = ClosestPointOnSegment(q, new Point3(0, 0, 0), new Point3(1000, 0, 0));

            Assert.That(pt.X, Is.EqualTo(1000).Within(0.001));
        }

        [Test]
        public void ClosestPointOnSegment_BeforeStart_ClampsToStart()
        {
            var q  = new Point3(-50, 20, 0);
            var pt = ClosestPointOnSegment(q, new Point3(0, 0, 0), new Point3(1000, 0, 0));

            Assert.That(pt.X, Is.EqualTo(0).Within(0.001));
            Assert.That(pt.Y, Is.EqualTo(0).Within(0.001));
        }

        [Test]
        public void ClosestPointOnSegment_DegenerateSegment_ReturnsStart()
        {
            var q  = new Point3(500, 200, 0);
            var a  = new Point3(100, 100, 0);
            var pt = ClosestPointOnSegment(q, a, a);   // A == B

            Assert.That(pt.X, Is.EqualTo(100).Within(0.001));
            Assert.That(pt.Y, Is.EqualTo(100).Within(0.001));
        }

        // ── ClosestPointOnSegments ────────────────────────────────────────

        [Test]
        public void ClosestPointOnSegments_EmptyList_ReturnsQueryPt()
        {
            var q   = new Point3(200, 200, 0);
            var pt  = ClosestPointOnSegments(q, System.Array.Empty<(Point3, Point3)>());

            Assert.That(pt.X, Is.EqualTo(200).Within(0.001));
            Assert.That(pt.Y, Is.EqualTo(200).Within(0.001));
        }

        [Test]
        public void ClosestPointOnSegments_LBar_CorrectCornerSnap()
        {
            // Punkt (650, 150, 0) jest bliżej pionowego odcinka (600,0)→(600,300)
            var q   = new Point3(650, 150, 0);
            var pt  = ClosestPointOnSegments(q, LBar);

            Assert.That(pt.X, Is.EqualTo(600).Within(0.001), "powinno snapnąć do X=600");
            Assert.That(pt.Y, Is.EqualTo(150).Within(0.001));
        }

        [Test]
        public void ClosestPointOnSegments_LBar_HorizontalLegNearer()
        {
            // Punkt (200, 30, 0) jest bliżej poziomego odcinka (0,0)→(600,0)
            var q   = new Point3(200, 30, 0);
            var pt  = ClosestPointOnSegments(q, LBar);

            Assert.That(pt.X, Is.EqualTo(200).Within(0.001));
            Assert.That(pt.Y, Is.EqualTo(0).Within(0.001));
        }

        // ── NeedsSnap ─────────────────────────────────────────────────────

        [Test]
        public void NeedsSnap_WithinTolerance_ReturnsFalse()
        {
            var arrow   = new Point3(0, 3, 0);   // 3 mm od pręta, < 5 mm
            var closest = new Point3(0, 0, 0);
            Assert.That(NeedsSnap(arrow, closest), Is.False);
        }

        [Test]
        public void NeedsSnap_ExceedsTolerance_ReturnsTrue()
        {
            var arrow   = new Point3(0, 10, 0);  // 10 mm od pręta, > 5 mm
            var closest = new Point3(0, 0, 0);
            Assert.That(NeedsSnap(arrow, closest), Is.True);
        }

        [Test]
        public void NeedsSnap_ExactlyAtTolerance_ReturnsFalse()
        {
            // 5 mm == DefaultTolerance → NOT greater-than → false
            var arrow   = new Point3(0, 5, 0);
            var closest = new Point3(0, 0, 0);
            Assert.That(NeedsSnap(arrow, closest), Is.False);
        }

        [Test]
        public void NeedsSnap_CustomTolerance_Respected()
        {
            var arrow   = new Point3(0, 3, 0);
            var closest = new Point3(0, 0, 0);
            // 3 mm > 2 mm → true przy tolerance=2
            Assert.That(NeedsSnap(arrow, closest, tolerance: 2.0), Is.True);
            // 3 mm < 4 mm → false przy tolerance=4
            Assert.That(NeedsSnap(arrow, closest, tolerance: 4.0), Is.False);
        }

        // ── ComputeSnap (pełny pipeline) ─────────────────────────────────

        [Test]
        public void ComputeSnap_ArrowOnBar_NoSnapNeeded()
        {
            // Grot dokładnie na pręcie poziomym
            var arrowPt = new Point3(400, 0, 0);
            var (needsSnap, _) = ComputeSnap(arrowPt, HBar);
            Assert.That(needsSnap, Is.False);
        }

        [Test]
        public void ComputeSnap_Arrow2mmAboveBar_NoSnapWithDefaultTolerance()
        {
            var arrowPt = new Point3(400, 2, 0);  // 2 mm powyżej HBar
            var (needsSnap, _) = ComputeSnap(arrowPt, HBar);
            Assert.That(needsSnap, Is.False);
        }

        [Test]
        public void ComputeSnap_Arrow100mmAboveHBar_SnapsToBar()
        {
            var arrowPt = new Point3(300, 100, 0);   // 100 mm nad X=300
            var (needsSnap, snapPt) = ComputeSnap(arrowPt, HBar);

            Assert.That(needsSnap, Is.True, "grot 100 mm od pręta powinien wymagać snapu");
            Assert.That(snapPt.X, Is.EqualTo(300).Within(0.001));
            Assert.That(snapPt.Y, Is.EqualTo(0).Within(0.001));
        }

        [Test]
        public void ComputeSnap_Arrow50mmFromVBar_SnapsToBar()
        {
            // Grot 50 mm na prawo od pionowego pręta X=500, na Y=400
            var arrowPt = new Point3(550, 400, 0);
            var (needsSnap, snapPt) = ComputeSnap(arrowPt, VBar);

            Assert.That(needsSnap, Is.True);
            Assert.That(snapPt.X, Is.EqualTo(500).Within(0.001));
            Assert.That(snapPt.Y, Is.EqualTo(400).Within(0.001));
        }

        [Test]
        public void ComputeSnap_ArrowNearCornerOfLBar_SnapsToNearestLeg()
        {
            // Punkt blisko narożnika L-bara
            var arrowPt = new Point3(620, 20, 0);
            var (needsSnap, snapPt) = ComputeSnap(arrowPt, LBar);

            Assert.That(needsSnap, Is.True);
            // Najbliższy punkt: grot bliżej pionowego ramienia (X=600)
            Assert.That(snapPt.X, Is.EqualTo(600).Within(0.001));
        }

        [Test]
        public void ComputeSnap_TextMovedNotArrow_ArrowStaysOnBar()
        {
            // Symulacja: grot NIE ruszył (1 mm tolerancji) — tekst etykiety przesunięty
            // RcMLeaderOverrule nie rusza gdy grot blisko pręta
            var arrowPt = new Point3(500, 1, 0);  // 1 mm nad prętem — user ruszył tekst
            var (needsSnap, _) = ComputeSnap(arrowPt, HBar);
            Assert.That(needsSnap, Is.False, "przesunięcie tekstu nie powinno triggerować snapu grotu");
        }
    }
}
