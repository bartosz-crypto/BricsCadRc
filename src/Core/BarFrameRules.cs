using System;
using System.Collections.Generic;
using System.Linq;

// Kompilowany także w tests (net8.0, Nullable enable) — kontekst nullable jak we wtyczce (wyłączony).
#nullable disable

namespace BricsCadRc.Core
{
    /// <summary>
    /// Rama pręta-polilinii: obrót układu lokalnego (oś X = kierunek pręta) do świata i odbicie.
    /// Odbicie = y → −y W UKŁADZIE LOKALNYM, przed obrotem (świat = R(Angle) · M · lokal, M = diag(1, −1)).
    /// Tożsamość (Angle == 0 dokładnie, bez odbicia) = dawne zachowanie wzorca: pręt poziomy, wynik bit w bit.
    /// </summary>
    internal readonly struct BarFrame2
    {
        public readonly double Angle;
        public readonly bool Mirrored;

        public BarFrame2(double angle, bool mirrored)
        {
            Angle = angle;
            Mirrored = mirrored;
        }

        public static BarFrame2 Identity => new BarFrame2(0.0, false);

        public bool IsIdentity => Angle == 0.0 && !Mirrored;

        /// <summary>Wektor lokalny → świat (R · M · v).</summary>
        public (double X, double Y) Apply(double x, double y)
        {
            if (IsIdentity) return (x, y);
            double ly = Mirrored ? -y : y;
            double c = Math.Cos(Angle), s = Math.Sin(Angle);
            return (c * x - s * ly, s * x + c * ly);
        }
    }

    /// <summary>
    /// Poprawka #4 (docs/POPRAWKI_WZORCA.md, W1d): rozpoznanie obrotu i odbicia pręta-polilinii z jego obrysu, żeby
    /// przebudowa (RebuildCompanions) nie kładła pręta zawsze poziomo. Czysta logika bez API BricsCAD (testy jednostkowe).
    ///
    /// Obrys pręta = left[0..n−1] + reverse(right[0..n−1]) (jak SingleBarEngine.ComputeOutlineVertices). Dopasowanie
    /// wierzchołek po wierzchołku (rozróżnia odbicie prostego i kształtów symetrycznych, wykrywa zepsuty obrys).
    /// Pierścień (kształt 75 wg BS 8666:2020; stare rekordy „44” bez znacznika ShapeStd czytane jako 75 — W4c) — osobne
    /// dopasowanie po środku i promieniu; rozpoznanie TYLKO przez IsRingShape.
    /// </summary>
    internal static class BarFrameRules
    {
        /// <summary>Tolerancja dopasowania obrysu [mm].</summary>
        public const double FitTol = 0.5;

        /// <summary>|kąt| &lt; SnapAngle i bez odbicia → tożsamość DOKŁADNIE (bit w bit z wzorcem).</summary>
        public const double SnapAngle = 1e-9;

        /// <summary>Kod kształtu pręta-pierścienia wg BS 8666:2020 (W4c; dawniej „44”, dziś 44 = „kapelusz”).</summary>
        public const string RingShapeCode = "75";

        /// <summary>
        /// Jedyne miejsce rozpoznania pręta-pierścienia (logika #5 — przebudowa z zachowaniem środka). W4c: kod 75
        /// (BS 8666:2020). Stary pierścień „44” (rekord bez znacznika [15] ShapeStd = 2020) parser ReadBarXData podaje
        /// w pamięci jako „75” (BarData.LegacyRing44) — tu nie trafia.
        /// </summary>
        public static bool IsRingShape(string shapeCode) => shapeCode == RingShapeCode;

        /// <summary>
        /// Obrys w układzie lokalnym (start (0,0), kierunek +X) — ta sama arytmetyka co ComputeOutlineVertices.
        /// null dla pierścienia albo gdy kształt ma &lt; 2 punkty osi.
        /// </summary>
        public static List<(double X, double Y)> LocalOutline(string shape, double[] pv, double d)
            => LocalOutline(shape, pv, d, BarGeometryOptions.Current);

        /// <summary>Obrys lokalny w wybranym wariancie geometrii (Plan W4, 0-POPRAWKI p.2). null jak LocalOutline.</summary>
        public static List<(double X, double Y)> LocalOutline(string shape, double[] pv, double d, BarGeometryOptions options)
        {
            if (IsRingShape(shape)) return null;
            return LocalOutlineFrom(BarGeometryBuilder.GetLocalPoints(shape, pv, d, options), d);
        }

