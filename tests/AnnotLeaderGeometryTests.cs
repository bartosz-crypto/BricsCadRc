using NUnit.Framework;
using BricsCadRc.Core;
using static BricsCadRc.Core.AnnotLeaderGeometry;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// Testy logiki geometrycznej leadera annotacji.
    ///
    /// Setup: X-bars, anchorPt=(100, 2451), barsSpan=2400
    ///   → dist line pionowa od Y=51 (dół) do Y=2451 (góra)
    ///   → barMinCoordH = 51 (MinPoint.Y)
    ///
    /// Setup: Y-bars, anchorPt=(500, 300), barsSpan=1800
    ///   → dist line pozioma od X=500 (lewo) do X=2300 (prawo)
    ///   → barMinCoordV = 500 (MinPoint.X)
    /// </summary>
    [TestFixture]
    public class AnnotLeaderGeometryTests
    {
        // ── X-bars setup ──────────────────────────────────────────────────
        const double AnchorX_H = 100;
        const double AnchorY_H = 2451;   // = MaxPoint.Y prętów = górny koniec dist line
        const double BarsSpan_H = 2400;
        const double BarMinH = 51;       // AnchorY_H - BarsSpan_H
        const double BarMinV_H = 50;     // MinPoint.X (nieużywane dla X-bars)
        const double Arm = 700;

        // ── Y-bars setup ──────────────────────────────────────────────────
        const double AnchorX_V = 500;
        const double AnchorY_V = 300;
        const double BarsSpan_V = 1800;
        const double BarMinH_V = 100;
        const double BarMinV_V = 500;    // = MinPoint.X prętów = lewy koniec dist line

        // ─────────────────────────────────────────────────────────────────
        // CASE 1: X-bars, kursor W GÓRĘ (leaderVertical=True)
        // ─────────────────────────────────────────────────────────────────
        [Test]
        public void XBars_CursorUp_LeaderExitsFromTopOfDist()
        {
            double cursorX = AnchorX_H;
            double cursorY = AnchorY_H + 1500; // wyraźnie w górę

            var pts = Compute(AnchorX_H, AnchorY_H, BarsSpan_H, cursorX, cursorY, barsHorizontal: true, Arm);

            // Seg1Start powinien być na górnym końcu dist line (anchorY = MaxPoint.Y)
            Assert.That(pts.Seg1Start.X, Is.EqualTo(AnchorX_H), "Seg1Start.X powinno być na dist line");
            Assert.That(pts.Seg1Start.Y, Is.EqualTo(AnchorY_H).Within(1), "Seg1Start.Y powinno być na górnym końcu dist");

            // Seg1End powinien iść pionowo do kursora
            Assert.That(pts.Seg1End.X, Is.EqualTo(AnchorX_H), "Seg1End.X pionowo (=dist line X)");
            Assert.That(pts.Seg1End.Y, Is.EqualTo(cursorY), "Seg1End.Y = cursor.Y");

            // ArmEnd powinien iść poziomo od Seg1End
            Assert.That(pts.ArmEnd.Y, Is.EqualTo(cursorY), "Arm poziomy — Y bez zmian");
            Assert.That(pts.ArmEnd.X, Is.Not.EqualTo(cursorX).Within(1), "Arm odchodzi w bok");
        }

        // ─────────────────────────────────────────────────────────────────
        // CASE 2: X-bars, kursor Z BOKU (leaderVertical=False)
        // ─────────────────────────────────────────────────────────────────
        [Test]
        public void XBars_CursorSide_LeaderExitsFromSideOfDist()
        {
            double cursorX = AnchorX_H + 2000; // wyraźnie w bok
            double cursorY = AnchorY_H - 1200; // gdzieś w połowie dist line

            var pts = Compute(AnchorX_H, AnchorY_H, BarsSpan_H, cursorX, cursorY, barsHorizontal: true, Arm);

            // Seg1Start powinien być na dist line na wysokości kursora
            Assert.That(pts.Seg1Start.X, Is.EqualTo(AnchorX_H), "Seg1Start.X na dist line");
            Assert.That(pts.Seg1Start.Y, Is.EqualTo(cursorY), "Seg1Start.Y = cursor.Y (poziomy wyjazd z dist)");

            // Seg1End powinien iść poziomo do kursora
            Assert.That(pts.Seg1End.X, Is.EqualTo(cursorX), "Seg1End.X = cursor.X");
            Assert.That(pts.Seg1End.Y, Is.EqualTo(cursorY), "Seg1End poziomo — Y bez zmian");

            // ArmEnd powinien iść pionowo od Seg1End
            Assert.That(pts.ArmEnd.X, Is.EqualTo(cursorX), "Arm pionowy — X bez zmian");
            Assert.That(pts.ArmEnd.Y, Is.Not.EqualTo(cursorY).Within(1), "Arm odchodzi w górę/dół");
        }

        // ─────────────────────────────────────────────────────────────────
        // CASE 3: Y-bars, kursor W GÓRĘ (leaderVertical=True)
        // ─────────────────────────────────────────────────────────────────
        [Test]
        public void YBars_CursorUp_LeaderExitsFromDistAtCursorX()
        {
            double cursorX = AnchorX_V + 100;  // bliżej osi — dx małe
            double cursorY = AnchorY_V + 1500; // wyraźnie w górę — dy >> 2*dx

            var pts = Compute(AnchorX_V, AnchorY_V, BarsSpan_V, cursorX, cursorY, barsHorizontal: false, Arm);

            // Seg1Start powinien być na dist line (poziomej) przy pozycji X kursora
            Assert.That(pts.Seg1Start.X, Is.EqualTo(cursorX), "Seg1Start.X = cursor.X");
            Assert.That(pts.Seg1Start.Y, Is.EqualTo(AnchorY_V), "Seg1Start.Y na dist line");

            // Seg1End pionowo do kursora
            Assert.That(pts.Seg1End.X, Is.EqualTo(cursorX), "Seg1End pionowo — X bez zmian");
            Assert.That(pts.Seg1End.Y, Is.EqualTo(cursorY), "Seg1End.Y = cursor.Y");

            // ArmEnd poziomo
            Assert.That(pts.ArmEnd.Y, Is.EqualTo(cursorY), "Arm poziomy — Y bez zmian");
        }

        // ─────────────────────────────────────────────────────────────────
        // CASE 4: Y-bars, kursor Z BOKU (leaderVertical=False)
        // ─────────────────────────────────────────────────────────────────
        [Test]
        public void YBars_CursorSide_LeaderExitsFromEndOfDist()
        {
            double cursorX = AnchorX_V + 2500; // wyraźnie za końcem dist line (end=2300)
            double cursorY = AnchorY_V + 200;  // prawie na poziomie dist

            var pts = Compute(AnchorX_V, AnchorY_V, BarsSpan_V, cursorX, cursorY, barsHorizontal: false, Arm);

            // Seg1Start powinien być na końcu dist line (prawy koniec = anchorX + barsSpan)
            Assert.That(pts.Seg1Start.Y, Is.EqualTo(AnchorY_V), "Seg1Start.Y na dist line");
            Assert.That(pts.Seg1Start.X, Is.EqualTo(AnchorX_V + BarsSpan_V).Within(1), "Seg1Start.X = prawy koniec dist");

            // Seg1End poziomo do kursora
            Assert.That(pts.Seg1End.X, Is.EqualTo(cursorX), "Seg1End.X = cursor.X");
            Assert.That(pts.Seg1End.Y, Is.EqualTo(AnchorY_V), "Seg1End poziomo — Y bez zmian");

            // ArmEnd pionowo
            Assert.That(pts.ArmEnd.X, Is.EqualTo(cursorX), "Arm pionowy — X bez zmian");
        }

        // ─────────────────────────────────────────────────────────────────
        // InsertPt tests
        // ─────────────────────────────────────────────────────────────────
        [Test]
        public void InsertPt_XBars_CursorUp_SnapsToBarMinH()
        {
            double cursorX = AnchorX_H + 10;
            double cursorY = AnchorY_H + 1500;

            var pt = ComputeInsertPt(cursorX, cursorY, AnchorX_H, AnchorY_H,
                                     BarMinH, BarMinV_H, barsHorizontal: true, BarsSpan_H);

            Assert.That(pt.X, Is.EqualTo(cursorX), "InsertPt.X = cursor.X");
            Assert.That(pt.Y, Is.EqualTo(BarMinH),  "InsertPt.Y = barMinCoordH (snap do prętów)");
        }

        [Test]
        public void InsertPt_XBars_CursorSide_SnapsToBarMinH()
        {
            double cursorX = AnchorX_H + 2000;
            double cursorY = AnchorY_H - 1200;

            var pt = ComputeInsertPt(cursorX, cursorY, AnchorX_H, AnchorY_H,
                                     BarMinH, BarMinV_H, barsHorizontal: true, BarsSpan_H);

            Assert.That(pt.X, Is.EqualTo(cursorX), "InsertPt.X = cursor.X");
            Assert.That(pt.Y, Is.EqualTo(BarMinH),  "InsertPt.Y = barMinCoordH (dolna krawędź prętów)");
        }

        // ─────────────────────────────────────────────────────────────────
        // FIX 1 — ArmTotalLen: arm kończy się przy końcu tekstu
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void ArmTotalLen_IsArmLengthPlusTextLen()
        {
            // Arrange
            double armLength = 500.0;
            double textLen   = 200.0;

            // Act
            double result = ComputeArmTotalLen(armLength, textLen);

            // Assert
            Assert.That(result, Is.EqualTo(700.0));
        }

        [Test]
        public void ArmTotalLen_DoesNotIncludeTextArmOffset()
        {
            // Arrange — TextArmOffset=70 NIE wchodzi do armTotalLen
            const double TextArmOffset = 70.0;
            double armLength = 500.0;
            double textLen   = 150.0;

            // Act
            double result = ComputeArmTotalLen(armLength, textLen);

            // Assert
            Assert.That(result, Is.Not.EqualTo(armLength + TextArmOffset + textLen),
                "armTotalLen nie powinno zawierać TextArmOffset");
            Assert.That(result, Is.EqualTo(armLength + textLen));
        }

        // ─────────────────────────────────────────────────────────────────
        // FIX 2 — TextStartX: koniec tekstu = koniec linii arm
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void TextStartX_LeaderRight_TextEndAlignsWithArmEnd()
        {
            // Arrange
            double hDir        = 1.0;   // leaderRight
            double armTotalLen = 700.0;
            double textLen     = 200.0;
            double armEndX     = hDir * armTotalLen;  // = 700

            // Act
            double textStartX = ComputeTextStartX(hDir, armTotalLen, textLen);

            // Assert: TextLeft → text end = textStartX + textLen
            double textEndX = textStartX + textLen;
            Assert.That(textEndX, Is.EqualTo(armEndX).Within(0.001),
                "Prawy koniec tekstu powinien być przy końcu linii arm");
        }

        [Test]
        public void TextStartX_LeaderLeft_TextEndAlignsWithArmEnd()
        {
            // Arrange
            double hDir        = -1.0;  // leaderLeft
            double armTotalLen = 700.0;
            double textLen     = 200.0;
            double armEndX     = hDir * armTotalLen;  // = -700

            // Act
            double textStartX = ComputeTextStartX(hDir, armTotalLen, textLen);

            // Assert: TextRight → Position = right edge, left edge = textStartX - textLen
            double textLeftEdge = textStartX - textLen;
            Assert.That(textLeftEdge, Is.EqualTo(armEndX).Within(0.001),
                "Lewy koniec tekstu (TextRight) powinien być przy końcu linii arm");
        }

        // ─────────────────────────────────────────────────────────────────
        // FIX 3 — ConstrainTranslation: leaderHorizontal przepuszcza Y
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void ConstrainTranslation_XBars_Normal_LocksY_AllowsX()
        {
            // Arrange: X-bars, etykieta z góry/dołu — ruch boczny (X)
            // Act
            var (tx, ty) = ConstrainTranslation("X", isLeaderHorizontal: false, tx: 100, ty: 50);

            // Assert
            Assert.That(tx, Is.EqualTo(100), "X dozwolony dla normalnego lidera");
            Assert.That(ty, Is.EqualTo(0.0), "Y zablokowany dla normalnego lidera");
        }

        [Test]
        public void ConstrainTranslation_XBars_LeaderHorizontal_LocksX_AllowsY()
        {
            // Arrange: X-bars, etykieta z boku — ruch góra-dół (Y) wzdłuż prętów
            // Act
            var (tx, ty) = ConstrainTranslation("X", isLeaderHorizontal: true, tx: 100, ty: 50);

            // Assert
            Assert.That(tx, Is.EqualTo(0.0), "X zablokowany dla leaderHorizontal");
            Assert.That(ty, Is.EqualTo(50),  "Y przepuszczony dla leaderHorizontal");
        }

        [Test]
        public void ConstrainTranslation_YBars_LocksX_AllowsY()
        {
            // Act
            var (tx, ty) = ConstrainTranslation("Y", isLeaderHorizontal: false, tx: 100, ty: 50);

            // Assert
            Assert.That(tx, Is.EqualTo(0.0), "X zablokowany dla Y-bars");
            Assert.That(ty, Is.EqualTo(50),  "Y dozwolony dla Y-bars");
        }
    }
}
