using System;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Czysta logika geometryczna leadera annotacji — bez zależności od BricsCAD API.
    /// Testowalny unit-test friendly.
    /// </summary>
    public static class AnnotLeaderGeometry
    {
        public struct Pt { public double X, Y; public Pt(double x, double y) { X = x; Y = y; } }

        public struct LeaderPoints
        {
            public Pt DistPreviewStart; // _anchorPt → seg1Start (podgląd dist line)
            public Pt Seg1Start;        // punkt wejścia leadera na rozkład
            public Pt Seg1End;          // koniec seg1 (skręt)
            public Pt ArmEnd;           // koniec ramienia (gdzie tekst)
        }

        /// <summary>
        /// Oblicza punkty leadera dla podglądu w jigu.
        /// </summary>
        /// <param name="anchorX">Centrum rozkładu X</param>
        /// <param name="anchorY">Centrum rozkładu Y (= MaxPoint.Y dla X-bars, bo _anchorPt = MinPoint dla X-bars)</param>
        /// <param name="barsSpan">(count-1)*spacing</param>
        /// <param name="cursorX">Pozycja kursora X</param>
        /// <param name="cursorY">Pozycja kursora Y</param>
        /// <param name="barsHorizontal">true = X-bars (dist line pionowa)</param>
        /// <param name="armLen">Długość ramienia w podglądzie</param>
        public static LeaderPoints Compute(
            double anchorX, double anchorY,
            double barsSpan,
            double cursorX, double cursorY,
            bool barsHorizontal,
            double armLen = 700.0)
        {
            double dx = Math.Abs(cursorX - anchorX);
            double dy = Math.Abs(cursorY - anchorY);
            bool leaderVertical = dy > dx * 2.0;

            double hDir = cursorX >= anchorX ? 1.0 : -1.0;
            double vDir = cursorY >= anchorY ? 1.0 : -1.0;

            var result = new LeaderPoints();
            result.DistPreviewStart = new Pt(anchorX, anchorY);

            if (barsHorizontal)
            {
                // X-bars: dist line pionowa przy x=anchorX, rozciąga się w Y
                if (leaderVertical)
                {
                    // Etykieta z góry/dołu: wychodzi z najbliższego końca dist line
                    double distToTop    = Math.Abs(cursorY - anchorY);
                    double distToBottom = Math.Abs(cursorY - (anchorY - barsSpan));
                    double edgeY = distToTop < distToBottom
                                   ? anchorY
                                   : anchorY - barsSpan;

                    result.Seg1Start = new Pt(anchorX, edgeY);
                    result.Seg1End   = new Pt(anchorX, cursorY);
                    result.ArmEnd    = new Pt(anchorX + hDir * armLen, cursorY);
                }
                else
                {
                    // Etykieta z boku: wychodzi z dist line na wysokości kursora
                    result.Seg1Start = new Pt(anchorX, cursorY);
                    result.Seg1End   = new Pt(cursorX, cursorY);
                    result.ArmEnd    = new Pt(cursorX, cursorY + vDir * armLen);
                }
            }
            else
            {
                // Y-bars: dist line pozioma przy y=anchorY, rozciąga się w X
                if (!leaderVertical)
                {
                    // Etykieta z boku: wychodzi z końca dist line
                    double distToRight = Math.Abs(cursorX - (anchorX + barsSpan));
                    double distToLeft  = Math.Abs(cursorX - anchorX);
                    double edgeX = distToRight < distToLeft
                                   ? anchorX + barsSpan
                                   : anchorX;

                    result.Seg1Start = new Pt(edgeX, anchorY);
                    result.Seg1End   = new Pt(cursorX, anchorY);
                    result.ArmEnd    = new Pt(cursorX, anchorY + vDir * armLen);
                }
                else
                {
                    // Etykieta z góry/dołu: wychodzi z dist line na pozycji X kursora
                    result.Seg1Start = new Pt(cursorX, anchorY);
                    result.Seg1End   = new Pt(cursorX, cursorY);
                    result.ArmEnd    = new Pt(cursorX + hDir * armLen, cursorY);
                }
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        // Czysta logika geometrii bloku annotacji — testowalna bez BRX
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Oblicza armTotalLen = długość od początku ramienia do końca tekstu.
        /// Dla obu trybów: arm kończy się dokładnie przy ostatnim znaku tekstu.
        /// </summary>
        public static double ComputeArmTotalLen(double armLength, double textLen)
            => armLength + textLen;

        /// <summary>
        /// Pozycja X początku tekstu dla leaderHorizontal=True.
        /// Dla TextLeft  (hDir= 1): textStartX + textLen = hDir*armTotalLen (koniec arm)
        /// Dla TextRight (hDir=-1): textStartX - textLen = hDir*armTotalLen (koniec arm)
        /// W obu przypadkach: hDir*(armTotalLen - textLen).
        /// </summary>
        public static double ComputeTextStartX(double hDir, double armTotalLen, double textLen)
            => hDir * (armTotalLen - textLen);

        /// <summary>
        /// Ogranicza wektor translacji do dopuszczalnej osi dla bloku annotacji.
        ///   dir="X", leaderHorizontal=True  → tylko Y (arm boczny, ruch góra-dół wzdłuż prętów)
        ///   dir="X", leaderHorizontal=False → tylko X (arm pionowy, ruch lewo-prawo)
        ///   dir="Y"                         → tylko Y
        /// </summary>
        public static (double tx, double ty) ConstrainTranslation(
            string dir, bool isLeaderHorizontal, double tx, double ty)
        {
            if (dir == "X" && isLeaderHorizontal)
                return (0.0, ty);
            if (dir == "X")
                return (tx, 0.0);
            return (0.0, ty);   // dir == "Y"
        }

        /// <summary>Oblicza InsertPt bloku annotacji po kliknięciu użytkownika.</summary>
        public static Pt ComputeInsertPt(
            double cursorX, double cursorY,
            double anchorX, double anchorY,
            double barMinCoordH, double barMinCoordV,
            bool barsHorizontal,
            double barsSpan)
        {
            double dx = Math.Abs(cursorX - anchorX);
            double dy = Math.Abs(cursorY - anchorY);
            bool leaderVertical = dy > dx * 2.0;

            if (barsHorizontal)
            {
                if (leaderVertical)
                    // Etykieta góra/dół: X-snap do kursora, Y-snap do MinPoint prętów
                    return new Pt(cursorX, barMinCoordH);
                else
                    // Etykieta z boku: Y = dolna krawędź prętów (blok od barMinCoordH w górę)
                    return new Pt(cursorX, barMinCoordH);
            }
            else
            {
                if (!leaderVertical)
                    // Etykieta z boku: Y-snap do kursora, X-snap do MinPoint prętów
                    return new Pt(barMinCoordV, cursorY);
                else
                    // Etykieta góra/dół: wstaw blok tak żeby dist line była na pozycji X kursora
                    return new Pt(cursorX, cursorY);
            }
        }
    }
}
