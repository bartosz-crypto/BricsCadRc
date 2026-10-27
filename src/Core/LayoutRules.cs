using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Czyste reguły modułu gotowych arkuszy RC_LAYOUT (bez API BricsCAD — testowalne; decyzje użytkownika 2026-10-19):
    ///   • rozmieszczenie rzutni na arkuszu A1 (pole rysunku bez kolumny notatek / tabelki): rzut dolny lewy górny róg, górny prawy
    ///     górny róg; przekroje i detale pod rzutami (od góry, od lewej), detale PH w dolnym rzędzie (od dołu, od lewej),
    ///   • podział na arkusze, gdy wszystko nie mieści się na jednym: 1) rzut dolny + przekroje (najpierw z nibem) + detale, które
    ///     się zmieszczą; 2) reszta przekrojów i detali (kolejne arkusze w razie potrzeby); 3) rzut górny + detale PH;
    ///     4) nadmiar detali PH na osobnych arkuszach,
    ///   • numery rysunków kolejno (…-RC0090 → …-RC0091), TITLE_3 / SCALE wg zawartości arkusza, odsyłacze „… SEE DRG. …”,
    ///   • notatka pod przekrojem z nibem (kierunek UB 03: 1 → prętem podłużnym od dołu i na górze, 2 → od góry i od spodu).
    /// </summary>
    public static class LayoutRules
    {
        // ── arkusz A1 (mm papieru) ───────────────────────────────────────
        /// <summary>Pole rysunku arkusza A1-BL: na lewo od kolumny notatek i tabelki (x ≈ 673), w ramce arkusza.</summary>
        public const double AreaX0 = 11, AreaY0 = 11, AreaX1 = 672, AreaY1 = 582;
        /// <summary>Odstęp między rzutniami (mm papieru).</summary>
        public const double Gap = 6;
        /// <summary>Margines rzutni wokół zawartości (mm papieru).</summary>
        public const double Pad = 2;
        /// <summary>Notatka pod przekrojem z nibem: szerokość MText, wysokość tekstu, miejsce pod rzutnią (mm papieru).</summary>
        public const double NibNoteWidth = 115.678, NibNoteHeight = 4.0, NibNoteGap = 0.0, NibNoteBox = 38.0, NibNoteMinWidth = 80.0;

        public enum Kind { PlanBottom, PlanTop, Section, Detail, Ph }

        public struct Rect
        {
            public double X0, Y0, X1, Y1;
            public Rect(double x0, double y0, double x1, double y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
            public double W => X1 - X0;
            public double H => Y1 - Y0;
            public bool Contains(Rect r) => r.X0 >= X0 - 1e-6 && r.Y0 >= Y0 - 1e-6 && r.X1 <= X1 + 1e-6 && r.Y1 <= Y1 + 1e-6;
            public bool Overlaps(Rect r) => r.X0 < X1 - 1e-6 && r.X1 > X0 + 1e-6 && r.Y0 < Y1 - 1e-6 && r.Y1 > Y0 + 1e-6;
            public override string ToString() => string.Format(CultureInfo.InvariantCulture, "[{0:0.#},{1:0.#}]-[{2:0.#},{3:0.#}]", X0, Y0, X1, Y1);
        }

        /// <summary>Element arkusza: rzutnia (W×H mm papieru) + opcjonalnie notatka pod nią (wliczona w W/H przez wywołującego).</summary>
        public sealed class Item
        {
            public string Key;
            public Kind Kind;
            public double W, H;
            /// <summary>Przekrój z nibem (na pierwszy arkusz w pierwszej kolejności).</summary>
            public bool Nib;
            /// <summary>Przekrój: klucz detalu, na którym jest jego linia cięcia — na tym samym arkuszu co detal (decyzja Bartka
            /// 2026-10-19); null — przekrój samodzielny.</summary>
            public string Group;
            public override string ToString() => Key;
        }

        public sealed class Placement
        {
            public Item Item;
            /// <summary>Prostokąt elementu na arkuszu (bez odstępu).</summary>
            public Rect Rect;
        }

        public sealed class Sheet
        {
            public List<Placement> Placed = new List<Placement>();
            public bool Has(Kind k) => Placed.Any(p => p.Item.Kind == k);
        }

        public sealed class Plan
        {
            public List<Sheet> Sheets = new List<Sheet>();
            public List<string> Warnings = new List<string>();
        }

        // ── pakowanie (wolne prostokąty, MaxRects) ───────────────────────

        public enum Corner { TopLeft, BottomLeft }

        /// <summary>Pakowanie prostokątów w polu arkusza: lista maksymalnych wolnych prostokątów (MaxRects).</summary>
        public sealed class Packer
        {
            private readonly List<Rect> _free = new List<Rect>();
            private readonly Rect _area;
            public Packer(Rect area) { _area = area; _free.Add(area); }
            public List<Rect> Snapshot() => new List<Rect>(_free);
            public void Restore(List<Rect> free) { _free.Clear(); _free.AddRange(free); }

            /// <summary>Zajęcie prostokąta (z odstępem — wywołujący powiększa o Gap).</summary>
            public void Occupy(Rect r)
            {
                var add = new List<Rect>();
                for (int i = _free.Count - 1; i >= 0; i--)
                {
                    var f = _free[i];
                    if (!f.Overlaps(r)) continue;
                    _free.RemoveAt(i);
                    if (r.X0 > f.X0) add.Add(new Rect(f.X0, f.Y0, r.X0, f.Y1));
                    if (r.X1 < f.X1) add.Add(new Rect(r.X1, f.Y0, f.X1, f.Y1));
                    if (r.Y0 > f.Y0) add.Add(new Rect(f.X0, f.Y0, f.X1, r.Y0));
                    if (r.Y1 < f.Y1) add.Add(new Rect(f.X0, r.Y1, f.X1, f.Y1));
                }
                foreach (var a in add) if (a.W > 1e-6 && a.H > 1e-6) _free.Add(a);
                // bez prostokątów zawartych w innych
                var keep = new List<Rect>();
                for (int i = 0; i < _free.Count; i++)
                {
                    bool inside = false;
                    for (int j = 0; j < _free.Count && !inside; j++)
                        inside = i != j && _free[j].Contains(_free[i]) && (!_free[i].Contains(_free[j]) || j < i);   // duplikat — zostaje pierwszy
                    if (!inside) keep.Add(_free[i]);
                }
                _free.Clear();
                _free.AddRange(keep);
            }

            /// <summary>Miejsce na prostokąt w×h: TopLeft — najwyżej, potem najbardziej w lewo; BottomLeft — najniżej, potem w lewo.</summary>
            public bool TryFind(double w, double h, Corner corner, out Rect r)
            {
                r = default;
                bool found = false;
                foreach (var f in _free)
                {
                    if (w > f.W + 1e-6 || h > f.H + 1e-6) continue;
                    var c = corner == Corner.TopLeft ? new Rect(f.X0, f.Y1 - h, f.X0 + w, f.Y1) : new Rect(f.X0, f.Y0, f.X0 + w, f.Y0 + h);
                    if (!found || Better(c, r, corner)) { r = c; found = true; }
                }
                return found;
            }

            private static bool Better(Rect a, Rect b, Corner corner)
            {
                if (corner == Corner.TopLeft)
                {
                    if (Math.Abs(a.Y1 - b.Y1) > 1e-6) return a.Y1 > b.Y1;
                    return a.X0 < b.X0 - 1e-6;
                }
                if (Math.Abs(a.Y0 - b.Y0) > 1e-6) return a.Y0 < b.Y0;
                return a.X0 < b.X0 - 1e-6;
            }
        }

        public static Rect Area => new Rect(AreaX0, AreaY0, AreaX1, AreaY1);

        private sealed class SheetBuilder
        {
            public readonly Sheet Sheet = new Sheet();
            private readonly Packer _p;
            // pole powiększone o pół odstępu — elementy dochodzą do krawędzi pola
            public SheetBuilder() { _p = new Packer(new Rect(AreaX0 - Gap / 2, AreaY0 - Gap / 2, AreaX1 + Gap / 2, AreaY1 + Gap / 2)); }

            public void Fixed(Item it, Rect r)
            {
                Sheet.Placed.Add(new Placement { Item = it, Rect = r });
                _p.Occupy(new Rect(r.X0 - Gap / 2, r.Y0 - Gap / 2, r.X1 + Gap / 2, r.Y1 + Gap / 2));
            }

            public bool TryPlace(Item it, Corner corner)
            {
                if (!_p.TryFind(it.W + Gap, it.H + Gap, corner, out var g)) return false;
                Fixed(it, new Rect(g.X0 + Gap / 2, g.Y0 + Gap / 2, g.X1 - Gap / 2, g.Y1 - Gap / 2));
                return true;
            }

            /// <summary>Wszystkie elementy jednostki (detal + jego przekroje) albo żaden.</summary>
            public bool TryPlaceAll(IList<Item> unit)
            {
                var free = _p.Snapshot();
                int n = Sheet.Placed.Count;
                foreach (var it in unit)
                    if (!TryPlace(it, CornerOf(it)))
                    {
                        _p.Restore(free);
                        Sheet.Placed.RemoveRange(n, Sheet.Placed.Count - n);
                        return false;
                    }
                return true;
            }
        }

        private static Corner CornerOf(Item it) => it.Kind == Kind.Ph ? Corner.BottomLeft : Corner.TopLeft;

        /// <summary>Rzuty na arkuszu: jeden — lewy górny róg; dwa — dolny z lewej, górny z prawej (wyrównany do prawej krawędzi pola).</summary>
        private static bool PlacePlans(SheetBuilder sb, Item b, Item t)
        {
            if (b != null && t != null && b.W + Gap + t.W > AreaX1 - AreaX0 + 1e-6) return false;
            if (b != null) sb.Fixed(b, new Rect(AreaX0, AreaY1 - b.H, AreaX0 + b.W, AreaY1));
            if (t != null)
            {
                double x0 = b != null ? AreaX1 - t.W : AreaX0;
                sb.Fixed(t, new Rect(x0, AreaY1 - t.H, x0 + t.W, AreaY1));
            }
            return true;
        }

        /// <summary>
        /// Arkusze dla elementów (wymiary w mm papieru). Kolejność w obrębie grup jak na liście wejściowej
        /// (przekroje wg litery, detale wg numeru, PH wg numeru — ustala wywołujący); przekroje z nibem — pierwsze.
        /// </summary>
        public static Plan Arrange(IList<Item> items)
        {
            var plan = new Plan();
            var b = items.FirstOrDefault(i => i.Kind == Kind.PlanBottom);
            var t = items.FirstOrDefault(i => i.Kind == Kind.PlanTop);
            var secs = items.Where(i => i.Kind == Kind.Section).OrderBy(i => i.Nib ? 0 : 1).ToList();   // stabilne: litery bez zmian
            var dets = items.Where(i => i.Kind == Kind.Detail).ToList();
            var phs = items.Where(i => i.Kind == Kind.Ph).ToList();

            foreach (var it in items)
                if (it.W > AreaX1 - AreaX0 + 1e-6 || it.H > AreaY1 - AreaY0 + 1e-6)
                    plan.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0}: {1:0} × {2:0} mm — większe niż pole arkusza A1 ({3:0} × {4:0} mm); rzutnia wychodzi poza pole",
                        it.Key, it.W, it.H, AreaX1 - AreaX0, AreaY1 - AreaY0));

            // 1) wszystko na jednym arkuszu (PH najpierw — dolny rząd; potem przekroje i detale pod rzutami)
            {
                var sb = new SheetBuilder();
                if (PlacePlans(sb, b, t))
                {
                    bool all = true;
                    foreach (var it in phs.Concat(secs).Concat(dets))
                        if (!sb.TryPlace(it, CornerOf(it))) { all = false; break; }
                    if (all) { plan.Sheets.Add(sb.Sheet); return plan; }
                }
            }

            // 2) kilka arkuszy — jednostki: przekrój samodzielny / detal razem z przekrojami z jego linią cięcia / PH
            var units = new List<List<Item>>();
            foreach (var it in secs.Where(x => !IsGrouped(x, dets))) units.Add(new List<Item> { it });
            foreach (var d in dets) units.Add(new List<Item> { d }.Concat(secs.Where(x => x.Group == d.Key)).ToList());
            var rest = new List<List<Item>>();
            {
                var sb = new SheetBuilder();
                PlacePlans(sb, b, null);
                foreach (var u in units)
                    if (!sb.TryPlaceAll(u)) rest.Add(u);
                if (b != null || sb.Sheet.Placed.Count > 0) plan.Sheets.Add(sb.Sheet);
            }
            FillSheets(plan, rest);

            var restPh = new List<List<Item>>();
            if (t != null || phs.Count > 0)
            {
                var sb = new SheetBuilder();
                PlacePlans(sb, null, t);
                foreach (var it in phs)
                    if (!sb.TryPlace(it, Corner.BottomLeft)) restPh.Add(new List<Item> { it });
                if (t != null || sb.Sheet.Placed.Count > 0) plan.Sheets.Add(sb.Sheet);
            }
            FillSheets(plan, restPh);
            return plan;
        }

        private static bool IsGrouped(Item sec, List<Item> dets)
            => !string.IsNullOrEmpty(sec.Group) && dets.Any(d => d.Key == sec.Group);

        /// <summary>
        /// Jednostki na kolejnych arkuszach (bez rzutów). Jednostka za duża na pusty arkusz — jej pierwszy element sam w lewym górnym
        /// rogu, reszta jednostki obok, jeśli się zmieści (inaczej na kolejnym arkuszu).
        /// </summary>
        private static void FillSheets(Plan plan, List<List<Item>> rest)
        {
            while (rest.Count > 0)
            {
                var sb = new SheetBuilder();
                var left = new List<List<Item>>();
                foreach (var u in rest)
                    if (!sb.TryPlaceAll(u)) left.Add(u);
                if (sb.Sheet.Placed.Count == 0)
                {
                    var u = left[0];
                    var it = u[0];
                    sb.Fixed(it, new Rect(AreaX0, AreaY1 - it.H, AreaX0 + it.W, AreaY1));
                    var tail = new List<Item>();
                    foreach (var x in u.Skip(1)) if (!sb.TryPlace(x, CornerOf(x))) tail.Add(x);
                    left.RemoveAt(0);
                    foreach (var x in tail) left.Insert(0, new List<Item> { x });
                }
                plan.Sheets.Add(sb.Sheet);
                rest = left;
            }
        }

        // ── teksty arkusza ───────────────────────────────────────────────

        private static readonly Regex TrailingNumberRx = new Regex(@"(\d+)(?!.*\d)");

        /// <summary>Numer rysunku + n: ostatnia grupa cyfr zwiększona z zachowaniem zer wiodących („CV129QD001-RC0090” + 1 → „…-RC0091”).</summary>
        public static string IncrementDrawingNumber(string number, int by)
        {
            if (string.IsNullOrEmpty(number) || by == 0) return number ?? "";
            var m = TrailingNumberRx.Match(number);
            if (!m.Success) return number + "-" + (by + 1).ToString(CultureInfo.InvariantCulture);
            long v = long.Parse(m.Value, CultureInfo.InvariantCulture) + by;
            string s = v.ToString(CultureInfo.InvariantCulture);
            if (s.Length < m.Value.Length) s = s.PadLeft(m.Value.Length, '0');
            return number.Substring(0, m.Index) + s + number.Substring(m.Index + m.Length);
        }

        /// <summary>Część numeru rysunku po ostatnim „-” („CV129QD001-RC0091” → „RC0091”) — nazwa layoutu = ta część + rewizja.</summary>
        public static string DrawingSuffix(string number)
        {
            if (string.IsNullOrEmpty(number)) return "";
            int i = number.LastIndexOf('-');
            return (i >= 0 ? number.Substring(i + 1) : number).Trim();
        }

        /// <summary>
        /// TITLE_3 wg zawartości arkusza (jak na arkuszach wzorcowych): „BOTTOM LAYER, TOP LAYER, DETAILS &amp; SECTIONS.”,
        /// „BOTTOM LAYER &amp; SECTIONS.”, „TOP LAYER &amp; PH DETAILS.”. Same detale PH (bez detali belek / otworów) — „PH DETAILS” (decyzja Bartka 2026-10-19: „BOTTOM LAYER, TOP LAYER &amp; PH DETAILS.”).
        /// </summary>
        public static string Title3(bool bottom, bool top, bool sections, bool details, bool ph)
            => Title3(bottom, top, sections, details, ph, null, null);

        /// <summary>
        /// TITLE_3 z częścią rzutu podzielonego na kilka arkuszy (wzór Bartka 2026-10-26, PLOT 357-362: „BOTTOM LAYER 1/2.”);
        /// same detale PH bez rzutu górnego — „TOP LAYER PH DETAILS” (detale przebicia to zbrojenie górne; ten sam wzór).
        /// </summary>
        public static string Title3(bool bottom, bool top, bool sections, bool details, bool ph, string bottomPart, string topPart)
        {
            var parts = new List<string>();
            if (bottom) parts.Add("BOTTOM LAYER" + (string.IsNullOrEmpty(bottomPart) ? "" : " " + bottomPart));
            if (top) parts.Add("TOP LAYER" + (string.IsNullOrEmpty(topPart) ? "" : " " + topPart));
            if (details) parts.Add("DETAILS");
            else if (ph) parts.Add(top ? "PH DETAILS" : "TOP LAYER PH DETAILS");
            if (sections) parts.Add("SECTIONS");
            if (parts.Count == 0) return "";
            string s = parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " & " + parts[parts.Count - 1];
            return s + ".";
        }

        public static string Title3(Sheet s)
            => Title3(s.Has(Kind.PlanBottom), s.Has(Kind.PlanTop), s.Has(Kind.Section), s.Has(Kind.Detail), s.Has(Kind.Ph));

        /// <summary>SCALE: skale arkusza od najmniejszej (1:50, 1:25 &amp; 1:20 @ A1).</summary>
        public static string ScaleText(IEnumerable<int> scales)
        {
            var l = scales.Distinct().OrderByDescending(x => x).Select(x => "1:" + x.ToString(CultureInfo.InvariantCulture)).ToList();
            if (l.Count == 0) return "";
            string s = l.Count == 1 ? l[0] : string.Join(", ", l.Take(l.Count - 1)) + " & " + l[l.Count - 1];
            return s + " @ A1";
        }

        public static int ScaleOf(Kind k) => k == Kind.PlanBottom || k == Kind.PlanTop ? 50 : k == Kind.Section ? 20 : 25;

        /// <summary>Część rzutu w tytule („BOTTOM LAYER 1/2.” → „1/2”); null — brak.</summary>
        private static readonly Regex PartRx = new Regex(@"\s+(\d+)/(\d+)(?=[\s.,&]|$)");

        /// <summary>Tytuł bez numeracji części („BOTTOM LAYER 1/2.” → „BOTTOM LAYER.”).</summary>
        public static string WithoutPart(string title3) => PartRx.Replace(title3 ?? "", "");

        /// <summary>Zakres numerów rysunków: „SO302HH001-RC3570” … „SO302HH001-RC3571” → „SO302HH001-RC3570-3571”.</summary>
        public static string NumberRange(string first, string last)
        {
            if (string.IsNullOrEmpty(last) || string.Equals(first, last, StringComparison.OrdinalIgnoreCase)) return first ?? "";
            var m = Regex.Match(last, @"(\d+)$");
            return (first ?? "") + "-" + (m.Success ? m.Groups[1].Value : last);
        }

        /// <summary>
        /// Odsyłacze arkusza <paramref name="k"/> (wzór Bartka 2026-10-26): pozostałe arkusze w kolejności; rzut podzielony na kilka arkuszy
        /// („BOTTOM LAYER 1/2.”, „… 2/2.”) — na innym arkuszu JEDEN odsyłacz z zakresem („BOTTOM LAYER SEE DRG. …RC3570-3571”), na arkuszu tej
        /// samej grupy — pozostałe części osobno („BOTTOM LAYER 2/2 SEE DRG. …RC3571”).
        /// </summary>
        public static List<(string Title3, string Number)> CrossRefsFor(IList<(string Title3, string Number)> sheets, int k)
        {
            var res = new List<(string, string)>();
            string Key(int i) => PartRx.IsMatch(sheets[i].Title3 ?? "") ? WithoutPart(sheets[i].Title3) : null;
            string myKey = Key(k);
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int j = 0; j < sheets.Count; j++)
            {
                if (j == k) continue;
                string key = Key(j);
                if (key == null || key == myKey) { res.Add(sheets[j]); continue; }
                if (!done.Add(key)) continue;
                var grp = Enumerable.Range(0, sheets.Count).Where(i => Key(i) == key).ToList();
                res.Add((key, NumberRange(sheets[grp.First()].Number, sheets[grp.Last()].Number)));
            }
            return res;
        }

        /// <summary>Odsyłacz do innych arkuszy: „TOP LAYER &amp; PH DETAILS SEE DRG. CV129QD001-RC0091” — linia na arkusz.</summary>
        public static List<string> CrossRefLines(IEnumerable<(string Title3, string Number)> others)
            => others.Select(o => (o.Title3 ?? "").TrimEnd('.', ' ') + " SEE DRG. " + o.Number).ToList();

        /// <summary>Znaków w linii odsyłacza (ROMANS NARROW 4 mm, ramka 89 mm; wzór — arkusz PLOT 9-10).</summary>
        public const int CrossRefChars = 28;

        /// <summary>
        /// Odsyłacz do jednego arkusza — osobna ramka (decyzja Bartka 2026-10-19): „&lt;TITLE_3&gt; SEE” łamane po słowach do
        /// <see cref="CrossRefChars"/> znaków, w ostatniej linii „DRG. &lt;numer&gt;” (jak „BOTTOM LAYER &amp; SECTIONS SEE / DRG. CV129QD001-RC0090”).
        /// </summary>
        public static List<string> CrossRefBlock(string title3, string number, int maxChars = CrossRefChars)
        {
            var lines = new List<string>();
            string cur = "";
            foreach (var w in ((title3 ?? "").TrimEnd('.', ' ') + " SEE").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (cur.Length > 0 && cur.Length + 1 + w.Length > maxChars) { lines.Add(cur); cur = w; }
                else cur = cur.Length == 0 ? w : cur + " " + w;
            }
            if (cur.Length > 0) lines.Add(cur);
            lines.Add("DRG. " + (number ?? ""));
            return lines;
        }

        /// <summary>Wysokość ramki odsyłacza dla n linii (mm papieru; 2 linie = 16,7 jak na arkuszu wzorcowym).</summary>
        public static double CrossRefBoxHeight(int lines) => 10.03 + Math.Max(0, lines - 1) * 6.67;


        /// <summary>
        /// Notatka pod przekrojem z nibem (teksty z arkuszy wzorcowych; decyzja Bartka 2026-10-19): notatka „A-A” — przekrój
        /// w kierunku 1 („fixed to the bottom of the U-bars, and on top of the U-bars”), notatka „B-B” — przekrój w kierunku 2
        /// („fixed to the top of the U-bars, and to the underside”). W tekście litera danego przekroju.
        /// </summary>
        public static string NibNote(string letter, int dir)
        {
            string l = (letter ?? "?") + "-" + (letter ?? "?");
            return dir == 2
                ? "{\\C3;Note:\\PSection " + l + " has longitudinal lacer\\Pbars inside fixed to the top of the\\PU-bars, and to the underside of the\\PU-bars to maintain cover to the\\Pbottom and top of the toe.}"
                : "{\\C3;Note:\\PSection " + l + " has longitudinal lacer\\Pbars inside fixed to the bottom of the\\PU-bars, and on top of the U-bars to\\Pmaintain cover to the bottom and\\Ptop of the toe.}";
        }

        /// <summary>Kierunek UB 03 z kodu warstwy rozkładu („B1” → 1, „B2” → 2); inny — 0.</summary>
        public static int DirOf(string layerCode)
        {
            if (string.IsNullOrEmpty(layerCode) || layerCode.Length < 2) return 0;
            char c = layerCode[layerCode.Length - 1];
            return c == '1' ? 1 : c == '2' ? 2 : 0;
        }

        /// <summary>Pole z wartości składnika odcisku przekroju („Mark:…;Lay:B1;Kind:NibUB;…”); brak — null.</summary>
        public static string Field(string value, string name)
        {
            if (string.IsNullOrEmpty(value)) return null;
            foreach (var f in value.Split(';'))
            {
                int i = f.IndexOf(':');
                if (i > 0 && string.Equals(f.Substring(0, i), name, StringComparison.Ordinal)) return f.Substring(i + 1);
            }
            return null;
        }

        private static double NumField(string value, string name)
            => double.TryParse(Field(value, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

        /// <summary>Przekrój z nibem: segment płyty z nibem (SEG… N0 / N1 &gt; 0) albo UB 03 (DIST… Kind:NibUB) w przekroju.</summary>
        public static bool IsNibSection(IDictionary<string, string> components)
        {
            if (components == null) return false;
            foreach (var kv in components)
            {
                if (kv.Key.StartsWith("SEG#", StringComparison.Ordinal) && (NumField(kv.Value, "N0") > 0 || NumField(kv.Value, "N1") > 0)) return true;
                if (kv.Key.StartsWith("DIST:", StringComparison.Ordinal) && Field(kv.Value, "Kind") == "NibUB") return true;
            }
            return false;
        }

        /// <summary>Przekrój przez belkę / pogrubienie (składnik BEAM:… albo rozkład Kind:Beam) — bez notatki o prętach w nibie
        /// (decyzja Bartka 2026-10-20).</summary>
        public static bool IsBeamSection(IDictionary<string, string> components)
        {
            if (components == null) return false;
            foreach (var kv in components)
            {
                if (kv.Key.StartsWith("BEAM:", StringComparison.Ordinal)) return true;
                if (kv.Key.StartsWith("DIST:", StringComparison.Ordinal) && Field(kv.Value, "Kind") == "Beam") return true;
            }
            return false;
        }

        /// <summary>
        /// Kierunek przekroju (1 / 2): kierunek prętów leżących w płaszczyźnie cięcia (rozkład z AX = kierunek cięcia) — najpierw
        /// pręty główne (Kind:Main), potem dowolne z kodem warstwy; pręty prostopadłe (kropki) — kierunek przeciwny;
        /// bez prętów — cięcie wzdłuż X = 1.
        /// </summary>
        public static int SectionDir(IDictionary<string, string> components, bool cutAlongX)
        {
            int inPlaneMain = 0, inPlaneAny = 0, crossMain = 0;
            if (components != null)
                foreach (var kv in components)
                {
                    if (!kv.Key.StartsWith("DIST:", StringComparison.Ordinal)) continue;
                    int d = DirOf(Field(kv.Value, "Lay"));
                    if (d == 0) continue;
                    bool inPlane = (Field(kv.Value, "AX") == "1") == cutAlongX;
                    bool main = Field(kv.Value, "Kind") == "Main";
                    if (inPlane && main && inPlaneMain == 0) inPlaneMain = d;
                    if (inPlane && inPlaneAny == 0) inPlaneAny = d;
                    if (!inPlane && main && crossMain == 0) crossMain = d;
                }
            if (inPlaneMain > 0) return inPlaneMain;
            if (inPlaneAny > 0) return inPlaneAny;
            if (crossMain > 0) return 3 - crossMain;
            return cutAlongX ? 1 : 2;
        }
    }
}
