#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BricsCadRc.Core
{
    /// <summary>
    /// RC_SECTION_UPDATE (bezpiecznik przekrojów, etap 3) — przeniesienie ręcznych poprawek opisów na przeliczony przekrój.
    /// Czysta logika (bez Teigha), testy tests/SectionLabelCarryTests.cs. Współrzędne względem punktu wstawienia przekroju
    /// (TopLeft) po odjęciu przesunięcia całego przekroju.
    /// Dopasowanie (plan, „Ręczne poprawki przy UPDATE”): opis stary ↔ nowy po tekście i rodzaju (kropka / pręt w widoku /
    /// T IN NIB), zachłannie po najbliższej pozycji domyślnej ≤ 300 mm; przesunięcie = bieżąca − domyślna (stara) → nowa
    /// domyślna + przesunięcie.
    /// </summary>
    public static class SectionLabelCarry
    {
        public const double MaxMatchDistance = 300.0;
        /// <summary>Przesunięcie mniejsze (mm) = opis na pozycji domyślnej.</summary>
        public const double MinOffset = 0.5;

        public const string KindDot = "DOT";
        public const string KindBar = "BAR";
        public const string KindTin = "TIN";

        public sealed class OldLabel
        {
            public string Text = "";
            public string Kind = "";
            /// <summary>Pozycja domyślna z rekordu (null = rekord sprzed etapu 3 — przesunięcia nie da się ustalić).</summary>
            public (double X, double Y)? Def;
            /// <summary>Pozycja bieżąca (po odjęciu przesunięcia całego przekroju).</summary>
            public double CurX, CurY;

            public (double X, double Y) Offset => Def.HasValue ? (CurX - Def.Value.X, CurY - Def.Value.Y) : (0.0, 0.0);
            public bool Moved => Def.HasValue && Len(Offset.X, Offset.Y) > MinOffset;
        }

        public sealed class NewLabel
        {
            public string Text = "";
            public string Kind = "";
            public double DefX, DefY;
        }

        public sealed class Result
        {
            /// <summary>Indeks nowego opisu → przesunięcie (tylko niezerowe).</summary>
            public Dictionary<int, (double DX, double DY)> Offsets = new Dictionary<int, (double DX, double DY)>();
            /// <summary>Liczba opisów z zachowanym przesunięciem.</summary>
            public int Kept;
            /// <summary>Teksty (numery pozycji) przesuniętych opisów bez pary — przesunięcie utracone.</summary>
            public List<string> Unmatched = new List<string>();
            /// <summary>Opisy bez pozycji domyślnej w rekordzie (przekrój sprzed etapu 3).</summary>
            public int NoDefault;
        }

        /// <summary>Rodzaj opisu nowego przekroju: ze strzałką = pręt w widoku, numer T IN NIB, inaczej kropka.</summary>
        public static string KindOf(bool hasLeader, bool tinNib) => hasLeader ? KindBar : tinNib ? KindTin : KindDot;

        public static Result Carry(IList<OldLabel> old, IList<NewLabel> neu, double maxDist = MaxMatchDistance)
        {
            var res = new Result();
            old = old ?? new List<OldLabel>();
            neu = neu ?? new List<NewLabel>();
            var cand = new List<(double D, int I, int J)>();
            for (int i = 0; i < old.Count; i++)
            {
                var o = old[i];
                if (!o.Def.HasValue) { if (o.Text.Length > 0) res.NoDefault++; continue; }
                for (int j = 0; j < neu.Count; j++)
                {
                    var n = neu[j];
                    if (!string.Equals(o.Text ?? "", n.Text ?? "", StringComparison.Ordinal)) continue;
                    if (!string.Equals(o.Kind ?? "", n.Kind ?? "", StringComparison.Ordinal)) continue;
                    double d = Len(o.Def.Value.X - n.DefX, o.Def.Value.Y - n.DefY);
                    if (d <= maxDist + 1e-9) cand.Add((d, i, j));
                }
            }
            var usedOld = new HashSet<int>();
            var usedNew = new HashSet<int>();
            foreach (var (_, i, j) in cand.OrderBy(c => c.D).ThenBy(c => c.I).ThenBy(c => c.J))
            {
                if (usedOld.Contains(i) || usedNew.Contains(j)) continue;
                usedOld.Add(i); usedNew.Add(j);
                if (!old[i].Moved) continue;
                res.Offsets[j] = old[i].Offset;
                res.Kept++;
            }
            for (int i = 0; i < old.Count; i++)
                if (!usedOld.Contains(i) && old[i].Moved) res.Unmatched.Add(old[i].Text ?? "");
            return res;
        }

        /// <summary>Przesunięcie całego przekroju: mediana składowych (bieżąca − domyślna) próbek (tytuł, wierzchołki betonu).</summary>
        public static (double X, double Y) MedianShift(IList<(double X, double Y)> samples)
        {
            if (samples == null || samples.Count == 0) return (0.0, 0.0);
            return (Median(samples.Select(s => s.X)), Median(samples.Select(s => s.Y)));
        }

        /// <summary>
        /// Przesunięcie całego przekroju z próbek: beton (wierzchołek [0] każdego odcinka) i tytuł. Mediana próbek betonu; tytuł
        /// dołączany jako rozjemca, gdy odcinków betonu jest ≥ 2 (przy jednym odcinku mediana dwóch próbek byłaby średnią —
        /// tytuł przesunięty osobno przesunąłby cały przekrój); sam tytuł — gdy betonu brak.
        /// </summary>
        public static (double X, double Y) SectionShift(IList<(double X, double Y)> slab, (double X, double Y)? title)
        {
            var s = new List<(double X, double Y)>(slab ?? new List<(double X, double Y)>());
            if (s.Count == 0) return title ?? (0.0, 0.0);
            if (title.HasValue && s.Count >= 2) s.Add(title.Value);
            return MedianShift(s);
        }

        private static double Median(IEnumerable<double> v)
        {
            var a = v.OrderBy(x => x).ToList();
            int n = a.Count;
            return n % 2 == 1 ? a[n / 2] : (a[n / 2 - 1] + a[n / 2]) / 2.0;
        }

        /// <summary>
        /// Grot leadera opisu przesuniętego o dU: zostaje na pręcie (labelU + dU w zakresie U któregoś pręta tej pozycji) →
        /// przesunięty o dU; inaczej na pozycji domyślnej (labelU).
        /// </summary>
        public static double LeaderTipU(double labelU, double dU, IEnumerable<(double Min, double Max)> barRanges)
        {
            double u = labelU + dU;
            if (barRanges != null)
                foreach (var (mn, mx) in barRanges)
                    if (u >= Math.Min(mn, mx) - 1e-6 && u <= Math.Max(mn, mx) + 1e-6) return u;
            return labelU;
        }

        /// <summary>
        /// Domyślne położenie litery znacznika przekroju z jego polilinii (DrawSection: wierzchołki t1 = p + dir·83, t0 = p + nrm·294,
        /// p, t2 = p + dir·300; litera w p + dir·214 + nrm·130). null — polilinia nie ma kształtu znacznika.
        /// </summary>
        public static (double X, double Y)? MarkTextDefault((double X, double Y) v0, (double X, double Y) v1, (double X, double Y) p)
        {
            double dl = Len(v0.X - p.X, v0.Y - p.Y), nl = Len(v1.X - p.X, v1.Y - p.Y);
            if (dl < 1e-6 || nl < 1e-6) return null;
            double dx = (v0.X - p.X) / dl, dy = (v0.Y - p.Y) / dl, nx = (v1.X - p.X) / nl, ny = (v1.Y - p.Y) / nl;
            // skala znacznika (detal 1:25 — 0,5; decyzja 2026-10-14) z długości ramienia w stronę patrzenia (294 na rzucie 1:50)
            double k = Math.Abs(nl - 294.0) < 1e-6 ? 1.0 : nl / 294.0;
            return (p.X + dx * (214 * k) + nx * (130 * k), p.Y + dy * (214 * k) + ny * (130 * k));
        }

        private static double Len(double x, double y) => Math.Sqrt(x * x + y * y);
    }
}
