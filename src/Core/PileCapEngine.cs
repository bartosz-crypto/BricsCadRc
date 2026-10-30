using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Pile cap — oczep pala poza płytą (uwaga Bartka 2026-10-30, GA RG215UH001 BLOCK 2 „APARTMENT 2”: „600SQx600DP / PILE CAP / TOC=…”).
    /// Prepare GA: zamknięty prostokąt SD-PILED-RAFT poza obrysem płyty z opisem „PILE CAP” obok → warstwa RC-PILECAP + rekord RC_PILECAP
    /// ([1] wersja, [2] B, [3] L, [4] H, [5] TOC) — nie jest płytą (nib, otwory, siatka, wsporniki go nie widzą).
    /// Zbrojenie ZAWSZE takie samo (pilecap.dwg + dlugosci.xls biura): na oczep 600×600×600 — 20 × H12 kształt 21 A480 / B500 / C480
    /// (U-bary: 5 dołem i 5 górą w każdym kierunku) + 4 × H12 kształt 21 A480 / B270 / C480 (2 w każdym kierunku przy osi pala) —
    /// AutoRebarEngine.GeneratePileCaps (detal 1:25 z przekrojami, rozkłady RC_BAR, BBS z rysunku).
    /// </summary>
    public static class PileCapEngine
    {
        public const string LayerName = "RC-PILECAP";
        public const string App = "RC_PILECAP";
        public const short FormatVersion = 1;
        public const short LayerColor = 6;
        /// <summary>Opis „PILE CAP” najwyżej tyle od środka prostokąta [mm].</summary>
        public const double TextSearch = 1500.0;
        public const double MinSize = 300.0, MaxSize = 1500.0;

        public sealed class Data
        {
            public double B, L, H, Toc;
        }

        public static bool IsLayer(string layer) => string.Equals(layer, LayerName, StringComparison.OrdinalIgnoreCase);

        public static bool IsCap(Entity e)
        {
            if (!(e is Polyline)) return false;
            using (var rb = e.GetXDataForApplication(App)) return rb != null;
        }

        public static Data Read(Entity e)
        {
            TypedValue[] v;
            using (var rb = e?.GetXDataForApplication(App)) v = rb?.AsArray();
            if (v == null) return null;
            double R(int i) => v.Length > i && v[i].Value is double d ? d : 0.0;
            return new Data { B = R(2), L = R(3), H = R(4), Toc = R(5) };
        }

        private static void Write(Transaction tr, Database db, Entity e, Data d)
        {
            BeamStore.EnsureApp(tr, db, App);
            if (!e.IsWriteEnabled) e.UpgradeOpen();
            e.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, App),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, FormatVersion),   // [1]
                new TypedValue((int)DxfCode.ExtendedDataReal, d.B),                 // [2]
                new TypedValue((int)DxfCode.ExtendedDataReal, d.L),                 // [3]
                new TypedValue((int)DxfCode.ExtendedDataReal, d.H),                 // [4]
                new TypedValue((int)DxfCode.ExtendedDataReal, d.Toc));              // [5]
        }

        public static void EnsureLayer(Transaction tr, Database db)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(LayerName)) return;
            var rec = new LayerTableRecord { Name = LayerName, Color = Color.FromColorIndex(ColorMethod.ByAci, LayerColor) };
            lt.UpgradeOpen();
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            try { rec.Description = "NA Smart RC: pile cap (oczep pala poza płytą) — rekord RC_PILECAP"; }
            catch (System.Exception ex) { Log.Error("PileCapEngine.EnsureLayer.Description", ex); }
        }

        // ── opis „600SQx600DP / PILE CAP / TOC=98.325” ───────────────────

        private static readonly Regex SqRx = new Regex(@"(\d{3,4})\s*SQ\s*[xX×]\s*(\d{3,4})\s*(?:mm\s*)?DP", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RectRx = new Regex(@"(\d{3,4})\s*[xX×]\s*(\d{3,4})\s*[xX×]\s*(\d{3,4})\s*(?:mm\s*)?DP", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CapRx = new Regex(@"PILE\s*CAP", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool IsCapText(string text) => !string.IsNullOrEmpty(text) && CapRx.IsMatch(text);

        /// <summary>„600SQx600DP” → (600, 600, 600); „600x900x600DP” → (600, 900, 600); inaczej null.</summary>
        public static (double B, double L, double H)? ParseSize(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var m = SqRx.Match(text);
            if (m.Success)
            {
                double s = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                return (s, s, double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
            }
            m = RectRx.Match(text);
            if (m.Success)
                return (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                        double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
            return null;
        }

        /// <summary>Prostokąt osiowy z 4 (albo 5 — powtórzony pierwszy) wierzchołków; null — inny kształt.</summary>
        public static (double X0, double Y0, double X1, double Y1)? AxisRect(IList<Point2d> v)
        {
            if (v == null) return null;
            var p = v.ToList();
            if (p.Count == 5 && p[0].GetDistanceTo(p[4]) < 1.0) p.RemoveAt(4);
            if (p.Count != 4) return null;
            for (int i = 0; i < 4; i++)
            {
                var a = p[i]; var b = p[(i + 1) % 4];
                if (Math.Abs(a.X - b.X) > 1.0 && Math.Abs(a.Y - b.Y) > 1.0) return null;
            }
            double x0 = p.Min(q => q.X), x1 = p.Max(q => q.X), y0 = p.Min(q => q.Y), y1 = p.Max(q => q.Y);
            if (x1 - x0 < MinSize - 1 || y1 - y0 < MinSize - 1 || x1 - x0 > MaxSize + 1 || y1 - y0 > MaxSize + 1) return null;
            return (x0, y0, x1, y1);
        }

        /// <summary>
        /// Prepare GA (po skopiowaniu rzutu): kandydaci — zamknięte prostokąty SD-PILED-RAFT; opisy — MText „PILE CAP”. Prostokąt poza obrysem
        /// płyty z opisem w odległości ≤ TextSearch → RC-PILECAP + RC_PILECAP; wymiary z rysunku (H z „…DP”, brak — B). Zwraca zamienione
        /// (do usunięcia z list kawałków obrysu / uskoku).
        /// </summary>
        public static List<ObjectId> MarkCaps(Database db, IEnumerable<ObjectId> candidates, IEnumerable<ObjectId> texts, ObjectId outlineId,
                                              List<string> messages, string where)
        {
            var res = new List<ObjectId>();
            var cands = candidates?.Where(id => !id.IsNull && !id.IsErased).Distinct().ToList() ?? new List<ObjectId>();
            if (cands.Count == 0) return res;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                List<Point2d> outline = null;
                if (!outlineId.IsNull && tr.GetObject(outlineId, OpenMode.ForRead) is Polyline opl) outline = GeometryHelper.GetPolylineVertices(opl);
                var txt = new List<(Point2d p, string t)>();
                foreach (var id in texts ?? Enumerable.Empty<ObjectId>())
                {
                    if (id.IsNull || id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is MText mt)) continue;
                    string t = string.Join("\n", GaImportEngine.TextLines(mt));
                    if (!IsCapText(t)) continue;
                    var c = GaImportEngine.MTextCenter(mt);
                    txt.Add((new Point2d(c.X, c.Y), t));
                }
                if (txt.Count == 0) { tr.Commit(); return res; }
                int noText = 0;
                foreach (var id in cands)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Polyline pl) || !GeometryHelper.IsEffectivelyClosed(pl) || IsCap(pl)) continue;
                    var r = AxisRect(GeometryHelper.GetRawPolylineVertices(pl));
                    if (r == null) continue;
                    var c = new Point2d((r.Value.X0 + r.Value.X1) / 2, (r.Value.Y0 + r.Value.Y1) / 2);
                    if (outline != null && GeometryHelper.IsPointInsidePolygon(outline, c)) continue;   // otwór / strefa w płycie — nie oczep
                    var near = txt.Select(x => (x.t, d: x.p.GetDistanceTo(c))).Where(x => x.d <= TextSearch).OrderBy(x => x.d).ToList();
                    if (near.Count == 0) { noText++; continue; }
                    string t = near[0].t;
                    double b = r.Value.X1 - r.Value.X0, l = r.Value.Y1 - r.Value.Y0;
                    var size = ParseSize(t);
                    double h = size?.H ?? b;
                    if (size.HasValue && (Math.Abs(Math.Min(size.Value.B, size.Value.L) - Math.Min(b, l)) > 5 || Math.Abs(Math.Max(size.Value.B, size.Value.L) - Math.Max(b, l)) > 5))
                        messages?.Add($"[RC PILECAP] {where}: opis „{t.Split('\n')[0]}” a prostokąt {b:F0} × {l:F0} — przyjęty z rysunku.");
                    var d = new Data { B = b, L = l, H = h, Toc = CantileverRules.ParseToc(t) ?? 0 };
                    EnsureLayer(tr, db);
                    pl.UpgradeOpen();
                    pl.Layer = LayerName;
                    pl.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                    Write(tr, db, pl, d);
                    res.Add(id);
                }
                tr.Commit();
                if (res.Count > 0) messages?.Add($"[RC PILECAP] {where}: pile cap: {res.Count} (warstwa {LayerName}) — zbrojenie: RC_GENERATE → PileCap.");
                if (noText > 0) messages?.Add($"[RC PILECAP] {where}: {noText} zamkniętych prostokątów poza płytą bez opisu „PILE CAP” — pominięte.");
            }
            return res;
        }

        /// <summary>Oczepy (RC_PILECAP) przy obrysie płyty: środek w obwiedni obrysu powiększonej o <paramref name="margin"/>.</summary>
        public static List<(ObjectId id, string handle, (double X0, double Y0, double X1, double Y1) box, Data data)> CapsNear(
            Database db, Extents3d slabBox, double margin)
        {
            var res = new List<(ObjectId, string, (double, double, double, double), Data)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl) || !IsCap(pl)) continue;
                    var r = AxisRect(GeometryHelper.GetRawPolylineVertices(pl));
                    if (r == null) continue;
                    double cx = (r.Value.X0 + r.Value.X1) / 2, cy = (r.Value.Y0 + r.Value.Y1) / 2;
                    if (cx < slabBox.MinPoint.X - margin || cx > slabBox.MaxPoint.X + margin
                        || cy < slabBox.MinPoint.Y - margin || cy > slabBox.MaxPoint.Y + margin) continue;
                    res.Add((id, pl.Handle.ToString(), r.Value, Read(pl)));
                }
                tr.Commit();
            }
            return res;
        }

        // ── zbrojenie standardowe (pilecap.dwg / dlugosci.xls) ──────────

        public const int Dia = 12;
        public const string Shape = "21";
        public const double Cover = 50.0;
        /// <summary>U-bar główny: 5 w każdym kierunku dołem i 5 górą (wzór 600×600×600: A480 / B500 / C480).</summary>
        public const int MainPerLayer = 5;
        /// <summary>U-bar przy osi pala: wzdłuż X, 2 dołem + 2 górą, B 270, rozstaw 150 (wzór: 4 × A480 / B270 / C480).</summary>
        public const int InnerPerDir = 2;
        public const double InnerB = 270.0, InnerSpacing = 150.0;   // wzór: „2x2 H12-18-150 T1+B1 U-BARS”

        /// <summary>Rozstaw U-barów głównych (wzór: „5 H12-17-120”).</summary>
        public const double MainSpacing = 120.0;

        /// <summary>Liczba i rozstaw U-barów głównych w poprzek boku: co 120 symetrycznie, pierwszy ≥ 60 od lica (600 → 5 co 120).</summary>
        public static (int Count, double Spacing) MainLayout(double size)
        {
            int n = Math.Max(2, (int)Math.Floor((size - 2 * 60.0) / MainSpacing + 1e-6) + 1);
            return (n, MainSpacing);
        }

        /// <summary>Wymiary U-bara głównego dla oczepu: B = wymiar − 2 · 50, A = C = H − 2 · 60 (600 → A480 / B500).</summary>
        public static (double A, double B) MainU(double size, double h)
            => (Math.Round((h - 120.0) / 5.0) * 5.0, Math.Round((size - 2 * Cover) / 5.0) * 5.0);
    }
}
