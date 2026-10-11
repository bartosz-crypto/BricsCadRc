#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BricsCadRc.Core
{
    /// <summary>
    /// K9 (decyzja użytkownika 2026-10-11): detal zbrojenia belki 1:25 jako osobny widok w przestrzeni modelu — czysta geometria
    /// (bez Teigha), testy `tests/BeamDetailRulesTests.cs`. Okno detalu (wycinek rzutu wokół strefy belki), przycinanie odcinków do okna
    /// (Liang–Barsky), miejsce detalu bez nachodzenia na rysunek, przejście detal → rzut. Sekcja 4i ARCHITECTURE.md („Detal 1:25”).
    /// </summary>
    public static class BeamDetailRules
    {
        /// <summary>Okno: margines za bokiem strefy (końce wzdłuż i lico wewnętrzne) [mm].</summary>
        public const double Margin = 600.0;
        /// <summary>Okno: wysunięcie poza krawędź płyty przy strefie [mm].</summary>
        public const double OutsideEdge = 300.0;
        /// <summary>Krawędź płyty uznana za „przy boku strefy”, gdy leży na zewnątrz boku najdalej o tyle [mm] (nib ≤ 255).</summary>
        public const double EdgeSearch = 600.0;
        /// <summary>Rezerwa na opisy wokół okna przy wyborze miejsca detalu [mm] (opisy 2,5 mm w 1:25 + linie odniesienia).</summary>
        public const double LabelMargin = 3000.0;
        /// <summary>Dodatkowa rezerwa pod oknem na tytuł „DETAIL 'n'” / „SCALE 1:25” [mm].</summary>
        public const double TitleMargin = 1000.0;
        /// <summary>Odstęp między detalem a innymi obiektami rysunku i odstęp detali nad rzutami [mm].</summary>
        public const double PlaceGap = 1000.0;
        public const double AboveViewsGap = 3000.0;

        /// <summary>Prostokąt osiowy (X0 ≤ X1, Y0 ≤ Y1).</summary>
        public struct Rect
        {
            public double X0, Y0, X1, Y1;
            public Rect(double x0, double y0, double x1, double y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
            public double W => X1 - X0;
            public double H => Y1 - Y0;
            public Rect Offset(double dx, double dy) => new Rect(X0 + dx, Y0 + dy, X1 + dx, Y1 + dy);
            public Rect Inflate(double m) => new Rect(X0 - m, Y0 - m, X1 + m, Y1 + m);
            public bool Contains(double x, double y) => x >= X0 - 1e-9 && x <= X1 + 1e-9 && y >= Y0 - 1e-9 && y <= Y1 + 1e-9;
            /// <summary>Nachodzenie z odstępem gap (styk w odległości ≥ gap — bez nachodzenia).</summary>
            public bool Overlaps(Rect o, double gap)
                => X0 < o.X1 + gap - 1e-9 && o.X0 < X1 + gap - 1e-9 && Y0 < o.Y1 + gap - 1e-9 && o.Y0 < Y1 + gap - 1e-9;
            public override string ToString()
                => string.Format(System.Globalization.CultureInfo.InvariantCulture, "[{0:0.#},{1:0.#} .. {2:0.#},{3:0.#}]", X0, Y0, X1, Y1);
        }

        public static Rect Bbox(IList<(double X, double Y)> pts)
        {
            if (pts == null || pts.Count == 0) return new Rect();
            return new Rect(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));
        }

        /// <summary>
        /// Okno detalu = obwiednia strefy powiększona na każdym boku: gdy na zewnątrz boku (≤ <see cref="EdgeSearch"/>) leży równoległa
        /// krawędź obrysu płyty zachodząca na zakres strefy — do tej krawędzi + <see cref="OutsideEdge"/> (w poprzek: krawędź płyty − 300,
        /// widać nib i krawędź; koniec belki przy krawędzi — tak samo), inaczej bok strefy + <see cref="Margin"/> (lico wewnętrzne + 600,
        /// końce wewnętrzne + 600). Strefa L / U — jedno okno (obwiednia całej strefy).
        /// </summary>
        public static Rect Window(IList<(double X, double Y)> zone, IList<(double X, double Y)> outline)
        {
            var z = Bbox(zone);
            double Side(bool vertical, int sgn, double c, double lo, double hi)
            {
                // vertical — bok pionowy (x = c), sgn — kierunek na zewnątrz (−1 / +1); lo..hi — zakres strefy wzdłuż boku
                double best = double.NaN;
                if (outline != null && outline.Count >= 2)
                    for (int i = 0; i < outline.Count; i++)
                    {
                        var a = outline[i];
                        var b = outline[(i + 1) % outline.Count];
                        bool isV = Math.Abs(a.X - b.X) < 1e-6, isH = Math.Abs(a.Y - b.Y) < 1e-6;
                        if (vertical ? !isV : !isH) continue;
                        double ec = vertical ? a.X : a.Y;
                        double el = vertical ? Math.Min(a.Y, b.Y) : Math.Min(a.X, b.X);
                        double eh = vertical ? Math.Max(a.Y, b.Y) : Math.Max(a.X, b.X);
                        if (Math.Min(eh, hi) - Math.Max(el, lo) <= 1.0) continue;
                        double d = (ec - c) * sgn;
                        if (d < -1.0 || d > EdgeSearch) continue;
                        if (double.IsNaN(best) || d < (best - c) * sgn) best = ec;
                    }
                return double.IsNaN(best) ? c + sgn * Margin : best + sgn * OutsideEdge;
            }
            return new Rect(Side(true, -1, z.X0, z.Y0, z.Y1), Side(false, -1, z.Y0, z.X0, z.X1),
                            Side(true, 1, z.X1, z.Y0, z.Y1), Side(false, 1, z.Y1, z.X0, z.X1));
        }

        /// <summary>Odcinek a–b przycięty do prostokąta (Liang–Barsky); null — odcinek poza oknem (albo krótszy niż 1e-6).</summary>
        public static ((double X, double Y) A, (double X, double Y) B)? ClipToRect(Rect r, (double X, double Y) a, (double X, double Y) b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double t0 = 0, t1 = 1;
            bool Edge(double p, double q)
            {
                if (Math.Abs(p) < 1e-12) return q >= -1e-9;
                double t = q / p;
                if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
                else { if (t < t0) return false; if (t < t1) t1 = t; }
                return true;
            }
            if (!Edge(-dx, a.X - r.X0) || !Edge(dx, r.X1 - a.X) || !Edge(-dy, a.Y - r.Y0) || !Edge(dy, r.Y1 - a.Y)) return null;
            if (t1 - t0 < 1e-9) return null;
            var p0 = (a.X + t0 * dx, a.Y + t0 * dy);
            var p1 = (a.X + t1 * dx, a.Y + t1 * dy);
            if (Math.Abs(p1.Item1 - p0.Item1) + Math.Abs(p1.Item2 - p0.Item2) < 1e-6) return null;
            return (p0, p1);
        }

        /// <summary>
        /// Krawędzie zamkniętego wielokąta przycięte do okna; <paramref name="skipOn"/> ≠ null — pomijane krawędzie leżące na obrysie
        /// skipOn (linia uskoku pokrywa się z obrysem tam, gdzie nibu nie ma — bez dublowania linii).
        /// </summary>
        public static List<((double X, double Y) A, (double X, double Y) B)> ClipPolygon(Rect r, IList<(double X, double Y)> poly,
                                                                                         IList<(double X, double Y)> skipOn = null)
        {
            var res = new List<((double X, double Y), (double X, double Y))>();
            if (poly == null || poly.Count < 2) return res;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                if (skipOn != null)
                {
                    var m = ((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                    if (DistToBoundary(skipOn, a) < 1.0 && DistToBoundary(skipOn, b) < 1.0 && DistToBoundary(skipOn, m) < 1.0) continue;
                }
                var c = ClipToRect(r, a, b);
                if (c.HasValue) res.Add(c.Value);
            }
            return res;
        }

        /// <summary>Odległość punktu od brzegu zamkniętego wielokąta.</summary>
        public static double DistToBoundary(IList<(double X, double Y)> poly, (double X, double Y) p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
                double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2));
                double ex = a.X + t * dx - p.X, ey = a.Y + t * dy - p.Y;
                best = Math.Min(best, Math.Sqrt(ex * ex + ey * ey));
            }
            return best;
        }

        /// <summary>
        /// Miejsce detalu (prostokąt w × h): od punktu (x0, y0) w prawo, aż prostokąt nie nachodzi (z odstępem gap) na żaden z zajętych.
        /// Przy kolizji skok za prawą krawędź najdalszej kolidującej obwiedni + gap — zawsze kończy się (x rośnie).
        /// </summary>
        public static Rect Place(double w, double h, IList<Rect> occupied, double x0, double y0, double gap)
        {
            double x = x0;
            for (int k = 0; k < 100000; k++)
            {
                var r = new Rect(x, y0, x + w, y0 + h);
                double next = double.NaN;
                if (occupied != null)
                    foreach (var o in occupied)
                        if (r.Overlaps(o, gap) && (double.IsNaN(next) || o.X1 + gap > next)) next = o.X1 + gap;
                if (double.IsNaN(next) || next <= x) return r;
                x = next;
            }
            return new Rect(x, y0, x + w, y0 + h);
        }

        /// <summary>Przesunięcie detalu względem rzutu: MinPoint(ANCHOR) − MinPoint(strefy) (punkt detalu = punkt rzutu + przesunięcie).</summary>
        public static (double X, double Y) ShiftOf(IList<(double X, double Y)> anchor, IList<(double X, double Y)> zone)
        {
            var a = Bbox(anchor); var z = Bbox(zone);
            return (a.X0 - z.X0, a.Y0 - z.Y0);
        }

        /// <summary>ANCHOR przystaje do strefy przesuniętej o shift (ta sama liczba wierzchołków, każdy wierzchołek ≤ tol).</summary>
        public static bool Congruent(IList<(double X, double Y)> anchor, IList<(double X, double Y)> zone, (double X, double Y) shift,
                                     double tol = 1.0)
        {
            if (anchor == null || zone == null || anchor.Count != zone.Count || anchor.Count == 0) return false;
            foreach (var z in zone)
            {
                double x = z.X + shift.X, y = z.Y + shift.Y;
                if (!anchor.Any(a => Math.Abs(a.X - x) <= tol && Math.Abs(a.Y - y) <= tol)) return false;
            }
            return true;
        }

        /// <summary>Punkt detalu → punkt rzutu strefy (odjęcie przesunięcia).</summary>
        public static (double X, double Y) MapToPlan((double X, double Y) p, (double X, double Y) shift) => (p.X - shift.X, p.Y - shift.Y);
    }
}
