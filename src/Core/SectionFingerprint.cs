#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Odcisk przekroju RC_SECTION (bezpiecznik aktualności przekrojów, docs/PLAN_BEZPIECZNIK_PRZEKROJOW_2026-10-10.md) —
    /// czysta logika (bez Teigha), testy tests/SectionFingerprintTests.cs.
    ///
    /// Składniki „istotnego wejścia” wybrane przez planer (rozkłady z ≥ 1 kropką / prętem w widoku, pale narysowane, średnice
    /// poziomów, odcinki betonu) + dane spoza planera (rzuty, cięcie względem obrysu, litera, parametry okna, opis płyty).
    /// Serializacja: linie „klucz=wartość” posortowane ordynalnie, liczby Math.Round(x, 1) „0.0” (Invariant, −0 → 0),
    /// współrzędne względem P1 — punktu cięcia na rzucie wskazanym (MOVE rzutu ze znacznikami i STRETCH krawędzi daleko od cięcia bez alarmu), SHA-256 UTF-8.
    /// Skrót wyniku (ResultDigest): kanoniczna serializacja Result z U − UMin — rozjemca dla zmian bez wpływu na rysunek.
    /// Werdykt: <see cref="Compare"/>; różnice rzutów / cięcia / litery ZAWSZE = NIEAKTUALNY (digest ich nie obejmuje).
    /// </summary>
    public static class SectionFingerprint
    {
        public const int FormatVersion = 2;   // 2: punkt odniesienia = P1 (punkt cięcia), CUT bez położenia
        /// <summary>Tolerancja porównania składników liczbowych [mm].</summary>
        public const double Tolerance = 0.15;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Dane przekroju spoza planera (z rysunku i z okna).</summary>
        public sealed class Source
        {
            /// <summary>
            /// Punkt odniesienia współrzędnych względnych = P1 (punkt cięcia na rzucie wskazanym: wierzchołek [2] znacznika V0.P1,
            /// bez znacznika — P1 z rekordu). Nie MinPoint obrysu: STRETCH krawędzi daleko od cięcia nie może przesuwać odniesienia.
            /// </summary>
            public double RefX, RefY;
            /// <summary>Przesunięcia rzutów płyty względem wskazanego (z (0, 0)).</summary>
            public List<(double X, double Y)> Views = new List<(double X, double Y)>();
            public string Letter = "";
            /// <summary>Grubość / SSL z opisu płyty (null = brak opisu) i czy w oknie nadpisano je ręcznie (P1).</summary>
            public double? DescThickness;
            public bool ThkOverridden;
            public string DescSsl;
            public bool SslOverridden;
        }

        /// <summary>Stan do porównania: składniki, skrót składników, skrót wyniku, wersja planera.</summary>
        public sealed class State
        {
            public SortedDictionary<string, string> Components = new SortedDictionary<string, string>(StringComparer.Ordinal);
            public string Hash = "";
            public string Digest = "";
            public int Version;
        }

        public sealed class Verdict
        {
            /// <summary>true = NIEAKTUALNY.</summary>
            public bool Stale;
            /// <summary>Przyczyny NIEAKTUALNY (po polsku).</summary>
            public List<string> Reasons = new List<string>();
            /// <summary>Informacje (zmiany bez wpływu na rysunek, opis płyty przy nadpisaniu, nowa wersja programu).</summary>
            public List<string> Info = new List<string>();
            /// <summary>P2: ten sam wkład, inna wersja planera — nowa wersja rysowałaby inaczej.</summary>
            public bool NewVersionDiffers;
        }

        // ── liczby i tekst ──────────────────────────────────────────────

        /// <summary>Liczba do odcisku: Math.Round(x, 1) „0.0”, −0 → 0.0, NaN / ∞ → „nan”.</summary>
        public static string Num(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return "nan";
            double r = Math.Round(x, 1);
            if (r == 0) r = 0;   // −0.0 → 0.0
            return r.ToString("0.0", Inv);
        }

        /// <summary>Liczba do komunikatu: bez zbędnych zer („200”, „21.9”).</summary>
        public static string Disp(double x) => Math.Round(x, 1) == 0 ? "0" : Math.Round(x, 1).ToString("0.#", Inv);

        private static string Disp(string fieldValue)
            => double.TryParse(fieldValue, NumberStyles.Float, Inv, out double v) ? Disp(v) : (fieldValue ?? "");

        /// <summary>Wartość tekstowa pola: \ → \\, ; → \s, | → \p, nowa linia → \n.</summary>
        public static string Esc(string s)
            => (s ?? "").Replace("\\", "\\\\").Replace(";", "\\s").Replace("|", "\\p").Replace("\r", "").Replace("\n", "\\n");

        public static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s ?? "";
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char c = s[++i];
                sb.Append(c == 's' ? ';' : c == 'n' ? '\n' : c == 'p' ? '|' : c);
            }
            return sb.ToString();
        }

        /// <summary>Pola wartości składnika („nazwa:wartość;…”) → słownik (kolejność bez znaczenia).</summary>
        public static Dictionary<string, string> Fields(string value)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(value)) return d;
            foreach (var part in value.Split(';'))
            {
                int k = part.IndexOf(':');
                if (k <= 0) continue;
                d[part.Substring(0, k)] = Unesc(part.Substring(k + 1));
            }
            return d;
        }

        private sealed class FieldList
        {
            private readonly List<string> _f = new List<string>();
            public FieldList N(string name, double v) { _f.Add(name + ":" + Num(v)); return this; }
            public FieldList S(string name, string v) { _f.Add(name + ":" + Esc(v)); return this; }
            public FieldList B(string name, bool v) { _f.Add(name + ":" + (v ? "1" : "0")); return this; }
            public override string ToString() => string.Join(";", _f);
        }

        // ── składniki ───────────────────────────────────────────────────

        /// <summary>Składniki istotnego wejścia przekroju (wejście planera po oknie + wynik planera + dane spoza planera).</summary>
        public static SortedDictionary<string, string> Components(SectionPlanner.Input inp, SectionPlanner.Result res, Source src)
        {
            src = src ?? new Source();
            var c = new SortedDictionary<string, string>(StringComparer.Ordinal);
            double mx = src.RefX, my = src.RefY;

            c["META"] = new FieldList().S("Ver", SectionPlanner.Version.ToString(Inv)).S("Fmt", FormatVersion.ToString(Inv)).ToString();

            var views = (src.Views ?? new List<(double X, double Y)>()).Select(v => (X: Num(v.X), Y: Num(v.Y)))
                        .OrderBy(v => v.X, StringComparer.Ordinal).ThenBy(v => v.Y, StringComparer.Ordinal).ToList();
            var vf = new FieldList().S("N", views.Count.ToString(Inv));
            for (int i = 0; i < views.Count; i++) vf.S("V" + i.ToString(Inv), views[i].X + "," + views[i].Y);
            c["VIEWS"] = vf.ToString();

            // położenie cięcia względem płyty wyłapią DIST / SEG i skrót wyniku (odniesienie = P1); tu tylko kierunek i długość
            c["CUT"] = new FieldList().B("X", inp.CutAlongX).N("Len", Math.Abs(inp.S1 - inp.S0))
                .S("L", (inp.LookSign >= 0 ? 1 : -1).ToString(Inv)).ToString();
            c["LETTER"] = new FieldList().S("L", src.Letter ?? "").ToString();

            c["PARAM"] = new FieldList().N("Thk", inp.Thickness).N("NibH", inp.NibHeight).N("Cb", inp.CoverBottom)
                .N("Ct", inp.CoverTop).N("Cn", inp.CoverNib).N("Tol", inp.UbTolerance).S("Ssl", inp.Ssl ?? "")
                .B("Piles", inp.DrawPiles).ToString();
            c["SRC"] = new FieldList().S("Thk", src.DescThickness.HasValue ? Num(src.DescThickness.Value) : "-")
                .B("ThkOvr", src.ThkOverridden).S("Ssl", src.DescSsl ?? "-").B("SslOvr", src.SslOverridden).ToString();

            if (res != null)
            {
                double u0 = res.UMin;
                for (int i = 0; i < res.Segments.Count; i++)
                {
                    var s = res.Segments[i];
                    c["SEG#" + i.ToString("D2", Inv)] = new FieldList().N("U0", s.U0 - u0).N("U1", s.U1 - u0)
                        .S("E0", s.End0.ToString()).S("E1", s.End1.ToString()).N("N0", s.Nib0).N("N1", s.Nib1).ToString();
                }
                c["LVL"] = new FieldList().N("B1", res.LevelDiameters.B1).N("T1", res.LevelDiameters.T1).ToString();

                int anon = 0;
                foreach (var d in res.UsedDists)
                {
                    string key = "DIST:" + (string.IsNullOrEmpty(d.SourceHandle) ? "#" + (anon++).ToString("D3", Inv) : d.SourceHandle);
                    if (c.ContainsKey(key)) key += "#" + (anon++).ToString("D3", Inv);
                    c[key] = DistValue(d, mx, my);
                }
                foreach (var (px, py, r) in res.UsedPiles)
                {
                    string key = "PILE:" + Num(px - mx) + "," + Num(py - my);
                    if (c.ContainsKey(key)) key += "#" + c.Count.ToString(Inv);
                    c[key] = new FieldList().N("R", r).ToString();
                }

                // Belki (plan belek E5): składnik BEAM:<handle> tylko dla belki przeciętej linią cięcia (brak belek — ten sam
                // odcisk co przed E5); pas względem P1 (współrzędna wzdłuż cięcia), H, nib, parametry rekordu
                double ma = inp.CutAlongX ? mx : my;
                int anonB = 0;
                foreach (var b in res.UsedBeams)
                {
                    string key = "BEAM:" + (string.IsNullOrEmpty(b.Handle) ? "#" + (anonB++).ToString("D3", Inv) : b.Handle);
                    while (c.ContainsKey(key)) key += "#" + (anonB++).ToString("D3", Inv);
                    c[key] = new FieldList().N("S0", Math.Min(b.S0, b.S1) - ma).N("S1", Math.Max(b.S0, b.S1) - ma).N("H", b.Height)
                        .N("NW", b.NibWidth).N("NH", b.NibHeight).N("Cv", b.Cover).S("LD", b.StirrupDia.ToString(Inv)).N("LA", b.StirrupA)
                        .S("P", b.Params).N("CvB", b.CoverBottom).ToString();   // CvB — otulina dolna (2026-10-10)
                }
            }
            return c;
        }

        private static string DistValue(SectionPlanner.Dist d, double mx, double my)
        {
            var f = new FieldList().S("Mark", d.Mark).S("Pos", d.PosNr).S("Lay", d.LayerCode).S("Kind", d.Kind.ToString())
                .B("AX", d.AlongX).N("X0", d.X0 - mx).N("Y0", d.Y0 - my).N("LenA", d.LengthA).N("Span", d.BarsSpan)
                .S("Cnt", d.Count.ToString(Inv)).N("Sp", d.Spacing).N("Nom", SectionPlanner.NominalSpacing(d))
                .S("Dia", d.Diameter.ToString(Inv)).S("Shape", d.ShapeCode).S("Side", d.SymbolSide);
            string[] names = { "A", "B", "C", "D", "E" };
            for (int i = 0; i < 5; i++)
                f.S(names[i], d.SourceDims != null && i < d.SourceDims.Length ? Num(d.SourceDims[i]) : "-");
            f.S("SL", d.ShapeLocal != null ? ShortHash(string.Join(";", d.ShapeLocal.Select(p => Num(p.X) + "," + Num(p.Y)))) : "-");
            // belki (E5 / E4b; tylko pręty belki — odcisk innych rozkładów bez zmian): skos, drugie ramię U-bara, poziomy prętów bocznych
            if (d.Kind == SectionPlanner.DistKind.Beam)
                f.N("SkS", d.SkewStart).N("SkE", d.SkewEnd).N("Pair", d.PairOffset).S("Lv", d.Layers.ToString(Inv));
            return f.ToString();
        }

        // ── skróty ──────────────────────────────────────────────────────

        public static string Serialize(SortedDictionary<string, string> comps)
            => string.Join("\n", (comps ?? new SortedDictionary<string, string>(StringComparer.Ordinal))
                                 .OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));

        public static string Hash(SortedDictionary<string, string> comps) => Sha(Serialize(comps));

        public static string Sha(string s)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? "")).Select(b => b.ToString("x2", Inv)));
        }

        private static string ShortHash(string s) => Sha(s).Substring(0, 12);

        /// <summary>Skrót wyniku planera: kanoniczna serializacja (U − UMin, Num, linie posortowane), SHA-256.</summary>
        public static string ResultDigest(SectionPlanner.Result r)
        {
            if (r == null) return "";
            double u0 = r.UMin;
            string U(double u) => Num(u - u0);
            string Pts(IEnumerable<(double U, double V)> pts)
                => pts == null ? "-" : string.Join(";", pts.Select(p => U(p.U) + "," + Num(p.V)));
            var lines = new List<string>
            {
                "META|" + Num(r.UMax - r.UMin) + "|" + Num(r.NibTop),
                "SSL|" + (r.SslMark.HasValue ? U(r.SslMark.Value.U) + "," + Num(r.SslMark.Value.V) : "-") + "|" + (r.SslText ?? "-"),
            };
            foreach (var s in r.Segments)
                lines.Add("SEG|" + U(s.U0) + "|" + U(s.U1) + "|" + s.End0 + "|" + s.End1 + "|" + Num(s.Nib0) + "|" + Num(s.Nib1) + "|" + Pts(s.Polygon));
            foreach (var d in r.Dots)
                lines.Add("DOT|" + U(d.U) + "|" + Num(d.V) + "|" + d.Diameter.ToString(Inv) + "|" + d.PosNr + "|" + (d.Top ? 1 : 0)
                          + "|" + (d.Mesh ? 1 : 0) + "|" + (d.TinNib ? 1 : 0));
            foreach (var b in r.Bars)
                lines.Add("BAR|" + b.Diameter.ToString(Inv) + "|" + b.PosNr + "|" + (b.Top ? 1 : 0) + "|" + b.Kind + "|" + Pts(b.Path));
            foreach (var p in r.Piles)
                lines.Add("PILE|" + U(p.U) + "|" + Num(p.Width) + "|" + Num(p.Top) + "|" + Num(p.Bottom));
            foreach (var m in r.Dims)
                lines.Add("DIM|" + U(m.U1) + "|" + Num(m.V1) + "|" + U(m.U2) + "|" + Num(m.V2) + "|" + U(m.LineU) + "|" + Num(m.LineV) + "|" + (m.Vertical ? 1 : 0));
            foreach (var l in r.Labels)
                lines.Add("LBL|" + l.Text + "|" + U(l.U) + "|" + Num(l.V) + "|" + Num(l.Height) + "|" + (l.HasLeader ? U(l.LeaderU) + "," + Num(l.LeaderV) : "-"));
            lines.Sort(StringComparer.Ordinal);
            return Sha(string.Join("\n", lines));
        }

        /// <summary>Stan bieżący (składniki + skróty) z wejścia i wyniku planera.</summary>
        public static State Build(SectionPlanner.Input inp, SectionPlanner.Result res, Source src)
        {
            var comps = Components(inp, res, src);
            return new State { Components = comps, Hash = Hash(comps), Digest = ResultDigest(res), Version = SectionPlanner.Version };
        }

        // ── porównanie ──────────────────────────────────────────────────

        private static readonly string[] AlwaysStale = { "VIEWS", "CUT", "LETTER" };

        /// <summary>
        /// Werdykt: hash równy → aktualny; różny → porównanie składników z tolerancją <see cref="Tolerance"/>: rzuty / cięcie /
        /// litera różne → NIEAKTUALNY zawsze; inne różnice przy równym skrócie wyniku → aktualny („zmiany bez wpływu na rysunek”),
        /// przy różnym → NIEAKTUALNY z przyczynami; składniki równe, wersja planera różna i skrót wyniku różny → aktualny (P2).
        /// </summary>
        public static Verdict Compare(State stored, State current)
        {
            var v = new Verdict();
            stored = stored ?? new State();
            current = current ?? new State();
            bool digestSame = stored.Digest == current.Digest;
            bool versionSame = stored.Version == current.Version;
            if (stored.Hash.Length > 0 && stored.Hash == current.Hash)
            {
                if (!digestSame && !versionSame) MarkNewVersion(v);
                return v;
            }

            var hard = new List<string>();
            var soft = new List<string>();
            var s = stored.Components; var c = current.Components;
            string Get(SortedDictionary<string, string> d, string k) => d != null && d.TryGetValue(k, out var x) ? x : null;

            // zawsze NIEAKTUALNY
            CompareViews(Get(s, "VIEWS"), Get(c, "VIEWS"), hard);
            CompareCut(Get(s, "CUT"), Get(c, "CUT"), hard);
            var ls = Fields(Get(s, "LETTER")); var lc = Fields(Get(c, "LETTER"));
            string l1 = ls.TryGetValue("L", out var a1) ? a1 : "", l2 = lc.TryGetValue("L", out var a2) ? a2 : "";
            if (l1 != l2) hard.Add($"litera przekroju {l1} → {l2}");

            // rozstrzyga skrót wyniku
            CompareParams(Get(s, "PARAM"), Get(c, "PARAM"), Get(s, "SRC"), Get(c, "SRC"), soft, v.Info);
            CompareSegments(s, c, soft);
            CompareLevels(Get(s, "LVL"), Get(c, "LVL"), soft);
            CompareDists(s, c, soft);
            ComparePiles(s, c, soft);
            CompareBeams(s, c, hard, soft, stored.Version < 2 && current.Version >= 2);

            if (hard.Count > 0)
            {
                v.Stale = true;
                v.Reasons.AddRange(hard);
                v.Reasons.AddRange(soft);
                return v;
            }
            if (soft.Count == 0)
            {
                // składniki równe w tolerancji (np. granica zaokrąglenia) — aktualny; inna wersja planera i inny wynik → P2
                if (!digestSame && !versionSame) MarkNewVersion(v);
                else if (!digestSame) v.Info.Add("wynik różni się w granicach zaokrągleń");
                return v;
            }
            if (digestSame)
            {
                v.Info.Add("zmiany bez wpływu na rysunek: " + string.Join("; ", soft));
                return v;
            }
            v.Stale = true;
            v.Reasons.AddRange(soft);
            return v;
        }

        private static void MarkNewVersion(Verdict v)
        {
            v.NewVersionDiffers = true;
            v.Info.Add("nowa wersja programu rysowałaby ten przekrój inaczej (RC_SECTION_UPDATE)");
        }

        private static bool NumEq(string a, string b)
        {
            if (a == b) return true;
            if (double.TryParse(a, NumberStyles.Float, Inv, out double x) && double.TryParse(b, NumberStyles.Float, Inv, out double y))
                return Math.Abs(x - y) <= Tolerance + 1e-9;
            return false;
        }

        private static bool PairEq(string a, string b)
        {
            if (a == b) return true;
            var pa = (a ?? "").Split(','); var pb = (b ?? "").Split(',');
            return pa.Length == pb.Length && pa.Zip(pb, NumEq).All(x => x);
        }

        private static string F(Dictionary<string, string> d, string k) => d.TryGetValue(k, out var x) ? x : "";

        private static void CompareViews(string s, string c, List<string> reasons)
        {
            if (s == c) return;
            // rzuty jako zbiory przesunięć (tolerancja) — nowe i usunięte z przesunięciem
            var vs = Fields(s).Where(kv => kv.Key != "N").Select(kv => kv.Value).ToList();
            var vc = Fields(c).Where(kv => kv.Key != "N").Select(kv => kv.Value).ToList();
            var removed = vs.Where(a => !vc.Any(b => PairEq(a, b))).ToList();
            var added = vc.Where(b => !vs.Any(a => PairEq(a, b))).ToList();
            string Shift(string xy) { var p = xy.Split(','); return p.Length == 2 ? $"{Disp(p[0])}, {Disp(p[1])}" : xy; }
            foreach (var b in added)
                reasons.Add($"nowy rzut płyty (przesunięcie {Shift(b)}) — jego rozkłady wchodzą do przekroju, brak znaczników; " +
                            "jeśli to inna płyta, przesuń/zmień obrys");
            foreach (var a in removed)
                reasons.Add($"usunięty rzut płyty (przesunięcie {Shift(a)}) — jego rozkłady nie wchodzą już do przekroju");
        }

        private static void CompareCut(string s, string c, List<string> reasons)
        {
            if (s == c) return;
            var fs = Fields(s); var fc = Fields(c);
            if (F(fs, "X") != F(fc, "X") || F(fs, "L") != F(fc, "L")) { reasons.Add("kierunek cięcia / strona patrzenia"); return; }
            if (!NumEq(F(fs, "Len"), F(fc, "Len")))
                reasons.Add($"długość linii cięcia {Disp(F(fs, "Len"))} → {Disp(F(fc, "Len"))}");
        }

        private static void CompareParams(string s, string c, string srcS, string srcC, List<string> reasons, List<string> info)
        {
            var fs = Fields(s); var fc = Fields(c);
            if (!NumEq(F(fs, "Thk"), F(fc, "Thk"))) reasons.Add($"zmieniona grubość {Disp(F(fs, "Thk"))} → {Disp(F(fc, "Thk"))}");
            if (F(fs, "Ssl") != F(fc, "Ssl"))
                reasons.Add($"zmieniony SSL {(F(fs, "Ssl").Length > 0 ? F(fs, "Ssl") : "brak")} → {(F(fc, "Ssl").Length > 0 ? F(fc, "Ssl") : "brak")}");
            if (!NumEq(F(fs, "NibH"), F(fc, "NibH"))) reasons.Add($"zmieniona wysokość nibu {Disp(F(fs, "NibH"))} → {Disp(F(fc, "NibH"))}");
            var other = new[] { ("Cb", "otulina dołem"), ("Ct", "otulina górą"), ("Cn", "otulina w nibie"), ("Tol", "zapas") }
                .Where(p => !NumEq(F(fs, p.Item1), F(fc, p.Item1)))
                .Select(p => $"{p.Item2} {Disp(F(fs, p.Item1))} → {Disp(F(fc, p.Item1))}").ToList();
            if (F(fs, "Piles") != F(fc, "Piles")) other.Add("rysowanie pali");
            if (other.Count > 0) reasons.Add("zmienione parametry przekroju (" + string.Join(", ", other) + ")");

            // opis płyty przy wartości nadpisanej ręcznie — tylko informacja (P1)
            var ss = Fields(srcS); var sc = Fields(srcC);
            if (F(ss, "ThkOvr") == "1" && !NumEq(F(ss, "Thk"), F(sc, "Thk")))
                info.Add($"opis płyty: grubość {Disp(F(ss, "Thk"))} → {Disp(F(sc, "Thk"))} (w przekroju wartość wpisana ręcznie — bez zmian)");
            if (F(ss, "SslOvr") == "1" && F(ss, "Ssl") != F(sc, "Ssl"))
                info.Add($"opis płyty: SSL {F(ss, "Ssl")} → {F(sc, "Ssl")} (w przekroju wartość wpisana ręcznie — bez zmian)");
        }

        private static void CompareSegments(SortedDictionary<string, string> s, SortedDictionary<string, string> c, List<string> reasons)
        {
            var ks = s.Keys.Where(k => k.StartsWith("SEG#", StringComparison.Ordinal)).ToList();
            var kc = c.Keys.Where(k => k.StartsWith("SEG#", StringComparison.Ordinal)).ToList();
            if (ks.Count != kc.Count) { reasons.Add($"obrys/nib/otwór na linii cięcia (odcinków betonu {ks.Count} → {kc.Count})"); return; }
            foreach (var k in ks)
            {
                var fs = Fields(s[k]); var fc = Fields(c.TryGetValue(k, out var x) ? x : "");
                bool same = F(fs, "E0") == F(fc, "E0") && F(fs, "E1") == F(fc, "E1")
                            && new[] { "U0", "U1", "N0", "N1" }.All(n => NumEq(F(fs, n), F(fc, n)));
                if (same) continue;
                reasons.Add($"obrys/nib/otwór na linii cięcia (odcinek {Disp(F(fs, "U0"))}–{Disp(F(fs, "U1"))} → {Disp(F(fc, "U0"))}–{Disp(F(fc, "U1"))}" +
                            (NumEq(F(fs, "N0"), F(fc, "N0")) && NumEq(F(fs, "N1"), F(fc, "N1")) ? ")" : ", nib)"));
                return;
            }
        }

        private static void CompareLevels(string s, string c, List<string> reasons)
        {
            var fs = Fields(s); var fc = Fields(c);
            var diffs = new[] { "B1", "T1" }.Where(n => !NumEq(F(fs, n), F(fc, n)))
                                            .Select(n => $"d{n} {Disp(F(fs, n))} → {Disp(F(fc, n))}").ToList();
            if (diffs.Count > 0) reasons.Add("średnice prętów głównych wyznaczające poziomy (" + string.Join(", ", diffs) + ")");
        }

        // ── rozkłady ────────────────────────────────────────────────────

        private static readonly (string Field, string Name)[] DistNames =
        {
            ("Mark", "opis"), ("Lay", "warstwa"), ("Kind", "rodzaj"), ("AX", "kierunek"), ("LenA", "długość"), ("Span", "zakres"),
            ("Cnt", "liczba prętów"), ("Sp", "rozstaw"), ("Nom", "rozstaw nominalny"), ("Dia", "średnica"), ("Shape", "kształt"),
            ("Side", "strona symbolu"), ("SL", "kształt pręta wzorcowego"),
            ("SkS", "skos (początek)"), ("SkE", "skos (koniec)"), ("Pair", "drugie ramię U-bara"), ("Lv", "liczba prętów bocznych na bok"),
        };

        /// <summary>
        /// Dopasowanie rozkładów: najpierw ten sam klucz (handle), potem zapasowo — ta sama pozycja, warstwa, rodzaj i kierunek,
        /// najbliższe położenie (X0, Y0). Wynik: pary (klucz zapisany, klucz bieżący), usunięte, nowe.
        /// </summary>
        public static (List<(string Stored, string Current)> Pairs, List<string> Removed, List<string> Added) MatchDists(
            IDictionary<string, string> stored, IDictionary<string, string> current)
        {
            var pairs = new List<(string, string)>();
            var sk = stored.Keys.Where(k => k.StartsWith("DIST:", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var ck = current.Keys.Where(k => k.StartsWith("DIST:", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            foreach (var k in sk.ToList())
            {
                if (k.StartsWith("DIST:#", StringComparison.Ordinal) || !ck.Contains(k)) continue;
                pairs.Add((k, k)); sk.Remove(k); ck.Remove(k);
            }
            var cand = new List<(string s, string c, double dist)>();
            foreach (var a in sk)
            {
                var fa = Fields(stored[a]);
                foreach (var b in ck)
                {
                    var fb = Fields(current[b]);
                    if (F(fa, "Pos") != F(fb, "Pos") || F(fa, "Lay") != F(fb, "Lay") || F(fa, "Kind") != F(fb, "Kind") || F(fa, "AX") != F(fb, "AX")) continue;
                    double dx = Val(F(fa, "X0")) - Val(F(fb, "X0")), dy = Val(F(fa, "Y0")) - Val(F(fb, "Y0"));
                    cand.Add((a, b, Math.Sqrt(dx * dx + dy * dy)));
                }
            }
            foreach (var x in cand.OrderBy(x => x.dist).ThenBy(x => x.s, StringComparer.Ordinal).ThenBy(x => x.c, StringComparer.Ordinal))
            {
                if (!sk.Contains(x.s) || !ck.Contains(x.c)) continue;
                pairs.Add((x.s, x.c)); sk.Remove(x.s); ck.Remove(x.c);
            }
            return (pairs, sk, ck);
        }

        private static double Val(string s) => double.TryParse(s, NumberStyles.Float, Inv, out double v) ? v : 0;

        private static string DistLabel(Dictionary<string, string> f) => $"poz. {F(f, "Pos")} {F(f, "Lay")}".TrimEnd();

        private static void CompareDists(SortedDictionary<string, string> s, SortedDictionary<string, string> c, List<string> reasons)
        {
            var (pairs, removed, added) = MatchDists(s, c);
            foreach (var (a, b) in pairs)
            {
                var fa = Fields(s[a]); var fb = Fields(c[b]);
                var diffs = new List<string>();
                foreach (var (field, name) in DistNames)
                {
                    string x = F(fa, field), y = F(fb, field);
                    if (x == y || NumEq(x, y)) continue;
                    if (field == "AX") { diffs.Add(name); continue; }
                    if (field == "SL") { diffs.Add(name); continue; }
                    diffs.Add($"{name} {Disp(x)} → {Disp(y)}");
                }
                if (!NumEq(F(fa, "X0"), F(fb, "X0")) || !NumEq(F(fa, "Y0"), F(fb, "Y0")))
                    diffs.Add($"położenie ({Disp(F(fa, "X0"))}, {Disp(F(fa, "Y0"))}) → ({Disp(F(fb, "X0"))}, {Disp(F(fb, "Y0"))})");
                if (diffs.Count > 0) reasons.Add($"zmieniony rozkład {DistLabel(fb)} ({string.Join(", ", diffs)})");
                var dims = new[] { "A", "B", "C", "D", "E" }.Where(n => !NumEq(F(fa, n), F(fb, n)))
                    .Select(n => $"{n} {Disp(F(fa, n))} → {Disp(F(fb, n))}").ToList();
                if (dims.Count > 0) reasons.Add($"wymiary pręta poz. {F(fb, "Pos")} ({string.Join(", ", dims)})");
            }
            foreach (var a in removed) reasons.Add($"usunięty rozkład {DistLabel(Fields(s[a]))} (albo już nie przecina linii cięcia)");
            foreach (var b in added) reasons.Add($"nowy rozkład {DistLabel(Fields(c[b]))} na linii cięcia");
        }

        // ── belki (plan belek E5) ───────────────────────────────────────

        private static readonly (string Field, string Name)[] BeamNames =
        {
            ("S0", "początek pasa"), ("S1", "koniec pasa"), ("H", "H"), ("NW", "szer. nibu"), ("NH", "wys. nibu"), ("Cv", "otulina boczna"), ("CvB", "otulina dolna"),
            ("LD", "Ø strzemion"), ("LA", "wysokość strzemienia"), ("P", "parametry"),
        };

        /// <summary>
        /// Belki: zapisany odcisk bez żadnego składnika BEAM, a bieżący z belką na linii cięcia → NIEAKTUALNY zawsze („belka
        /// nieuwzględniona” — przekrój narysowany przed E5 albo belka dodana później); inne różnice (zmieniona / usunięta / nowa belka)
        /// rozstrzyga skrót wyniku. Dopasowanie po handle strefy, zapasowo — najbliższy pas (S0).
        /// <paramref name="fromV1"/> (przekrój narysowany planerem wersji 1, bieżący ≥ 2 — K6): pola zmienione wyłącznie przez wersję
        /// programu nie są przyczyną — „LA” (A strzemienia nowym wzorem: H − otulina górna [24] − dolna) i nowe pola „P” (rekord v2:
        /// CT, SM, …, NT); porównywane tylko pola parametrów wersji 1 (<see cref="BeamParamsV1"/>). Gdy dane wejściowe belki (H, nib,
        /// otuliny, Ø strzemion, parametry v1, pas) są bez zmian — brak przyczyn, werdykt P2 („nowa wersja rysowałaby inaczej”).
        /// </summary>
        private static void CompareBeams(SortedDictionary<string, string> s, SortedDictionary<string, string> c, List<string> hard, List<string> soft,
                                         bool fromV1 = false)
        {
            var sk = s.Keys.Where(k => k.StartsWith("BEAM:", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var ck = c.Keys.Where(k => k.StartsWith("BEAM:", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (sk.Count == 0 && ck.Count == 0) return;
            string Name(string k) => "belka " + k.Substring("BEAM:".Length);
            if (sk.Count == 0)
            {
                foreach (var k in ck)
                {
                    var f = Fields(c[k]);
                    hard.Add($"{Name(k)} (H {Disp(F(f, "H"))}) nieuwzględniona w przekroju (przekrój sprzed obsługi belek albo belka dodana później) — RC_SECTION_UPDATE");
                }
                return;
            }
            var pairs = new List<(string, string)>();
            foreach (var k in sk.ToList())
                if (ck.Contains(k) && !k.StartsWith("BEAM:#", StringComparison.Ordinal)) { pairs.Add((k, k)); sk.Remove(k); ck.Remove(k); }
            foreach (var x in sk.SelectMany(a => ck.Select(b => (a, b, d: Math.Abs(Val(F(Fields(s[a]), "S0")) - Val(F(Fields(c[b]), "S0"))))))
                                .OrderBy(x => x.d).ThenBy(x => x.a, StringComparer.Ordinal).ToList())
            {
                if (!sk.Contains(x.a) || !ck.Contains(x.b)) continue;
                pairs.Add((x.a, x.b)); sk.Remove(x.a); ck.Remove(x.b);
            }
            foreach (var (a, b) in pairs)
            {
                var fa = Fields(s[a]); var fb = Fields(c[b]);
                var diffs = new List<string>();
                foreach (var (field, name) in BeamNames)
                {
                    string x = F(fa, field), y = F(fb, field);
                    if (fromV1 && field == "LA") continue;                                   // K6: nowy wzór A strzemienia
                    if (fromV1 && field == "P") { x = BeamParamsV1(x); y = BeamParamsV1(y); }   // K6: nowe pola rekordu v2
                    if (x == y || NumEq(x, y)) continue;
                    diffs.Add(field == "P" ? name : $"{name} {Disp(x)} → {Disp(y)}");
                }
                if (diffs.Count > 0) soft.Add($"zmieniona {Name(b)} ({string.Join(", ", diffs)})");
            }
            foreach (var a in sk) soft.Add($"usunięta {Name(a)} (albo już nie przecina linii cięcia)");
            foreach (var b in ck) soft.Add($"nowa {Name(b)} na linii cięcia");
        }

        /// <summary>Liczba pól „P” zapisywanych przez planer wersji 1 (W, L@, B, T, U, S, LAP, MAX, TL — AutoRebarEngine.BeamParamsText).</summary>
        private const int BeamParamsV1Fields = 9;

        /// <summary>Pola parametrów belki wersji 1 (pierwsze <see cref="BeamParamsV1Fields"/> pól „|”) — bez pól dopisanych w K6.</summary>
        public static string BeamParamsV1(string p)
            => string.Join("|", (p ?? "").Split('|').Take(BeamParamsV1Fields));

        // ── pale ────────────────────────────────────────────────────────

        private static (double X, double Y) PileXY(string key)
        {
            string t = key.Substring("PILE:".Length);
            int h = t.IndexOf('#');
            if (h >= 0) t = t.Substring(0, h);
            var p = t.Split(',');
            return p.Length == 2 ? (Val(p[0]), Val(p[1])) : (0, 0);
        }

        /// <summary>Dopasowanie pali po położeniu (≤ tolerancja), najbliższe najpierw. Wynik: pary, usunięte / przesunięte, nowe.</summary>
        public static (List<(string Stored, string Current)> Pairs, List<string> Removed, List<string> Added) MatchPiles(
            IDictionary<string, string> stored, IDictionary<string, string> current)
        {
            var sk = stored.Keys.Where(k => k.StartsWith("PILE:", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var ck = current.Keys.Where(k => k.StartsWith("PILE:", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var cand = new List<(string s, string c, double d)>();
            foreach (var a in sk)
                foreach (var b in ck)
                {
                    var pa = PileXY(a); var pb = PileXY(b);
                    double d = Math.Sqrt((pa.X - pb.X) * (pa.X - pb.X) + (pa.Y - pb.Y) * (pa.Y - pb.Y));
                    if (d <= Tolerance * Math.Sqrt(2) + 0.1 + 1e-9) cand.Add((a, b, d));
                }
            var pairs = new List<(string, string)>();
            foreach (var x in cand.OrderBy(x => x.d).ThenBy(x => x.s, StringComparer.Ordinal))
            {
                if (!sk.Contains(x.s) || !ck.Contains(x.c)) continue;
                pairs.Add((x.s, x.c)); sk.Remove(x.s); ck.Remove(x.c);
            }
            return (pairs, sk, ck);
        }

        private static void ComparePiles(SortedDictionary<string, string> s, SortedDictionary<string, string> c, List<string> reasons)
        {
            var (pairs, removed, added) = MatchPiles(s, c);
            string Where(string k) { var p = PileXY(k); return $"({Disp(p.X)}, {Disp(p.Y)} od punktu P1 cięcia)"; }
            foreach (var (a, b) in pairs)
            {
                string ra = F(Fields(s[a]), "R"), rb = F(Fields(c[b]), "R");
                if (!NumEq(ra, rb)) reasons.Add($"zmieniona średnica pala {Where(b)}: {Disp(2 * Val(ra))} → {Disp(2 * Val(rb))}");
            }
            foreach (var a in removed) reasons.Add($"usunięty/przesunięty pal {Where(a)}");
            foreach (var b in added) reasons.Add($"nowy pal na linii cięcia {Where(b)}");
        }
    }
}
