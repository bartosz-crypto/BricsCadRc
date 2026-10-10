using System;
using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Automat domykania nibu (POPRAWKI_WZORCA 23, plan docs/PLAN_NIB_GARAZE_2026-10-09.md, część A).
    /// Gdy płyta nie ma zamkniętego nibu (<see cref="NibDetector.Detect"/> == null), a przy krawędzi leży OTWARTA linia
    /// uskoku (polilinie / linie / łuki na warstwie obrysu, końce na obrysie) — linia jest domykana krawędzią obrysu
    /// (<see cref="NibLoopBuilder"/>) i zapisywana jako zamknięta polilinia na warstwie obrysu; użyte kawałki są usuwane.
    /// Zamknięty nib — bez zmian (bramka). Zapis tylko w komendzie (Prepare GA, AutoRebar, RC_SECTION), w jej grupie UNDO.
    /// Bez XLink i bez stanu statycznego.
    /// </summary>
    public static class NibAutoClose
    {
        /// <summary>
        /// Próba domknięcia nibu obrysu <paramref name="outlineId"/>. pieceIds = kandydaci (null = skan przestrzeni modelu:
        /// Line / Arc / otwarta Polyline na warstwie obrysu). tag = znakowanie nowej polilinii (null = kopia XData RC_GA
        /// pierwszego użytego kawałka). Komunikaty (bez prefiksu komendy) dopisywane do msgs. true = nib domknięty.
        /// Nie otwiera transakcji zewnętrznej — wołać poza transakcją (po Commit), w zablokowanym dokumencie.
        /// </summary>
        public static bool TryClose(Database db, ObjectId outlineId, IList<ObjectId> pieceIds, Action<Entity> tag, List<string> msgs)
        {
            var r = Analyze(db, outlineId, pieceIds, out var ids, out string layer, out double elevation);
            if (r == null) return false;
            if (!r.Ok)
            {
                if (r.InBand == 0) return false;   // brak linii uskoku przy krawędzi — cisza
                msgs.Add("Nib: nie domknięto — " + (r.Problems.FirstOrDefault() ?? "nieznany powód.") + " Płyta bez nibu.");
                foreach (var p in r.Problems.Skip(1)) msgs.Add("Nib: " + p);
                return false;
            }
            return Write(db, outlineId, r, ids, layer, elevation, tag, msgs);
        }

        /// <summary>
        /// Bezpiecznik przekrojów (RC_SECTION_CHECK): czy automat DOMKNĄŁBY nib tego obrysu (otwarta linia uskoku przy krawędzi,
        /// <see cref="TryClose"/> zakończyłby się zapisem). Tylko odczyt — bez zapisu i bez komunikatów.
        /// </summary>
        public static bool WouldClose(Database db, ObjectId outlineId)
        {
            var r = Analyze(db, outlineId, null, out _, out _, out _);
            return r != null && r.Ok;
        }

        /// <summary>
        /// Część tylko do odczytu automatu: bramka (zamknięty nib → null), kawałki na warstwie obrysu, NibLoopBuilder.Build.
        /// null = nic do zrobienia (brak obrysu / nib zamknięty / brak kawałków).
        /// </summary>
        private static NibLoopBuilder.Result Analyze(Database db, ObjectId outlineId, IList<ObjectId> pieceIds,
                                                     out List<ObjectId> ids, out string layer, out double elevation)
        {
            ids = new List<ObjectId>();
            layer = null; elevation = 0;
            if (db == null || outlineId.IsNull || outlineId.IsErased) return null;
            if (NibDetector.Detect(db, outlineId) != null) return null;   // bramka: zamknięty nib — bez zmian

            List<(double X, double Y)> outline; List<double> outlineBulges;
            Extents3d oext;
            var pieces = new List<NibLoopBuilder.Piece>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                if (!(tr.GetObject(outlineId, OpenMode.ForRead) is Polyline pl)) return null;
                if (!GeometryHelper.IsEffectivelyClosed(pl) || pl.Normal.Z < 0.999) return null;
                outline = new List<(double X, double Y)>(); outlineBulges = new List<double>();
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    var p = pl.GetPoint2dAt(i);
                    outline.Add((p.X, p.Y));
                    outlineBulges.Add(pl.GetBulgeAt(i));
                }
                layer = pl.Layer; elevation = pl.Elevation;
                oext = Ext(outline);

                IEnumerable<ObjectId> cand = pieceIds;
                if (cand == null)
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    cand = ms.Cast<ObjectId>();
                }
                double margin = NibLoopBuilder.DefaultGapTol + 1.0;
                foreach (var id in cand)
                {
                    if (id.IsNull || id.IsErased || id == outlineId) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    if (!string.Equals(e.Layer, layer, StringComparison.OrdinalIgnoreCase)) continue;
                    var piece = ToPiece(e);
                    if (piece == null) continue;
                    var px = Ext(piece.Points);
                    if (px.MaxPoint.X < oext.MinPoint.X - margin || px.MinPoint.X > oext.MaxPoint.X + margin ||
                        px.MaxPoint.Y < oext.MinPoint.Y - margin || px.MinPoint.Y > oext.MaxPoint.Y + margin) continue;
                    ids.Add(id);
                    pieces.Add(piece);
                }
                tr.Commit();
            }
            if (pieces.Count == 0) return null;

            return NibLoopBuilder.Build(outline, outlineBulges, pieces, NibDetector.MinNibWidth, NibDetector.MaxNibWidth);
        }

        private static bool Write(Database db, ObjectId outlineId, NibLoopBuilder.Result r, List<ObjectId> ids, string layer,
                                  double elevation, Action<Entity> tag, List<string> msgs)
        {
            // Zapis: jedna transakcja — nowa zamknięta polilinia, użyte kawałki usunięte
            var used = r.Used.Select(i => ids[i]).ToList();
            ObjectId newId;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var first = (Entity)tr.GetObject(used[0], OpenMode.ForRead);
                var pl = new Polyline();
                pl.SetDatabaseDefaults(db);
                pl.Layer = layer;
                pl.Color = first.Color;
                pl.LinetypeId = first.LinetypeId;
                pl.LinetypeScale = first.LinetypeScale;
                pl.LineWeight = first.LineWeight;
                for (int i = 0; i < r.Loop.Count; i++)
                    pl.AddVertexAt(i, new Point2d(r.Loop[i].X, r.Loop[i].Y), r.LoopBulges[i], 0, 0);
                pl.Closed = true;
                pl.Elevation = elevation;
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                newId = ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                if (tag != null) tag(pl);
                else
                {
                    using (var rb = first.GetXDataForApplication(GaImportEngine.XApp))
                        if (rb != null) pl.XData = rb;
                }
                foreach (var id in used) tr.GetObject(id, OpenMode.ForWrite).Erase();
                tr.Commit();
            }

            // Kontrola po zapisie: NibDetector musi rozpoznać nib, inaczej wycofanie
            if (NibDetector.Detect(db, outlineId) != null)
            {
                msgs.Add($"Nib: domknięto linię uskoku krawędzią obrysu płyty (kawałków linii: {used.Count}, zamknięta polilinia: {r.Loop.Count} wierzch.).");
                return true;
            }
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    tr.GetObject(newId, OpenMode.ForWrite).Erase();
                    foreach (var id in used)
                    {
                        var o = tr.GetObject(id, OpenMode.ForWrite, true);
                        if (o.IsErased) o.Erase(false);
                    }
                    tr.Commit();
                }
                Log.Error("NibAutoClose.TryClose", new InvalidOperationException(
                    $"Pętla nibu (obrys {outlineId.Handle}, {r.Loop.Count} wierzch.) nie rozpoznana przez NibDetector po zapisie — wycofano."));
            }
            catch (System.Exception ex) { Log.Error("NibAutoClose.TryClose.Rollback", ex); }
            msgs.Add("Nib: nie domknięto — zamknięta linia uskoku nie została rozpoznana jako nib (szczegóły w logu). Płyta bez nibu.");
            return false;
        }

        /// <summary>
        /// Domknięcie nibu w komendzie korzystającej z nibu (AutoRebar: Mesh, B1, B2, T1, T2, UB*; RC_SECTION) — po zebraniu
        /// wszystkich wskazań. Komunikaty w linii poleceń z prefiksem; błąd → Log.Error, komenda idzie dalej (płyta bez nibu).
        /// </summary>
        public static bool EnsureForCommand(Document doc, ObjectId slabId, string prefix)
        {
            if (doc == null || slabId.IsNull) return false;
            var msgs = new List<string>();
            bool ok = false;
            try
            {
                using (doc.LockDocument())
                    ok = TryClose(doc.Database, slabId, null, null, msgs);
            }
            catch (System.Exception ex)
            {
                Log.Error("NibAutoClose.EnsureForCommand", ex);
                msgs.Add("Nib: błąd domykania linii uskoku — " + ex.Message + " Płyta bez nibu.");
            }
            foreach (var m in msgs) doc.Editor.WriteMessage($"\n{prefix} {m}");
            return ok;
        }

        // ── kawałki ─────────────────────────────────────────────────────

        /// <summary>Line / Arc / otwarta Polyline (normalna +Z) → kawałek; inne → null.</summary>
        private static NibLoopBuilder.Piece ToPiece(Entity e)
        {
            switch (e)
            {
                case Line ln:
                    return new NibLoopBuilder.Piece(new[] { (ln.StartPoint.X, ln.StartPoint.Y), (ln.EndPoint.X, ln.EndPoint.Y) });
                case Arc arc when arc.Normal.Z >= 0.999:
                {
                    double sweep = arc.EndAngle - arc.StartAngle;
                    while (sweep <= 0) sweep += 2 * Math.PI;
                    while (sweep > 2 * Math.PI) sweep -= 2 * Math.PI;
                    return new NibLoopBuilder.Piece(
                        new[] { (arc.StartPoint.X, arc.StartPoint.Y), (arc.EndPoint.X, arc.EndPoint.Y) },
                        new[] { Math.Tan(sweep / 4.0) });
                }
                case Polyline pl when !pl.Closed && pl.NumberOfVertices >= 2 && pl.Normal.Z >= 0.999:
                {
                    var pts = new List<(double X, double Y)>();
                    var bs = new List<double>();
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                    {
                        var p = pl.GetPoint2dAt(i);
                        pts.Add((p.X, p.Y));
                        if (i < pl.NumberOfVertices - 1) bs.Add(pl.GetBulgeAt(i));
                    }
                    return new NibLoopBuilder.Piece(pts, bs);
                }
                default:
                    return null;
            }
        }

        private static Extents3d Ext(IList<(double X, double Y)> v)
            => new Extents3d(new Point3d(v.Min(p => p.X), v.Min(p => p.Y), 0), new Point3d(v.Max(p => p.X), v.Max(p => p.Y), 0));
    }
}
