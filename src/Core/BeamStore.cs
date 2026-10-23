using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Belki / pogrubienia na rysunku (sekcja 4i ARCHITECTURE.md, docs/PLAN_BELKI_2026-10-10.md).
    ///   • Strefa belki = zamknięta polilinia na warstwie <see cref="LayerName"/> z XData <see cref="App"/>:
    ///     [1] FormatVersion (Int16) [2] H [3] szerokość [4] szer. nibu [5] wys. nibu (Real) [6] obrys płyty (1005, XLink)
    ///     [7] Ø strzemion (Int16) [8] rozstaw strzemion (Real) [9] Ø dołem [10] liczba dołem [11] Ø górą [12] liczba górą
    ///     [13] Ø U-barów (Int16) [14] kod strzemienia (tekst) [15] zakład [16] maks. długość (Real)
    ///     [17] góra strzemienia pod: 2 = obiema warstwami T, 1 = jedną (Int16)
    ///     [18] Ø prętów bocznych (Int16, 0 = brak) [19] rozstaw pionowy prętów bocznych (Real) [20] Ø pręta w nibie górą
    ///     [21] Ø pręta w nibie dołem (Int16, 0 = brak) — E4b.
    ///     [22] otulina boczna / czołowa strzemion [23] otulina dolna (Real) — decyzja użytkownika 2026-10-10 (45 / 50); rekord bez
    ///     tych pól (sprzed decyzji) czytany z NOWYMI domyślnymi 45 / 50 (nie dawnymi 40 / 40 — świadomie: belki generowane ponownie
    ///     dostają nowe otuliny).
    ///     v2 (K2, docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md; [1] = 2): [24] otulina górna strzemienia (Real; A1 35 / A2 50)
    ///     [25] tryb prętów bocznych (Int16; 1 B1 brak, 2 B2 zawsze, 3 B3 od progu) [26] próg H prętów bocznych (Real)
    ///     [27] krotność zakładu (Real; zakład = Ceil25(krotność · Ø)) [28] rozstaw nominalny w poprzek w opisie (Real)
    ///     [29] B+T jedną pozycją (Int16 0 / 1) [30] obniżenie U-barów drugiego kierunku (Real) [31] pręty proste w kwadrat narożnika
    ///     (Int16 0 / 1) [32] koniec pręta prostego przy U-barze od lica (Real; 0 = auto) [33] strzemiona przy UB 03 +100 (Int16 0 / 1)
    ///     [34] strzemiona w kwadracie narożnika (Int16 0 / 1) [35] naddatek ramienia U-bara końca (Real)
    ///     [36] K9: pręty belki w detalu 1:25 (Int16 1, domyślne; brak pola = 1) albo na rzucie 1:50 (0).
    ///     [37] cięcie strzemion (Int16; decyzja użytkownika 2026-10-11): 0 = Auto (EN 1992-1-1 §9.2.2(8); brak pola = Auto),
    ///     2 = 2-cięte, 4 = 4-cięte (dwa identyczne zachodzące strzemiona jedną pozycją).
    ///     [38] nib przy belce (Int16; decyzja użytkownika 2026-10-11 „opcja dodania lub nie dodawania nibu”): 0 = wg geometrii (brak pola),
    ///     1 = belka bez nibu (lico zewnętrzne = krawędź płyty — BeamGeometry.ZoneWithoutNib; bez UB 03 / T IN NIB / prętów w nibie przy belce).
    ///     [39] GB poza płytą (Real; decyzja 2026-10-23): obniżenie góry belki względem SSL (SSL − TOC), 0 = brak (brak pola).
    ///     Od v2: [15] = Ceil25(krotność · Ø dołem) (tylko dla starej wtyczki), [17] nieużywane (zapis bez zmian).
    ///     Rekord v1 ([1] &lt; 2) — migracja w pamięci (BeamParams.MigrateV1: nowe domyślne + komunikat), zapis przy OK w RC_BEAM
    ///     albo przy generowaniu.
    ///     Nowe pola tylko na końcu; odczyt toleruje krótsze rekordy (brak pola = wartość domyślna BeamParams).
    ///   • Obiekty wygenerowane dla belki (rozkłady RC_BAR_BLOCK) — XData <see cref="TagApp"/> [1] = handle strefy (1005).
    ///     Ponowne generowanie usuwa tylko obiekty tej strefy; kopia samych prętów (CloneRemap → "") — test punktu w strefie.
    /// Bezstanowe, bez cache (UNDO / REDO).
    /// </summary>
    public static class BeamStore
    {
        public const string LayerName = "RC-BEAM";
        public const string App = "RC_BEAM";
        public const string TagApp = "RC_AUTOREBAR_BEAM";
        public const short FormatVersion = BeamParams.CurrentFormat;
        public const int SlabIdx = 6;     // RC_BEAM [6] obrys płyty (CloneRemap)
        public const int TagIdx = 1;      // RC_AUTOREBAR_BEAM [1] strefa (CloneRemap)

        public static bool IsBeamLayer(string layer) => string.Equals(layer, LayerName, StringComparison.OrdinalIgnoreCase);

        /// <summary>Strefa belki: zamknięta polilinia z rekordem RC_BEAM.</summary>
        public static bool IsZone(Entity e)
        {
            if (!(e is Polyline)) return false;
            using (var rb = e.GetXDataForApplication(App)) return rb != null;
        }

        // ── zapis / odczyt rekordu strefy ───────────────────────────────

        public static ResultBuffer Build(BeamParams p, string slabHandle)
            => new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, App),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, FormatVersion),          // [1]
                new TypedValue((int)DxfCode.ExtendedDataReal, p.Height),                    // [2]
                new TypedValue((int)DxfCode.ExtendedDataReal, p.Width),                     // [3]
                new TypedValue((int)DxfCode.ExtendedDataReal, p.NibWidth),                  // [4]
                new TypedValue((int)DxfCode.ExtendedDataReal, p.NibHeight),                 // [5]
                XLink.Write(slabHandle),                                                    // [6]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.StirrupDia),    // [7]
                new TypedValue((int)DxfCode.ExtendedDataReal, p.StirrupSpacing),            // [8]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.BottomDia),     // [9]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.BottomCount),   // [10]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.TopDia),        // [11]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.TopCount),      // [12]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.UBarDia),       // [13]
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, p.StirrupShape ?? "51"), // [14]
                new TypedValue((int)DxfCode.ExtendedDataReal, p.LapFor(p.BottomDia)),       // [15] dla starej wtyczki (v2)
                new TypedValue((int)DxfCode.ExtendedDataReal, p.MaxLength),                 // [16]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.StirrupTopLayers), // [17]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.SideDia),       // [18] E4b
                new TypedValue((int)DxfCode.ExtendedDataReal, p.SideSpacing),               // [19]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.NibTopDia),     // [20]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.NibBottomDia),  // [21]
                new TypedValue((int)DxfCode.ExtendedDataReal, p.CoverSide),                 // [22] otulina boczna / czołowa
                new TypedValue((int)DxfCode.ExtendedDataReal, p.CoverBottom),               // [23] otulina dolna
                new TypedValue((int)DxfCode.ExtendedDataReal, p.CoverTop),                  // [24] v2: otulina górna strzemienia
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.SideMode),      // [25] tryb prętów bocznych
                new TypedValue((int)DxfCode.ExtendedDataReal, p.SideThreshold),             // [26] próg H prętów bocznych
                new TypedValue((int)DxfCode.ExtendedDataReal, p.LapMultiple),               // [27] krotność zakładu
                new TypedValue((int)DxfCode.ExtendedDataReal, p.AcrossNominal),             // [28] rozstaw nominalny w poprzek
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(p.BothOneMark ? 1 : 0)),   // [29] B+T jedną pozycją
                new TypedValue((int)DxfCode.ExtendedDataReal, p.UBarDrop),                  // [30] obniżenie U-barów 2. kierunku
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(p.CornerStraightIntoSquare ? 1 : 0)), // [31]
                new TypedValue((int)DxfCode.ExtendedDataReal, p.StraightEndOffset),         // [32] 0 = auto
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(p.StirrupsAtUb03 ? 1 : 0)),  // [33]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(p.CornerSquareLinks ? 1 : 0)), // [34]
                new TypedValue((int)DxfCode.ExtendedDataReal, p.UBarArmExtra),              // [35] naddatek ramienia U-bara
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(p.DetailView ? 1 : 0)),   // [36] K9: 1 = detal 1:25, 0 = rzut
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)BeamParams.NormalizeLegs(p.StirrupLegs)),   // [37] 0 Auto / 2 / 4
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(p.NoNib ? 1 : 0)),   // [38] nib: 0 wg geometrii / 1 bez nibu
                new TypedValue((int)DxfCode.ExtendedDataReal, p.TopDrop));                  // [39] GB: obniżenie góry belki (SSL − TOC)

        /// <summary>Zapis rekordu strefy (RegApp rejestrowany); encja otwarta do zapisu w transakcji tr.</summary>
        public static void Write(Transaction tr, Database db, Entity zone, BeamParams p, string slabHandle)
        {
            EnsureApp(tr, db, App);
            if (!zone.IsWriteEnabled) zone.UpgradeOpen();
            zone.XData = Build(p, slabHandle);
        }

        /// <summary>
        /// Parametry strefy (null gdy brak RC_BEAM); krótsze rekordy — wartości domyślne. Rekord v1 ([1] &lt; 2, wersje testowe) —
        /// migracja w pamięci (<see cref="BeamParams.MigrateV1"/>: MigratedFromV1 + MigrationNote); zapis wywołującego (OK w RC_BEAM,
        /// generowanie).
        /// </summary>
        public static BeamParams Read(Entity e)
        {
            TypedValue[] v;
            using (var rb = e?.GetXDataForApplication(App)) v = rb?.AsArray();
            if (v == null) return null;
            var p = new BeamParams();
            double D(int i, double def) => v.Length > i ? Num(v[i], def) : def;
            int I(int i, int def) => v.Length > i ? (int)Math.Round(Num(v[i], def)) : def;
            p.Height = D(2, p.Height);
            p.Width = D(3, p.Width);
            p.NibWidth = D(4, p.NibWidth);
            p.NibHeight = D(5, p.NibHeight);
            p.StirrupDia = I(7, p.StirrupDia);
            p.StirrupSpacing = D(8, p.StirrupSpacing);
            p.BottomDia = I(9, p.BottomDia);
            p.BottomCount = I(10, p.BottomCount);
            p.TopDia = I(11, p.TopDia);
            p.TopCount = I(12, p.TopCount);
            p.UBarDia = I(13, p.UBarDia);
            if (v.Length > 14) { string s = Convert.ToString(v[14].Value, CultureInfo.InvariantCulture); if (!string.IsNullOrWhiteSpace(s)) p.StirrupShape = s.Trim(); }
            p.Lap = D(15, p.Lap);
            p.MaxLength = D(16, p.MaxLength);
            p.StirrupTopLayers = I(17, p.StirrupTopLayers);
            p.SideDia = I(18, p.SideDia);
            p.SideSpacing = D(19, p.SideSpacing);
            p.NibTopDia = I(20, p.NibTopDia);
            p.NibBottomDia = I(21, p.NibBottomDia);
            p.CoverSide = D(22, p.CoverSide);       // brak pola (rekord sprzed 2026-10-10) → nowe domyślne 45 / 50
            p.CoverBottom = D(23, p.CoverBottom);
            int ver = I(1, 1);
            if (ver < BeamParams.CurrentFormat) return BeamParams.MigrateV1(p);
            // v2
            p.CoverTop = D(24, p.CoverTop);
            p.SideMode = I(25, p.SideMode);
            p.SideThreshold = D(26, p.SideThreshold);
            p.LapMultiple = D(27, p.LapMultiple);
            p.AcrossNominal = D(28, p.AcrossNominal);
            p.BothOneMark = I(29, p.BothOneMark ? 1 : 0) != 0;
            p.UBarDrop = D(30, p.UBarDrop);
            p.CornerStraightIntoSquare = I(31, p.CornerStraightIntoSquare ? 1 : 0) != 0;
            p.StraightEndOffset = D(32, p.StraightEndOffset);
            p.StirrupsAtUb03 = I(33, p.StirrupsAtUb03 ? 1 : 0) != 0;
            p.CornerSquareLinks = I(34, p.CornerSquareLinks ? 1 : 0) != 0;
            p.UBarArmExtra = D(35, p.UBarArmExtra);
            p.DetailView = I(36, 1) != 0;              // K9: brak pola (rekord sprzed K9) = detal
            p.StirrupLegs = BeamParams.NormalizeLegs(I(37, BeamParams.LegsAuto));   // brak pola = Auto (decyzja 2026-10-11)
            p.NoNib = I(38, 0) == 1;                   // brak pola = wg geometrii (decyzja 2026-10-11 „opcja nibu”); inna wartość = wg geometrii
            p.TopDrop = Math.Max(0, D(39, 0));         // brak pola = 0 (belka z górą na SSL)
            p.Lap = p.LapFor(p.BottomDia);
            return p;
        }

        /// <summary>RC_BEAM [6] — handle obrysu płyty („X8” albo "").</summary>
        public static string ReadSlab(Entity e)
        {
            using (var rb = e?.GetXDataForApplication(App))
            {
                var v = rb?.AsArray();
                return v != null && v.Length > SlabIdx ? XLink.Read(v[SlabIdx]) : "";
            }
        }

        /// <summary>
        /// Zmiana tylko [6] (obrys płyty) w istniejącym rekordzie, reszta bez zmian (także wersja [1]) — np. kopia strefy
        /// z rekordem v1 na drugi rzut: rekord v2 zapisuje dopiero OK w RC_BEAM albo generowanie. False — brak rekordu.
        /// </summary>
        public static bool WriteSlab(Transaction tr, Database db, Entity zone, string slabHandle)
        {
            TypedValue[] v;
            using (var rb = zone?.GetXDataForApplication(App)) v = rb?.AsArray();
            if (v == null || v.Length <= SlabIdx) return false;
            v[SlabIdx] = XLink.Write(slabHandle);
            EnsureApp(tr, db, App);
            if (!zone.IsWriteEnabled) zone.UpgradeOpen();
            using (var nrb = new ResultBuffer(v)) zone.XData = nrb;
            return true;
        }

        private static double Num(TypedValue tv, double def)
        {
            try
            {
                switch (tv.Value)
                {
                    case short s: return s;
                    case int i: return i;
                    case double d: return d;
                    case string str: return NumberParser.TryParseDouble(str, out double x) ? x : def;
                    default: return def;
                }
            }
            catch (System.Exception ex) { Log.Error("BeamStore.Num", ex); return def; }
        }

        // ── znacznik obiektów wygenerowanych ────────────────────────────

        /// <summary>RC_AUTOREBAR_BEAM [1] — handle strefy („X8”), "" (kopia bez strefy) albo null (brak znacznika).</summary>
        public static string ReadTag(Entity e)
        {
            using (var rb = e?.GetXDataForApplication(TagApp))
            {
                var v = rb?.AsArray();
                if (v == null) return null;
                return v.Length > TagIdx ? XLink.Read(v[TagIdx]) : "";
            }
        }

        public static void Tag(Transaction tr, Database db, Entity e, string zoneHandle)
        {
            EnsureApp(tr, db, TagApp);
            if (!e.IsWriteEnabled) e.UpgradeOpen();
            e.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, TagApp),
                XLink.Write(zoneHandle));
        }

        /// <summary>
        /// Rozkłady belki (kody belki — BeamGeometry.IsBeamCode: BM / BU / BB / BT / BL / BS / BN) należące do strefy: znacznik = strefa; znacznik pusty / brak — punkt wstawienia
        /// w wielokącie strefy (kopia samych prętów). Zwraca pary (rozkład, opis).
        /// </summary>
        public static List<(ObjectId distId, ObjectId annotId)> FindGenerated(Transaction tr, Database db, string zoneHandle,
                                                                             List<Point2d> zonePoly, Func<string, bool> codeFilter = null)
        {
            var res = new List<(ObjectId, ObjectId)>();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                var bar = BarBlockEngine.ReadXData(br);
                if (bar == null || !BeamGeometry.IsBeamCode(bar.LayerCode)) continue;
                if (codeFilter != null && !codeFilter(bar.LayerCode)) continue;
                string tag = ReadTag(br);
                bool mine = !string.IsNullOrEmpty(tag)
                    ? XLink.Same(tag, zoneHandle)
                    : zonePoly != null && GeometryHelper.IsPointInsidePolygon(zonePoly, MidOf(br, bar));
                if (!mine) continue;
                res.Add((id, AnnotOf(db, bar)));
            }
            return res;
        }

        /// <summary>
        /// Rozkłady płyty (kod spoza belki — UB 03 przy belce, decyzja 2026-10-11) narysowane w detalu 1:25 strefy: znacznik
        /// RC_AUTOREBAR_BEAM = strefa. Usuwane przy ponownym Prepare GA razem z detalem (Beam ich nie usuwa — generuje je ponownie
        /// GenerateNibUBLayer). Zwraca pary (rozkład, opis).
        /// </summary>
        public static List<(ObjectId distId, ObjectId annotId)> FindDetailSlabDists(Transaction tr, Database db, string zoneHandle)
        {
            var res = new List<(ObjectId, ObjectId)>();
            if (string.IsNullOrEmpty(zoneHandle)) return res;
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                var bar = BarBlockEngine.ReadXData(br);
                if (bar == null || BeamGeometry.IsBeamCode(bar.LayerCode)) continue;
                string tag = ReadTag(br);
                if (string.IsNullOrEmpty(tag) || !XLink.Same(tag, zoneHandle)) continue;
                res.Add((id, AnnotOf(db, bar)));
            }
            return res;
        }

        /// <summary>Środek rozkładu (do testu przynależności): punkt wstawienia + połowa długości / rozpiętości.</summary>
        internal static Point2d MidOf(BlockReference br, BarData bar)
        {
            bool alongX = !string.Equals(bar.Direction, "Y", StringComparison.OrdinalIgnoreCase);
            double a = bar.LengthA / 2, s = bar.BarsSpan / 2;
            return alongX ? new Point2d(br.Position.X + a, br.Position.Y + s) : new Point2d(br.Position.X + s, br.Position.Y + a);
        }

        private static ObjectId AnnotOf(Database db, BarData bar)
        {
            if (string.IsNullOrEmpty(bar.AnnotHandle)) return ObjectId.Null;
            try
            {
                if (XLink.TryParse(bar.AnnotHandle, out long h) && h != 0 && db.TryGetObjectId(new Handle(h), out ObjectId aid))
                    return aid;
            }
            catch (System.Exception ex) { Log.Error("BeamStore.AnnotOf", ex); }
            return ObjectId.Null;
        }

        /// <summary>
        /// Ponowne RC_PRZYGOTUJ_GA: rozkłady belek wskazujące usuwane strefy (handle) albo bez znacznika w ich wielokącie
        /// — usunięte razem z opisami. Wywoływane PRZED usunięciem stref (wielokąty czytane z nich). Zwraca liczbę rozkładów.
        /// </summary>
        public static int EraseGeneratedForZones(Database db, List<ObjectId> zoneIds)
        {
            if (zoneIds == null || zoneIds.Count == 0) return 0;
            var all = new List<(ObjectId distId, ObjectId annotId)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (var zid in zoneIds)
                {
                    if (zid.IsErased || !(tr.GetObject(zid, OpenMode.ForRead) is Polyline pl)) continue;
                    var poly = GeometryHelper.GetPolylineVertices(pl);
                    foreach (var pair in FindGenerated(tr, db, pl.Handle.ToString(), poly).Concat(FindDetailSlabDists(tr, db, pl.Handle.ToString())))
                        if (!all.Any(x => x.distId == pair.distId)) all.Add(pair);
                }
                tr.Commit();
            }
            // K9: obiekty detali 1:25 tych stref (ANCHOR, linie, pale, teksty, tytuł, odnośniki na rzutach)
            EraseDetail(db, zoneIds);
            if (all.Count == 0) return 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var (d, a) in all)
                {
                    try
                    {
                        if (!d.IsNull && !d.IsErased) tr.GetObject(d, OpenMode.ForWrite).Erase();
                        if (!a.IsNull && !a.IsErased) tr.GetObject(a, OpenMode.ForWrite).Erase();
                    }
                    catch (System.Exception ex) { Log.Error("BeamStore.EraseGeneratedForZones", ex); }
                }
                tr.Commit();
            }
            return all.Count;
        }

        // ── K9: detal belki 1:25 — obiekty detalu niebędące prętami ────
        // XData RC_BEAM_DETAIL: [1] FormatVersion (Int16, 1) [2] strefa (1005, XLink) [3] numer detalu (Int16) [4] rola (tekst):
        // ANCHOR (kopia polilinii strefy — miejsce detalu), LINE (przycięte linie betonu), PILE (pal w oknie), TEXT („xxxTHK”),
        // TITLE („DETAIL 'n'” / „SCALE 1:25”), FRAME (ramka detalu = okno, czerwona przerywana — jak detal otworu; decyzja 2026-10-12),
        // PLANREF (na rzucie: ramka okna detalu i odnośnik „DETAIL 'n'”). Pręty detalu — zwykłe rozkłady z RC_AUTOREBAR_BEAM.

        public const string DetailApp = "RC_BEAM_DETAIL";
        public const string DetailLayer = "RC-BEAM-DETAIL";
        public const short DetailFormat = 1;
        public const int DetailZoneIdx = 2;   // RC_BEAM_DETAIL [2] strefa (CloneRemap)
        public const string RoleAnchor = "ANCHOR", RoleLine = "LINE", RolePile = "PILE", RoleText = "TEXT", RoleTitle = "TITLE",
                            RolePlanRef = "PLANREF", RoleFrame = "FRAME";

        public static void TagDetail(Transaction tr, Database db, Entity e, string zoneHandle, int nr, string role)
        {
            EnsureApp(tr, db, DetailApp);
            if (!e.IsWriteEnabled) e.UpgradeOpen();
            e.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, DetailApp),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, DetailFormat),     // [1]
                XLink.Write(zoneHandle),                                              // [2]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)nr),        // [3]
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, role ?? ""));    // [4]
        }

        /// <summary>Rekord RC_BEAM_DETAIL: strefa („X8” albo ""), numer, rola; null — obiekt nie jest częścią detalu belki.</summary>
        public static (string zone, int nr, string role)? ReadDetail(Entity e)
        {
            TypedValue[] v;
            using (var rb = e?.GetXDataForApplication(DetailApp)) v = rb?.AsArray();
            if (v == null) return null;
            string zone = v.Length > DetailZoneIdx ? XLink.Read(v[DetailZoneIdx]) : "";
            int nr = v.Length > 3 ? (int)Math.Round(Num(v[3], 0)) : 0;
            string role = v.Length > 4 ? Convert.ToString(v[4].Value, CultureInfo.InvariantCulture) : "";
            return (zone, nr, role);
        }

        /// <summary>Obiekty detalu strefy (RC_BEAM_DETAIL [2] = strefa) w przestrzeni modelu.</summary>
        public static List<(ObjectId id, int nr, string role)> FindDetail(Transaction tr, Database db, string zoneHandle)
        {
            var res = new List<(ObjectId, int, string)>();
            if (string.IsNullOrEmpty(zoneHandle)) return res;
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                var d = ReadDetail(e);
                if (d == null || string.IsNullOrEmpty(d.Value.zone) || !XLink.Same(d.Value.zone, zoneHandle)) continue;
                res.Add((id, d.Value.nr, d.Value.role));
            }
            return res;
        }

        /// <summary>
        /// Usuwa obiekty detali 1:25 stref (handle „X8”) — ponowne Beam, przełączenie [36] detal → rzut, ponowne Prepare GA. Najpierw tylko
        /// odczyt (bez zapisu, gdy detali brak). Zwraca liczbę usuniętych obiektów.
        /// </summary>
        public static int EraseDetail(Database db, IEnumerable<string> zoneHandles)
        {
            var hs = (zoneHandles ?? Enumerable.Empty<string>()).Where(h => !string.IsNullOrEmpty(h)).ToList();
            if (hs.Count == 0) return 0;
            var ids = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    var d = ReadDetail(e);
                    if (d != null && !string.IsNullOrEmpty(d.Value.zone) && hs.Any(h => XLink.Same(h, d.Value.zone))) ids.Add(id);
                }
                tr.Commit();
            }
            if (ids.Count == 0) return 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    try { if (!id.IsErased) tr.GetObject(id, OpenMode.ForWrite).Erase(); }
                    catch (System.Exception ex) { Log.Error("BeamStore.EraseDetail", ex); }
                }
                tr.Commit();
            }
            return ids.Count;
        }

        /// <summary>Wersja dla ObjectId stref (handle).</summary>
        public static int EraseDetail(Database db, IEnumerable<ObjectId> zoneIds)
            => EraseDetail(db, (zoneIds ?? Enumerable.Empty<ObjectId>()).Where(z => !z.IsNull).Select(z => z.Handle.ToString()));

        /// <summary>Strefy belek (polilinie RC_BEAM) w przestrzeni modelu.</summary>
        public static List<ObjectId> AllZones(Transaction tr, Database db)
        {
            var res = new List<ObjectId>();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
                if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is Polyline pl && IsZone(pl)) res.Add(id);
            return res;
        }

        /// <summary>
        /// Strefy belek płyty: [6] = obrys (handle) albo (pusty / inny nieistniejący) — środek strefy w obrysie.
        /// </summary>
        public static List<ObjectId> ZonesOfSlab(Transaction tr, Database db, ObjectId slabId, List<Point2d> slabPoly)
        {
            string sh = slabId.Handle.ToString();
            var res = new List<ObjectId>();
            foreach (var zid in AllZones(tr, db))
            {
                var pl = (Polyline)tr.GetObject(zid, OpenMode.ForRead);
                string s = ReadSlab(pl);
                bool mine;
                if (XLink.Same(s, sh)) mine = true;
                else if (!string.IsNullOrEmpty(s) && XLink.TryParse(s, out long h) && db.TryGetObjectId(new Handle(h), out var other)
                         && !other.IsErased) mine = false;   // wskazuje inną żywą płytę
                else mine = slabPoly != null && GeometryHelper.IsPointInsidePolygon(slabPoly, Centroid(pl));
                if (mine) res.Add(zid);
            }
            return res;
        }

        /// <summary>Belka GB poza płytą (RC_BEAM [39] &gt; 0 — decyzja 2026-10-23).</summary>
        public static bool IsGroundBeam(Entity e) => IsZone(e) && (Read(e)?.TopDrop ?? 0) > 0;

        /// <summary>
        /// Strefy płyty dla siatki (B1/B2/T1/T2, UB 01/02/03 przy belce — E2): jak <see cref="ZonesOfSlab"/>, bez belek GB poza płytą
        /// (uwaga Bartka 2026-10-23, test13: strefa narożna GB w płycie jak pogrubienie przy wsporniku — siatka i UB 01/02 bez zmian,
        /// UB 03 przerwane tylko tam, gdzie GB przechodzi przez krawędź — <c>GroundBeamSpansOnEdge</c>).
        /// </summary>
        public static List<ObjectId> MeshZonesOfSlab(Transaction tr, Database db, ObjectId slabId, List<Point2d> slabPoly)
            => ZonesOfSlab(tr, db, slabId, slabPoly).Where(z => !(tr.GetObject(z, OpenMode.ForRead) is Entity e && IsGroundBeam(e))).ToList();

        internal static Point2d Centroid(Polyline pl)
        {
            var v = GeometryHelper.GetPolylineVertices(pl);
            // punkt wewnątrz strefy (pas U / L — środek ciężkości bywa poza strefą): środek pierwszego prostokąta analizy
            var r = BeamGeometry.Analyze(v.Select(p => (p.X, p.Y)).ToList(), null);
            var s = r.Segments.FirstOrDefault();
            if (s != null)
            {
                double a = (s.OwnLo + s.OwnHi) / 2;
                return s.AlongX ? new Point2d(a, s.Axis) : new Point2d(s.Axis, a);
            }
            return new Point2d(v.Average(p => p.X), v.Average(p => p.Y));
        }

        // ── warstwa i RegApp ────────────────────────────────────────────

        /// <summary>Warstwa RC-BEAM (kolor i rodzaj linii jak warstwa źródłowa GA, gdy jest; inaczej magenta).</summary>
        public static void EnsureLayer(Transaction tr, Database db, string likeLayer = null)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(LayerName)) return;
            var rec = new LayerTableRecord { Name = LayerName, Color = Color.FromColorIndex(ColorMethod.ByAci, 6) };
            if (!string.IsNullOrEmpty(likeLayer) && lt.Has(likeLayer))
            {
                var src = (LayerTableRecord)tr.GetObject(lt[likeLayer], OpenMode.ForRead);
                rec.Color = src.Color;
                rec.LinetypeObjectId = src.LinetypeObjectId;
            }
            lt.UpgradeOpen();
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        // ── warstwa do ręcznego rysowania belek (decyzja użytkownika 2026-10-11, punkt D) ──
        // Zamknięta polilinia użytkownika na warstwie ManualLayer wewnątrz obrysu płyty = strefa belki: RC_PREPARE_GA, RC_GENERATE → Beam
        // i RC_BEAM zamieniają ją w strefę RC-BEAM + RC_BEAM na obu rzutach (AutoRebarEngine.ConvertManualBeams). Polilinia ręczna zostaje
        // (dane użytkownika, bez RC_GA — ponowne Prepare GA jej nie kasuje); XData ManualApp: [1] FormatVersion (Int16, 1)
        // [2] strefa na rzucie polilinii (1005, XLink) [3] strefa na drugim rzucie (1005, XLink; "" gdy brak) — przetworzona, bez duplikatów.

        public const string ManualLayer = "RC-BEAM-MANUAL";
        public const string ManualApp = "RC_BEAM_MANUAL";
        public const short ManualFormat = 1;
        public const int ManualZoneIdx = 2, ManualPartnerIdx = 3;   // RC_BEAM_MANUAL [2] / [3] (CloneRemap)

        public static bool IsManualLayer(string layer) => string.Equals(layer, ManualLayer, StringComparison.OrdinalIgnoreCase);

        /// <summary>Warstwa RC-BEAM-MANUAL (kolor 30 — pomarańczowy, opis w Layer description); tworzona przez RC_BEAM / RC_PREPARE_GA.</summary>
        public static void EnsureManualLayer(Transaction tr, Database db)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(ManualLayer)) return;
            var rec = new LayerTableRecord { Name = ManualLayer, Color = Color.FromColorIndex(ColorMethod.ByAci, 30) };
            lt.UpgradeOpen();
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            try
            {
                rec.Description = "NA Smart RC: zamknięta polilinia wewnątrz obrysu płyty = strefa belki rysowana ręcznie "
                                  + "(RC_PREPARE_GA / RC_GENERATE → Beam / RC_BEAM → strefa RC-BEAM na obu rzutach; polilinia zostaje)";
            }
            catch (System.Exception ex) { Log.Error("BeamStore.EnsureManualLayer.Description", ex); }
        }

        /// <summary>RC_BEAM_MANUAL: (strefa na rzucie polilinii, strefa na drugim rzucie) — „X8” albo ""; null — polilinia nieprzetworzona.</summary>
        public static (string zone, string partner)? ReadManual(Entity e)
        {
            TypedValue[] v;
            using (var rb = e?.GetXDataForApplication(ManualApp)) v = rb?.AsArray();
            if (v == null) return null;
            return (v.Length > ManualZoneIdx ? XLink.Read(v[ManualZoneIdx]) : "", v.Length > ManualPartnerIdx ? XLink.Read(v[ManualPartnerIdx]) : "");
        }

        public static void TagManual(Transaction tr, Database db, Entity e, string zoneHandle, string partnerHandle)
        {
            EnsureApp(tr, db, ManualApp);
            if (!e.IsWriteEnabled) e.UpgradeOpen();
            e.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, ManualApp),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, ManualFormat),   // [1]
                XLink.Write(zoneHandle ?? ""),                                     // [2]
                XLink.Write(partnerHandle ?? ""));                                  // [3]
        }

        public static void EnsureApp(Transaction tr, Database db, string app)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(app)) return;
            rat.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = app };
            rat.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }
    }
}
