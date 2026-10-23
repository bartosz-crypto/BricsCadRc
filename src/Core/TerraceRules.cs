#nullable disable   // kompilowany też w tests/ (Nullable enable) — bez ostrzeżeń CS86xx
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Szereg płyt połączonych belkami gruntowymi GB poza płytą (decyzja Bartka 2026-10-23, etap 1 — wzorzec PLOT 304–307, test11).
    /// GA: jeden obrys zewnętrzny SD-PILED-RAFT obejmuje kilka płyt; w nim zamknięte linie uskoku płyt (każda z opisem „PLOT …”),
    /// prostokątne otwory (pustki między płytami) i otwarte linie stref narożnych (735 × 600, opis „525THK”); pas betonu między
    /// płytami = belka „GB-600x450mm deep / TOC=23.350”.
    /// Model RC („3 płyty + 4 belki GB”):
    ///   • płyta = linia uskoku odsunięta na zewnątrz o szerokość nibu (do najbliższej równoległej krawędzi obrysu albo otworu,
    ///     <see cref="MinNib"/>–<see cref="MaxNib"/>; brak — krawędź bez nibu),
    ///   • pas = beton obrysu poza płytami i otworami (prostokąt 200–1500 szerokości), belka tylko z opisem GB w pasie,
    ///   • strefa belki = pas przedłużony wzdłuż osi o strefy narożne w płytach (z linii strefy GA; bez niej — opis „…THK” przy końcu:
    ///     nib + <see cref="CornerInSlab"/>) — jedna belka od narożnika do narożnika,
    ///   • H = głębokość GB + obniżenie (SSL − TOC); obniżenie bez TOC = THK narożnika − głębokość.
    /// Czysta logika (bez Teigha) — testy `tests/TerraceRulesTests.cs`.
    /// </summary>
    public static class TerraceRules
    {
        public const double MinNib = 50.0, MaxNib = 255.0;
        public const double SnapTol = 5.0;
        /// <summary>Krawędź osiowa: odchyłka końców ≤ 4 mm (jak BeamGeometry.AxisTol).</summary>
        public const double AxisTol = 4.0;
        public const double MinBandWidth = 200.0, MaxBandWidth = 1500.0;
        /// <summary>Strefa narożna bez linii strefy w GA: nib + 600 w głąb płyty (jak strefy K8).</summary>
        public const double CornerInSlab = 600.0;
        /// <summary>Tolerancja dopasowania linii strefy narożnej do pasa (GA: 27857 / 27858).</summary>
        public const double MatchTol = 10.0;

        public sealed class Box
        {
            public double X0, Y0, X1, Y1;
            public Box() { }
            public Box(double x0, double y0, double x1, double y1)
            { X0 = Math.Min(x0, x1); Y0 = Math.Min(y0, y1); X1 = Math.Max(x0, x1); Y1 = Math.Max(y0, y1); }
        }

        public sealed class Text
        {
            public string Value;
            public double X, Y;
        }

        public sealed class Band
        {
            public bool AlongX;
            /// <summary>Pas betonu między płytami: wzdłuż osi Lo..Hi, w poprzek CLo..CHi.</summary>
            public double Lo, Hi, CLo, CHi;
            /// <summary>Strefa belki wzdłuż osi (z narożnymi strefami w płytach).</summary>
            public double ZoneLo, ZoneHi;
            /// <summary>Indeksy płyt (linii uskoku) przy końcach pasa; −1 — brak.</summary>
            public int SlabLo = -1, SlabHi = -1;
            /// <summary>Opis GB w pasie (null — pas bez belki); szerokość / głębokość z opisu, TOC (null — brak).</summary>
            public string Label;
            public int GbWidth, GbDepth;
            public double? Toc;
            /// <summary>„…THK” w strefach narożnych (null — brak).</summary>
            public int? ThkLo, ThkHi;
            /// <summary>Obniżenie góry belki względem SSL i wysokość H = głębokość + obniżenie.</summary>
            public int TopDrop, Height;
            public double Width => CHi - CLo;

            public List<(double X, double Y)> ZonePolygon()
                => AlongX
                    ? new List<(double, double)> { (ZoneLo, CLo), (ZoneHi, CLo), (ZoneHi, CHi), (ZoneLo, CHi) }
                    : new List<(double, double)> { (CLo, ZoneLo), (CHi, ZoneLo), (CHi, ZoneHi), (CLo, ZoneHi) };

            /// <summary>Długie lica pasa (krawędź betonu poza płytami) — do narysowania w miejscu obrysu GA.</summary>
            public List<((double X, double Y) A, (double X, double Y) B)> Faces()
                => AlongX
                    ? new List<((double, double), (double, double))> { ((Lo, CLo), (Hi, CLo)), ((Lo, CHi), (Hi, CHi)) }
                    : new List<((double, double), (double, double))> { ((CLo, Lo), (CLo, Hi)), ((CHi, Lo), (CHi, Hi)) };
        }

        public sealed class Result
        {
            /// <summary>Obrysy płyt (kolejność jak linie uskoku).</summary>
            public List<List<(double X, double Y)>> Slabs = new List<List<(double X, double Y)>>();
            public List<Band> Bands = new List<Band>();
            /// <summary>Indeksy linii stref narożnych użytych w strefach belek (do usunięcia z rysunku).</summary>
            public HashSet<int> UsedBoxes = new HashSet<int>();
            /// <summary>Indeksy opisów użytych przez belki (GB, „…THK” w strefach) — nie dla K8 / wsporników.</summary>
            public HashSet<int> UsedTexts = new HashSet<int>();
            public List<string> Messages = new List<string>();
        }

        private static readonly Regex GbRx = new Regex(@"\bGB\s*[-–]?\s*(\d{3,4})\s*[xX×]\s*(\d{3,4})", RegexOptions.IgnoreCase);
        private static readonly Regex TocRx = new Regex(@"\bTOC\s*=?\s*(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
        private static readonly Regex ThkRx = new Regex(@"\b(\d{3,4})\s*THK\b", RegexOptions.IgnoreCase);

        /// <summary>„GB-600x450mm deep | TOC=23.350” → szerokość 600, głębokość 450, TOC 23.350 (null gdy brak).</summary>
        public static bool TryParseGb(string text, out int width, out int depth, out double? toc)
        {
            width = depth = 0; toc = null;
            var m = GbRx.Match(text ?? "");
            if (!m.Success) return false;
            width = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            depth = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var t = TocRx.Match(text);
            if (t.Success && double.TryParse(t.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) toc = v;
            return true;
        }

        public static int? ParseThk(string text)
        {
            var m = ThkRx.Match(text ?? "");
            return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : (int?)null;
        }

        /// <summary>Obniżenie góry GB [mm] = (SSL − TOC) · 1000, zaokrąglone.</summary>
        public static int TopDropFrom(double ssl, double toc) => (int)Math.Round((ssl - toc) * 1000.0);

        /// <summary>
        /// Obrys płyty z linii uskoku (prostokątnej / prostokątnej wielokątnej): każda krawędź odsunięta na zewnątrz do najbliższej równoległej
        /// krawędzi obrysu zewnętrznego albo otworu (MinNib–MaxNib, zachodzenie &gt; 1 mm); brak — krawędź bez odsunięcia.
        /// Linia uskoku ze skosem — bez zmian.
        /// </summary>
        public static List<(double X, double Y)> SlabOutline(IList<(double X, double Y)> step, IList<(double X, double Y)> outer,
                                                             IList<IList<(double X, double Y)>> voids)
        {
            var p = Clean(step);
            if (p.Count < 4) return p;
            if (SignedArea(p) < 0) p.Reverse();
            var bounds = new List<List<(double X, double Y)>> { Clean(outer) };
            if (voids != null) bounds.AddRange(voids.Select(Clean));
            int n = p.Count;
            var off = new double[n];
            var horiz = new bool[n];
            var coord = new double[n];   // współrzędna krawędzi (średnia końców — GA: krawędź prawie osiowa, np. x 33885,71 → 33885,76)
            for (int i = 0; i < n; i++)
            {
                var a = p[i]; var b = p[(i + 1) % n];
                // test12 (2026-10-23): krawędź prawie osiowa (odchyłka 0,045 mm) — tolerancja AxisTol, nie 1e-6
                bool h = Math.Abs(a.Y - b.Y) <= AxisTol && Math.Abs(a.X - b.X) > AxisTol;
                bool v = Math.Abs(a.X - b.X) <= AxisTol && Math.Abs(a.Y - b.Y) > AxisTol;
                if (!h && !v) return p;   // skos — bez zmian
                horiz[i] = h;
                coord[i] = h ? (a.Y + b.Y) / 2 : (a.X + b.X) / 2;
                // CCW: na zewnątrz po prawej stronie kierunku krawędzi
                double nx = h ? 0 : (b.Y > a.Y ? 1 : -1), ny = h ? (b.X > a.X ? -1 : 1) : 0;
                double c = coord[i], lo = h ? Math.Min(a.X, b.X) : Math.Min(a.Y, b.Y), hi = h ? Math.Max(a.X, b.X) : Math.Max(a.Y, b.Y);
                int sg = h ? (int)ny : (int)nx;
                double best = 0;
                foreach (var bp in bounds)
                    for (int k = 0; k < bp.Count; k++)
                    {
                        var e0 = bp[k]; var e1 = bp[(k + 1) % bp.Count];
                        bool eh = Math.Abs(e0.Y - e1.Y) <= AxisTol, ev = Math.Abs(e0.X - e1.X) <= AxisTol;   // GA: krawędź 27857 / 27858
                        if (h ? !eh : !ev) continue;
                        double ec = h ? (e0.Y + e1.Y) / 2 : (e0.X + e1.X) / 2;
                        double d = (ec - c) * sg;
                        if (d < MinNib - 1e-6 || d > MaxNib + 1.0) continue;
                        double elo = h ? Math.Min(e0.X, e1.X) : Math.Min(e0.Y, e1.Y), ehi = h ? Math.Max(e0.X, e1.X) : Math.Max(e0.Y, e1.Y);
                        if (Math.Min(hi, ehi) - Math.Max(lo, elo) <= 1.0) continue;
                        if (best == 0 || d < best) best = d;
                    }
                off[i] = best * sg;
            }
            var res = new List<(double X, double Y)>();
            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;
                if (horiz[prev] == horiz[i]) { res.Add(p[i]); continue; }
                int iv = horiz[i] ? prev : i, ih = horiz[i] ? i : prev;
                double x = coord[iv] + off[iv], y = coord[ih] + off[ih];
                res.Add((x, y));
            }
            return res;
        }

        /// <summary>
        /// Analiza szeregu płyt: obrysy płyt, pasy betonu między nimi, strefy belek GB (z liniami stref narożnych / opisami).
        /// steps — linie uskoku płyt, voids — otwory, corners — obwiednie otwartych linii stref, texts — opisy (GB, „…THK”), ssl — SSL płyt.
        /// </summary>
        public static Result Analyze(IList<(double X, double Y)> outer, IList<IList<(double X, double Y)>> steps,
                                     IList<IList<(double X, double Y)>> voids, IList<Box> corners, IList<Text> texts, double? ssl)
        {
            var res = new Result();
            var outerC = Clean(outer);
            var voidsC = (voids ?? new List<IList<(double X, double Y)>>()).Select(Clean).ToList();
            foreach (var s in steps) res.Slabs.Add(SlabOutline(s, outerC, voidsC.Cast<IList<(double X, double Y)>>().ToList()));
            corners = corners ?? new List<Box>();
            texts = texts ?? new List<Text>();

            // siatka z wszystkich współrzędnych (sklejone ≤ SnapTol — GA: 27857 / 27858)
            var xs = Snap(outerC.Select(q => q.X).Concat(res.Slabs.SelectMany(s => s.Select(q => q.X))).Concat(voidsC.SelectMany(v => v.Select(q => q.X))));
            var ys = Snap(outerC.Select(q => q.Y).Concat(res.Slabs.SelectMany(s => s.Select(q => q.Y))).Concat(voidsC.SelectMany(v => v.Select(q => q.Y))));
            int nx = xs.Count - 1, ny = ys.Count - 1;
            if (nx < 1 || ny < 1) return res;
            var conc = new bool[nx, ny];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    var c = ((xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2);
                    conc[i, j] = Inside(outerC, c) && !res.Slabs.Any(s => Inside(s, c)) && !voidsC.Any(v => Inside(v, c));
                }

            var seen = new bool[nx, ny];
            for (int i0 = 0; i0 < nx; i0++)
                for (int j0 = 0; j0 < ny; j0++)
                {
                    if (!conc[i0, j0] || seen[i0, j0]) continue;
                    var comp = new List<(int i, int j)>();
                    var st = new Stack<(int, int)>();
                    st.Push((i0, j0)); seen[i0, j0] = true;
                    while (st.Count > 0)
                    {
                        var (i, j) = st.Pop();
                        comp.Add((i, j));
                        foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int a = i + di, b = j + dj;
                            if (a < 0 || b < 0 || a >= nx || b >= ny || seen[a, b] || !conc[a, b]) continue;
                            seen[a, b] = true; st.Push((a, b));
                        }
                    }
                    int ia = comp.Min(c => c.i), ib = comp.Max(c => c.i), ja = comp.Min(c => c.j), jb = comp.Max(c => c.j);
                    double x0 = xs[ia], x1 = xs[ib + 1], y0 = ys[ja], y1 = ys[jb + 1];
                    string where = string.Format(CultureInfo.InvariantCulture, "pas {0:0}..{1:0} × {2:0}..{3:0}", x0, x1, y0, y1);
                    if (comp.Count != (ib - ia + 1) * (jb - ja + 1))
                    {
                        res.Messages.Add($"{where}: beton poza płytami nie jest prostokątem — belka nie utworzona (RC_BEAM ręcznie).");
                        continue;
                    }
                    bool alongX = x1 - x0 >= y1 - y0;
                    var band = alongX ? new Band { AlongX = true, Lo = x0, Hi = x1, CLo = y0, CHi = y1 }
                                      : new Band { AlongX = false, Lo = y0, Hi = y1, CLo = x0, CHi = x1 };
                    if (band.Width < MinBandWidth - 1e-6 || band.Width > MaxBandWidth + 1e-6)
                    {
                        res.Messages.Add($"{where}: szerokość {band.Width:F0} poza zakresem {MinBandWidth:F0}–{MaxBandWidth:F0} — pominięto.");
                        continue;
                    }
                    double mid = (band.CLo + band.CHi) / 2;
                    band.SlabLo = SlabAt(res.Slabs, band.AlongX ? (band.Lo - SnapTol, mid) : (mid, band.Lo - SnapTol));
                    band.SlabHi = SlabAt(res.Slabs, band.AlongX ? (band.Hi + SnapTol, mid) : (mid, band.Hi + SnapTol));

                    // opis GB w pasie
                    for (int t = 0; t < texts.Count; t++)
                    {
                        if (!InBand(band, band.Lo, band.Hi, texts[t].X, texts[t].Y)) continue;
                        if (!TryParseGb(texts[t].Value, out int gw, out int gd, out double? toc)) continue;
                        band.Label = texts[t].Value; band.GbWidth = gw; band.GbDepth = gd; band.Toc = toc;
                        res.UsedTexts.Add(t);
                        break;
                    }
                    if (band.Label == null)
                    {
                        res.Messages.Add($"{where}: brak opisu „GB-…” w pasie — belka nie utworzona (beton poza płytą bez zbrojenia — sprawdź).");
                        res.Bands.Add(band);
                        continue;
                    }

                    band.ZoneLo = CornerEnd(band, true, steps, corners, texts, res);
                    band.ZoneHi = CornerEnd(band, false, steps, corners, texts, res);

                    int thk = band.ThkLo ?? band.ThkHi ?? 0;
                    if (band.ThkLo.HasValue && band.ThkHi.HasValue && band.ThkLo != band.ThkHi)
                        res.Messages.Add($"{where}: różne „…THK” w narożnikach ({band.ThkLo}/{band.ThkHi}) — przyjęto {thk}.");
                    if (band.Toc.HasValue && ssl.HasValue) band.TopDrop = Math.Max(0, TopDropFrom(ssl.Value, band.Toc.Value));
                    else if (thk > band.GbDepth) band.TopDrop = thk - band.GbDepth;
                    band.Height = band.GbDepth + band.TopDrop;
                    if (thk > 0 && thk != band.Height)
                        res.Messages.Add($"{where}: H = {band.GbDepth} + {band.TopDrop} (SSL − TOC) = {band.Height} ≠ {thk}THK narożnika — sprawdź.");
                    if (Math.Abs(band.GbWidth - band.Width) > 1.0)
                        res.Messages.Add($"{where}: szerokość pasa {band.Width:F0} ≠ {band.GbWidth} z opisu GB — przyjęto szerokość pasa.");
                    res.Bands.Add(band);
                }
            return res;
        }

        /// <summary>
        /// Podział zakresu [lo, hi] wzdłuż osi belki na części w płycie (inside — odcinki osi w obrysach płyt) i poza płytą (GB).
        /// Części krótsze niż minPart dołączone do sąsiedniej.
        /// </summary>
        public static List<(double Lo, double Hi, bool InSlab)> SplitParts(double lo, double hi, IList<(double Lo, double Hi)> inside, double minPart = 50.0)
        {
            var cuts = new List<double> { lo, hi };
            foreach (var (a, b) in inside ?? new List<(double, double)>())
            {
                if (a > lo && a < hi) cuts.Add(a);
                if (b > lo && b < hi) cuts.Add(b);
            }
            cuts = cuts.Distinct().OrderBy(c => c).ToList();
            var parts = new List<(double Lo, double Hi, bool InSlab)>();
            for (int k = 0; k + 1 < cuts.Count; k++)
            {
                double m = (cuts[k] + cuts[k + 1]) / 2;
                bool ins = (inside ?? new List<(double, double)>()).Any(iv => m >= iv.Lo && m <= iv.Hi);
                parts.Add((cuts[k], cuts[k + 1], ins));
            }
            // krótkie części — do sąsiedniej (dłuższej); sąsiednie tego samego rodzaju — razem
            bool changed = true;
            while (changed && parts.Count > 1)
            {
                changed = false;
                for (int k = 0; k < parts.Count; k++)
                {
                    if (parts[k].Hi - parts[k].Lo >= minPart) continue;
                    int nb = k == 0 ? 1 : k == parts.Count - 1 ? k - 1
                           : (parts[k - 1].Hi - parts[k - 1].Lo >= parts[k + 1].Hi - parts[k + 1].Lo ? k - 1 : k + 1);
                    var q = parts[nb];
                    parts[nb] = (Math.Min(q.Lo, parts[k].Lo), Math.Max(q.Hi, parts[k].Hi), q.InSlab);
                    parts.RemoveAt(k);
                    changed = true;
                    break;
                }
            }
            for (int k = parts.Count - 1; k > 0; k--)
                if (parts[k].InSlab == parts[k - 1].InSlab)
                {
                    parts[k - 1] = (parts[k - 1].Lo, parts[k].Hi, parts[k].InSlab);
                    parts.RemoveAt(k);
                }
            return parts;
        }

        // ── wewnętrzne ──────────────────────────────────────────────────

        /// <summary>Koniec strefy belki przy końcu pasa: linia strefy narożnej z GA, opis „…THK” (nib + 600) albo koniec pasa.</summary>
        private static double CornerEnd(Band band, bool atLo, IList<IList<(double X, double Y)>> steps, IList<Box> corners, IList<Text> texts, Result res)
        {
            double end = atLo ? band.Lo : band.Hi;
            int slab = atLo ? band.SlabLo : band.SlabHi;
            string where = string.Format(CultureInfo.InvariantCulture, "belka GB {0:0}..{1:0} ({2})", band.Lo, band.Hi, atLo ? "początek" : "koniec");
            if (slab < 0)
            {
                res.Messages.Add($"{where}: koniec pasa nie przylega do płyty — strefa belki do końca pasa.");
                return end;
            }
            for (int k = 0; k < corners.Count; k++)
            {
                var b = corners[k];
                double cLo = band.AlongX ? b.Y0 : b.X0, cHi = band.AlongX ? b.Y1 : b.X1;
                double aLo = band.AlongX ? b.X0 : b.Y0, aHi = band.AlongX ? b.X1 : b.Y1;
                if (Math.Abs(cLo - band.CLo) > MatchTol || Math.Abs(cHi - band.CHi) > MatchTol) continue;
                double far = atLo ? aLo : aHi, near = atLo ? aHi : aLo;
                if (Math.Abs(near - end) > MatchTol || (atLo ? far >= end - MinNib : far <= end + MinNib)) continue;
                res.UsedBoxes.Add(k);
                Thk(band, atLo, atLo ? far : end, atLo ? end : far, texts, res);
                return far;
            }
            // bez linii strefy: opis „…THK” w pasie nib + 600 za końcem pasa
            double nib = NibAt(steps[slab], band, end, atLo);
            double ext = nib + CornerInSlab;
            double z0 = atLo ? end - ext : end, z1 = atLo ? end : end + ext;
            if (Thk(band, atLo, z0, z1, texts, res))
            {
                res.Messages.Add($"{where}: strefa narożna bez linii w GA — z opisu „…THK”: {ext:F0} w głąb płyty (nib {nib:F0} + {CornerInSlab:F0}).");
                return atLo ? z0 : z1;
            }
            res.Messages.Add($"{where}: brak strefy narożnej (linia / opis „…THK”) — strefa belki do krawędzi płyty, U-bary przy krawędzi — sprawdź.");
            return end;
        }

        private static bool Thk(Band band, bool atLo, double a0, double a1, IList<Text> texts, Result res)
        {
            for (int t = 0; t < texts.Count; t++)
            {
                if (!InBand(band, a0, a1, texts[t].X, texts[t].Y)) continue;
                if (!(ParseThk(texts[t].Value) is int h)) continue;
                if (atLo) band.ThkLo = h; else band.ThkHi = h;
                res.UsedTexts.Add(t);
                return true;
            }
            return false;
        }

        private static bool InBand(Band band, double a0, double a1, double x, double y)
        {
            double along = band.AlongX ? x : y, across = band.AlongX ? y : x;
            return along >= a0 - SnapTol && along <= a1 + SnapTol && across >= band.CLo - SnapTol && across <= band.CHi + SnapTol;
        }

        /// <summary>Szerokość nibu przy końcu pasa: odległość krawędzi płyty od równoległej krawędzi linii uskoku (0 — brak).</summary>
        private static double NibAt(IList<(double X, double Y)> step, Band band, double end, bool atLo)
        {
            var p = Clean(step);
            double best = 0;
            for (int i = 0; i < p.Count; i++)
            {
                var a = p[i]; var b = p[(i + 1) % p.Count];
                // krawędź prostopadła do osi pasa
                bool perp = band.AlongX ? Math.Abs(a.X - b.X) <= AxisTol : Math.Abs(a.Y - b.Y) <= AxisTol;
                if (!perp) continue;
                double c = band.AlongX ? (a.X + b.X) / 2 : (a.Y + b.Y) / 2;
                double d = (c - end) * (atLo ? -1 : 1);
                if (d < MinNib - 1e-6 || d > MaxNib + 1.0) continue;
                if (best == 0 || d < best) best = d;
            }
            return best;
        }

        private static int SlabAt(List<List<(double X, double Y)>> slabs, (double X, double Y) p)
        {
            for (int i = 0; i < slabs.Count; i++) if (Inside(slabs[i], p)) return i;
            return -1;
        }

        private static List<double> Snap(IEnumerable<double> v)
        {
            var s = v.OrderBy(x => x).ToList();
            var res = new List<double>();
            foreach (var x in s)
                if (res.Count == 0 || x - res[res.Count - 1] > SnapTol) res.Add(x);
            return res;
        }

        internal static List<(double X, double Y)> Clean(IList<(double X, double Y)> pts)
        {
            var r = new List<(double X, double Y)>();
            foreach (var p in pts ?? new List<(double X, double Y)>())
                if (r.Count == 0 || Math.Abs(r[r.Count - 1].X - p.X) + Math.Abs(r[r.Count - 1].Y - p.Y) > 1e-3) r.Add(p);
            if (r.Count > 2 && Math.Abs(r[0].X - r[r.Count - 1].X) + Math.Abs(r[0].Y - r[r.Count - 1].Y) < 1e-3) r.RemoveAt(r.Count - 1);
            bool changed = true;
            while (changed && r.Count > 3)
            {
                changed = false;
                for (int i = 0; i < r.Count; i++)
                {
                    var a = r[(i - 1 + r.Count) % r.Count]; var b = r[i]; var c = r[(i + 1) % r.Count];
                    double cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                    double len = Math.Abs(c.X - a.X) + Math.Abs(c.Y - a.Y);
                    if (Math.Abs(cross) <= 1e-6 * Math.Max(1, len * len) || (Math.Abs(a.Y - b.Y) < 1.5 && Math.Abs(b.Y - c.Y) < 1.5)
                        || (Math.Abs(a.X - b.X) < 1.5 && Math.Abs(b.X - c.X) < 1.5))
                    { r.RemoveAt(i); changed = true; break; }
                }
            }
            return r;
        }

        private static double SignedArea(IList<(double X, double Y)> v)
        {
            double a = 0;
            for (int i = 0; i < v.Count; i++) { var p = v[i]; var q = v[(i + 1) % v.Count]; a += p.X * q.Y - q.X * p.Y; }
            return a / 2;
        }

        internal static bool Inside(IList<(double X, double Y)> poly, (double X, double Y) p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }
    }
}
