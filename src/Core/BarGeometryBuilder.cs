using System;
using System.Collections.Generic;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Plan W4 (docs/PLAN_W4_BS8666.md, 0-POPRAWKI p.2) — wariant geometrii pręta. Obrysy zapisane w DWG w różnych
    /// etapach W4 rozpoznaje BarFrameRules.TryFitVariants, próbując WSZYSTKICH kombinacji flag (16).
    /// Wszystkie flagi false = Legacy = geometria sprzed W4 (LegacyBarGeometry, zamrożona).
    /// Każda inna kombinacja = parametry po nazwie (W4a-3) i odcinki zerowe bez łuku (W4a-2) — plus flagi:
    ///   TangentArcs  — łuk styczny: punkt styczności r·tan(φ/2) od narożnika (W4a-1); false → zawsze r (jak dawniej);
    ///   TableRadius  — geometria W4b: promień z Table 2 (H25 → 87; false = 2d / 3.5d sprzed W4b) oraz haki 51/63
    ///                  przy pustym C = C_eff i lewy hak 63 długości C (false = haki sprzed W4b: max(16d,160) /
    ///                  max(14d,150), lewy hak 63 od (0, hak));
    ///   Bs2020Sharp  — ostre punkty wg rysunków normy (W4d, Table 5): 12 łuk o promieniu R; 14, 15 A skośne + C poziome
    ///                  (kąt z B i D, BarShape.SlopeAngle); 22 A–B–półkole C–D; 24 A poziome, B skos (kąt z D/E), C pion;
    ///                  33 „bieżnia” (półkola B, długość A, zakład C); 46 A–B skos–C–B skos–E (kąt z D); 51 A = wysokość;
    ///                  false = ostre punkty sprzed W4d;
    ///   ExternalDims — wymiary zewnętrzne kl. 7.1 (W4e, poprawka #13): wymiary A–E przeliczane na oś
    ///                  (BarDimensionRules.ToAxis — rys. 4 / 5 normy), łuk osi r + d/2, pierścień 75 o średnicy osi A − d;
    ///                  false = wymiary jako oś (jak przed W4e).
    ///   SlopeFromB   — W4h: kąt ramienia skośnego (14, 15, 24) także z JEDNEGO wymiaru (BarShape.SlopeAngle fromSingle:
    ///                  θ = asin(B/A) itd.); false = reguła W4d–W4e (oba wymiary albo kąt domyślny). Ma znaczenie tylko
    ///                  z Bs2020Sharp.
    /// Rzeczywiste etapy: przed W4 = Legacy; W4a = {TangentArcs}; W4b–W4c = {TangentArcs, TableRadius} = W4b;
    /// W4d = {Bs2020Sharp, TangentArcs, TableRadius} = W4d; W4e = W4d + ExternalDims = W4e; W4h = wszystkie flagi = Current.
    /// </summary>
    public readonly struct BarGeometryOptions
    {
        public bool Bs2020Sharp  { get; }
        public bool TangentArcs  { get; }
        public bool ExternalDims { get; }
        public bool TableRadius  { get; }
        public bool SlopeFromB   { get; }

        public BarGeometryOptions(bool bs2020Sharp, bool tangentArcs, bool externalDims, bool tableRadius, bool slopeFromB = false)
        {
            Bs2020Sharp  = bs2020Sharp;
            TangentArcs  = tangentArcs;
            ExternalDims = externalDims;
            TableRadius  = tableRadius;
            SlopeFromB   = slopeFromB;
        }

        /// <summary>Wszystkie flagi false — geometria sprzed W4 (LegacyBarGeometry).</summary>
        public bool IsLegacy => !Bs2020Sharp && !TangentArcs && !ExternalDims && !TableRadius && !SlopeFromB;

        /// <summary>Geometria sprzed W4 — tylko do rozpoznania ramy starych obrysów.</summary>
        public static BarGeometryOptions Legacy => new BarGeometryOptions(false, false, false, false);

        /// <summary>Etap W4a (staging): łuki styczne, promień i haki sprzed W4b.</summary>
        public static BarGeometryOptions W4a => new BarGeometryOptions(false, true, false, false);

        /// <summary>Etap W4b–W4c (staging): łuki styczne, Table 2, haki 51/63 wg 2020; ostre punkty sprzed W4d.</summary>
        public static BarGeometryOptions W4b => new BarGeometryOptions(false, true, false, true);

        /// <summary>
        /// Etap W4d (staging): ostre punkty wg rysunków normy (Bs2020Sharp), łuki styczne, Table 2, haki 51/63 wg 2020;
        /// wymiary jako oś, kąt skosu z dwóch wymiarów albo domyślny.
        /// </summary>
        public static BarGeometryOptions W4d => new BarGeometryOptions(true, true, false, true);

        /// <summary>Etap W4e (staging): W4d + wymiary zewnętrzne kl. 7.1 (ExternalDims); kąt skosu jak W4d.</summary>
        public static BarGeometryOptions W4e => new BarGeometryOptions(true, true, true, true);

        /// <summary>
        /// Geometria bieżąca (W4h): W4e + kąt skosu także z jednego wymiaru (SlopeFromB — decyzja użytkownika 2026-10-09).
        /// </summary>
        public static BarGeometryOptions Current => new BarGeometryOptions(true, true, true, true, true);

        /// <summary>
        /// Ten sam wariant w umowie OSI (bez ExternalDims) — punkt odniesienia długości rozkładu Manual (D29, D37): obrys
        /// z wymiarami zewnętrznymi → odcinek geometrii bez wymiarów zewnętrznych, z tą samą regułą kąta.
        /// </summary>
        public BarGeometryOptions WithoutExternalDims()
            => new BarGeometryOptions(Bs2020Sharp, TangentArcs, false, TableRadius, SlopeFromB);

        public bool SameAs(BarGeometryOptions o)
            => Bs2020Sharp == o.Bs2020Sharp && TangentArcs == o.TangentArcs && ExternalDims == o.ExternalDims
               && TableRadius == o.TableRadius && SlopeFromB == o.SlopeFromB;

        /// <summary>
        /// Kolejność prób dopasowania obrysu: bieżąca (W4h) → W4e → W4d → W4b (etap W4b–W4c) → W4a → pozostałe kombinacje
        /// czterech flag (bez SlopeFromB — obrysy z regułą kąta W4h zapisuje tylko geometria bieżąca) → Legacy (ostatnia).
        /// </summary>
        public static IReadOnlyList<BarGeometryOptions> FitCandidates { get; } = BuildCandidates();

        private static List<BarGeometryOptions> BuildCandidates()
        {
            var list = new List<BarGeometryOptions> { Current, W4e, W4d, W4b, W4a };
            for (int m = 15; m >= 1; m--)
            {
                var o = new BarGeometryOptions((m & 8) != 0, (m & 4) != 0, (m & 2) != 0, (m & 1) != 0);
                bool known = false;
                foreach (var k in list) if (k.SameAs(o)) known = true;
                if (!known) list.Add(o);
            }
            list.Add(Legacy);
            return list;
        }

        public override string ToString()
            => IsLegacy ? "Legacy"
             : $"Bs2020Sharp={Bs2020Sharp} TangentArcs={TangentArcs} ExternalDims={ExternalDims} TableRadius={TableRadius} SlopeFromB={SlopeFromB}";
    }

    /// <summary>
    /// Etykieta odcinka osi (punkty i → i+1 z GetLocalPoints) — Plan W4, 0-POPRAWKI p.1: mapowanie odcinka obrysu między
    /// wariantami geometrii (różna liczba punktów łuku). Kind: Leg = prosty odcinek ramienia nr Index (ramię k = odcinek
    /// ostrych punktów k → k+1, numeracja PRZED usunięciem odcinków zerowych); Arc = łuk w ostrym punkcie Index, pododcinek Sub.
    /// </summary>
    public readonly struct BarSegmentLabel
    {
        public const int Leg = 0, Arc = 1;
        public int Kind  { get; }
        public int Index { get; }
        public int Sub   { get; }
        public BarSegmentLabel(int kind, int index, int sub) { Kind = kind; Index = index; Sub = sub; }
        public bool SameAs(BarSegmentLabel o) => Kind == o.Kind && Index == o.Index && Sub == o.Sub;
        public override string ToString() => (Kind == Leg ? "ramię " : "łuk ") + Index + (Kind == Arc ? "." + Sub : "");
    }

    /// <summary>
    /// Czysta geometria kształtów prętów wg BS8666.
    /// Nie zależy od BricsCAD API — testowalny w net8.0.
    ///
    /// Układ lokalny:
    ///   X = wzdłuż pręta (wymiar A)
    ///   Y = prostopadły, "w górę" w widoku elewacji
    ///
    /// Każdy naróż wewnętrzny zastępowany jest łukiem kołowym
    /// o promieniu r = BarShape.MinBendRadius(diameter) (W4e, geometria bieżąca: łuk OSI r + d/2, wymiary zewnętrzne —
    /// BarDimensionRules).
    /// Łuk aproksymowany jest (steps+1) punktami co 15° (steps=6 → 7 pkt).
    /// </summary>
    public static class BarGeometryBuilder
    {
        private static readonly HashSet<string> _supported = new HashSet<string>
        {
            "00", "11", "12", "13", "14", "15",
            "21", "22", "23", "24", "25", "26", "27", "28", "29",
            "31", "32", "33", "34", "35", "36",
            "41", "44", "46", "47",
            "51", "56", "63", "64",
            "75", "98", "99"
        };

        /// <summary>Zwraca true jeśli shape code jest zaimplementowany (nie fallback).</summary>
        public static bool IsSupported(string shapeCode) =>
            shapeCode != null && _supported.Contains(shapeCode);

        /// <summary>
        /// Zwraca węzły polilinii w układzie lokalnym pręta z łukami zagięć.
        /// Każde zagięcie = 7 punktów łuku (steps=6, co 15°) zamiast 1 ostrego węzła.
        /// Nieobsługiwane shape codes → fallback do 00 (prosta linia A).
        /// Pierścień (W4c: 75 — BarFrameRules.IsRingShape) zwraca punkty okręgu bez aproksymacji zagięć;
        /// 44 = „kapelusz” BS 8666:2020 (zwykły kształt z zagięciami). 77 (helisa) nieobsługiwany.
        /// </summary>
        public static List<(double X, double Y)> GetLocalPoints(
            string shapeCode, double[] paramValues, double diameter)
            => GetLocalPoints(shapeCode, paramValues, diameter, BarGeometryOptions.Current);

        /// <summary>
        /// Rozkład ANY (decyzja 2026-10-14, jak „Any” w ASD): oś całego pręta w układzie bloku rozkładu — najdłuższy prosty odcinek
        /// kształtu leży na osi +X od (0, 0) (pręty X) albo +Y (pręty Y, <paramref name="alongY"/>); <paramref name="mirror"/> — odbicie
        /// w poprzek tego odcinka (zagięcia po drugiej stronie), <paramref name="reverse"/> — odbicie wzdłuż (odcinek od długości do 0,
        /// zagięcie przy punkcie wstawienia). Pusta lista, gdy kształt nie ma punktów.
        /// </summary>
        public static List<(double X, double Y)> AnyAxis(string shapeCode, double[] paramValues, double diameter, bool mirror, bool alongY,
                                                         bool reverse = false)
        {
            var pts = GetLocalPoints(shapeCode ?? "00", (double[])paramValues.Clone(), diameter);
            var res = new List<(double X, double Y)>();
            if (pts == null || pts.Count < 2) return res;
            int best = 0; double bestLen = -1;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                double l = Math.Sqrt((pts[i + 1].X - pts[i].X) * (pts[i + 1].X - pts[i].X) + (pts[i + 1].Y - pts[i].Y) * (pts[i + 1].Y - pts[i].Y));
                if (l > bestLen + 1e-9) { bestLen = l; best = i; }
            }
            var o = pts[best];
            double ang = Math.Atan2(pts[best + 1].Y - o.Y, pts[best + 1].X - o.X);
            double c = Math.Cos(-ang), sn = Math.Sin(-ang);
            foreach (var p in pts)
            {
                double x = p.X - o.X, y = p.Y - o.Y;
                double rx = x * c - y * sn, ry = x * sn + y * c;
                if (Math.Abs(rx) < 1e-9) rx = 0;
                if (Math.Abs(ry) < 1e-9) ry = 0;
                if (mirror) ry = -ry;
                if (reverse) rx = bestLen - rx;   // odcinek od długości do 0 — zagięcie przy punkcie wstawienia
                if (Math.Abs(rx) < 1e-9) rx = 0;
                res.Add(alongY ? (-ry == 0 ? 0 : -ry, rx) : (rx, ry));
            }
            return res;
        }

        /// <summary>
        /// Jak GetLocalPoints, w wybranym wariancie geometrii (Plan W4, 0-POPRAWKI p.2). Legacy → LegacyBarGeometry
        /// (geometria sprzed W4, bit w bit). Wywołania bez opcji = BarGeometryOptions.Current.
        /// </summary>
        public static List<(double X, double Y)> GetLocalPoints(
            string shapeCode, double[] paramValues, double diameter, BarGeometryOptions options)
        {
            if (options.IsLegacy) return LegacyBarGeometry.GetLocalPoints(shapeCode, paramValues, diameter);
            return Build(shapeCode, paramValues, diameter, options, null);
        }

        /// <summary>
        /// Etykiety odcinków osi (BarSegmentLabel, jeden na odcinek i → i+1 z GetLocalPoints w tym samym wariancie);
        /// null dla pierścienia. Plan W4, 0-POPRAWKI p.1 (długość rozkładu Manual wg geometrii sprzed W4).
        /// </summary>
        public static List<BarSegmentLabel> GetSegmentLabels(
            string shapeCode, double[] paramValues, double diameter, BarGeometryOptions options)
        {
            if (options.IsLegacy) return LegacyBarGeometry.SegmentLabels(shapeCode, paramValues, diameter);
            if (BarFrameRules.IsRingShape(shapeCode)) return null;
            var labels = new List<BarSegmentLabel>();
            Build(shapeCode, paramValues, diameter, options, labels);
            return labels;
        }

        /// <summary>Geometria bieżąca (nie-Legacy); labels != null → etykiety odcinków osi.</summary>
        private static List<(double X, double Y)> Build(
            string shapeCode, double[] paramValues, double diameter, BarGeometryOptions options, List<BarSegmentLabel> labels)
        {
            // W4c: 75 = pierścień; W4e (ExternalDims): średnica osi A − d, środek (A/2, A/2) bez zmian
            if (BarFrameRules.IsRingShape(shapeCode)) return CirclePoints(paramValues, options.ExternalDims ? diameter : 0.0);

            // W4e (ExternalDims, poprawka #13): wymiary zewnętrzne → oś (BarDimensionRules); kąt skosu z wymiarów zewnętrznych
            double[] rawValues = paramValues;
            if (options.ExternalDims) paramValues = BarDimensionRules.ToAxis(shapeCode, paramValues, diameter, options.SlopeFromB);

            if (shapeCode == "13")
            {
                var hp = HairpinPoints(paramValues, diameter);
                if (labels != null) HairpinLabels(hp.Count, labels);
                return hp;
            }

            // TableRadius = geometria W4b (Table 2); false = promień sprzed W4b (etap W4a)
            double r = options.TableRadius ? BarShape.MinBendRadius(diameter) : LegacyBarGeometry.MinBendRadius(diameter);
            // W4e: r z Table 2 to promień wewnętrzny — łuk osi r + d/2
            if (options.ExternalDims) r = BarDimensionRules.AxisBendRadius(r, diameter);

            // W4d (Bs2020Sharp): 22 i 33 z półkolami — osobna budowa osi wg rysunków Table 5
            if (options.Bs2020Sharp && (shapeCode == "22" || shapeCode == "33"))
            {
                var pb = new PathBuilder(labels);
                var sh = ShapeCodeLibrary.Get(shapeCode);
                if (shapeCode == "22") Bs22Points(pb, sh, paramValues, r, options.TangentArcs);
                else                   Bs33Points(pb, sh, paramValues);
                return pb.Points;
            }

            // W4d (Bs2020Sharp): 12 — łuk o promieniu R (parametr kształtu); R ≤ 0 → minimalny promień gięcia
            // (W4e: R po ToAxis = promień osi R + d/2)
            if (options.Bs2020Sharp && shapeCode == "12")
            {
                double rr = ShapeCodeLibrary.Get("12")?.Value(paramValues, "R") ?? 0.0;
                if (rr > 0) r = rr;
            }
            var sharp = GetSharpPoints(shapeCode, paramValues, rawValues, r, diameter, options);
            if (sharp.Count < 3)
            {
                if (labels != null) for (int k = 0; k + 1 < sharp.Count; k++) labels.Add(new BarSegmentLabel(BarSegmentLabel.Leg, k, 0));
                return sharp;
            }

            // W4a-2: odcinki zerowej długości (np. 25 bez C) — kolejne powtórzone węzły usuwane przed łukami
            var orig = new List<int>();
            var dedup = RemoveRepeated(sharp, orig);
            if (dedup.Count < 2)
            {
                if (labels != null) for (int k = 0; k + 1 < sharp.Count; k++) labels.Add(new BarSegmentLabel(BarSegmentLabel.Leg, k, 0));
                return sharp;
            }
            if (dedup.Count < 3)
            {
                if (labels != null) labels.Add(new BarSegmentLabel(BarSegmentLabel.Leg, orig[1] - 1, 0));
                return dedup;
            }
            sharp = dedup;

            var result = new List<(double, double)>();
            result.Add(sharp[0]);
            for (int i = 1; i < sharp.Count - 1; i++)
            {
                int before = result.Count;
                foreach (var p in BendArcPoints(sharp[i - 1], sharp[i], sharp[i + 1], r, options.TangentArcs))
                    result.Add(p);
                if (labels != null)
                {
                    labels.Add(new BarSegmentLabel(BarSegmentLabel.Leg, orig[i] - 1, 0));   // prosty odcinek przed łukiem
                    for (int m = 0; m < result.Count - before - 1; m++)
                        labels.Add(new BarSegmentLabel(BarSegmentLabel.Arc, orig[i], m));
                }
            }
            result.Add(sharp[sharp.Count - 1]);
            if (labels != null) labels.Add(new BarSegmentLabel(BarSegmentLabel.Leg, orig[sharp.Count - 1] - 1, 0));
            return result;
        }

        /// <summary>Hairpin 13: pierwszy odcinek = ramię 0, pododcinki półkola = łuk 1, ostatni = ramię 1.</summary>
        internal static void HairpinLabels(int pointCount, List<BarSegmentLabel> labels)
        {
            for (int j = 0; j < pointCount - 1; j++)
                labels.Add(j == 0 ? new BarSegmentLabel(BarSegmentLabel.Leg, 0, 0)
                         : j == pointCount - 2 ? new BarSegmentLabel(BarSegmentLabel.Leg, 1, 0)
                         : new BarSegmentLabel(BarSegmentLabel.Arc, 1, j - 1));
        }

        /// <summary>Kolejne węzły w odległości &lt; 1e-9 → jeden (W4a-2); origIndex = indeks zachowanego węzła w pts.</summary>
        private static List<(double X, double Y)> RemoveRepeated(List<(double X, double Y)> pts, List<int> origIndex)
        {
            var r = new List<(double X, double Y)>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                if (r.Count > 0)
                {
                    var q = r[r.Count - 1];
                    double dx = p.X - q.X, dy = p.Y - q.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) < ZeroLen) continue;
                }
                r.Add(p);
                origIndex.Add(i);
            }
            return r;
        }

        /// <summary>Odcinek krótszy = zerowy (bez łuku).</summary>
        private const double ZeroLen = 1e-9;

        /// <summary>
        /// Generuje (steps+1) punktów na łuku kołowym.
        /// Kąty w stopniach; interpolacja liniowa kąta od startAngleDeg do endAngleDeg.
        /// </summary>
        public static List<(double X, double Y)> ArcPoints(
            (double X, double Y) center, double radius,
            double startAngleDeg, double endAngleDeg, int steps = 6)
        {
            var pts = new List<(double, double)>(steps + 1);
            double startRad = startAngleDeg * Math.PI / 180.0;
            double endRad   = endAngleDeg   * Math.PI / 180.0;
            for (int i = 0; i <= steps; i++)
            {
                double t     = (double)i / steps;
                double angle = startRad + (endRad - startRad) * t;
                pts.Add((center.X + radius * Math.Cos(angle),
                         center.Y + radius * Math.Sin(angle)));
            }
            return pts;
        }

        /// <summary>
        /// 7-punktowa aproksymacja łuku na narożniku prev→curr→next.
        /// Identyczna logika jak wewnętrzna BendArcPoints używana przez GetLocalPoints.
        /// </summary>
        public static IEnumerable<(double X, double Y)> CornerArcPoints(
            (double X, double Y) prev, (double X, double Y) curr, (double X, double Y) next,
            double diameter)
            => BendArcPoints(prev, curr, next, BarShape.MinBendRadius(diameter), true);

        // ── Private helpers ───────────────────────────────────────────────────

        // cos45° = sin45° = √2/2
        private static readonly double Cos45 = Math.Sqrt(2.0) / 2.0;

        /// <summary>
        /// Zwraca ostre węzły kształtu (bez aproksymacji łuków). W4a-3: wymiary PO NAZWIE parametru kształtu
        /// (paramValues[i] = wartość Parameters[i], jak XData i BBS) — np. 25 {A,B,E}: E = paramValues[2], C = 0.
        /// Nieznany kod → po pozycji (A = [0] …). rawValues — wymiary przed przeliczeniem na oś (W4e): kąt skosu
        /// (BarShape.SlopeAngle) liczony z wymiarów podanych przez użytkownika.
        /// </summary>
        private static List<(double X, double Y)> GetSharpPoints(
            string shapeCode, double[] paramValues, double[] rawValues, double r, double diameter, BarGeometryOptions options)
        {
            var shape = ShapeCodeLibrary.Get(shapeCode);
            double V(string name, int positional)
                => shape != null ? shape.Value(paramValues, name) : Param(paramValues, positional);

            double a = V("A", 0);
            double b = V("B", 1);
            double c = V("C", 2);
            double d = V("D", 3);
            double e = V("E", 4);

            // W4d (Bs2020Sharp): ostre punkty wg rysunków BS 8666:2020 Table 5 (umowa osi jak dotąd — wymiary zewnętrzne W4e)
            if (options.Bs2020Sharp && shape != null)
            {
                switch (shapeCode)
                {
                    case "14": // A skośne (od wolnego końca w dół do zagięcia), C poziome WSTECZ pod A; kąt z B i D
                    {
                        double t = shape.SlopeAngle(rawValues, out _, options.SlopeFromB);
                        double ax = a * Math.Cos(t), ay = a * Math.Sin(t);
                        return Pts((0, ay), (ax, 0), (ax - c, 0));
                    }
                    case "15": // A skośne (od wolnego końca w dół do zagięcia), C poziome dalej; kąt z B i D
                    {
                        double t = shape.SlopeAngle(rawValues, out _, options.SlopeFromB);
                        double ax = a * Math.Cos(t), ay = a * Math.Sin(t);
                        return Pts((0, ay), (ax, 0), (ax + c, 0));
                    }
                    case "24": // A poziome, B skos (kąt z D/E), C pionowe (90° do A)
                    {
                        double t = shape.SlopeAngle(rawValues, out _, options.SlopeFromB);
                        double bx = b * Math.Cos(t), by = b * Math.Sin(t);
                        return Pts((0, 0), (a, 0), (a + bx, by), (a + bx, by + c));
                    }
                    case "46": // A poziome, B skos w dół, C poziome, B skos w górę, E poziome; kąt z D (wysokość skosu)
                    {
                        double t = shape.SlopeAngle(rawValues, out _, options.SlopeFromB);
                        double bx = b * Math.Cos(t), by = b * Math.Sin(t);
                        return Pts((0, 0), (a, 0), (a + bx, -by), (a + bx + c, -by), (a + 2 * bx + c, 0), (a + 2 * bx + c + e, 0));
                    }
                    case "51": // P9: A = wysokość, B = szerokość (dawniej odwrotnie); haki jak W4b
                    {
                        double hk = c > 0 ? c : DefaultHook51(diameter, options);
                        return Pts((b, a - hk), (b, a), (0, a), (0, 0), (b, 0), (b, a), (b - hk, a));
                    }
                }
            }

            switch (shapeCode ?? "00")
            {
                // ── Prosta ────────────────────────────────────────────────────
                case "00":
                    return Pts((0, 0), (a, 0));

                // ── Haki jednostronne / obustronne ───────────────────────────
                case "11": // hak 90° na prawym końcu
                    return Pts((0, 0), (a, 0), (a, b));

                case "12": // L-bend z większym promieniem R (R = p[2] = LengthC)
                    return Pts((0, 0), (a, 0), (a, b));

                // ── Haki 45° i 135° ──────────────────────────────────────────
                case "14": // hook 45°
                    return Pts((0, 0), (a, 0), (a + b * Cos45, b * Cos45));

                case "15": // hook 135°
                    return Pts((0, 0), (a, 0), (a - b * Cos45, b * Cos45));

                // ── U-bary ────────────────────────────────────────────────────
                case "21": // U-bar symetryczny/asymetryczny
                {
                    double cv = shape != null ? (shape.Has(paramValues, "C") ? c : b)
                                              : (paramValues != null && paramValues.Length > 2 ? paramValues[2] : b);
                    return Pts((0, 0), (0, -a), (b, -a), (b, cv - a));
                }

                case "22": // U-shape nierówny
                    return Pts((0, a), (0, 0), (b, 0), (b, c));

                // ── Z-bary i cranki ───────────────────────────────────────────
                case "23": // Z-bar (BS 8666) — prostokątne zgięcia 90°
                    return Pts((0, 0), (a, 0), (a, b), (a + c, b));

                case "24": // Crank łagodny (BS 8666) — skośny segment C pod kątem 30° od poziomu
                // TODO: zweryfikować kąt z pełnym tekstem BS 8666:2020 — 30° to prowizorka,
                //       norma może definiować specyficzny slope (np. 1:6 lub 1:12)
                {
                    const double angleRad = Math.PI / 6.0; // 30°
                    double skosDX = c * Math.Cos(angleRad);
                    double skosDY = c * Math.Sin(angleRad);
                    return Pts(
                        (0,              0),
                        (a,              0),
                        (a + skosDX,     skosDY),
                        (a + skosDX + b, skosDY));
                }

                case "25": // Hook + crank — {A,B,E}: ostatnie ramię 45° = E (W4a-3; dawniej wartość z pola C)
                    return Pts((0, 0), (a, 0), (a, b), (a + c, b),
                               (a + c + e * Cos45, b + e * Cos45));

                case "26": // Hook + leg (w dół)
                    return Pts((0, 0), (a, 0), (a, -b), (a + c, -b));

                case "27": // Crank z hakiem górnym (stub = r)
                    return Pts((0, 0), (a, 0), (a + b, b), (a + b + c, b),
                               (a + b + c, b + r));

                case "28": // Crank z hakiem dolnym (stub = r)
                    return Pts((0, 0), (a, 0), (a + b, b), (a + b + c, b),
                               (a + b + c, b - r));

                case "29": // Crank symetryczny (stub startowy = r)
                    return Pts((0, -r), (0, 0), (a, 0), (a + b, b), (a + b + c, b));

                // ── Kształty Z + hak / S ──────────────────────────────────────
                case "31": // Z + hook 45°
                    return Pts((0, 0), (a, 0), (a, b), (a + c, b),
                               (a + c + d * Cos45, b + d * Cos45));

                case "32": // S-shape
                    return Pts((0, 0), (a, 0), (a, b), (a + c, b), (a + c, -d));

                case "33": // S-shape odwrócony
                    return Pts((0, 0), (a, 0), (a, -b), (a + c, -b), (a + c, d));

                // ── Zamknięte prostokąty / kwadraty ──────────────────────────
                case "34": // prostokąt zamknięty A×B
                    return Pts((0, 0), (a, 0), (a, b), (0, b), (0, 0));

                case "35": // kwadrat zamknięty A×A
                    return Pts((0, 0), (a, 0), (a, a), (0, a), (0, 0));

                case "36": // prostokąt nierówny (różne wysokości lewej i prawej)
                    return Pts((0, 0), (a, 0), (a, b), (0, c), (0, 0));

                // ── Inne zamknięte kształty ───────────────────────────────────
                case "41": // wielokąt 4-boczny z ukośnym narożnikiem
                    return Pts((0, 0), (a, 0), (a + b * Cos45, b * Cos45), (a, b), (0, 0));

                case "46": // romb
                    return Pts((a / 2, 0), (a, b / 2), (a / 2, b), (0, b / 2), (a / 2, 0));

                // ── 44 „kapelusz” (W4c, BS 8666:2020; dawniej pierścień — dziś 75) ──
                case "44":
                    return Pts((0, 0), (a, 0), (a, -b), (a + c, -b), (a + c, -b + d), (a + c + e, -b + d));

                case "47": // trójkąt
                    return Pts((0, 0), (a, 0), (a / 2, b), (0, 0));

                // ── Linki zamknięte z hakiem ──────────────────────────────────
                case "51": // BS8666: closed link — jeden pręt, overlap w górnym prawym rogu
                // Górny prawy narożnik odwiedzany DWUKROTNIE (oba haki wychodzą z tego samego rogu).
                // Daje 5 narożników 90° (wszystkie identyczne jak w shape 34).
                {
                    // W4b (0-POPRAWKI p.3): C = 0 → hak C_eff spójny z formułą minimalną BS 8666:2020
                    double hook51 = c > 0 ? c : DefaultHook51(diameter, options);
                    return Pts((a, b - hook51),    // prawy hak (dół)
                               (a, b),             // górny prawy — 1. przejście: UP→LEFT
                               (0, b),             // górny lewy — LEFT→DOWN
                               (0, 0),             // dolny lewy — DOWN→RIGHT
                               (a, 0),             // dolny prawy — RIGHT→UP
                               (a, b),             // górny prawy — 2. przejście (overlap): UP→LEFT
                               (a - hook51, b));   // lewy hak (top)
                }

                case "63": // BS8666: closed link — haki PIONOWO W DÓŁ z obu górnych rogów
                // Jeden ciągły pręt, double-visit na górnych rogach (jak shape 51).
                // 8 węzłów, 6 narożników 90° CW → 1+6×7+1=44 pkt
                // A=wysokość (p[0]), B=szerokość (p[1]), C=hook (p[2])
                {
                    // W4b (0-POPRAWKI p.3): C = 0 → hak C_eff spójny z formułą minimalną BS 8666:2020
                    double hook63 = c > 0 ? c : DefaultHook63(diameter, options);
                    // W4b (0-POPRAWKI p.3): lewy hak ma długość hook63 (dawniej A − hook63 — start w (0, hook63);
                    // tak też etap W4a, TableRadius = false)
                    double start63 = options.TableRadius ? a - hook63 : hook63;
                    return Pts((0,         start63),     // lewy hak koniec — free end
                               (0,         a),           // górny lewy — UP→RIGHT   (CW, 1. wizyta)
                               (b,         a),           // górny prawy — RIGHT→DOWN (CW, 1. wizyta)
                               (b,         0),           // dolny prawy — DOWN→LEFT  (CW)
                               (0,         0),           // dolny lewy — LEFT→UP    (CW)
                               (0,         a),           // górny lewy — UP→RIGHT   (CW, 2. wizyta)
                               (b,         a),           // górny prawy — RIGHT→DOWN (CW, 2. wizyta)
                               (b,         a - hook63)); // prawy hak koniec — free end
                }

                // ── Złożone 5-ramienne ────────────────────────────────────────
                case "56": // complex 5-leg
                case "64": // complex 5-leg variant (ta sama geometria)
                    return Pts((0, 0), (a, 0), (a, b), (a + c, b),
                               (a + c, b - d), (a + c + e, b - d));

                // ── Custom / fallback ─────────────────────────────────────────
                case "98":
                case "99":
                default:
                    return Pts((0, 0), (a, 0));
            }
        }


        /// <summary>
        /// 51, C = 0 — długość haka osi: W4b (TableRadius) C_eff spójne z formułą minimalną 2020, etap W4a max(16d, 160);
        /// W4e (ExternalDims): C_eff to wymiar zewnętrzny haka (jak C) → oś C_eff − d/2.
        /// </summary>
        private static double DefaultHook51(double diameter, BarGeometryOptions options)
        {
            double hk = options.TableRadius ? BarShape.Code51EffectiveHook(diameter)
                      : Math.Max(16.0 * diameter, 160.0);   // etap W4a (sprzed W4b)
            return options.ExternalDims ? Math.Max(0.0, hk - diameter / 2.0) : hk;
        }

        /// <summary>63, C = 0 — jak DefaultHook51: C_eff (W4b) / max(14d, 150) (W4a); W4e: oś C_eff − d/2.</summary>
        private static double DefaultHook63(double diameter, BarGeometryOptions options)
        {
            double hk = options.TableRadius ? BarShape.Code63EffectiveHook(diameter)
                      : Math.Max(14.0 * diameter, 150.0);   // etap W4a (sprzed W4b)
            return options.ExternalDims ? Math.Max(0.0, hk - diameter / 2.0) : hk;
        }

        /// <summary>
        /// Pierścień (W4c: kształt 75; dawniej 44): okrąg aproksymowany 8 punktami co 45°, Closed=true (9 pkt: start=koniec).
        /// Parametr A = średnica; środek w (A/2, A/2). W4e: diameter &gt; 0 → A = średnica ZEWNĘTRZNA, oś o promieniu (A − d)/2
        /// (BarDimensionRules.RingAxisRadius), środek bez zmian; diameter = 0 → promień A/2 (oś, jak przed W4e).
        /// </summary>
        private static List<(double X, double Y)> CirclePoints(double[] paramValues, double diameter)
        {
            double diam = Param(paramValues, 0);
            double cx   = diam / 2.0;
            double cy   = diam / 2.0;
            double rad  = diameter > 0 ? BarDimensionRules.RingAxisRadius(diam, diameter) : diam / 2.0;
            var pts = new List<(double, double)>(9);
            for (int i = 0; i <= 8; i++)
            {
                double angle = i * Math.PI / 4.0;
                pts.Add((cx + rad * Math.Cos(angle), cy + rad * Math.Sin(angle)));
            }
            return pts;
        }

        // BS 8666 shape 13 — hairpin z łukiem 180°.
        // A = długa (dolna) noga, B = wysokość pętli oś-do-osi (= 2·r),
        // C = krótka (górna) noga. Promień łuku = B/2 (parametr usera, NIE MinBendRadius).
        // Długość fizyczna: (A - B/2) + π·B/2 + (C - B/2) = A + C + B·(π/2 - 1).
        // TODO: dla B/2 < MinBendRadius(d) bar naruszałby minimalny promień gięcia normy —
        //       nie blokujemy, plugin rysuje zgodnie z wymiarami podanymi przez usera.
        private static List<(double X, double Y)> HairpinPoints(double[] paramValues, double diameter)
        {
            double a = Param(paramValues, 0);
            double b = Param(paramValues, 1);
            double c = Param(paramValues, 2);
            double r = b / 2.0;

            // Środek łuku półkola: prawy kraniec pętli minus promień, na wysokości B/2
            double cx = a - r;
            double cy = r;

            var pts = new List<(double, double)>();

            // 1. Lewy koniec dolnej nogi
            pts.Add((0.0, 0.0));

            // 2. Punkt styczny dolnej nogi do łuku (= start półkola)
            pts.Add((cx, 0.0));

            // 3. Półkole 180° od dołu (kąt -π/2) do góry (kąt +π/2), 13 punktów pośrednich
            //    (12 kroków po 15°). Pomijamy endpoint startu bo już dodany w pkt. 2.
            const int arcSteps = 12;
            double startAngle = -Math.PI / 2.0;       // punkt (cx, 0) = dół
            double sweep = Math.PI;                    // +180°, obrót CCW
            for (int i = 1; i <= arcSteps; i++)
            {
                double t = (double)i / arcSteps;
                double angle = startAngle + sweep * t;
                pts.Add((cx + r * Math.Cos(angle), cy + r * Math.Sin(angle)));
            }

            // Teraz ostatni dodany punkt to (cx, 2r) = (a - r, b) — koniec półkola, góra

            // 4. Lewy koniec górnej nogi
            pts.Add((a - c, b));

            return pts;
        }

        // ── W4d: kształty z półkolami (22, 33) ───────────────────────────────

        /// <summary>
        /// W4d — składanie osi z punktów i etykiet odcinków: kolejny punkt w odległości &lt; 1e-9 od poprzedniego jest
        /// pomijany razem z etykietą (odcinek zerowy, jak W4a-2).
        /// </summary>
        private sealed class PathBuilder
        {
            public readonly List<(double X, double Y)> Points = new List<(double X, double Y)>();
            private readonly List<BarSegmentLabel> _labels;

            public PathBuilder(List<BarSegmentLabel> labels) { _labels = labels; }

            /// <summary>Punkt osi; label = etykieta odcinka od poprzedniego punktu (dla pierwszego punktu ignorowana).</summary>
            public void Add((double X, double Y) p, BarSegmentLabel label)
            {
                if (Points.Count > 0)
                {
                    var q = Points[Points.Count - 1];
                    double dx = p.X - q.X, dy = p.Y - q.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) < ZeroLen) return;
                    _labels?.Add(label);
                }
                Points.Add(p);
            }
        }

        private static double Val(BarShape shape, double[] pv, string name, int positional)
            => shape != null ? shape.Value(pv, name) : Param(pv, positional);

        /// <summary>Liczba kroków półkola (co 15°).</summary>
        private const int HalfCircleSteps = 12;

        /// <summary>
        /// W4d — 22 wg BS 8666:2020: A pionowe (od wolnego końca w górę do narożnika), B poziome do skrajni półkola, półkole
        /// o średnicy C w dół, D od wolnego końca do skrajni półkola. Start (0, −A), narożnik (0, 0) z łukiem r, półkole:
        /// środek (B − C/2, −C/2), koniec (B − D, −C). Etykiety: ramię 0 (A), łuk 1 (narożnik), ramię 1 (B),
        /// łuk 2 (półkole, 12 pododcinków), ramię 2 (D).
        /// </summary>
        private static void Bs22Points(PathBuilder pb, BarShape shape, double[] pv, double r, bool tangentArcs)
        {
            double a = Val(shape, pv, "A", 0), b = Val(shape, pv, "B", 1);
            double c = Val(shape, pv, "C", 2), d = Val(shape, pv, "D", 3);
            double half = Math.Max(c, 0.0) / 2.0;
            (double X, double Y) p0 = (0.0, -a), corner = (0.0, 0.0), top = (b - half, 0.0);

            pb.Add(p0, default);
            bool first = true;
            int m = 0;
            foreach (var p in BendArcPoints(p0, corner, top, r, tangentArcs))
            {
                pb.Add(p, first ? new BarSegmentLabel(BarSegmentLabel.Leg, 0, 0) : new BarSegmentLabel(BarSegmentLabel.Arc, 1, m++));
                first = false;
            }
            pb.Add(top, new BarSegmentLabel(BarSegmentLabel.Leg, 1, 0));
            for (int i = 1; i <= HalfCircleSteps; i++)
            {
                double ang = Math.PI / 2.0 - Math.PI * i / HalfCircleSteps;   // od góry półkola zgodnie z ruchem wskazówek
                pb.Add((top.X + half * Math.Cos(ang), -half + half * Math.Sin(ang)), new BarSegmentLabel(BarSegmentLabel.Arc, 2, i - 1));
            }
            pb.Add((Math.Min(b - d, top.X), -2.0 * half), new BarSegmentLabel(BarSegmentLabel.Leg, 2, 0));
        }

        /// <summary>
        /// W4d — 33 wg BS 8666:2020 („bieżnia”): A = długość skrajna (z półkolami), półkola o średnicy B, pręt okrąża kształt
        /// 1½ raza (formuła 2A + 1,7B + 2C − 4d), oba końce w odległości C od prawej skrajni (zakład na prawym półkolu).
        /// Oś: góra y = B, dół y = 0. Start (A − C, B) → w prawo → półkole prawe → dół w lewo → półkole lewe → góra w prawo →
        /// półkole prawe (drugi raz) → dół do (A − C, 0). Etykiety: ramiona 0–3, łuki 1–3 (półkola, po 12 pododcinków).
        /// </summary>
        private static void Bs33Points(PathBuilder pb, BarShape shape, double[] pv)
        {
            double a = Val(shape, pv, "A", 0), b = Val(shape, pv, "B", 1), c = Val(shape, pv, "C", 2);
            double rad = Math.Max(b, 0.0) / 2.0, top = 2.0 * rad;
            double xL = rad, xR = Math.Max(a - rad, xL);
            double xs = Math.Min(a - c, xR);

            void Right(int arc)
            {
                for (int i = 1; i <= HalfCircleSteps; i++)
                {
                    double ang = Math.PI / 2.0 - Math.PI * i / HalfCircleSteps;
                    pb.Add((xR + rad * Math.Cos(ang), rad + rad * Math.Sin(ang)), new BarSegmentLabel(BarSegmentLabel.Arc, arc, i - 1));
                }
            }

            pb.Add((xs, top), default);
            pb.Add((xR, top), new BarSegmentLabel(BarSegmentLabel.Leg, 0, 0));
            Right(1);
            pb.Add((xL, 0.0), new BarSegmentLabel(BarSegmentLabel.Leg, 1, 0));
            for (int i = 1; i <= HalfCircleSteps; i++)
            {
                double ang = -Math.PI / 2.0 - Math.PI * i / HalfCircleSteps;   // lewe półkole: od dołu przez lewą skrajnię do góry
                pb.Add((xL + rad * Math.Cos(ang), rad + rad * Math.Sin(ang)), new BarSegmentLabel(BarSegmentLabel.Arc, 2, i - 1));
            }
            pb.Add((xR, top), new BarSegmentLabel(BarSegmentLabel.Leg, 2, 0));
            Right(3);
            pb.Add((xs, 0.0), new BarSegmentLabel(BarSegmentLabel.Leg, 3, 0));
        }

        // ── Micro-helpers ─────────────────────────────────────────────────────

        private static double Param(double[] pv, int i) =>
            pv != null && pv.Length > i ? pv[i] : 0.0;

        private static List<(double X, double Y)> Pts(params (double X, double Y)[] points) =>
            new List<(double, double)>(points);

        /// <summary>Odchylenie kierunku powyżej tego kąta (zawrócenie) → ostry narożnik bez łuku + wpis w logu.</summary>
        private const double MaxArcDeflection = 179.0 * Math.PI / 180.0;

        /// <summary>
        /// Zastępuje ostry naróż w <paramref name="curr"/> serią 7 punktów łuku
        /// (od punktu stycznego tp1 do tp2).
        /// W4a-1 (tangentArcs): punkt styczności w odległości t = r·tan(φ/2) od narożnika (φ = kąt odchylenia
        /// kierunku) — łuk styczny do obu ramion także dla zagięć ≠ 90°; |φ − 90°| &lt; 1e-12 → t = r DOKŁADNIE
        /// (bit w bit jak dawniej). tangentArcs = false → zawsze t = r (dawny łuk).
        /// W4a-2: odcinek zerowy (&lt; 1e-9) albo φ &lt; 1e-9 → sam narożnik; φ &gt; 179° → narożnik + log.
        /// </summary>
        private static IEnumerable<(double X, double Y)> BendArcPoints(
            (double X, double Y) prev,
            (double X, double Y) curr,
            (double X, double Y) next,
            double r, bool tangentArcs, int steps = 6)
        {
            // Wektory jednostkowe kierunków
            double d1x = curr.X - prev.X, d1y = curr.Y - prev.Y;
            double len1 = Math.Sqrt(d1x * d1x + d1y * d1y);
            double d2x = next.X - curr.X, d2y = next.Y - curr.Y;
            double len2 = Math.Sqrt(d2x * d2x + d2y * d2y);
            if (len1 < ZeroLen || len2 < ZeroLen)
            {
                yield return curr;
                yield break;
            }
            d1x /= len1; d1y /= len1;
            d2x /= len2; d2y /= len2;

            // Iloczyn wektorowy: > 0 → skręt CCW (w lewo)
            double cross = d1x * d2y - d1y * d2x;
            bool ccw = cross > 0;

            // Kąt odchylenia kierunku φ ∈ [0, π]
            double phi = Math.Atan2(Math.Abs(cross), d1x * d2x + d1y * d2y);
            if (phi < 1e-9)
            {
                yield return curr;
                yield break;
            }
            if (phi > MaxArcDeflection)
            {
                Log.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "BarGeometryBuilder.BendArcPoints: zagięcie {0:F2}° > 179° w ({1:F1}; {2:F1}) — narożnik bez łuku.",
                    phi * 180.0 / Math.PI, curr.X, curr.Y));
                yield return curr;
                yield break;
            }

            // Punkty styczne (t od narożnika, na odpowiednich odcinkach)
            double tl = !tangentArcs || Math.Abs(phi - Math.PI / 2.0) < 1e-12 ? r : r * Math.Tan(phi / 2.0);
            double tp1x = curr.X - d1x * tl,  tp1y = curr.Y - d1y * tl;
            double tp2x = curr.X + d2x * tl,  tp2y = curr.Y + d2y * tl;

            // Normalna wewnętrzna (ku środkowi łuku) w tp1
            double nx = ccw ? -d1y :  d1y;
            double ny = ccw ?  d1x : -d1x;

            double cx = tp1x + nx * r;
            double cy = tp1y + ny * r;

            double startAngle = Math.Atan2(tp1y - cy, tp1x - cx);
            double endAngle   = Math.Atan2(tp2y - cy, tp2x - cx);

            // Wymuś właściwy kierunek obrotu
            double sweep = endAngle - startAngle;
            if ( ccw && sweep < 0) sweep += 2 * Math.PI;
            if (!ccw && sweep > 0) sweep -= 2 * Math.PI;

            for (int i = 0; i <= steps; i++)
            {
                double t     = (double)i / steps;
                double angle = startAngle + sweep * t;
                yield return (cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
            }
        }
    }
}
