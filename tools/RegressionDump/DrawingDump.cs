// S-REG: znormalizowany zrzut rysunku (model space + zawartość bloków).
// Normalizacja: liczby zaokrąglone do 0.01 (kąty 0.0001), handle (1005) → numer porządkowy encji
// w kolejności sortowania (sortowanie po treści, nie po handle), nazwy BTR bez GUID / numerów *U.
//
// Tryb bar-equiv (Plan C, etap 3; RC_REG_BAR_EQUIV=1, dla obu zrzutów): pręt — polilinia z RC_SINGLE_BAR albo
// pręt-blok v2 (BlockReference, [14] = Int16 2) — renderowany kanonicznie jako „Bar”: warstwa, efektywne
// C/LT/LTS/LW, XData RC_SINGLE_BAR [0]–[13] (bez [14]), pierścienie wierzchołków WCS 0.01 z bulge; encje RC_BAR_LINK
// (polilinie) dołączane jako pierścienie pręta głównego. BTR pręta nie jest renderowany; typ encji pomijany.
// Osobne asercje bloków (linie „# BARBLOCK …”, poza diffem): [14] = 2, rekord 15, *U, Explodable = false, refs = 1,
// zawartość bez XData, warstwa 0 / ByBlock.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace RegressionDump
{
    internal static class DrawingDump
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const int MaxDepth = 4;

        /// <param name="filter">Tylko encje spełniające filtr (zrzut przypadku poprawki — pas Y); null = wszystkie.
        /// Numeracja „#n” lokalna dla przefiltrowanych encji; odwołania poza filtr → „@ext:…” / „@erased”.</param>
        /// <param name="withLayers">Lista warstw (zrzut główny); w zrzutach przypadków pomijana.</param>
        internal static List<string> Run(Database db, bool barEquiv = false, Func<Entity, bool> filter = null,
                                         bool withLayers = true)
        {
            var outL = new List<string>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                BarInfo bars = barEquiv ? CollectBars(tr, ms, outL, filter) : null;

                // Warstwy (nazwa, kolor, rodzaj linii)
                if (withLayers)
                {
                    var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    var layers = new List<string>();
                    foreach (ObjectId id in lt)
                    {
                        var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        string ltName = l.LinetypeObjectId.IsNull ? "" :
                            ((LinetypeTableRecord)tr.GetObject(l.LinetypeObjectId, OpenMode.ForRead)).Name;
                        layers.Add($"layer {l.Name} C={l.Color.ColorIndex} LT={ltName} off={l.IsOff} frozen={l.IsFrozen}");
                    }
                    layers.Sort(StringComparer.Ordinal);
                    outL.AddRange(layers);
                }

                // Encje model space: render z placeholderem handle → sortowanie → numery → render końcowy
                var ents = new List<(Entity e, string key)>();
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    if (filter != null && !filter(e)) continue;
                    if (bars != null && bars.Companion.Contains(e.Handle.Value)) continue;   // pierścień pręta głównego
                    var ctxSort = new Ctx(tr, db, null) { Bars = bars };
                    ents.Add((e, string.Join("\n", Render(e, ctxSort, 0))));
                }
                // Stabilne sortowanie po treści (remisy = identyczne encje, kolejność bez znaczenia)
                var sorted = ents.Select((x, i) => (x.e, x.key, i))
                                 .OrderBy(x => x.key, StringComparer.Ordinal).ThenBy(x => x.i).ToList();
                var map = new Dictionary<long, int>();
                for (int i = 0; i < sorted.Count; i++) map[sorted[i].e.Handle.Value] = i + 1;

                outL.Add($"entities={sorted.Count}");
                var ctx = new Ctx(tr, db, map) { Bars = bars };
                for (int i = 0; i < sorted.Count; i++)
                {
                    var r = Render(sorted[i].e, ctx, 0);
                    r[0] = $"#{i + 1} " + r[0];
                    outL.AddRange(r);
                }
            }
            return outL;
        }

        private sealed class Ctx
        {
            public readonly Transaction Tr;
            public readonly Database Db;
            public readonly Dictionary<long, int> Map;   // null = tryb sortowania (handle → "@")
            public BarInfo Bars;                          // != null = tryb bar-equiv
            public Ctx(Transaction tr, Database db, Dictionary<long, int> map) { Tr = tr; Db = db; Map = map; }

            public string Ref(long h)
            {
                if (h == 0) return "@0";
                if (Map == null) return "@";
                if (Map.TryGetValue(h, out int n)) return "#" + n;
                if (Db.TryGetObjectId(new Handle(h), out var id))
                {
                    if (id.IsErased) return "@erased";
                    try { return "@ext:" + Tr.GetObject(id, OpenMode.ForRead).GetRXClass().DxfName; }
                    catch (System.Exception ex) { return "@ext:?" + ex.GetType().Name; }
                }
                return "@missing";
            }
        }

        // ------------------------------------------------------------------

        private static List<string> Render(Entity e, Ctx c, int depth)
        {
            if (c.Bars != null && depth == 0 && c.Bars.IsBar(e)) return RenderBar(e, c);
            var L = new List<string>();
            string lt = e.Linetype;
            L.Add($"{e.GetType().Name} L={e.Layer} C={e.ColorIndex} LT={lt} LTS={F(e.LinetypeScale)} LW={e.LineWeight} vis={e.Visible}");
            XData(e, c, L);

            switch (e)
            {
                case Polyline pl:
                    L.Add($"  closed={pl.Closed} elev={F(pl.Elevation)} cw={TryF(() => pl.ConstantWidth)} n={pl.NumberOfVertices}");
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                        L.Add($"  v{i} {P2(pl.GetPoint2dAt(i))} b={F(pl.GetBulgeAt(i), 4)} w={F(pl.GetStartWidthAt(i))}/{F(pl.GetEndWidthAt(i))}");
                    break;
                case Line ln:
                    L.Add($"  {P(ln.StartPoint)} -> {P(ln.EndPoint)}");
                    break;
                case Circle ci:
                    L.Add($"  c={P(ci.Center)} r={F(ci.Radius)}");
                    break;
                case Arc ar:
                    L.Add($"  c={P(ar.Center)} r={F(ar.Radius)} a={F(ar.StartAngle, 4)}..{F(ar.EndAngle, 4)}");
                    break;
                case DBText t:
                    L.Add($"  text=\"{t.TextString}\" pos={P(t.Position)} align={P(t.AlignmentPoint)} h={F(t.Height)} rot={F(t.Rotation, 4)} " +
                          $"wf={F(t.WidthFactor)} hm={t.HorizontalMode} vm={t.VerticalMode} style={StyleName(c, t.TextStyleId)}");
                    break;
                case MText mt:
                    L.Add($"  mtext=\"{mt.Contents}\" loc={P(mt.Location)} h={F(mt.TextHeight)} rot={F(mt.Rotation, 4)} " +
                          $"att={mt.Attachment} w={F(mt.Width)} style={StyleName(c, mt.TextStyleId)}");
                    break;
                case MLeader ml:
                    RenderMLeader(ml, c, L);
                    break;
                case Dimension dim:
                    RenderDim(dim, c, L);
                    break;
                case BlockReference br:
                    RenderBlockRef(br, c, L, depth);
                    break;
                case Hatch h:
                    L.Add($"  hatch pat={h.PatternName} loops={h.NumberOfLoops} ext={Ext(e)}");
                    break;
                default:
                    L.Add($"  ext={Ext(e)}");
                    break;
            }
            return L;
        }

        private static void RenderMLeader(MLeader ml, Ctx c, List<string> L)
        {
            string txt = "";
            if (ml.ContentType == ContentType.MTextContent && ml.MText != null)
                txt = $"mtext=\"{ml.MText.Contents}\" loc={P(ml.MText.Location)} h={F(ml.MText.TextHeight)}";
            L.Add($"  mleader content={ml.ContentType} {txt} tl={TryS(() => P(ml.TextLocation))} " +
                  $"style={TryS(() => ((MLeaderStyle)c.Tr.GetObject(ml.MLeaderStyle, OpenMode.ForRead)).Name)}");
            try
            {
                foreach (int li in ml.GetLeaderIndexes())
                    foreach (int lni in ml.GetLeaderLineIndexes(li))
                    {
                        var pts = new List<string>();
                        int n = ml.VerticesCount(lni);
                        for (int k = 0; k < n; k++) pts.Add(P(ml.GetVertex(lni, k)));
                        L.Add($"  leader {li}/{lni}: {string.Join(" ", pts)}");
                    }
            }
            catch (System.Exception ex) { L.Add("  leader EXC " + ex.GetType().Name); }
        }

        private static void RenderDim(Dimension dim, Ctx c, List<string> L)
        {
            L.Add($"  dim txt=\"{dim.DimensionText}\" meas={TryF(() => dim.Measurement)} tp={P(dim.TextPosition)} " +
                  $"style={TryS(() => ((DimStyleTableRecord)c.Tr.GetObject(dim.DimensionStyle, OpenMode.ForRead)).Name)}");
            switch (dim)
            {
                case RotatedDimension rd:
                    L.Add($"  rot x1={P(rd.XLine1Point)} x2={P(rd.XLine2Point)} dl={P(rd.DimLinePoint)} r={F(rd.Rotation, 4)}");
                    break;
                case AlignedDimension ad:
                    L.Add($"  al x1={P(ad.XLine1Point)} x2={P(ad.XLine2Point)} dl={P(ad.DimLinePoint)}");
                    break;
                default:
                    L.Add($"  ext={Ext(dim)}");
                    break;
            }
        }

        private static void RenderBlockRef(BlockReference br, Ctx c, List<string> L, int depth)
        {
            var btr = (BlockTableRecord)c.Tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            L.Add($"  block={NormName(btr.Name)} pos={P(br.Position)} rot={F(br.Rotation, 4)} " +
                  $"scale={F(br.ScaleFactors.X, 4)},{F(br.ScaleFactors.Y, 4)},{F(br.ScaleFactors.Z, 4)}");
            foreach (ObjectId aid in br.AttributeCollection)
            {
                if (!(c.Tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar)) continue;
                L.Add($"  attr {ar.Tag}=\"{ar.TextString}\" pos={P(ar.Position)}");
            }
            if (depth >= MaxDepth) { L.Add("  (zagnieżdżenie za głębokie)"); return; }

            // Zawartość BTR — encje posortowane po treści (handle wewnętrzne → kontekst sortowania)
            var inner = new List<string>();
            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                if (!(c.Tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                inner.Add(string.Join("\n", Render(e, c, depth + 1)));
            }
            inner.Sort(StringComparer.Ordinal);
            foreach (var block in inner)
                foreach (var line in block.Split('\n'))
                    L.Add("    | " + line);
        }

        /// <summary>XData encji; barHeadOnly → z RC_SINGLE_BAR tylko [0]–[13] (bar-equiv: [14] pomijane).</summary>
        private static void XData(Entity e, Ctx c, List<string> L, bool barHeadOnly = false)
        {
            var rb = e.XData;
            if (rb == null) return;
            string app = "?";
            int idx = 0;
            foreach (TypedValue tv in rb)
            {
                if (tv.TypeCode == (int)DxfCode.ExtendedDataRegAppName)
                {
                    app = tv.Value?.ToString() ?? "?";
                    idx = 0;
                    L.Add($"  X {app}[0] 1001");
                    idx++;
                    continue;
                }
                if (!(barHeadOnly && app == BarApp && idx >= BarHeadLen))
                    L.Add($"  X {app}[{idx}] {tv.TypeCode}={XVal(tv, c)}");
                idx++;
            }
        }

        // ------------------------------------------------------------------
        // Tryb bar-equiv
        // ------------------------------------------------------------------

        private const string BarApp     = "RC_SINGLE_BAR";
        private const string BarLinkApp = "RC_BAR_LINK";
        private const int    BarHeadLen = 14;

        /// <summary>Pręty w przestrzeni modelu (bar-equiv): polilinie RC_SINGLE_BAR, pręty-bloki v2, towarzysze RC_BAR_LINK.</summary>
        private sealed class BarInfo
        {
            public readonly HashSet<long> Bars = new HashSet<long>();
            public readonly HashSet<long> Companion = new HashSet<long>();
            public readonly Dictionary<long, List<Polyline>> Rings = new Dictionary<long, List<Polyline>>();
            public bool IsBar(Entity e) => Bars.Contains(e.Handle.Value);
        }

        private static bool IsBlockBar(BlockReference br, out TypedValue[] rec)
        {
            rec = null;
            using (var rb = br.GetXDataForApplication(BarApp))
            {
                if (rb == null) return false;
                rec = rb.AsArray();
            }
            return rec.Length > BarHeadLen && rec[BarHeadLen].TypeCode == (short)DxfCode.ExtendedDataInteger16
                   && rec[BarHeadLen].Value is short s && s == 2;
        }

        private static long HandleOf(object v)
        {
            if (v is Handle hh) return hh.Value;
            return v != null && long.TryParse(v.ToString(), NumberStyles.HexNumber, Inv, out long h) ? h : 0;
        }

        /// <summary>Zbiera pręty i towarzyszy; dla prętów-bloków dopisuje asercje „# BARBLOCK …” do outL.</summary>
        private static BarInfo CollectBars(Transaction tr, BlockTableRecord ms, List<string> outL,
                                           Func<Entity, bool> filter = null)
        {
            var info = new BarInfo();
            var links = new List<(Polyline pl, long target)>();
            int ok = 0;
            var fails = new List<string>();
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                if (filter != null && !filter(e)) continue;
                if (e is Polyline pl)
                {
                    using (var rb = pl.GetXDataForApplication(BarApp))
                        if (rb != null) { info.Bars.Add(pl.Handle.Value); continue; }
                    using (var lk = pl.GetXDataForApplication(BarLinkApp))
                    {
                        var v = lk?.AsArray();
                        if (v != null && v.Length >= 2) links.Add((pl, HandleOf(v[1].Value)));
                    }
                }
                else if (e is BlockReference br && IsBlockBar(br, out var rec))
                {
                    info.Bars.Add(br.Handle.Value);
                    string err = CheckBarBlock(tr, br, rec);
                    if (err == null) ok++;
                    else fails.Add($"# BARBLOCK FAIL {Convert.ToString(rec[1].Value, Inv)}: {err}");
                }
            }
            foreach (var (pl, target) in links)
            {
                if (!info.Bars.Contains(target)) continue;   // towarzysz bez pręta-polilinii → zwykła encja
                info.Companion.Add(pl.Handle.Value);
                if (!info.Rings.TryGetValue(target, out var list)) info.Rings[target] = list = new List<Polyline>();
                list.Add(pl);
            }
            outL.Add($"# BARBLOCK OK={ok} FAIL={fails.Count}");
            outL.AddRange(fails);
            return info;
        }

        /// <summary>Asercje pręta-bloku (plan 3.3); null = OK.</summary>
        private static string CheckBarBlock(Transaction tr, BlockReference br, TypedValue[] rec)
        {
            var errs = new List<string>();
            // W4d (runda 2): pręt-blok wtyczki W4d+ ma znacznik geometrii [15] = Int16 2021 — rekord 16
            bool w4dMarker = rec.Length == BarHeadLen + 2 && rec[BarHeadLen + 1].Value is short s15 && s15 >= 2021;
            if (rec.Length != BarHeadLen + 1 && !w4dMarker)
                errs.Add("rekord " + rec.Length.ToString(Inv) + " (oczekiwano 15 albo 16 ze znacznikiem W4d)");
            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            if (!btr.IsAnonymous || !btr.Name.StartsWith("*U", StringComparison.OrdinalIgnoreCase)) errs.Add("BTR " + btr.Name + " nie *U");
            if (btr.Explodable) errs.Add("Explodable=true");
            int refs = btr.GetBlockReferenceIds(true, true).Count;
            if (refs != 1) errs.Add("refs=" + refs.ToString(Inv));
            int n = 0;
            foreach (ObjectId id in btr)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                n++;
                using (var xd = e.XData) if (xd != null) errs.Add("zawartość z XData");
                if (e.Layer != "0") errs.Add("warstwa zawartości " + e.Layer);
                if (e.ColorIndex != 0) errs.Add("kolor zawartości " + e.ColorIndex.ToString(Inv));
                if (!string.Equals(e.Linetype, "ByBlock", StringComparison.OrdinalIgnoreCase)) errs.Add("rodzaj linii zawartości " + e.Linetype);
                if (e.LineWeight != LineWeight.ByBlock) errs.Add("grubość zawartości " + e.LineWeight);
            }
            if (n == 0) errs.Add("pusty BTR");
            return errs.Count == 0 ? null : string.Join("; ", errs.Distinct());
        }

        /// <summary>Pręt kanonicznie (bez typu encji): efektywne właściwości, XData [0]–[13], pierścienie WCS.</summary>
        private static List<string> RenderBar(Entity e, Ctx c)
        {
            var L = new List<string>();
            var rings = new List<(Polyline pl, Matrix3d xf)>();
            string layer = e.Layer, lt = e.Linetype;
            int color = e.ColorIndex;
            double lts = e.LinetypeScale;
            LineWeight lw = e.LineWeight;

            if (e is Polyline pl)
            {
                rings.Add((pl, Matrix3d.Identity));
                if (c.Bars.Rings.TryGetValue(pl.Handle.Value, out var comp))
                    foreach (var cp in comp) rings.Add((cp, Matrix3d.Identity));
            }
            else if (e is BlockReference br)
            {
                var btr = (BlockTableRecord)c.Tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                Entity inner = null;
                foreach (ObjectId id in btr)
                {
                    if (id.IsErased || !(c.Tr.GetObject(id, OpenMode.ForRead) is Entity ie)) continue;
                    if (inner == null) inner = ie;
                    if (ie is Polyline bpl) rings.Add((bpl, br.BlockTransform));
                }
                if (inner != null)
                {
                    if (inner.ColorIndex != 0) color = inner.ColorIndex;
                    if (!string.Equals(inner.Linetype, "ByBlock", StringComparison.OrdinalIgnoreCase)) lt = inner.Linetype;
                    lts = br.LinetypeScale * inner.LinetypeScale;
                    if (inner.LineWeight != LineWeight.ByBlock) lw = inner.LineWeight;
                }
            }

            // Pierwsza linia dostaje prefiks „#n” (poza diffem) — właściwości w osobnej, porównywanej linii
            L.Add("Bar");
            L.Add($"  props L={layer} C={color} LT={lt} LTS={F(lts)} LW={lw} vis={e.Visible}");
            XData(e, c, L, barHeadOnly: true);
            for (int r = 0; r < rings.Count; r++)
            {
                var (rp, xf) = rings[r];
                var cs = xf.CoordinateSystem3d;
                double sign = cs.Xaxis.CrossProduct(cs.Yaxis).DotProduct(cs.Zaxis) < 0 ? -1.0 : 1.0;
                L.Add($"  ring{r} closed={rp.Closed} n={rp.NumberOfVertices}");
                for (int i = 0; i < rp.NumberOfVertices; i++)
                    L.Add($"  r{r}v{i} {P(rp.GetPoint3dAt(i).TransformBy(xf))} b={F(rp.GetBulgeAt(i) * sign, 4)}");
            }
            return L;
        }

        private static string XVal(TypedValue tv, Ctx c)
        {
            object v = tv.Value;
            if (tv.TypeCode == (int)DxfCode.ExtendedDataHandle)
            {
                long h = 0;
                if (v is Handle hh) h = hh.Value;
                else if (v != null) long.TryParse(v.ToString(), NumberStyles.HexNumber, Inv, out h);
                return c.Ref(h);
            }
            switch (v)
            {
                case null: return "null";
                case double d: return F(d);
                case Point3d p: return P(p);
                case Point2d p2: return P2(p2);
                case Vector3d vv: return $"({F(vv.X)},{F(vv.Y)},{F(vv.Z)})";
                case string s: return "\"" + s + "\"";
                case IFormattable f: return f.ToString(null, Inv);
                default: return v.ToString();
            }
        }

        // ------------------------------------------------------------------
        // Formatowanie
        // ------------------------------------------------------------------

        internal static string F(double v, int dec = 2)
        {
            if (double.IsNaN(v)) return "NaN";
            if (double.IsInfinity(v)) return v > 0 ? "+Inf" : "-Inf";
            double r = Math.Round(v, dec, MidpointRounding.AwayFromZero);
            if (r == 0) r = 0.0;   // bez „-0.00”
            return r.ToString("F" + dec, Inv);
        }

        internal static string P(Point3d p) => $"({F(p.X)},{F(p.Y)},{F(p.Z)})";
        private static string P2(Point2d p) => $"({F(p.X)},{F(p.Y)})";

        private static string TryF(Func<double> f)
        {
            try { return F(f()); }
            catch (System.Exception ex) { return "EXC " + ex.GetType().Name; }
        }

        private static string TryS(Func<string> f)
        {
            try { return f(); }
            catch (System.Exception ex) { return "EXC " + ex.GetType().Name; }
        }

        private static string Ext(Entity e)
        {
            try
            {
                var x = e.GeometricExtents;
                return P(x.MinPoint) + ".." + P(x.MaxPoint);
            }
            catch (System.Exception ex) { return "EXC " + ex.GetType().Name; }
        }

        private static string StyleName(Ctx c, ObjectId id)
        {
            if (id.IsNull) return "";
            return TryS(() => ((TextStyleTableRecord)c.Tr.GetObject(id, OpenMode.ForRead)).Name);
        }

        /// <summary>*U12 → *U#; RC_ANNOT_004_3f2a… / RC_SLAB_BARS_004_&lt;guid&gt; → …_#.</summary>
        internal static string NormName(string n)
        {
            if (string.IsNullOrEmpty(n)) return n;
            if (n.StartsWith("*", StringComparison.Ordinal)) return Regex.Replace(n, @"\d+$", "#");
            return Regex.Replace(n, @"_[0-9A-Fa-f]{12,}", "_#");
        }
    }
}
