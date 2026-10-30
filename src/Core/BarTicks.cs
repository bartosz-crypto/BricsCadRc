using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Końcówki pręta „do środka” na rysunku pręta RC_BAR (prośba Bartka 2026-10-30, jak S1 w przekrojach): na każdym końcu pręta
    /// krótka kreska 45° (długość <see cref="TickLength"/> ≈ 3·d) — pręt gięty: do środka obwiedni osi pręta, pręt prosty: w górę
    /// albo w dół (w układzie pręta). Tylko grafika: wymiary, długość, BBS bez zmian.
    ///
    /// Dane w osobnej aplikacji XData <see cref="AppName"/> na polilinii pręta (rekord RC_SINGLE_BAR bez zmian):
    /// [1] włączone (0/1), [2] strona pręta prostego (+1 w górę / −1 w dół), [3] maska końcówek NARYSOWANYCH w obrysie
    /// (bit 0 — początek, bit 1 — koniec, bit 2 — geometria S1: ramię skrócone o s, skos). Maskę pisze wyłącznie budowa obrysu (<see cref="SetApplied"/>).
    ///
    /// Obrys z końcówkami: standardowy obrys 2n (lewy brzeg 0..n−1 + prawy wstecz) z dopisanymi wierzchołkami końca kreski:
    /// [Lt0, ML0, L1 … Ln−2, MLn, Ltn, Rtn, MRn, Rn−2 … R1, MR0, Rt0] — ML / MR: narożniki skosu w miejscu rogów końca (ramię
    /// skrócone o s = L / √2, zasięg pręta bez zmian, jak S1 w przekrojach); Lt / Rt: rogi końca kreski (tylko końce z maską).
    /// Odczyt geometrii pręta (oś, rama, długość,
    /// grip) idzie przez <see cref="Strip"/> — dla reszty programu obrys jest jak bez końcówek.
    /// </summary>
    public static class BarTicks
    {
        public const string AppName = "RC_BAR_TICKS";

        /// <summary>Długość kreski końcówki [mm] — 3·d, min. 30 (decyzja Bartka 2026-10-30: „zależna od średnicy”).</summary>
        public static double TickLength(double d) => Math.Max(30.0, 3.0 * d);

        /// <summary>Kształty bez końcówek: pierścień, strzemiona zamknięte.</summary>
        public static bool Supports(string shapeCode)
        {
            string c = shapeCode ?? "00";
            return !BarFrameRules.IsRingShape(c) && c != "44" && c != "51" && c != "63";
        }

        /// <summary>Odczyt: włączone, strona pręta prostego, maska narysowanych końcówek. Brak danych → (false, −1, 0).</summary>
        public static (bool Enabled, int Side, int Mask) Read(Entity e)
        {
            try
            {
                using (var rb = e?.GetXDataForApplication(AppName))
                {
                    var v = rb?.AsArray();
                    if (v == null || v.Length < 4) return (false, -1, 0);
                    return (Convert.ToInt32(v[1].Value) != 0, Convert.ToInt32(v[2].Value) >= 0 ? 1 : -1, Convert.ToInt32(v[3].Value) & 7);
                }
            }
            catch (System.Exception ex) { Log.Error("BarTicks.Read", ex); return (false, -1, 0); }
        }

        /// <summary>Maska końcówek narysowanych w obrysie (0 — obrys standardowy).</summary>
        public static int AppliedMask(Entity e) => Read(e).Mask;

        /// <summary>
        /// Zapis wyboru użytkownika (włączone, strona) — maska narysowanych końcówek zostaje (zmienia ją tylko budowa obrysu).
        /// Wyłączone i brak danych — bez zapisu. Encja otwarta do zapisu.
        /// </summary>
        public static void WriteRequest(Entity e, bool enabled, int side)
        {
            if (e == null) return;
            var cur = Read(e);
            bool has = HasData(e);
            if (!enabled && !has) return;
            Write(e, enabled, side, cur.Mask);
        }

        /// <summary>Maska końcówek narysowanych w obrysie (po budowie obrysu). Brak danych i maska 0 — bez zapisu.</summary>
        public static void SetApplied(Entity e, int mask)
        {
            if (e == null) return;
            var cur = Read(e);
            if (!HasData(e) && mask == 0) return;
            Write(e, cur.Enabled, cur.Side, mask);
        }

        private static bool HasData(Entity e)
        {
            using (var rb = e.GetXDataForApplication(AppName)) return rb != null;
        }

        private static void Write(Entity e, bool enabled, int side, int mask)
        {
            EnsureApp(e.Database);
            e.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(enabled ? 1 : 0)),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(side >= 0 ? 1 : -1)),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(mask & 7)));
        }

        private static void EnsureApp(Database db)
        {
            if (db == null) return;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                if (!rat.Has(AppName))
                {
                    rat.UpgradeOpen();
                    var rec = new RegAppTableRecord { Name = AppName };
                    rat.Add(rec);
                    tr.AddNewlyCreatedDBObject(rec, true);
                }
                tr.Commit();
            }
        }

        /// <summary>
        /// Obrys bez końcówek (maska z budowy obrysu): usuwa rogi końca kreski (Lt / Rt) i odtwarza rogi końca pręta
        /// (narożniki skosu ML / MR → L / R): środek skosu C, kierunek ramienia z poprzedniego punktu osi, d = szerokość końca
        /// kreski |Lt − Rt|, koniec pręta P = C + s · t (s = TickLength(d) / √2). Liczba punktów niezgodna z maską → bez zmian.
        /// </summary>
        public static List<Point3d> Strip(IReadOnlyList<Point3d> pts, int mask)
        {
            var r = new List<Point3d>(pts ?? new List<Point3d>());
            if ((mask & 3) == 0) return r;
            // bit 2 (4) — geometria jak S1 (ramię skrócone, skos); bez niego — pierwsza wersja z fix128 (kreska doklejona za
            // końcem pręta, rogi końca bez zmian): tylko usunięcie rogów kreski
            bool v2 = (mask & 4) != 0;
            int extra = ((mask & 1) != 0 ? 2 : 0) + ((mask & 2) != 0 ? 2 : 0);
            if (r.Count < 4 + extra || r.Count % 2 != 0) return r;
            if ((mask & 1) != 0)
            {
                var lt = r[0]; var rt = r[r.Count - 1];
                r.RemoveAt(r.Count - 1); r.RemoveAt(0);
                int last = r.Count - 1;
                var q = Mid(r[1], r[last - 1]);
                if (v2) Restore(r, 0, last, q, lt.DistanceTo(rt));
            }
            if ((mask & 2) != 0)
            {
                int h = r.Count / 2;
                var lt = r[h - 1]; var rt = r[h];
                r.RemoveAt(h); r.RemoveAt(h - 1);
                int n = r.Count / 2;
                var q = Mid(r[n - 2], r[n + 1]);
                if (v2) Restore(r, n - 1, n, q, lt.DistanceTo(rt));
            }
            return r;
        }

        private static Point3d Mid(Point3d a, Point3d b) => new Point3d((a.X + b.X) / 2, (a.Y + b.Y) / 2, 0);

        /// <summary>Narożniki skosu (r[iL], r[iR]) → rogi końca pręta; q — poprzedni punkt osi, d — średnica.</summary>
        private static void Restore(List<Point3d> r, int iL, int iR, Point3d q, double d)
        {
            var c = Mid(r[iL], r[iR]);
            var t = c - q;
            t = new Vector3d(t.X, t.Y, 0);
            if (t.Length < 1e-9 || d < 1e-9) return;
            t = t.GetNormal();
            double s = TickLength(d) / Math.Sqrt(2.0);
            var p = c + t * s;
            var a = new Vector3d(-t.Y, t.X, 0);
            if ((r[iL] - c).DotProduct(a) < 0) a = -a;
            r[iL] = p + a * (d / 2.0);
            r[iR] = p - a * (d / 2.0);
        }

        /// <summary>Wierzchołki polilinii pręta w WCS (Z = 0) bez końcówek.</summary>
        public static List<Point3d> StandardVertices(Polyline pl)
        {
            var raw = new List<Point3d>(pl.NumberOfVertices);
            for (int i = 0; i < pl.NumberOfVertices; i++) { var p = pl.GetPoint2dAt(i); raw.Add(new Point3d(p.X, p.Y, 0)); }
            if (raw.Count == 0) return raw;
            int mask = AppliedMask(pl);
            if ((mask & 3) == 0)
            {
                // bez końcówek — wierzchołki jak dotąd (GetPoint3dAt, Z polilinii)
                raw.Clear();
                for (int i = 0; i < pl.NumberOfVertices; i++) raw.Add(pl.GetPoint3dAt(i));
                return raw;
            }
            return Strip(raw, mask);
        }

        /// <summary>
        /// Dopisuje końcówki do obrysu standardowego (2n punktów: lewy brzeg + prawy wstecz), jak S1 w przekrojach: ramię skrócone
        /// o s = L / √2, kreska 45° od skróconego końca C do P + s · n (zasięg pręta bez zmian), stała szerokość d, narożniki skosu
        /// na dwusiecznej. straight — pręt prosty (kreska wg upWorld · side), inaczej do środka obwiedni osi. Ramię krótsze niż
        /// s + d — bez końcówki na tym końcu. Zwraca maskę narysowanych końcówek (0 — nic nie dopisano).
        /// </summary>
        public static int Apply(List<Point2d> outline, bool straight, int side, Vector2d upWorld, double d)
        {
            if (outline == null || outline.Count < 4 || outline.Count % 2 != 0 || d <= 0) return 0;
            int n = outline.Count / 2;
            Point2d Ax(int i) => new Point2d((outline[i].X + outline[2 * n - 1 - i].X) / 2, (outline[i].Y + outline[2 * n - 1 - i].Y) / 2);
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                var a = Ax(i);
                minX = Math.Min(minX, a.X); maxX = Math.Max(maxX, a.X); minY = Math.Min(minY, a.Y); maxY = Math.Max(maxY, a.Y);
            }
            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            double L = TickLength(d), s = L / Math.Sqrt(2.0), half = d / 2.0;

            // (narożnik skosu lewy, prawy, róg końca kreski lewy, prawy) albo null
            (Point2d ML, Point2d MR, Point2d Lt, Point2d Rt)? Tick(int iEnd, int iPrev)
            {
                var P = Ax(iEnd); var Q = Ax(iPrev);
                var t = P - Q;
                if (t.Length < s + d) return null;
                t = t.GetNormal();
                var nrm = new Vector2d(-t.Y, t.X);
                Vector2d nIn;
                if (straight)
                {
                    var up = upWorld.Length > 1e-9 ? upWorld.GetNormal() : nrm;
                    nIn = nrm * ((up * (side >= 0 ? 1.0 : -1.0)).DotProduct(nrm) >= 0 ? 1.0 : -1.0);
                }
                else
                {
                    double dot = nrm.X * (cx - P.X) + nrm.Y * (cy - P.Y);
                    nIn = dot >= 0 ? nrm : -nrm;
                }
                // normalna ramienia po stronie lewego brzegu
                var aL = nrm;
                if ((outline[iEnd] - P).DotProduct(aL) < 0) aL = -aL;
                var u = (t + nIn).GetNormal();               // kierunek kreski (45°)
                var m = new Vector2d(-u.Y, u.X);
                if (m.DotProduct(aL) < 0) m = -m;            // normalna kreski po stronie lewego brzegu
                var C = P - t * s;                            // skrócony koniec ramienia
                var T = C + u * L;                            // koniec kreski = P + s · n
                var miter = (aL + m) * (half / (1.0 + aL.DotProduct(m)));
                return (C + miter, C - miter, T + m * half, T - m * half);
            }

            int mask = 0;
            var endTick = Tick(n - 1, n - 2);
            var startTick = Tick(0, 1);
            if (endTick.HasValue)
            {
                outline[n - 1] = endTick.Value.ML;
                outline[n]     = endTick.Value.MR;
                outline.Insert(n, endTick.Value.Rt);   // prawy brzeg zaczyna się od końca kreski
                outline.Insert(n, endTick.Value.Lt);   // przed nim — koniec lewego brzegu
                mask |= 2;
            }
            if (startTick.HasValue)
            {
                outline[0] = startTick.Value.ML;
                outline[outline.Count - 1] = startTick.Value.MR;
                outline.Insert(0, startTick.Value.Lt);
                outline.Add(startTick.Value.Rt);
                mask |= 1;
            }
            return mask == 0 ? 0 : mask | 4;   // 4 — geometria S1 (Strip odtwarza rogi końca pręta)
        }
    }
}
