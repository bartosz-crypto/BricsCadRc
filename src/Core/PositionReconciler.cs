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
                // Pręt: polilinia albo pręt-blok v2 (Plan C)
                var meBar = BarEntity.Open(tr, barId, OpenMode.ForRead, BarKinds.Polyline | BarKinds.Block);
                me = meBar != null ? meBar.Read() : null;
                if (me == null) return null;

                int myNr = SingleBarEngine.ExtractPosNr(me.Mark);
                if (myNr < PositionCounter.FirstAutoNumber || myNr >= PositionCounter.SeparateSeriesStart)
                    return null;

                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId oid in ms)
                {
                    if (oid == barId || oid.IsErased) continue;
                    // Inne pręty: polilinie i pręty-bloki v2 (numer zajęty przez blok = konflikt)
                    var other = BarEntity.From(tr.GetObject(oid, OpenMode.ForRead) as Entity,
                                               BarKinds.Polyline | BarKinds.Block);
                    if (other == null) continue;
                    var ob = other.Read();
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
            // Zgłoszenie kolegi 2026-10-16: kopia pręta bez etykiety — widoczna etykieta należy do oryginału i zostaje ze starym numerem
            if (string.IsNullOrEmpty(me.LabelHandle))
                try
                {
                    Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                        $"\n[RC] Pręt przenumerowany {me.Mark} → {newMark}, ale nie ma własnej etykiety (kopia bez opisu) — " +
                        "etykieta obok należy do pręta oryginalnego. Dodaj etykietę temu prętowi.\n");
                }
                catch (System.Exception ex) { Log.Error("PositionReconciler.NoLabel", ex); }

            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Zapis przez BarEntity — dla pręta-bloku zachowuje [14] = 2
                var be = BarEntity.Open(tr, barId, OpenMode.ForWrite, BarKinds.Polyline | BarKinds.Block);
                var bar = be != null ? be.Read() : null;
                if (bar == null) { tr.Abort(); return null; }
                bar.Mark = newMark;
                be.Write(bar);
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
            var    toRebuild = new List<(ObjectId id, BarData bar, double len)>();

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
                    // 2026-10-29: opis bez rozstawu (ShowSpacing = false, fix122) zostaje bez rozstawu; Any — rozstaw w opisie
                    // mimo jednego pręta w bloku (jak RunAnnotationFlow)
                    if (!barDist.ShowSpacing) distSp = 0;
                    int markCount = BarBlockEngine.IsAny(barDist) ? Math.Max(2, barDist.EffectiveCount) : barDist.Count;

                    brDist.UpgradeOpen();
                    barDist.Mark     = BarData.FormatMark(bar.Diameter, newPosNr, distSp, markCount) + sfx;
                    barDist.Diameter = bar.Diameter;
                    // długość w rzucie: strzemię 51 / 63 — bok B (szerokość), inaczej A. Kierunek widoku Manual (bok wskazany
                    // przez użytkownika, 2026-10-29) — długość bez zmian
                    // Any — LengthA to wymiar A kształtu (nie długość w rzucie): A i kształt z pręta, przebudowa z wymiarów pręta
                    bool anyDist = BarBlockEngine.IsAny(barDist);
                    double len = anyDist ? bar.LengthA
                               : barDist.ViewingDirection == "Manual" && barDist.LengthA > 0 ? barDist.LengthA
                               : PlanLengthOf(bar);
                    if (anyDist) barDist.ShapeCode = bar.ShapeCode;
                    barDist.LengthA  = len;
                    BarBlockEngine.WriteXData(brDist, barDist);
                    toRebuild.Add((oid, barDist, len));
                }
                tr.Commit();
            }

            foreach (var (id, bd, len) in toRebuild)
            {
                if (BarBlockEngine.IsAny(bd)) BarBlockEngine.RebuildBars(db, id);   // Any: kształt z wymiarów pręta (BuildAny)
                else BarBlockEngine.UpdateBarLength(db, id, len);
                AnnotationEngine.SyncAnnotation(db, bd);
            }
            return toRebuild.Count;
        }

        /// <summary>
        /// Długość pręta w rzucie rozkładu: strzemię belki / wspornika (kształt 51 / 63, kod BL) — bok B (szerokość; A to wysokość),
        /// inaczej A (błąd PLOT 344-346, 2026-10-28: zmiana B szablonu 500 → 510 dawała w rzucie 425).
        /// </summary>
        public static double PlanLengthOf(BarData bar)
        {
            string code = bar?.ShapeCode ?? "00";
            // 2026-10-29: każde strzemię 51 / 63 (P9: B = szerokość), nie tylko kod BL — jak BarCommands.GetViewLength
            if ((code == "51" || code == "63") && bar.LengthB > 0) return bar.LengthB;
            return bar?.LengthA ?? 0;
        }

        /// <summary>
        /// Numery (Mark) innych prętów (polilinie i pręty-bloki, poza <paramref name="exceptId"/>) z numerem pozycji
        /// <paramref name="nr"/>, ale o INNYM kształcie / wymiarach niż <paramref name="candidate"/> (zasada: jeden numer = jeden
        /// kształt; 2026-10-29: RC_EDIT_BAR z ręcznie zmienionym numerem nie sprawdzał tego). Pusta lista — numer wolny albo
        /// zajęty przez identyczne pręty. Tylko odczyt.
        /// </summary>
        public static List<string> ShapeConflicts(Database db, int nr, BarData candidate, ObjectId exceptId)
        {
            var res = new List<string>();
            if (db == null || nr <= 0 || candidate == null) return res;
            try
            {
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    foreach (var be in BarEntity.ScanModelSpace(tr, db, BarKinds.Polyline | BarKinds.Block))
                    {
                        if (be.Entity.ObjectId == exceptId) continue;
                        var ob = be.Read();
                        if (ob == null || SingleBarEngine.ExtractPosNr(ob.Mark) != nr) continue;
                        if (!SameShape(candidate, ob) && !res.Contains(ob.Mark)) res.Add(ob.Mark);
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex) { Log.Error("PositionReconciler.ShapeConflicts", ex); }
            return res;
        }

        /// <summary>
        /// Kontrola całego rysunku (2026-10-29, zgłoszenie Bartka: H12-11 kształt 21 i H16-11 kształt 00): numery pozycji, pod
        /// którymi są pręty o różnych kształtach / wymiarach — komunikat na każdy taki numer (RC_BBS, RC_SCHEDULE). Tylko odczyt.
        /// </summary>
        public static List<string> NumberConflicts(Database db)
        {
            var res = new List<string>();
            if (db == null) return res;
            try
            {
                var byNr = new SortedDictionary<int, List<BarData>>();
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    foreach (var be in BarEntity.ScanModelSpace(tr, db, BarKinds.Polyline | BarKinds.Block))
                    {
                        var ob = be.Read();
                        int nr = ob != null ? SingleBarEngine.ExtractPosNr(ob.Mark) : 0;
                        if (nr <= 0) continue;
                        if (!byNr.TryGetValue(nr, out var list)) byNr[nr] = list = new List<BarData>();
                        list.Add(ob);
                    }
                    tr.Commit();
                }
                foreach (var kv in byNr)
                {
                    // różne kształty pod tym numerem (reprezentant każdej grupy identycznych prętów)
                    var kinds = new List<BarData>();
                    foreach (var b in kv.Value)
                        if (!kinds.Exists(k => SameShape(k, b))) kinds.Add(b);
                    if (kinds.Count < 2) continue;
                    var parts = new List<string>();
                    foreach (var k in kinds)
                        parts.Add($"{k.Mark} (kształt {k.ShapeCode ?? "00"}, A={k.LengthA:F0})");
                    res.Add($"Pozycja {kv.Key:D2}: różne pręty pod jednym numerem — {string.Join(", ", parts)}. " +
                            "Zmień numer jednego z nich (RC_EDIT_BAR).");
                }
            }
            catch (System.Exception ex) { Log.Error("PositionReconciler.NumberConflicts", ex); }
            return res;
        }

        /// <summary>Te same wymiary pręta (średnica, kształt, A–E) z tolerancją 1 mm.</summary>
        public static bool SameShape(BarData a, BarData b)
        {
            if (a == null || b == null) return false;
            if (a.Diameter != b.Diameter) return false;
            if (!string.Equals(a.ShapeCode ?? "00", b.ShapeCode ?? "00", StringComparison.OrdinalIgnoreCase))
                return false;
            // Plan W4c: tylko wymiary kształtu (Take(Parameters.Length)) — np. pierścień 75 zmigrowany z „44” może mieć
            // w polach C–E stare wartości bez znaczenia. Nieznany kształt → wszystkie pola A–E (jak dotąd).
            var shape = ShapeCodeLibrary.Get(a.ShapeCode ?? "00");
            int n = shape?.Parameters?.Length ?? 5;
            double[] pa = a.ParamValues, pb = b.ParamValues;
            for (int i = 0; i < n && i < pa.Length; i++)
                if (!Near(pa[i], pb[i])) return false;
            return true;
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
