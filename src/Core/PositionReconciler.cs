using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Pilnuje zasady: JEDEN numer pozycji = JEDEN kształt pręta.
    ///
    /// Gdy pręt zmienia geometrię (stretch / grip / RC_EDIT_BAR), a ten sam numer ma jeszcze
    /// inny pręt o INNYCH wymiarach (typowo: skopiowany pręt, któremu zmieniono długość),
    /// zmieniany pręt dostaje:
    ///   - numer istniejącej pozycji o identycznych wymiarach (jeśli taka jest), albo
    ///   - nowy numer z <see cref="PositionCounter.NextAuto"/>.
    /// Pręt, który jest jedyny w swojej pozycji, zachowuje numer (zmienia się cała pozycja).
    /// UB (01/02) i seria 501+ nie są przenumerowywane.
    /// </summary>
    public static class PositionReconciler
    {
        private const double LenTol = 1.0;   // mm

        /// <summary>
        /// Sprawdza numer pręta po zmianie geometrii. Zwraca nowy Mark, jeśli pręt został
        /// przenumerowany (XData pręta już zapisane), albo null gdy numer zostaje.
        /// </summary>
        public static string ReconcileAfterGeometryChange(Database db, ObjectId barId)
        {
            if (db == null || barId.IsNull || barId.IsErased) return null;

            BarData me;
            var sameNrOthers = new List<BarData>();
            // numer → lista prętów z tym numerem (do szukania identycznej pozycji)
            var byNr = new Dictionary<int, List<BarData>>();

            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var pl = tr.GetObject(barId, OpenMode.ForRead) as Polyline;
                me = pl != null ? SingleBarEngine.ReadBarXData(pl) : null;
                if (me == null) return null;

                int myNr = SingleBarEngine.ExtractPosNr(me.Mark);
                if (myNr < PositionCounter.FirstAutoNumber || myNr >= PositionCounter.SeparateSeriesStart)
                    return null;

                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId oid in ms)
                {
                    if (oid == barId || oid.IsErased) continue;
                    if (!(tr.GetObject(oid, OpenMode.ForRead) is Polyline other)) continue;
                    if (other.GetXDataForApplication(SingleBarEngine.XAppName) == null) continue;
                    var ob = SingleBarEngine.ReadBarXData(other);
                    if (ob == null) continue;

                    int nr = SingleBarEngine.ExtractPosNr(ob.Mark);
                    if (nr <= 0) continue;
                    if (!byNr.TryGetValue(nr, out var list)) byNr[nr] = list = new List<BarData>();
                    list.Add(ob);
                    if (nr == myNr) sameNrOthers.Add(ob);
                }

                // Numer jest tylko mój, albo wszyscy z tym numerem mają te same wymiary → zostaje
                bool conflict = false;
                foreach (var ob in sameNrOthers)
                    if (!SameShape(me, ob)) { conflict = true; break; }
                Log.Info($"PositionReconciler: {me.Mark} A={me.LengthA:F0} innych z tym numerem={sameNrOthers.Count} konflikt={conflict}");
                if (!conflict) return null;
            }

            int oldNr = SingleBarEngine.ExtractPosNr(me.Mark);
            // Seria: dół 03–100, góra 101–499 — nowy numer zawsze z tej samej serii
            bool top = oldNr >= PositionCounter.TopSeriesStart;
            int seriesLo = top ? PositionCounter.TopSeriesStart : PositionCounter.FirstAutoNumber;
            int seriesHi = top ? PositionCounter.SeparateSeriesStart : PositionCounter.TopSeriesStart;

            // 1) istniejąca pozycja o identycznych wymiarach (wszystkie jej pręty zgodne)
            int newNr = 0;
            var keys = new List<int>(byNr.Keys);
            keys.Sort();
            foreach (int nr in keys)
            {
                if (nr == oldNr || nr < seriesLo || nr >= seriesHi) continue;
                bool allSame = true;
                foreach (var ob in byNr[nr])
                    if (!SameShape(me, ob)) { allSame = false; break; }
                if (allSame) { newNr = nr; break; }
            }

            // 2) nowy numer
            if (newNr == 0)
            {
                newNr = top ? PositionCounter.NextAutoTop(db) : PositionCounter.NextAuto(db);
                PositionCounter.Increment(db, newNr);   // seria 101+ nie podbija licznika (ignorowane)
            }

            string newMark = RebuildMark(me.Mark, me.Diameter, newNr);
            Log.Info($"PositionReconciler: {me.Mark} → {newMark}");

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pl = tr.GetObject(barId, OpenMode.ForWrite) as Polyline;
                var bar = pl != null ? SingleBarEngine.ReadBarXData(pl) : null;
                if (bar == null) { tr.Abort(); return null; }
                bar.Mark = newMark;
                SingleBarEngine.WriteXData(pl, bar);
                tr.Commit();
            }
            return newMark;
        }

        /// <summary>
        /// Przenosi Mark / średnicę / długość pręta na rozkłady powiązane z nim przez SourceBarHandle
        /// i przebudowuje je (linie + annotacja). Rozkłady stare, bez SourceBarHandle, są brane po
        /// numerze pozycji tylko gdy <paramref name="legacyPosNr"/> > 0.
        /// Zwraca liczbę zaktualizowanych rozkładów.
        /// </summary>
        public static int PropagateToDistributions(Database db, ObjectId barId, BarData bar, int legacyPosNr = 0)
        {
            string myHandle = barId.Handle.Value.ToString("X8");
            int    newPosNr = SingleBarEngine.ExtractPosNr(bar.Mark);
            var    toRebuild = new List<(ObjectId id, BarData bar)>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId oid in ms)
                {
                    if (oid.IsErased) continue;
                    var brDist = tr.GetObject(oid, OpenMode.ForRead) as BlockReference;
                    if (brDist == null) continue;
                    var barDist = BarBlockEngine.ReadXData(brDist);
                    if (barDist == null) continue;

                    bool mine = XLink.Same(barDist.SourceBarHandle, myHandle);
                    bool legacy = !mine && legacyPosNr > 0
                               && string.IsNullOrEmpty(XLink.Norm(barDist.SourceBarHandle))
                               && SingleBarEngine.ExtractPosNr(barDist.Mark) == legacyPosNr;
                    if (!mine && !legacy) continue;

                    // Odbuduj Mark: zachowaj spacing i suffix, podmień prefix H{dia}-{posNr}
                    var    mp  = (barDist.Mark ?? "").Split(' ');
                    var    cp  = mp[0].Split('-');
                    string sfx = mp.Length > 1 ? " " + string.Join(" ", mp, 1, mp.Length - 1) : "";
                    int distSp = cp.Length >= 3 && int.TryParse(cp[2], out int spParsed)
                        ? spParsed : (int)barDist.Spacing;

                    brDist.UpgradeOpen();
                    barDist.Mark     = BarData.FormatMark(bar.Diameter, newPosNr, distSp, barDist.Count) + sfx;
                    barDist.Diameter = bar.Diameter;
                    barDist.LengthA  = bar.LengthA;
                    BarBlockEngine.WriteXData(brDist, barDist);
                    toRebuild.Add((oid, barDist));
                }
                tr.Commit();
            }

            foreach (var (id, bd) in toRebuild)
            {
                BarBlockEngine.UpdateBarLength(db, id, bar.LengthA);
                AnnotationEngine.SyncAnnotation(db, bd);
            }
            return toRebuild.Count;
        }

        /// <summary>Te same wymiary pręta (średnica, kształt, A–E) z tolerancją 1 mm.</summary>
        public static bool SameShape(BarData a, BarData b)
        {
            if (a == null || b == null) return false;
            if (a.Diameter != b.Diameter) return false;
            if (!string.Equals(a.ShapeCode ?? "00", b.ShapeCode ?? "00", StringComparison.OrdinalIgnoreCase))
                return false;
            return Near(a.LengthA, b.LengthA) && Near(a.LengthB, b.LengthB) && Near(a.LengthC, b.LengthC)
                && Near(a.LengthD, b.LengthD) && Near(a.LengthE, b.LengthE);
        }

        private static bool Near(double x, double y) => Math.Abs(x - y) < LenTol;

        /// <summary>"H12-03" → "H12-07"; zachowuje ewentualny suffix po spacji.</summary>
        private static string RebuildMark(string oldMark, int diameter, int newNr)
        {
            var mp  = (oldMark ?? "").Split(' ');
            string sfx = mp.Length > 1 ? " " + string.Join(" ", mp, 1, mp.Length - 1) : "";
            return BarData.FormatMark(diameter, newNr, 0, 1) + sfx;
        }
    }
}
