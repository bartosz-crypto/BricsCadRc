using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Plan C, etap 3 — silnik pręta-bloku dla wszystkich kształtów (uogólnienie TestBarBlockEngine z etapu 1).
    ///
    /// Pręt = BlockReference do własnego BTR (domyślnie anonimowy *U, Explodable = false). Zawartość BTR = obrys
    /// pręta (dla 44 dwa pierścienie) z SingleBarEngine.BuildVisualEntities, warstwa 0, kolor / rodzaj linii /
    /// grubość ByBlock, bez XData. XData RC_SINGLE_BAR [0]–[13] + [14] = 2 tylko na referencji (BarXData.Write).
    ///
    /// Układ lokalny BTR: oś pręta zaczyna się w (0,0) — punkt wstawienia = axis[0] w WCS (geometria od LocalStart
    /// = −lPts[0]). Wyjątek: kształt 44 — referencja w punkcie wstawienia polilinii, geometria jak
    /// BuildVisualEntities z Origin (0-POPRAWKI p.3). Przebudowa zawartości nie rusza Position → axis[0] w miejscu;
    /// wyjątek: pierścień przy zmianie A — zostaje środek (poprawka #5, Rebuild przesuwa Position).
    /// </summary>
    internal static class SingleBarBlock
    {
        /// <summary>BTR pręta anonimowy (*U) — decyzja planu (p. 2); PASTE/INSERT dają zawsze nową kopię.</summary>
        internal const bool UseAnonymousBtr = true;

        /// <summary>Prefiks nazwanych BTR prętów testowych etapu 1 (RC_TEST_BARBLOCK bez opcji *U).</summary>
        internal const string LegacyNamePrefix = "RC_BAR_TEST_";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Wynik zapisu długości (grip / STRETCH końca).</summary>
        internal sealed class ApplyResult
        {
            public bool   Applied;
            public bool   Rejected;        // stan pręta niezgodny z oczekiwanym (odrzucone)
            public bool   TooShort;        // odcinek < max(2d, 50) — zmiana cofnięta (jak wzorzec)
            public bool   NotLengthShape;  // kształt inny niż 00/99 — długość tylko przez RC_EDIT_BAR
            public double OldLength;
            public double NewLength;
            public double AttemptedLength; // długość po zaokrągleniu, także gdy odrzucona
            public bool   NewBtr;
            public bool   LengthOverridden; // TotalLength nadpisana ręcznie — bez zmian (ostrzeżenie u wywołującego)
            public double TotalLength;      // TotalLength po zapisie
            public string Handle  = "";
            public string BtrName = "";
            public string Mark    = "";
        }

        // ----------------------------------------------------------------
        // Kształt i oś
        // ----------------------------------------------------------------

        /// <summary>Kształt pręta; nieznany → "00" (jak SingleBarEngine.PlaceBar).</summary>
        internal static BarShape ShapeOf(BarData bar)
            => ShapeCodeLibrary.Get(bar?.ShapeCode) ?? ShapeCodeLibrary.Get("00");

        /// <summary>Początek geometrii w układzie lokalnym BTR: −lPts[0] (≠ 44) albo (0,0) (44).</summary>
        internal static Vector3d LocalStart(BarData bar)
        {
            var (x, y) = BarLengthRules.LocalStart(ShapeOf(bar).Code, bar.ParamValues, bar.Diameter);
            return new Vector3d(x, y, 0);
        }

        /// <summary>Grip [0]: punkt wstawienia bloku (axis[0] w WCS; dla 44 punkt wstawienia polilinii).</summary>
        internal static Point3d AxisStartWcs(BlockReference br)
            => Point3d.Origin.TransformBy(br.BlockTransform);

        /// <summary>Punkty osi pręta (BarGeometryBuilder.GetLocalPoints + LocalStart) w WCS.</summary>
        internal static List<Point3d> AxisPointsWcs(BlockReference br, BarData bar)
        {
            var result = new List<Point3d>();
            var shape = ShapeOf(bar);
            var lPts = BarGeometryBuilder.GetLocalPoints(shape.Code, bar.ParamValues, bar.Diameter);
            if (lPts == null) return result;
            var ls = LocalStart(bar);
            var xf = br.BlockTransform;
            foreach (var p in lPts)
                result.Add(new Point3d(p.X + ls.X, p.Y + ls.Y, 0).TransformBy(xf));
            return result;
        }

        /// <summary>Ostatni punkt osi w WCS (dla 00/99: (LengthA, 0) lokalnie).</summary>
        internal static Point3d AxisEndWcs(BlockReference br, BarData bar)
        {
            var pts = AxisPointsWcs(br, bar);
            return pts.Count > 0 ? pts[pts.Count - 1] : AxisStartWcs(br);
        }

        /// <summary>Przesunięcie WCS → układ lokalny bloku (.X = wzdłuż osi pręta 00/99).</summary>
        internal static Vector3d ToLocal(BlockReference br, Vector3d wcsOffset)
            => wcsOffset.TransformBy(br.BlockTransform.Inverse());

        /// <summary>Kształty, których długość zmienia grip / STRETCH końca: 00 i 99.</summary>
        internal static bool IsLengthGripShape(string shapeCode)
        {
            string c = shapeCode ?? "00";
            return c == "00" || c == "99";
        }

        // ----------------------------------------------------------------
        // Geometria BTR
        // ----------------------------------------------------------------

        /// <summary>
        /// Obrys pręta w układzie lokalnym BTR (od LocalStart, kierunek +X), wg bar.ShapeCode. Bez XData.
        /// Gdy db != null: warstwa 0 i rodzaj linii ByBlock przez ObjectId tej bazy.
        /// Kolor i grubość ByBlock, skala rodzaju linii 1 — zawsze.
        /// </summary>
        internal static List<Entity> BuildEntities(BarData bar, Database db = null)
        {
            var shape = ShapeOf(bar);
            var ls = LocalStart(bar);
            var ents = SingleBarEngine.BuildVisualEntities(shape, bar.ParamValues, bar.Diameter,
                                                          new Point3d(ls.X, ls.Y, 0), Vector3d.XAxis);
            foreach (var ent in ents)
            {
                if (db != null)
                {
                    ent.LayerId    = db.LayerZero;
                    ent.LinetypeId = db.ByBlockLinetype;
                }
                ent.ColorIndex    = 0;                      // ByBlock
                ent.LineWeight    = LineWeight.ByBlock;
                ent.LinetypeScale = 1.0;
            }
            return ents;
        }

        /// <summary>Usuwa encje BTR i dodaje obrys dla bieżących danych pręta.</summary>
        internal static void RebuildContents(Transaction tr, BlockTableRecord btr, BarData bar)
        {
            var db = btr.Database;
            var ents = BuildEntities(bar, db);
            if (ents.Count == 0)
                throw new InvalidOperationException($"Pręt {bar.Mark} (kształt {bar.ShapeCode}): brak geometrii obrysu.");
            if (!btr.IsWriteEnabled) btr.UpgradeOpen();
            foreach (ObjectId id in btr)
            {
                if (id.IsNull || id.IsErased) continue;
                var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                ent?.Erase();
            }
            foreach (var ent in ents)
            {
                btr.AppendEntity(ent);
                tr.AddNewlyCreatedDBObject(ent, true);
            }
        }

        /// <summary>Pierwsza wolna nazwa RC_BAR_TEST_001… (tylko nazwane BTR prętów testowych etapu 1).</summary>
        internal static string NextFreeName(BlockTable bt)
        {
            for (int i = 1; i < 100000; i++)
            {
                string name = LegacyNamePrefix + i.ToString("D3", Inv);
                if (!bt.Has(name)) return name;
            }
            return LegacyNamePrefix + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        }

        private static ObjectId NewBtr(Transaction tr, Database db, bool anonymous, bool explodable, out string name)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
            name = anonymous ? "*U" : NextFreeName(bt);
            var btr = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
            btr.Explodable = explodable;
            var id = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);
            name = btr.Name;
            return id;
        }

        /// <summary>
        /// Copy-on-write: jeśli BTR pręta ma więcej niż jedną referencję (COPY, ARRAY, kopia bez wtyczki),
        /// pręt dostaje nowy BTR (anonimowy, gdy stary był anonimowy; inaczej następna wolna nazwa RC_BAR_TEST_nnn),
        /// z tą samą flagą Explodable. Zawartość wypełnia potem RebuildContents. Zwraca true, gdy utworzono nowy BTR.
        /// </summary>
        internal static bool EnsureOwnBtr(Transaction tr, Database db, BlockReference br)
        {
            var oldId = br.BlockTableRecord;
            if (oldId.IsNull) return false;
            var oldBtr = tr.GetObject(oldId, OpenMode.ForRead) as BlockTableRecord;
            if (oldBtr == null) return false;

            var refIds = oldBtr.GetBlockReferenceIds(true, true);
            if (refIds.Count <= 1) return false;

            var newId = NewBtr(tr, db, oldBtr.IsAnonymous, oldBtr.Explodable, out _);
            if (!br.IsWriteEnabled) br.UpgradeOpen();
            br.BlockTableRecord = newId;
            return true;
        }

        /// <summary>
        /// Przebudowa pręta-bloku z danych bar: ZAWSZE EnsureOwnBtr (copy-on-write) → RebuildContents →
        /// RecordGraphicsModified. XData zapisuje wywołujący (BarXData.Write / BarEntity.Write).
        /// Zwraca true, gdy pręt dostał nowy BTR.
        /// </summary>
        internal static bool Rebuild(Transaction tr, BlockReference br, BarData bar)
        {
            // Poprawka #5 (0-POPRAWKI p.2 planu W1/W2): pierścień ma środek lokalny (A/2, A/2) — przy zmianie A środek
            // zostaje w miejscu (punkt wstawienia przesunięty w układzie bloku: obrót i odbicie uwzględnione).
            // Stary środek z obecnej zawartości BTR (przed copy-on-write); zawartość nie-pierścieniowa (zmiana kształtu
            // na pierścień) → bez przesunięcia (pierścień zaczyna się w axis[0] starego pręta, jak polilinia).
            Vector3d? ringShift = RingCenterShift(tr, br, bar);
            // W4h runda 2: stary 14 / 15 / 24 z rzutem > ramię — komunikat [RC AUTO] (obrys sprzed przebudowy)
            if (bar.Mark != TestBarBlockEngine.TestMark)
                SingleBarEngine.WarnSlopeClampOnRebuild(bar, BarEntity.From(br, BarKinds.Block)?.OutlineVertices(tr));

            bool newBtr = EnsureOwnBtr(tr, br.Database, br);
            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
            RebuildContents(tr, btr, bar);
            if (!br.IsWriteEnabled) br.UpgradeOpen();
            if (ringShift.HasValue)
                br.Position = br.Position + ringShift.Value.TransformBy(br.BlockTransform);
            br.RecordGraphicsModified(true);
            // W4d (rundy 2–3, D34): BTR przebudowany geometrią W4d → znacznik [15] = 2021 — jedno miejsce dla wszystkich
            // ścieżek przebudowy bloku (RC_EDIT_BAR, RC_UPDATE_BAR / RepairScale, naprawa po SCALE, grip końca).
            // Wyjątki jak w polilinii: pierścień (geometria bez zmian w W4d) i pręt testowy H12-T.
            if (!SingleBarEngine.IsRingShape(bar) && bar.Mark != TestBarBlockEngine.TestMark)
                BarXData.MarkW4dGeometry(br);
            return newBtr;
        }

        /// <summary>
        /// Przesunięcie lokalne (stary środek pierścienia − nowy środek (A/2, A/2)) albo null: pręt nie-pierścień,
        /// obecna zawartość nie jest pierścieniem, przesunięcie pomijalne (≤ 1e-9 — np. RepairScale bez zmiany A).
        /// </summary>
        private static Vector3d? RingCenterShift(Transaction tr, BlockReference br, BarData bar)
        {
            if (!SingleBarEngine.IsRingShape(bar) || br.BlockTableRecord.IsNull) return null;
            if (!(tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) is BlockTableRecord oldBtr)) return null;
            foreach (ObjectId id in oldBtr)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl)) continue;
                var pts = new List<(double X, double Y)>(pl.NumberOfVertices);
                for (int i = 0; i < pl.NumberOfVertices; i++) { var p = pl.GetPoint2dAt(i); pts.Add((p.X, p.Y)); }
                if (!BarFrameRules.TryFitRing(pts, out var c, out _)) return null;
                var ls = LocalStart(bar);
                double half = (bar.ParamValues.Length > 0 ? bar.ParamValues[0] : bar.Diameter) / 2.0;
                var shift = new Vector3d(c.X - (ls.X + half), c.Y - (ls.Y + half), 0);
                return shift.Length > 1e-9 ? shift : (Vector3d?)null;
            }
            return null;
        }

        // ----------------------------------------------------------------
        // Skala (Plan C, etap 3d / 3e2): pręt-blok zawsze w skali ±1
        // ----------------------------------------------------------------

        /// <summary>|skala| = 1 na każdej osi (znak dowolny — MIRROR daje −1), tolerancja 1e-6.</summary>
        internal static bool IsUnitScale(Scale3d s, double tol = 1e-6)
            => Math.Abs(Math.Abs(s.X) - 1.0) <= tol
            && Math.Abs(Math.Abs(s.Y) - 1.0) <= tol
            && Math.Abs(Math.Abs(s.Z) - 1.0) <= tol;

        /// <summary>Skala (±1, ±1, 1) — znak X i Y zachowany (odbicie zostaje), Z = 1.</summary>
        internal static Scale3d UnitScale(Scale3d s)
            => new Scale3d(s.X < 0 ? -1.0 : 1.0, s.Y < 0 ? -1.0 : 1.0, 1.0);

        /// <summary>
        /// Naprawa skali pręta-bloku: ScaleFactors → (±1, ±1, 1) ze znakiem, punkt wstawienia (axis[0]) bez zmian,
        /// potem Rebuild z danych bar (copy-on-write). XData bez zmian. Zwraca true, gdy pręt dostał nowy BTR.
        /// </summary>
        internal static bool RepairScale(Transaction tr, BlockReference br, BarData bar)
        {
            if (!br.IsWriteEnabled) br.UpgradeOpen();
            var pos = br.Position;
            br.ScaleFactors = UnitScale(br.ScaleFactors);
            if (br.Position.DistanceTo(pos) > 1e-9) br.Position = pos;   // punkt wstawienia = axis[0] zostaje
            return Rebuild(tr, br, bar);
        }

        /// <summary>Skala do komunikatów: „2” (jednakowa) albo „(2; 2; 1)”. InvariantCulture.</summary>
        internal static string ScaleText(Scale3d s)
            => Math.Abs(s.X - s.Y) < 1e-9 && Math.Abs(s.Y - s.Z) < 1e-9
                ? F(s.X)
                : "(" + F(s.X) + "; " + F(s.Y) + "; " + F(s.Z) + ")";

        // ----------------------------------------------------------------
        // Tworzenie
        // ----------------------------------------------------------------

        /// <summary>
        /// Pręt-blok w miejscu, w którym SingleBarEngine.PlaceBar narysowałby polilinię (insertPt = punkt startowy
        /// BuildVisualEntities). Referencja w insertPt + lPts[0] (= axis[0]); dla 44 w insertPt.
        /// Wywoływane z PlaceBar po EnsureAppIdRegistered / EnsureLayersExist. Jedna transakcja.
        /// </summary>
        internal static ObjectId Create(Database db, BarData bar, Point3d insertPt)
        {
            var flat = new Point3d(insertPt.X, insertPt.Y, 0);     // polilinia pręta: wierzchołki 2D, elewacja 0
            var ls = LocalStart(bar);
            var position = flat - ls;                              // ≠ 44: insertPt + lPts[0]; 44: LocalStart = 0
            return CreateCore(db, bar, position, 0.0, UseAnonymousBtr, out _, markW4d: true);
        }

        /// <summary>
        /// Wstawia pręt-blok: nowy BTR (Explodable = false) z obrysem, BlockReference w position (= axis[0] WCS)
        /// z obrotem rotation, bez SetDatabaseDefaults (jak polilinia PlaceBar): warstwa LeaderLayer, kolor ByLayer,
        /// grubość 0,00; XData BarXData.Write(br, bar, 2).
        /// </summary>
        internal static ObjectId CreateCore(Database db, BarData bar, Point3d position, double rotation,
                                            bool anonymous, out string btrName, bool markW4d = false)
        {
            var ents = BuildEntities(bar, db);
            if (ents.Count == 0)
                throw new InvalidOperationException($"Pręt {bar.Mark} (kształt {bar.ShapeCode}): brak geometrii obrysu.");

            using var tr = db.TransactionManager.StartTransaction();
            var btrId = NewBtr(tr, db, anonymous, false, out btrName);
            var btr   = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);
            foreach (var ent in ents)
            {
                btr.AppendEntity(ent);
                tr.AddNewlyCreatedDBObject(ent, true);
            }

            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var br = new BlockReference(position, btrId);
            br.Rotation   = rotation;
            br.Layer      = LayerManager.LeaderLayer;
            br.ColorIndex = 256;                              // ByLayer
            br.LineWeight = LineWeight.LineWeight000;         // jak pręt-polilinia (SingleBarEngine.PlaceBar)
            var id = space.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
            BarXData.Write(br, bar, BarRecordRules.BlockFormatVersion);
            if (markW4d) BarXData.MarkW4dGeometry(br);   // W4d (runda 2): pręt-blok wtyczki (nie pręt testowy H12-T)
            tr.Commit();
            return id;
        }

        // ----------------------------------------------------------------
        // Zmiana długości końcem (grip / STRETCH; tylko z CommandEnded)
        // ----------------------------------------------------------------

        /// <summary>
        /// Zmiana długości pręta 00/99 przesunięciem końca o delta (rzut na oś, na zewnątrz = +) — reguły
        /// BarLengthRules (5 mm, minimum max(2d, 50), TotalLength wg kształtu, chyba że LengthOverridden).
        /// atStart: przesuwa się początek (Position), koniec stoi; inaczej początek stoi.
        /// expectedLength / expectedTransform — stan pręta z chwili dragu; niezgodność = odrzucenie.
        /// Zapis: BarEntity.Write (XData [14] = 2) + Rebuild (copy-on-write). Numer pozycji, rozkłady i etykietę
        /// aktualizuje wywołujący (krok 3f: BarChangePropagator.Run z SingleBarGripOverrule).
        /// </summary>
        internal static ApplyResult ApplyEndLength(Database db, ObjectId id, bool atStart, double delta,
                                                   double? expectedLength = null, Matrix3d? expectedTransform = null)
        {
            var res = new ApplyResult();
            using var tr = db.TransactionManager.StartTransaction();
            var be = BarEntity.Open(tr, id, OpenMode.ForWrite, BarKinds.Block);
            var br = be?.Entity as BlockReference;
            var bar = be?.Read();
            if (br == null || bar == null) { tr.Commit(); return res; }

            res.Handle    = br.Handle.ToString();
            res.Mark      = bar.Mark ?? "";
            res.OldLength = bar.LengthA;
            res.NewLength = bar.LengthA;
            res.LengthOverridden = bar.LengthOverridden;
            res.TotalLength      = bar.TotalLength;

            if ((expectedLength.HasValue && Math.Abs(expectedLength.Value - bar.LengthA) > 1e-6)
                || (expectedTransform.HasValue && !SameTransform(expectedTransform.Value, br.BlockTransform)))
            {
                res.Rejected = true;
                tr.Commit();
                return res;
            }
            if (!IsLengthGripShape(bar.ShapeCode))
            {
                res.NotLengthShape = true;
                tr.Commit();
                return res;
            }

            res.AttemptedLength = BarLengthRules.RoundToStep(bar.LengthA + delta);
            double? newLen = BarLengthRules.StretchedLength(bar.LengthA, delta, bar.Diameter);
            if (!newLen.HasValue)
            {
                res.TooShort = true;
                tr.Commit();
                return res;
            }
            res.NewLength = newLen.Value;
            if (Math.Abs(newLen.Value - bar.LengthA) < 1e-6)
            {
                tr.Commit();
                return res;
            }

            double shift = atStart ? bar.LengthA - newLen.Value : 0.0;
            if (Math.Abs(shift) > 1e-9)
                br.Position = new Point3d(shift, 0, 0).TransformBy(br.BlockTransform);

            bar.LengthA = newLen.Value;
            if (!bar.LengthOverridden)
            {
                try { bar.TotalLength = BarLengthRules.TotalLengthFor(bar.ShapeCode, bar.ParamValues, bar.Diameter); }
                catch (System.Exception ex) { Log.Error("SingleBarBlock.ApplyEndLength.TotalLength", ex); }
            }
            be.Write(bar);
            res.TotalLength = bar.TotalLength;
            res.NewBtr = Rebuild(tr, br, bar);
            res.BtrName = (tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord)?.Name ?? "";
            tr.Commit();
            res.Applied = true;
            return res;
        }

        /// <summary>Porównanie przekształceń bloku po obrazach punktów (0,0,0), (1,0,0), (0,1,0).</summary>
        internal static bool SameTransform(Matrix3d a, Matrix3d b, double tol = 1e-6)
        {
            var pts = new[] { Point3d.Origin, new Point3d(1, 0, 0), new Point3d(0, 1, 0) };
            foreach (var p in pts)
                if (p.TransformBy(a).DistanceTo(p.TransformBy(b)) > tol) return false;
            return true;
        }

        // ----------------------------------------------------------------
        // Info
        // ----------------------------------------------------------------

        /// <summary>Opis pręta-bloku do linii poleceń (opcja Info). Liczby w InvariantCulture.</summary>
        internal static string Describe(Transaction tr, BlockReference br)
        {
            var sb = new StringBuilder();
            var btr = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            int refs = btr != null ? btr.GetBlockReferenceIds(true, true).Count : 0;
            sb.Append("\n[RC TEST] Uchwyt: ").Append(br.Handle.ToString());
            sb.Append("\n[RC TEST] BTR: ").Append(btr?.Name ?? "?")
              .Append("  IsAnonymous=").Append(btr != null && btr.IsAnonymous ? "tak" : "nie")
              .Append("  Explodable=").Append(btr != null && btr.Explodable ? "tak" : "nie")
              .Append("  referencje=").Append(refs.ToString(Inv));

            var rb = br.GetXDataForApplication(SingleBarEngine.XAppName);
            var v  = rb?.AsArray() ?? new TypedValue[0];
            var bar = SingleBarEngine.ReadBarXData(br);
            string ver = v.Length > BarRecordRules.VersionIndex
                ? Convert.ToString(v[BarRecordRules.VersionIndex].Value, Inv) : "brak";
            sb.Append("\n[RC TEST] XData: Mark=").Append(bar?.Mark ?? "?")
              .Append("  kształt=").Append(bar?.ShapeCode ?? "?")
              .Append("  LengthA=").Append(bar != null ? F(bar.LengthA) : "?")
              .Append("  TotalLength=").Append(bar != null ? F(bar.TotalLength) : "?")
              .Append("  [14]=").Append(ver)
              .Append("  długość rekordu=").Append(v.Length.ToString(Inv));

            // Długość z geometrii BTR (zakres X obrysów w układzie lokalnym)
            double minX = double.MaxValue, maxX = double.MinValue;
            int n = 0;
            if (btr != null)
            {
                foreach (ObjectId id in btr)
                {
                    if (id.IsNull || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    try
                    {
                        var ext = ent.GeometricExtents;
                        minX = Math.Min(minX, ext.MinPoint.X);
                        maxX = Math.Max(maxX, ext.MaxPoint.X);
                        n++;
                    }
                    catch (System.Exception ex) { Log.Error("SingleBarBlock.Describe.Extents", ex); }
                }
            }
            sb.Append("\n[RC TEST] Geometria BTR: encje=").Append(n.ToString(Inv))
              .Append("  długość=").Append(n > 0 ? F(maxX - minX) : "?")
              .Append(n > 0 ? "  X od " + F(minX) + " do " + F(maxX) : "");

            var p = br.Position;
            var s = br.ScaleFactors;
            sb.Append("\n[RC TEST] Position=(").Append(F(p.X)).Append(", ").Append(F(p.Y)).Append(", ").Append(F(p.Z)).Append(")")
              .Append("  Rotation=").Append((br.Rotation * 180.0 / Math.PI).ToString("0.###", Inv)).Append("°")
              .Append("  ScaleFactors=(").Append(s.X.ToString("0.###", Inv)).Append(", ")
              .Append(s.Y.ToString("0.###", Inv)).Append(", ").Append(s.Z.ToString("0.###", Inv)).Append(")");
            return sb.ToString();
        }

        internal static string F(double d) => d.ToString("0.###", Inv);
    }
}
