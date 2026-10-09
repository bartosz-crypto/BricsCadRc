using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Zapis rekordu RC_SINGLE_BAR (Plan C, etap 2). Jedyny punkt składania nagłówka [0]–[13];
    /// ogon istniejącego rekordu od [14] (FormatVersion i ewentualne nowsze pola) jest zachowywany.
    /// </summary>
    internal static class BarXData
    {
        /// <summary>
        /// Nagłówek [0]–[13] (przeniesiony 1:1 z dawnego SingleBarEngine.WriteXData) + ogon istniejącego rekordu.
        /// formatVersion != null → [14] podmieniony albo dopisany (nigdy zdublowany).
        /// Kształt 44 (W4c) → znacznik [15] ShapeStd = 2020 (BarRecordRules.WithShapeStd).
        /// Encja musi być otwarta do zapisu (bez UpgradeOpen).
        /// </summary>
        public static void Write(Entity e, BarData bar, short? formatVersion = null)
        {
            var head = new[]
            {
                new TypedValue((int)DxfCode.ExtendedDataRegAppName,  SingleBarEngine.XAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Mark ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)bar.Diameter),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.ShapeCode ?? "00"),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.LengthA),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.LengthB),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.LengthC),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.LayerCode ?? "B1"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Position  ?? "BOT"),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.LengthD),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.LengthE),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.TotalLength),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)(bar.LengthOverridden ? 1 : 0)),
                XLink.Write(bar.LabelHandle)                                  // [13] handle 1005
            };

            TypedValue[] existing;
            using (var rb = e.GetXDataForApplication(SingleBarEngine.XAppName))
                existing = rb != null ? rb.AsArray() : new TypedValue[0];

            short version = formatVersion ?? (short)0;   // Int16 (1070) — wartość musi być typu short
            var merged = BarRecordRules.Merge(head, existing, formatVersion.HasValue,
                new TypedValue((int)DxfCode.ExtendedDataInteger16, version));
            // Plan W4c: „44” = kapelusz BS 8666:2020 — znacznik [15] ShapeStd = 2020 (polilinia bez [14] → [14] = 1);
            // rekord „44” bez znacznika to stary pierścień (ReadBarXData → 75).
            // W4d (runda 2): znacznik ≥ 2020 nie jest obniżany (kapelusz z obrysem W4d ma 2021).
            if (BarRecordRules.NeedsShapeStd(bar.ShapeCode))
                merged = BarRecordRules.WithShapeStdAtLeast(merged,
                    new TypedValue((int)DxfCode.ExtendedDataInteger16, BarRecordRules.PolylineFormatVersion),
                    new TypedValue((int)DxfCode.ExtendedDataInteger16, BarRecordRules.ShapeStd2020),
                    BarRecordRules.ShapeStd2020, Std);
            e.XData = new ResultBuffer(merged.ToArray());
        }

        private static int Std(TypedValue t) => BarRecordRules.ShapeStdOf(BarRecordRules.ShapeStdIndex + 1, t.TypeCode, t.Value);

        /// <summary>
        /// W4d (runda 2, D34): znacznik [15] = 2021 „obrys zbudowany geometrią W4d” — wołane tam, gdzie wtyczka buduje obrys
        /// pręta (PlaceBar, RebuildCompanions, pręt-blok: tworzenie i RC_EDIT_BAR). Polilinia bez [14] → [14] = 1. Bez zapisu,
        /// gdy znacznik już jest (≥ 2021), rekord krótszy niż nagłówek albo stary pierścień „44” bez znacznika (zostaje
        /// pierścieniem do migracji na 75). Encja otwarta do zapisu.
        /// </summary>
        public static void MarkW4dGeometry(Entity e)
        {
            TypedValue[] v;
            using (var rb = e.GetXDataForApplication(SingleBarEngine.XAppName))
                v = rb != null ? rb.AsArray() : null;
            if (v == null || v.Length < BarRecordRules.HeadCount) return;
            var t15 = v.Length > BarRecordRules.ShapeStdIndex ? v[BarRecordRules.ShapeStdIndex] : default(TypedValue);
            if (BarRecordRules.IsW4dGeometry(v.Length, t15.TypeCode, t15.Value)) return;
            string code = v[3].Value as string;
            if (BarRecordRules.IsLegacyRing44(code, v.Length, t15.TypeCode, t15.Value)) return;
            var rec = BarRecordRules.WithShapeStd(new List<TypedValue>(v),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, BarRecordRules.PolylineFormatVersion),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, BarRecordRules.ShapeStdW4d));
            e.XData = new ResultBuffer(rec.ToArray());
        }
    }

    /// <summary>Rodzaj encji pręta (RC_SINGLE_BAR).</summary>
    internal enum BarKind { Polyline, Block, Other }

    /// <summary>Zbiór akceptowanych rodzajów pręta — podawany przy każdym wywołaniu.</summary>
    [Flags]
    internal enum BarKinds { Polyline = 1, Block = 2, Other = 4 }

    /// <summary>
    /// Pręt RC_SINGLE_BAR niezależnie od encji: polilinia (v1) albo BlockReference z [14] = 2 (Plan C).
    /// Bezstanowa nakładka na encję (bez cache) — każdy odczyt idzie do bazy.
    /// </summary>
    internal sealed class BarEntity
    {
        public Entity  Entity { get; }
        public BarKind Kind   { get; }

        private BarEntity(Entity entity, BarKind kind)
        {
            Entity = entity;
            Kind   = kind;
        }

        /// <summary>
        /// Rodzaj pręta albo null (brak RC_SINGLE_BAR, BlockReference bez [14] = 2).
        /// Polyline → Polyline, BlockReference z [14] = Int16 2 → Block, inna encja z RC_SINGLE_BAR → Other.
        /// </summary>
        public static BarKind? KindOf(Entity e)
        {
            if (e == null) return null;
            using (var rb = e.GetXDataForApplication(SingleBarEngine.XAppName))
            {
                if (rb == null) return null;
                if (e is Polyline) return BarKind.Polyline;
                if (e is BlockReference)
                {
                    var v = rb.AsArray();
                    if (v.Length <= BarRecordRules.VersionIndex) return null;
                    var tv = v[BarRecordRules.VersionIndex];
                    return BarRecordRules.IsBlockFormat(v.Length, tv.TypeCode, tv.Value) ? BarKind.Block : (BarKind?)null;
                }
                return BarKind.Other;
            }
        }

        /// <summary>Pręt z encji, gdy jej rodzaj jest w accept; inaczej null.</summary>
        public static BarEntity From(Entity e, BarKinds accept)
        {
            var kind = KindOf(e);
            if (kind == null || (accept & ToFlag(kind.Value)) == 0) return null;
            return new BarEntity(e, kind.Value);
        }

        /// <summary>
        /// Otwiera pręt po ObjectId. Encja najpierw do odczytu; do zapisu dopiero, gdy jest prętem z accept
        /// (obca encja nie jest otwierana do zapisu). Null, gdy id puste/usunięte albo to nie pręt z accept.
        /// </summary>
        public static BarEntity Open(Transaction tr, ObjectId id, OpenMode mode, BarKinds accept)
        {
            if (id.IsNull || id.IsErased) return null;
            var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
            var bar = From(e, accept);
            if (bar == null || mode != OpenMode.ForWrite) return bar;
            var ew = (Entity)tr.GetObject(id, OpenMode.ForWrite);
            return new BarEntity(ew, bar.Kind);
        }

        /// <summary>
        /// Pręty z accept w przestrzeni modelu, w kolejności przestrzeni modelu, bez usuniętych (Plan C, etap 3).
        /// Encje otwarte do odczytu w transakcji tr.
        /// </summary>
        public static IEnumerable<BarEntity> ScanModelSpace(Transaction tr, Database db, BarKinds accept)
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                var bar = From(tr.GetObject(id, OpenMode.ForRead) as Entity, accept);
                if (bar != null) yield return bar;
            }
        }

        /// <summary>Pierwsza polilinia zawartości BTR pręta-bloku (obrys główny; dla 44 pierścień zewnętrzny) albo null.</summary>
        private Polyline FirstBtrPolyline(Transaction tr)
        {
            if (!(Entity is BlockReference br)) return null;
            var btr = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            if (btr == null) return null;
            foreach (ObjectId id in btr)
            {
                if (id.IsNull || id.IsErased) continue;
                if (tr.GetObject(id, OpenMode.ForRead) is Polyline bpl) return bpl;
            }
            return null;
        }

        /// <summary>
        /// Pierwszy wierzchołek obrysu w WCS: polilinia — GetPoint3dAt(0) (jak dotychczasowe skany szablonów);
        /// blok — pierwszy wierzchołek pierwszej polilinii BTR × BlockTransform. Brak obrysu → null.
        /// </summary>
        public Point3d? FirstOutlineVertex(Transaction tr)
        {
            if (Kind == BarKind.Polyline && Entity is Polyline pl)
                return pl.NumberOfVertices > 0 ? pl.GetPoint3dAt(0) : (Point3d?)null;
            if (Kind == BarKind.Block && Entity is BlockReference br)
            {
                var bpl = FirstBtrPolyline(tr);
                if (bpl == null || bpl.NumberOfVertices == 0) return null;
                return bpl.GetPoint3dAt(0).TransformBy(br.BlockTransform);
            }
            return null;
        }

        /// <summary>
        /// Wywołuje f na obrysie pręta w WCS: polilinia — ona sama; blok — kopia pierwszej polilinii BTR
        /// przekształcona BlockTransform (GetTransformedCopy, zwalniana po wywołaniu). Brak obrysu → default.
        /// </summary>
        public T WithWcsOutline<T>(Transaction tr, Func<Curve, T> f)
        {
            if (Kind == BarKind.Polyline && Entity is Polyline pl) return f(pl);
            if (Kind == BarKind.Block && Entity is BlockReference br)
            {
                var bpl = FirstBtrPolyline(tr);
                if (bpl == null) return default(T);
                using (var copy = bpl.GetTransformedCopy(br.BlockTransform))
                {
                    return copy is Curve c ? f(c) : default(T);
                }
            }
            return default(T);
        }

        /// <summary>Najbliższy punkt obrysu (GetClosestPointTo, bez przedłużenia) w WCS; brak obrysu → null.</summary>
        public Point3d? ClosestPoint(Transaction tr, Point3d pt)
            => WithWcsOutline(tr, c => (Point3d?)c.GetClosestPointTo(pt, false));

        /// <summary>Punkt obrysu w połowie parametru (jak RC_BAR: GetPointAtParameter(EndParam / 2)); brak obrysu → null.</summary>
        public Point3d? MidPoint(Transaction tr)
            => WithWcsOutline(tr, c => (Point3d?)c.GetPointAtParameter(c.EndParam / 2.0));

        /// <summary>Dane pręta (SingleBarEngine.ReadBarXData — jedyny parser rekordu).</summary>
        public BarData Read() => SingleBarEngine.ReadBarXData(Entity);

        /// <summary>Zapis danych pręta; dla bloku [14] = 2 zachowane. Encja musi być otwarta do zapisu.</summary>
        public void Write(BarData bar)
            => BarXData.Write(Entity, bar, Kind == BarKind.Block ? BarRecordRules.BlockFormatVersion : (short?)null);

        /// <summary>
        /// Wierzchołki obrysu pręta w WCS (Z = 0 dla polilinii, jak dotychczasowy odczyt GetPoint2dAt).
        /// Polilinia: jej wierzchołki. Blok: wierzchołki pierwszej polilinii z BTR przekształcone przez BlockTransform.
        /// Inna encja / brak polilinii w BTR: pusta lista.
        /// </summary>
        public IReadOnlyList<Point3d> OutlineVertices(Transaction tr)
        {
            var result = new List<Point3d>();
            if (Kind == BarKind.Polyline && Entity is Polyline pl)
            {
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    var p = pl.GetPoint2dAt(i);
                    result.Add(new Point3d(p.X, p.Y, 0));
                }
            }
            else if (Kind == BarKind.Block && Entity is BlockReference br)
            {
                var btr = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                if (btr == null) return result;
                var xf = br.BlockTransform;
                foreach (ObjectId id in btr)
                {
                    if (id.IsNull || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Polyline bpl)) continue;
                    for (int i = 0; i < bpl.NumberOfVertices; i++)
                    {
                        var p = bpl.GetPoint2dAt(i);
                        result.Add(new Point3d(p.X, p.Y, 0).TransformBy(xf));
                    }
                    break;
                }
            }
            return result;
        }

        private static BarKinds ToFlag(BarKind k)
            => k == BarKind.Polyline ? BarKinds.Polyline
             : k == BarKind.Block    ? BarKinds.Block
             : BarKinds.Other;
    }
}