        /// <summary>Obrys lokalny geometrii sprzed W4 (LegacyBarGeometry) — rozpoznanie ramy starych obrysów.</summary>
        public static List<(double X, double Y)> LegacyLocalOutline(string shape, double[] pv, double d)
            => LocalOutline(shape, pv, d, BarGeometryOptions.Legacy);

        /// <summary>
        /// Rama z obrysu dopasowanego do KTÓREGOKOLWIEK wariantu geometrii (BarGeometryOptions.FitCandidates:
        /// bieżąca → pozostałe kombinacje flag → Legacy) — obrysy zapisane w DWG w dowolnym etapie W4. Pierwszy
        /// wariant z dopasowaniem (maxDev ≤ FitTol) wygrywa. false dla pierścienia albo braku dopasowania.
        /// </summary>
        public static bool TryFitVariants(IReadOnlyList<(double X, double Y)> world, string shape, double[] pv, double d,
                                          out BarFrame2 frame, out BarGeometryOptions matched)
            => TryFitVariants(world, shape, pv, d, out frame, out matched, false);

        /// <summary>
        /// Jak TryFitVariants; preferHistorical → najpierw warianty historyczne (bez Bs2020Sharp), potem W4d — obrys pasujący
        /// do obu (np. 51 A = B, C = 0: W4b–W4c == W4d) bez znacznika W4d uznawany za historyczny (W4d, runda 2).
        /// </summary>
        public static bool TryFitVariants(IReadOnlyList<(double X, double Y)> world, string shape, double[] pv, double d,
                                          out BarFrame2 frame, out BarGeometryOptions matched, bool preferHistorical)
        {
            frame = BarFrame2.Identity;
            matched = BarGeometryOptions.Current;
            if (world == null || IsRingShape(shape)) return false;
            var order = preferHistorical
                ? BarGeometryOptions.FitCandidates.Where(o => !o.Bs2020Sharp).Concat(BarGeometryOptions.FitCandidates.Where(o => o.Bs2020Sharp))
                : BarGeometryOptions.FitCandidates;
            foreach (var opt in order)
            {
                var local = LocalOutline(shape, pv, d, opt);
                if (local == null || local.Count != world.Count) continue;
                if (TryFit(world, local, out var f, out _))
                {
                    frame = f;
                    matched = opt;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Plan W4d (runda 1, uwaga 1): kształty, których UKŁAD RAMION zmienił się w W4d (geometria wg rysunków normy) —
        /// ramię o tym samym numerze nie jest tym samym odcinkiem co przed W4 (np. 33: dolna prosta bieżni ≠ ramię 1 „S”).
        /// 12 (tylko promień R) i kształty bez zmian W4d — nie.
        /// </summary>
        public static bool ArmLayoutChangedInW4d(string shape)
        {
            switch (shape)
            {
                case "14": case "15": case "22": case "24": case "33": case "46": case "51": return true;
                default: return false;
            }
        }

        /// <summary>
        /// Plan W4, 0-POPRAWKI p.1 — długość rozkładu Manual (RC_DISTRIBUTION, RC_EDIT_DISTRIBUTION): odcinek obrysu
        /// wskazany na BIEŻĄCYM obrysie (indeks segIdx: wierzchołek segIdx → segIdx+1, jak GetViewLengthManual), długość
        /// z obrysu WZORCOWEGO dla TEGO SAMEGO odcinka. Obrys wzorcowy (W4d, runda 1, uwaga 1):
        ///  (a) pręt BEZ znacznika geometrii W4d (geometryW4d = false: rekord sprzed W4d albo zapisany starą wtyczką) albo
        ///      obrys historyczny (sprzed W4, W4a, W4b–W4c — bez Bs2020Sharp) albo kształt bez zmiany układu ramion w W4d →
        ///      geometria sprzed W4 (LegacyBarGeometry) — wynik jak we wzorcu; bez znacznika obrys dopasowywany najpierw do
        ///      wariantów historycznych (niejednoznaczne dopasowanie → historyczny);
        ///  (b) pręt ze znacznikiem [15] ≥ 2021 (BarData.GeometryW4d), obrys rozpoznany jako W4d lub późniejszy (Bs2020Sharp)
        ///      i kształt ze zmienionym układem ramion (ArmLayoutChangedInW4d) → geometria W4d tego wariantu BEZ wymiarów
        ///      zewnętrznych (ExternalDims = false) — punkt odniesienia W4d zostaje także po W4e.
        /// Mapowanie (liczba wierzchołków wariantów może się różnić przy łukach): wariant obrysu z TryFitVariants →
        /// odcinek obrysu → strona (lewa: oś j = segIdx; prawa: j = 2n − 2 − segIdx) i odcinek osi j → etykieta
        /// (ramię k / łuk w ostrym punkcie c, pododcinek m — BarGeometryBuilder.GetSegmentLabels) → ten sam odcinek osi
        /// i ta sama strona w obrysie wzorcowym. Pierścień — RingSegmentLength (W4e). null (wywołujący zostawia długość
        /// zmierzoną, jak dotąd): pierścień z obrysem sprzed W4e, obrys
        /// niedopasowany, zaślepka końca, etykieta bez odpowiednika, NaN. W4e: obrys z wymiarami zewnętrznymi i etykieta bez
        /// odpowiednika → ten sam odcinek wariantu bez ExternalDims (długość taka jak zmierzona przed W4e — rozkład Manual
        /// przed / po W4e bez zmian). Istniejące rozkłady nie są przeliczane — reguła działa przy wskazaniu ramienia.
        /// </summary>
        public static double? LegacySegmentLength(IReadOnlyList<(double X, double Y)> world, int segIdx,
                                                  string shape, double[] pv, double d, bool geometryW4d = false)
        {
            if (IsRingShape(shape)) return RingSegmentLength(world, segIdx, pv, d);
            if (world == null || world.Count < 4 || world.Count % 2 != 0) return null;
            int n = world.Count / 2;
            if (segIdx < 0 || segIdx >= world.Count - 1 || segIdx == n - 1) return null;   // zaślepka końca
            bool left = segIdx < n - 1;
            int j = left ? segIdx : 2 * n - 2 - segIdx;

            if (!TryFitVariants(world, shape, pv, d, out _, out var matched, preferHistorical: !geometryW4d)) return null;
            // Obrys wzorcowy: (b) znacznik W4d + obrys W4d, geometria W4d bez wymiarów zewnętrznych; (a) sprzed W4
            var refOpt = geometryW4d && matched.Bs2020Sharp && ArmLayoutChangedInW4d(shape)
                ? matched.WithoutExternalDims()
                : BarGeometryOptions.Legacy;
            var labels = BarGeometryBuilder.GetSegmentLabels(shape, pv, d, matched);
            if (labels == null || j >= labels.Count) return null;
            int jl = FindLabel(BarGeometryBuilder.GetSegmentLabels(shape, pv, d, refOpt), labels[j]);
            if (jl < 0)
            {
                // W4e (0-POPRAWKI p.1): brak odpowiednika (np. 44 — wzorzec to pierścień; półkola 22 / 33) — przed W4e wywołujący
                // brał długość ZMIERZONĄ na obrysie (umowa osi). Obrys z wymiarami zewnętrznymi → ten sam odcinek wariantu bez
                // ExternalDims (= długość zmierzona przed W4e); obrys bez ExternalDims → null jak dotąd.
                if (!matched.ExternalDims) return null;
                refOpt = matched.WithoutExternalDims();
                jl = FindLabel(BarGeometryBuilder.GetSegmentLabels(shape, pv, d, refOpt), labels[j]);
                if (jl < 0) return null;
            }

            var lo = LocalOutline(shape, pv, d, refOpt);
            if (lo == null || lo.Count % 2 != 0) return null;
            int nl = lo.Count / 2;
            int sl = left ? jl : 2 * nl - 2 - jl;
            if (sl < 0 || sl + 1 >= lo.Count) return null;
            double dx = lo[sl + 1].X - lo[sl].X, dy = lo[sl + 1].Y - lo[sl].Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            return double.IsNaN(len) || double.IsInfinity(len) ? (double?)null : len;
        }

        /// <summary>
        /// W4e (runda poprawek 1, uwaga 1) — rozkład Manual na pierścieniu 75: obrys zewnętrzny po W4e ma promień A/2 (dawniej
        /// A/2 + d/2), więc zmierzona cięciwa odcinka okręgu byłaby krótsza. Obrys pierścienia z wymiarami zewnętrznymi
        /// (TryFitRing, promień = (A − d)/2 + d/2 ± FitTol) → cięciwa TEGO SAMEGO odcinka (ta sama liczba wierzchołków)
        /// okręgu sprzed W4e o promieniu A/2 + d/2; obrys sprzed W4e albo inny → null (długość zmierzona, jak dotąd).
        /// Polilinia i pręt-blok (pierwsza polilinia BTR = okrąg zewnętrzny).
        /// </summary>
        private static double? RingSegmentLength(IReadOnlyList<(double X, double Y)> world, int segIdx, double[] pv, double d)
        {
            if (world == null || world.Count < 8 || segIdx < 0 || segIdx >= world.Count - 1) return null;
            if (!TryFitRing(world, out var c, out _)) return null;
            double r = Math.Sqrt((world[0].X - c.X) * (world[0].X - c.X) + (world[0].Y - c.Y) * (world[0].Y - c.Y));
            double a = pv != null && pv.Length > 0 ? pv[0] : d;
            double outerNow = BarDimensionRules.RingAxisRadius(a, d) + d / 2.0;
            if (Math.Abs(r - outerNow) > FitTol) return null;
            double outerOld = a / 2.0 + d / 2.0;
            return 2.0 * outerOld * Math.Sin(Math.PI / world.Count);
        }

        /// <summary>Indeks pierwszej etykiety równej lab; −1 gdy brak (także labels == null).</summary>
        private static int FindLabel(List<BarSegmentLabel> labels, BarSegmentLabel lab)
        {
            if (labels == null) return -1;
            for (int i = 0; i < labels.Count; i++)
                if (labels[i].SameAs(lab)) return i;
            return -1;
        }

        /// <summary>
        /// Obrys z punktów osi (left[0..n−1] + reverse(right[0..n−1])) — arytmetyka ComputeOutlineVertices.
        /// null, gdy &lt; 2 punkty osi.
        /// </summary>
        public static List<(double X, double Y)> LocalOutlineFrom(List<(double X, double Y)> localPts, double d)
        {
            if (localPts == null) return null;
            int n = localPts.Count;
            if (n < 2) return null;

            double halfD = d / 2.0;
            var left  = new (double X, double Y)[n];
            var right = new (double X, double Y)[n];
            for (int i = 0; i < n; i++)
            {
                double tx, ty;
                if      (i == 0)     { tx = localPts[1].X   - localPts[0].X;   ty = localPts[1].Y   - localPts[0].Y; }
                else if (i == n - 1) { tx = localPts[n-1].X - localPts[n-2].X; ty = localPts[n-1].Y - localPts[n-2].Y; }
                else                 { tx = localPts[i+1].X - localPts[i-1].X; ty = localPts[i+1].Y - localPts[i-1].Y; }

                double tlen = Math.Sqrt(tx * tx + ty * ty);
                if (tlen < 1e-9) { tx = 1; ty = 0; }
                else             { tx /= tlen; ty /= tlen; }

                double nx = -ty, ny = tx;
                left[i]  = (localPts[i].X + nx * halfD, localPts[i].Y + ny * halfD);
                right[i] = (localPts[i].X - nx * halfD, localPts[i].Y - ny * halfD);
            }

            var result = new List<(double X, double Y)>(2 * n);
            for (int i = 0; i < n; i++) result.Add(left[i]);
            for (int i = n - 1; i >= 0; i--) result.Add(right[i]);
            return result;
        }

        /// <summary>
        /// Dopasowanie obrysu w świecie do obrysu lokalnego: world[i] ≈ world[0] + R(Angle)·M·(local[i] − local[0]).
        /// Kandydaci: bez odbicia i z odbiciem (lokal (x, −y)); kąt z wierzchołka k najdalszego od local[0]; wybór
        /// mniejszej odchyłki maxDev; akceptacja maxDev ≤ FitTol. Kąt bez odbicia &lt; SnapAngle → dokładnie 0.
        /// </summary>
        public static bool TryFit(IReadOnlyList<(double X, double Y)> world, IReadOnlyList<(double X, double Y)> local,
                                  out BarFrame2 frame, out double maxDev)
        {
            frame = BarFrame2.Identity;
            maxDev = double.PositiveInfinity;
            if (world == null || local == null || world.Count != local.Count || world.Count < 4) return false;

            int n = local.Count, k = 0;
            double best = -1;
            for (int i = 1; i < n; i++)
            {
                double dx = local[i].X - local[0].X, dy = local[i].Y - local[0].Y;
                double dd = dx * dx + dy * dy;
                if (dd > best) { best = dd; k = i; }
            }
            if (best < 1e-12) return false;

            double wkx = world[k].X - world[0].X, wky = world[k].Y - world[0].Y;
            if (wkx * wkx + wky * wky < 1e-12) return false;

            bool found = false;
            foreach (bool mirrored in new[] { false, true })
            {
                double lkx = local[k].X - local[0].X;
                double lky = (mirrored ? -1 : 1) * (local[k].Y - local[0].Y);
                double angle = Normalize(Math.Atan2(wky, wkx) - Math.Atan2(lky, lkx));
                double c = Math.Cos(angle), s = Math.Sin(angle);
                double dev = 0;
                for (int i = 0; i < n && dev <= maxDev; i++)
                {
                    double lx = local[i].X - local[0].X;
                    double ly = (mirrored ? -1 : 1) * (local[i].Y - local[0].Y);
                    double px = world[0].X + c * lx - s * ly;
                    double py = world[0].Y + s * lx + c * ly;
                    double e = Math.Sqrt((px - world[i].X) * (px - world[i].X) + (py - world[i].Y) * (py - world[i].Y));
                    if (e > dev) dev = e;
                }
                if (dev < maxDev)
                {
                    maxDev = dev;
                    frame = Snap(angle, mirrored);
                    found = true;
                }
            }
            return found && maxDev <= FitTol;
        }

        /// <summary>
        /// Pierścień: środek = średnia wierzchołków; kierunek = v0 − c (lokalnie wierzchołek 0 leży na +X od środka);
        /// odbicie = obieg zgodny z ruchem wskazówek zegara (cross(v0 − c, v1 − c) &lt; 0). Poprawny, gdy ≥ 8 wierzchołków
        /// i |vi − c| stałe ±FitTol (skalowany pierścień też — promień i tak bierze się z danych pręta).
        /// </summary>
        public static bool TryFitRing(IReadOnlyList<(double X, double Y)> world, out (double X, double Y) center, out BarFrame2 frame)
        {
            center = (0.0, 0.0);
            frame = BarFrame2.Identity;
            if (world == null || world.Count < 8) return false;

            double cx = 0, cy = 0;
            foreach (var p in world) { cx += p.X; cy += p.Y; }
            cx /= world.Count;
            cy /= world.Count;

            double r0x = world[0].X - cx, r0y = world[0].Y - cy;
            double r0 = Math.Sqrt(r0x * r0x + r0y * r0y);
            if (r0 < FitTol) return false;
            foreach (var p in world)
            {
                double r = Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
                if (Math.Abs(r - r0) > FitTol) return false;
            }

            double r1x = world[1].X - cx, r1y = world[1].Y - cy;
            bool mirrored = r0x * r1y - r0y * r1x < 0;
            center = (cx, cy);
            frame = Snap(Math.Atan2(r0y, r0x), mirrored);
            return true;
        }

        /// <summary>
        /// Kierunek z cięciwy osi (ostatnia deska ratunku, gdy obrysu nie da się dopasować): kąt wektora
        /// axis[0] → axis[n−1] w świecie minus kąt tego wektora lokalnie; bez odbicia. false przy zdegenerowanej cięciwie.
        /// </summary>
        public static bool TryChord((double X, double Y) worldStart, (double X, double Y) worldEnd,
                                    (double X, double Y) localStart, (double X, double Y) localEnd, out BarFrame2 frame)
        {
            frame = BarFrame2.Identity;
            double wx = worldEnd.X - worldStart.X, wy = worldEnd.Y - worldStart.Y;
            double lx = localEnd.X - localStart.X, ly = localEnd.Y - localStart.Y;
            if (wx * wx + wy * wy < 1e-12 || lx * lx + ly * ly < 1e-12) return false;
            frame = Snap(Normalize(Math.Atan2(wy, wx) - Math.Atan2(ly, lx)), false);
            return true;
        }

        private static BarFrame2 Snap(double angle, bool mirrored)
            => new BarFrame2(!mirrored && Math.Abs(angle) < SnapAngle ? 0.0 : angle, mirrored);

        /// <summary>Kąt do przedziału (−π, π].</summary>
        private static double Normalize(double a)
        {
            while (a <= -Math.PI) a += 2 * Math.PI;
            while (a > Math.PI) a -= 2 * Math.PI;
            return a;
        }
    }
}
