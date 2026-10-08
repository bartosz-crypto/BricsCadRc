using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Tworzy blok RC_ANNOT_nnn — OSOBNA annotacja ukladu pretow.
    /// Odpowiednik RBCR_ENDE_BARDDESC / lidera w ASD.
    ///
    /// Architektura bloku (horizontal, Direction="X"):
    ///   Origin = (annotX, minY)   — annotX = prawy bok pretow + offset
    ///   Romby  : (0, i*spacing)   — WZGLEDNE pozycje (i=0..count-1)
    ///            KLUCZ: NIE uzywamy world-coordinates pretow!
    ///            Dzieki temu przesuwanie annotacji wzd. X nie zrywa wyrownania.
    ///   Dist line: x=0, y: 0 → barsSpan  (linetype _DOT)
    ///   Arm      : x=0, y: barsSpan → barsSpan+armTotalLen
    ///   Tekst    : (-TextArmOffset, barsSpan+ArmLength), rot=90
    ///
    /// Gripy (sterowane przez AnnotGripOverrule):
    ///   [0] @ insertion point      → ruch boczny (X-constrained)
    ///   [1] @ top of arm           → wydluzenie ramienia
    /// </summary>
    public static class AnnotationEngine
    {
        public const string AnnotAppName = "RC_BAR_ANNOT";

        public const double DefaultTextHeight  = 125.0;
        public const double DotRadius          = 35.0;
        public const double ArmLength          = 500.0;
        public const double TextCharWidth      = 65.0;   // txt.shx XScale=0.70 @ H=125
        public const double TextArmOffset      = 70.0;
        private const double DistEndExtension  = 250.0;  // ekstensja dist line poza ostatni pręt

        private static double Scaled(double baseValue, BarData bar)
            => baseValue * (bar.AnnotScale > 0 ? bar.AnnotScale : 1.0);

        // Zwraca środek geometryczny dist line w lokalnych BTR coords (0,0)-based.
        // Używany jako anchor pts[0] leadera — spójny z grip[0] UI (p279/p280).
        private static Point3d CalcDistLineCenter(BarData bar)
        {
            double midAlong = (bar.Count - 1) * bar.Spacing / 2.0;
            double midSkew  = (bar.SkewStart + bar.SkewEnd) / 2.0;
            return (bar.Direction == "X")
                ? new Point3d(midSkew, midAlong, 0)
                : new Point3d(midAlong, midSkew, 0);
        }

        // Domyslna odleglosc annotacji od prawego boku pretow [mm]
        public const double AnnotDefaultOffset = 300.0;

        // ----------------------------------------------------------------
        // LeaderResult
        // ----------------------------------------------------------------

        public struct LeaderResult
        {
            public ObjectId BlockRefId;
        }

        // ----------------------------------------------------------------
        // CreateLeader — tworzy blok RC_ANNOT_nnn
        // Wywolywane po BarBlockEngine.Generate().
        // bar.Count i bar.Spacing musza byc juz ustawione.
        // ----------------------------------------------------------------

        public static LeaderResult CreateLeader(
            Database db,
            BarBlockEngine.BarBlockResult barResult,
            BarData bar,
            bool leaderHorizontal,
            int posNr,
            Point3d? customInsertPt = null,
            bool barsHorizontal = true,
            bool leaderRight = true,
            bool leaderUp = true)
        {
            var res = new LeaderResult();
            if (!barResult.IsValid || bar.Count == 0) return res;

            EnsureAppIdRegistered(db);

            using var tr = db.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            string ltName = ResolveLinetype(db, tr, "_DOT", "CENTER");

            string blockName = $"RC_ANNOT_{posNr:D3}_{Guid.NewGuid():N}".Substring(0, 32);
            var blockTable   = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);

            var btr   = new BlockTableRecord { Name = blockName };
            var btrId = blockTable.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            // Init ArmMidY przed budowaniem geometrii (cursor Y dostępny tylko tutaj)
            if (barsHorizontal && leaderHorizontal && double.IsNaN(bar.ArmMidY))
            {
                double barsSpanInit  = bar.BarsSpan;
                double barCenterY    = barResult.MinPoint.Y + barsSpanInit / 2.0;
                bool   initLeaderUp  = !customInsertPt.HasValue || customInsertPt.Value.Y >= barCenterY;
                bar.ArmMidY = barsSpanInit / 2.0 + (initLeaderUp ? ArmLength : -ArmLength);
            }

            // Init ArmMidY dla Y-bars z zagięciem (leaderVertical=true)
            if (!barsHorizontal && leaderHorizontal && double.IsNaN(bar.ArmMidY))
            {
                double barsSpanInit = bar.BarsSpan;
                double barCenterX   = barResult.MinPoint.X + barsSpanInit / 2.0;
                bool   initRight    = !customInsertPt.HasValue || customInsertPt.Value.X >= barCenterX;
                bar.ArmMidY = barsSpanInit / 2.0 + (initRight ? ArmLength : -ArmLength);
            }

            // barsHorizontal decyduje o geometrii prętów (BuildHorizontal vs BuildVertical)
            double armTotalLen = barsHorizontal
                ? BuildHorizontal(tr, btr, db, bar, ltName, leaderHorizontal, leaderRight, leaderUp)
                : BuildVertical  (tr, btr, db, bar, ltName,
                    leaderVertical: leaderHorizontal,
                    leaderRight:    leaderRight,
                    leaderUp:       leaderUp);

            bar.ArmTotalLen = armTotalLen;

            // Punkt wstawienia: customInsertPt (klik użytkownika) lub auto (wg kierunku prętów)
            Point3d insertPt = customInsertPt ?? (barsHorizontal
                ? new Point3d(barResult.MaxPoint.X + AnnotDefaultOffset, barResult.MinPoint.Y,              0)
                : new Point3d(barResult.MinPoint.X,                      barResult.MaxPoint.Y + AnnotDefaultOffset, 0));

            bar.LeaderHorizontal = leaderHorizontal;
            bar.LeaderRight      = leaderRight;
            bar.LeaderUp         = leaderUp;
            // ArmMidY już zainicjalizowane powyżej (przed BuildHorizontal)

            var blockRef = new BlockReference(insertPt, btrId) { Layer = "0" };
            blockRef.ColorIndex = 256;
            if (Math.Abs(bar.Angle) > 1e-6)
                blockRef.Rotation = bar.Angle;
            space.AppendEntity(blockRef);
            tr.AddNewlyCreatedDBObject(blockRef, true);

            WriteAnnotXData(blockRef, bar);
            tr.Commit();

            res.BlockRefId = blockRef.ObjectId;
            return res;
        }

        // ----------------------------------------------------------------
        // GetDistributionAxisMidPoint — środek linii rozkładu w układzie świata
        // ----------------------------------------------------------------
        public static Point3d GetDistributionAxisMidPoint(
            BarBlockEngine.BarBlockResult barResult, bool horizontal)
        {
            if (horizontal)
                return new Point3d(
                    barResult.MaxPoint.X,
                    barResult.MinPoint.Y + (barResult.MaxPoint.Y - barResult.MinPoint.Y) / 2.0,
                    0);
            else
                return new Point3d(
                    barResult.MinPoint.X + (barResult.MaxPoint.X - barResult.MinPoint.X) / 2.0,
                    barResult.MaxPoint.Y,
                    0);
        }

        // ----------------------------------------------------------------
        // BuildHorizontal — prety poziome (Direction="X")
        //
        //   Romb i : center=(0, i*spacing),  i=0..count-1
        //   Dist   : x=0, y: 0 → barsSpan   (linetype _DOT)
        //   Tekst  : (-TextArmOffset, barsSpan+ArmLength), rot=90, TextLeft
        //            → tekst rosnie W GORE od barsSpan+ArmLength
        //   Arm    : x=0, y: barsSpan → barsSpan+ArmLength+realTextLen
        //            → koniec ramienia DOKLADNIE przy ostatnim znaku ("B1")
        //
        // Zwraca rzeczywiste armTotalLen (ArmLength + zmierzony textLen).
        // ----------------------------------------------------------------
        private static double BuildHorizontal(
            Transaction tr, BlockTableRecord btr, Database db,
            BarData bar, string ltName, bool leaderHorizontal = false, bool leaderRight = true, bool leaderUp = true,
            Vector3d offset = default)
        {
            double ox = offset.X, oy = offset.Y;
            double lineExt  = (bar.Count >= 1 && bar.Count <= 3) ? Scaled(DotRadius, bar) : 0.0;
            double lastBarY = (bar.Count - 1) * bar.Spacing;

            // Endpoints with offset — potrzebne do rescale leadera poniżej
            var baseStartH = new Point3d(bar.SkewStart + ox, -lineExt + oy,           0);
            var baseEndH   = new Point3d(bar.SkewEnd + ox,   lastBarY + lineExt + oy, 0);
            bool hasSkewH  = Math.Abs(bar.SkewEnd - bar.SkewStart) > 1e-6;
            Point3d finalEndH;
            if (NeedsElbow(bar))
            {
                var axDir = (baseEndH - baseStartH).Length > 1e-9
                    ? (baseEndH - baseStartH).GetNormal() : Vector3d.YAxis;
                finalEndH = baseEndH + axDir * Scaled(DistEndExtension, bar);
            }
            else
                finalEndH = baseEndH;

            BuildDistLineAndDots(tr, btr, bar, ltName, offset);

            // === MLeader wielosegmentowy ===
            // Zdekoduj punkty leadera z bar.LeaderPoints
            // Jeśli puste — użyj domyślnego: punkt na dist line → punkt nad/pod dist line
            var leaderPtsH = DecodeLeaderPoints(bar.LeaderPoints);
            if (leaderPtsH.Count < 2)
            {
                double midY    = bar.BarsSpan / 2.0;
                double hDir    = leaderRight ? 1.0 : -1.0;
                double vDir    = leaderUp    ? 1.0 : -1.0;
                double anchorX = leaderUp    ? bar.SkewEnd : bar.SkewStart;

                if (!leaderHorizontal)
                {
                    // Prosta pionowa (Up/Down bez złamania) — arm wzdłuż Y, X jak koniec dist line
                    leaderPtsH = new List<Point3d>
                    {
                        new Point3d(anchorX, midY,                    0),
                        new Point3d(anchorX, midY + vDir * ArmLength, 0)
                    };
                }
                else
                {
                    // Złamana (Left/Right lub Up+Jig3) — stem pionowy + arm poziomy
                    double kinkY = !double.IsNaN(bar.ArmMidY) && bar.ArmMidY != 0
                        ? bar.ArmMidY
                        : midY;
                    leaderPtsH = new List<Point3d>
                    {
                        new Point3d(anchorX,         midY,  0),  // środek dist line
                        new Point3d(anchorX,         kinkY, 0),  // punkt złamania (ta sama X)
                        new Point3d(hDir * ArmLength, kinkY, 0)  // koniec arma (poziomy)
                    };
                }
            }

            // Rescale leadera — pts[0] na środek dist line (spójny z grip[0] UI)
            if (leaderPtsH.Count >= 2)
            {
                var c0H = CalcDistLineCenter(bar);
                var targetH = new Point3d(c0H.X + ox, c0H.Y + oy, 0);
                double deltaX = targetH.X - leaderPtsH[0].X;
                for (int k = 0; k < leaderPtsH.Count; k++)
                {
                    var p = leaderPtsH[k];
                    leaderPtsH[k] = new Point3d(p.X + deltaX, p.Y, 0);
                }
                leaderPtsH[0] = new Point3d(leaderPtsH[0].X, targetH.Y, 0);
            }

            // Rozkład skośny / obrócony: ramię idzie od środka WZDŁUŻ linii rozkładu do jej końca,
            // dopiero tam skręca do tekstu (wcześniej ramię ze środka przecinało pręty).
            leaderPtsH = ApplyElbow(bar, leaderPtsH, offset);

            // Zapisz rescalowane punkty do XData — bez tego GetGripPoints czytałoby stare pozycje
            bar.LeaderPoints = EncodeLeaderPoints(leaderPtsH);

            BuildMLeaderInBtr(tr, btr, db, bar, leaderPtsH);

            return bar.ArmTotalLen;
        }

        // ----------------------------------------------------------------
        // BuildVertical — prety pionowe (Direction="Y")
        //
        // Symetryczna wersja BuildHorizontal, obrocona o 90° CW:
        //
        //   Block insert = (minX, maxY) — lewa-gorna krawedz pretow
        //   Romb i : (i*spacing, 0)          — na gorze pretow (y=0 = maxY w world)
        //   Dist   : y=0, x: 0 → barsSpan    (linetype _DOT, pozioma)
        //   Tekst  : (barsSpan+ArmLength, TextArmOffset), rot=0, TextLeft
        //            → tekst rosnie W PRAWO od barsSpan+ArmLength
        //   Arm    : y=0, x: barsSpan → barsSpan+ArmLength+realTextLen
        //            → koniec ramienia DOKLADNIE przy ostatnim znaku tekstu
        //
        // Zwraca rzeczywiste armTotalLen (ArmLength + zmierzona szerokosc tekstu).
        // ----------------------------------------------------------------
        private static double BuildVertical(
            Transaction tr, BlockTableRecord btr, Database db,
            BarData bar, string ltName,
            bool leaderVertical = false, bool leaderRight = true, bool leaderUp = true,
            Vector3d offset = default)
        {
            double ox = offset.X, oy = offset.Y;
            double lineExt  = (bar.Count >= 1 && bar.Count <= 3) ? Scaled(DotRadius, bar) : 0.0;
            double lastBarX = (bar.Count - 1) * bar.Spacing;

            // Endpoints with offset — potrzebne do rescale leadera poniżej
            var baseStartV = new Point3d(-lineExt + ox,           bar.SkewStart + oy, 0);
            var baseEndV   = new Point3d(lastBarX + lineExt + ox, bar.SkewEnd + oy,   0);
            bool hasSkewV  = Math.Abs(bar.SkewEnd - bar.SkewStart) > 1e-6;
            Point3d finalEndV;
            if (NeedsElbow(bar))
            {
                var axDir = (baseEndV - baseStartV).Length > 1e-9
                    ? (baseEndV - baseStartV).GetNormal() : Vector3d.XAxis;
                finalEndV = baseEndV + axDir * Scaled(DistEndExtension, bar);
            }
            else
                finalEndV = baseEndV;

            BuildDistLineAndDots(tr, btr, bar, ltName, offset);

            // === MLeader wielosegmentowy ===
            // Zdekoduj punkty leadera z bar.LeaderPoints
            // Jeśli puste — użyj domyślnego: punkt na dist line → punkt po prawej/lewej
            var leaderPtsV = DecodeLeaderPoints(bar.LeaderPoints);
            if (leaderPtsV.Count < 2)
            {
                double midX    = bar.BarsSpan / 2.0;
                double hDir    = leaderRight ? 1.0 : -1.0;
                double vDir    = leaderUp    ? 1.0 : -1.0;
                double anchorY = leaderRight ? bar.SkewEnd : bar.SkewStart;

                if (!leaderVertical)
                {
                    // Prosta pozioma (Left/Right bez złamania) — arm wzdłuż X, Y jak koniec dist line
                    leaderPtsV = new List<Point3d>
                    {
                        new Point3d(midX,                    anchorY, 0),
                        new Point3d(midX + hDir * ArmLength, anchorY, 0)
                    };
                }
                else
                {
                    // Złamana (Up/Down lub Left+Jig3) — stem poziomy + arm pionowy
                    // bar.ArmMidY przechowuje lokalną X złamania dla Y-bars
                    double kinkX = !double.IsNaN(bar.ArmMidY) && bar.ArmMidY != 0
                        ? bar.ArmMidY
                        : midX;
                    leaderPtsV = new List<Point3d>
                    {
                        new Point3d(midX,  anchorY, 0),  // środek dist line
                        new Point3d(kinkX, anchorY, 0),  // punkt złamania
                        new Point3d(kinkX, vDir * ArmLength, 0)   // koniec arma
                    };
                }
            }

            // Rescale leadera — pts[0] na środek dist line (spójny z grip[0] UI)
            if (leaderPtsV.Count >= 2)
            {
                var c0V = CalcDistLineCenter(bar);
                var targetV = new Point3d(c0V.X + ox, c0V.Y + oy, 0);
                double deltaY = targetV.Y - leaderPtsV[0].Y;
                for (int k = 0; k < leaderPtsV.Count; k++)
                {
                    var p = leaderPtsV[k];
                    leaderPtsV[k] = new Point3d(p.X, p.Y + deltaY, 0);
                }
                leaderPtsV[0] = new Point3d(targetV.X, leaderPtsV[0].Y, 0);
            }

            // Rozkład skośny / obrócony: ramię wzdłuż linii rozkładu do jej końca, potem do tekstu
            leaderPtsV = ApplyElbow(bar, leaderPtsV, offset);

            // Zapisz rescalowane punkty do XData — bez tego GetGripPoints czytałoby stare pozycje
            bar.LeaderPoints = EncodeLeaderPoints(leaderPtsV);

            BuildMLeaderInBtr(tr, btr, db, bar, leaderPtsV);
            return bar.ArmTotalLen;
        }

        /// <summary>
        /// Leader prosty (ostatni odcinek wzdłuż osi rozkładu: pionowy dla prętów X, poziomy dla Y):
        /// przy skosie — [środek linii rozkładu, koniec linii rozkładu po stronie tekstu, ramię od tego końca].
        /// Bez skosu — wraca do prostego [środek, koniec ramienia]. Leadery złamane (poziome dla X) bez zmian.
        /// </summary>
        /// <summary>Kąt obrotu nie jest wielokrotnością 90° (rozkład ukośny względem osi rysunku).</summary>
        internal static bool IsOblique(double angle)
        {
            double q = Math.PI / 2.0;
            double m = angle % q;
            if (m < 0) m += q;
            return Math.Min(m, q - m) > 1e-4;
        }

        /// <summary>Rozkład skośny albo obrócony ukośnie — linia rozkładu przedłużona, leader z załamaniem na jej końcu.</summary>
        private static bool NeedsElbow(BarData bar)
            => Math.Abs(bar.SkewEnd - bar.SkewStart) > 1e-6 || IsOblique(bar.Angle);

        /// <summary>
        /// Leader „jak ASD” dla rozkładu skośnego/obróconego: od środka wzdłuż linii rozkładu do jej
        /// (przedłużonego) końca, potem ramię do tekstu. offset = przesunięcie linii rozkładu w BTR annotacji.
        /// bar.Angle = obrót bloku annotacji (WCS).
        /// </summary>
        /// <summary>Odcinek leadera za końcem linii rozkładu do załamania (ustawiany gripem).</summary>
        private static double ElbowExtOf(BarData bar)
            => bar.ElbowExt > 0 ? bar.ElbowExt : Scaled(DistEndExtension, bar);

        public static double GetElbowExt(BarData bar) => ElbowExtOf(bar);

        /// <summary>Przedłużenie rysowanej linii rozkładu (nie dłuższe niż odcinek do załamania).</summary>
        private static double DrawnExtOf(BarData bar)
            => Math.Min(Scaled(DistEndExtension, bar), ElbowExtOf(bar));

        /// <summary>Annotacja ma leader z załamaniem na końcu linii rozkładu (grip długości odcinka skośnego).</summary>
        internal static bool HasElbowGrip(BarData annot, double rotation, List<Point3d> pts)
        {
            if (annot == null || pts == null || pts.Count < 3) return false;
            bool skew = Math.Abs(annot.SkewEnd - annot.SkewStart) > 1e-6;
            if (IsOblique(rotation)) return true;
            if (pts.Count != 3) return false;
            if (!skew) return false;
            var seg = pts[2] - pts[1];
            return annot.Direction == "X" ? Math.Abs(seg.X) < 1e-3 : Math.Abs(seg.Y) < 1e-3;
        }

        /// <summary>
        /// Grip załamania: nowa długość odcinka za ostatnim prętem (wzdłuż linii rozkładu).
        /// Ramię do tekstu przesuwa się razem z załamaniem (zachowuje długość i kierunek).
        /// </summary>
        public static void SetElbowExtension(BlockReference br, double newExt)
        {
            var db = br.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var brRw = tr.GetObject(br.ObjectId, OpenMode.ForWrite) as BlockReference;
            var bar = brRw != null ? ReadAnnotXData(brRw) : null;
            if (bar == null) { tr.Commit(); return; }
            bar.Angle = brRw.Rotation;
            var pts = DecodeLeaderPoints(bar.LeaderPoints);
            if (pts.Count < 3) { tr.Commit(); return; }

            double oldExt = ElbowExtOf(bar);
            newExt = Math.Max(Scaled(50.0, bar), newExt);
            var axis = pts[1] - pts[0];
            if (axis.Length < 1e-9) { tr.Commit(); return; }
            axis = axis.GetNormal();
            // całe ramię (z dalszymi załamaniami) przesuwa się razem z załamaniem
            var move = axis * (newExt - oldExt);
            for (int i = 1; i < pts.Count; i++) pts[i] = pts[i] + move;
            bar.ElbowExt = newExt;

            var offset = pts[0] - CalcDistLineCenter(bar);
            pts = ApplyElbow(bar, pts, offset);
            bar.LeaderPoints = EncodeLeaderPoints(pts);

            var btr = (BlockTableRecord)tr.GetObject(brRw.BlockTableRecord, OpenMode.ForWrite);
            var ids = new List<ObjectId>();
            foreach (ObjectId oid in btr) if (!oid.IsErased) ids.Add(oid);
            foreach (var oid in ids) ((DBObject)tr.GetObject(oid, OpenMode.ForWrite)).Erase();
            string ltName = ResolveLinetype(db, tr, "_DOT", "CENTER");
            BuildDistLineAndDots(tr, btr, bar, ltName, offset);
            BuildMLeaderInBtr(tr, btr, db, bar, pts);
            WriteAnnotXData(brRw, bar);
            tr.Commit();
            try { brRw.RecordGraphicsModified(true); } catch { }
        }

        private static List<Point3d> ApplyElbow(BarData bar, List<Point3d> pts, Vector3d offset)
        {
            if (pts == null || pts.Count < 2 || bar == null) return pts;
            bool horizontal = bar.Direction == "X";
            double ox = offset.X, oy = offset.Y;
            double lineExt = (bar.Count >= 1 && bar.Count <= 3) ? Scaled(DotRadius, bar) : 0.0;
            double lastBar = (bar.Count - 1) * bar.Spacing;
            var bs = horizontal ? new Point3d(bar.SkewStart + ox, -lineExt + oy, 0)
                                : new Point3d(-lineExt + ox, bar.SkewStart + oy, 0);
            var be = horizontal ? new Point3d(bar.SkewEnd + ox, lastBar + lineExt + oy, 0)
                                : new Point3d(lastBar + lineExt + ox, bar.SkewEnd + oy, 0);
            bool hasSkew = Math.Abs(bar.SkewEnd - bar.SkewStart) > 1e-6;
            bool elbow = NeedsElbow(bar);
            var ax = (be - bs).Length > 1e-9 ? (be - bs).GetNormal() : (horizontal ? Vector3d.YAxis : Vector3d.XAxis);
            double ext = ElbowExtOf(bar);
            var endExt   = elbow ? be + ax * ext : be;
            var startExt = elbow ? bs - ax * ext : bs;
            // Obrócony ukośnie: ramię z tekstem nie krótsze niż tekst + odstępy (tekst nie wchodzi na pręty)
            double minArm = ArmLength;
            if (IsOblique(bar.Angle))
            {
                double textLen = bar.TextLen > 0 ? bar.TextLen
                    : ($"{bar.EffectiveCount} {bar.Mark}").Length * Scaled(DefaultTextHeight, bar) * 0.85;
                minArm = textLen + 2 * Scaled(TextArmOffset, bar);
            }
            var res = FollowSkewEnd(pts, startExt, endExt, horizontal, hasSkew, minArm, bar.Angle,
                                    bs, be, Scaled(50.0, bar));
            // Rozkład obrócony: załamanie ustawia się swobodnie (odcinek skośny wynika z położenia końca ramienia)
            // — zapamiętaj faktyczną długość, żeby grip załamania i linia rozkładu z nią współgrały
            if (IsOblique(bar.Angle) && res != null && res.Count >= 3)
            {
                double dS = (res[1] - bs).Length, dE = (res[1] - be).Length;
                bar.ElbowExt = Math.Min(dS, dE);
            }
            return res;
        }

        private static List<Point3d> FollowSkewEnd(List<Point3d> pts, Point3d distStart, Point3d distEnd,
                                                   bool alongIsY, bool hasSkew, double minArm, double rotation = 0.0,
                                                   Point3d? baseStart = null, Point3d? baseEnd = null, double minExt = 0.0)
        {
            if (pts == null || pts.Count < 2) return pts;
            var p0 = pts[0];
            var last = pts[pts.Count - 1];
            var prev = pts[pts.Count - 2];

            if (IsOblique(rotation))
            {
                // Rozkład obrócony ukośnie (jak ASD): wzdłuż linii rozkładu do jej końca po stronie tekstu,
                // potem ramię POZIOME albo PIONOWE w układzie rysunku (tekst 0° / 90°, czytelny).
                var axis = distEnd - distStart;
                if (axis.Length < 1e-9) return pts;
                axis = axis.GetNormal();
                // Struktura: [środek, załamanie (na osi rozkładu), koniec ramienia, dalsze załamania użytkownika...]
                // Z jigu przychodzi [środek, koniec ramienia, ...] — wtedy załamanie dopiero wstawiamy.
                var rel1 = pts[1] - p0;
                // załamanie z jigu leży na osi rozkładu (odległość od osi, nie iloczyn — przy dłuższym odcinku
                // drobne przesunięcie środka dawało > 1 i załamanie brane było za koniec ramienia → krzywe odcinki)
                bool hasElbow = pts.Count >= 3 && rel1.Length > 1e-6
                                && Math.Abs(rel1.X * axis.Y - rel1.Y * axis.X) / rel1.Length < 10.0;
                int armIdx = hasElbow ? 2 : 1;
                var armTip = pts[armIdx];
                var rest = pts.Skip(armIdx + 1).ToList();
                bool toEnd = (armTip - p0).DotProduct(axis) >= 0;
                var elbowPt = toEnd ? distEnd : distStart;
                var outward = toEnd ? axis : -axis;
                var desired = armTip - (hasElbow ? pts[1] : elbowPt);
                if (rest.Count > 0) minArm = 50.0;   // tekst na ostatnim odcinku — pierwsze ramię dowolnie krótkie
                Vector3d best = outward;
                double bestScore = double.NegativeInfinity;
                for (int k = 0; k < 4; k++)
                {
                    var dW = new Vector3d(Math.Cos(k * Math.PI / 2.0), Math.Sin(k * Math.PI / 2.0), 0);
                    var dL = dW.RotateBy(-rotation, Vector3d.ZAxis);
                    if (dL.DotProduct(outward) <= 1e-6) continue;   // ramię nie może wracać na pręty
                    double score = desired.Length > 1e-6 ? dL.DotProduct(desired) : dL.DotProduct(outward);
                    if (score > bestScore) { bestScore = score; best = dL; }
                }
                double len = Math.Max(best.DotProduct(desired), minArm);
                // Załamanie SWOBODNE: odcinek skośny tak długi, żeby ramię (poziome / pionowe) trafiało w koniec
                // ramienia wskazany przez użytkownika (grip / jig) — wcześniej stała długość odcinka skośnego.
                var baseP = toEnd ? baseEnd : baseStart;
                if (baseP.HasValue)
                {
                    double det = outward.X * best.Y - outward.Y * best.X;
                    if (Math.Abs(det) > 1e-9)
                    {
                        var d = armTip - baseP.Value;
                        double t = (d.X * best.Y - d.Y * best.X) / det;
                        double sArm = (outward.X * d.Y - outward.Y * d.X) / det;
                        t = Math.Max(t, minExt);
                        elbowPt = baseP.Value + outward * t;
                        len = Math.Max(sArm, minArm);
                    }
                }
                var res = new List<Point3d> { p0, elbowPt, elbowPt + best * len };
                res.AddRange(rest);
                return res;
            }
            // Leader „wzdłuż osi rozkładu” (dla prętów X: ostatni odcinek bardziej pionowy niż poziomy).
            // Leadery złamane w bok (ostatni odcinek poprzeczny) zostawiamy bez zmian.
            double dAlong = alongIsY ? Math.Abs(last.Y - prev.Y) : Math.Abs(last.X - prev.X);
            double dCross = alongIsY ? Math.Abs(last.X - prev.X) : Math.Abs(last.Y - prev.Y);
            bool straight = dCross < 1e-3 || (hasSkew && pts.Count == 3 && dAlong > dCross);
            if (!straight) return pts;

            double a0 = alongIsY ? p0.Y : p0.X, aL = alongIsY ? last.Y : last.X;
            bool positive = aL > a0;
            if (!hasSkew)
            {
                // Po usunięciu skosu: punkt pośredni zbędny, jeśli wszystkie punkty leżą na jednej prostej osi
                bool colinear = pts.All(q => alongIsY ? Math.Abs(q.X - p0.X) < 1e-3 : Math.Abs(q.Y - p0.Y) < 1e-3);
                return colinear && pts.Count > 2 ? new List<Point3d> { p0, last } : pts;
            }

            var end = positive ? distEnd : distStart;
            double eA = alongIsY ? end.Y : end.X;
            double tipA = positive ? Math.Max(aL, eA + minArm) : Math.Min(aL, eA - minArm);
            var tip = alongIsY ? new Point3d(end.X, tipA, 0) : new Point3d(tipA, end.Y, 0);
            return new List<Point3d> { p0, end, tip };
        }

        // ----------------------------------------------------------------
        // KinkCircle — puste kółko w punkcie styku arma z dist line
        // ----------------------------------------------------------------

        private static void AddKinkCircle(Transaction tr, BlockTableRecord btr, Point3d center, double r)
        {
            var circle = new Circle(center, Vector3d.ZAxis, r);
            circle.Layer       = LayerManager.LeaderLayer;
            circle.ColorIndex  = 256;
            btr.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);
        }

        // ----------------------------------------------------------------
        // Dot (romb Solid)
        // ----------------------------------------------------------------

        private static void AddDot(Transaction tr, BlockTableRecord btr, Point3d c, double r)
        {
            var circle = new Circle(c, Vector3d.ZAxis, r);
            circle.Layer       = LayerManager.LeaderLayer;
            circle.ColorIndex  = 256;
            btr.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);

            var hatch = new Hatch();
            hatch.Layer       = LayerManager.LeaderLayer;
            hatch.ColorIndex  = 256;
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            hatch.Associative = false;
            btr.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);

            hatch.AppendLoop(HatchLoopTypes.Outermost,
                new ObjectIdCollection { circle.ObjectId });
            hatch.EvaluateHatch(true);
        }

        private static void AddArrow(Transaction tr, BlockTableRecord btr, Point3d tip, Vector3d dir, double halfWidth = 22.5, double height = 151)
        {
            dir = dir.GetNormal();
            var perp = Vector3d.ZAxis.CrossProduct(dir).GetNormal();

            var p1 = tip;
            var p2 = tip - dir * height + perp * halfWidth;
            var p3 = tip - dir * height - perp * halfWidth;

            var solid = new Solid();
            solid.SetPointAt(0, new Point3d(p1.X, p1.Y, 0));
            solid.SetPointAt(1, new Point3d(p2.X, p2.Y, 0));
            solid.SetPointAt(2, new Point3d(p3.X, p3.Y, 0));
            solid.SetPointAt(3, new Point3d(p3.X, p3.Y, 0));
            solid.Layer       = LayerManager.LeaderLayer;
            solid.ColorIndex  = 256;
            btr.AppendEntity(solid);
            tr.AddNewlyCreatedDBObject(solid, true);
        }

        private static void AddEndTick(Transaction tr, BlockTableRecord btr, Point3d center, Vector3d perpDir, double halfLen)
        {
            var line = new Line(
                center - perpDir * halfLen,
                center + perpDir * halfLen
            );
            line.Layer       = LayerManager.LeaderLayer;
            line.ColorIndex  = 256;
            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }

        // ----------------------------------------------------------------
        // BuildDistLineAndDots — p258 ETAP 2: buduje dist line + doty/strzałki/ticki
        // Wspólna logika dla BuildHorizontal, BuildVertical i RebuildDistLineInBtr.
        // ----------------------------------------------------------------
        /// <summary>
        /// Czy linia w BTR annotacji to linia rozkładu (dist line), a nie segment leadera.
        /// Gdy w rysunku nie ma typu linii _DOT/CENTER, dist line dostaje "Continuous" —
        /// tak samo jak leader — i UpdateLeaderInBlock kasował ją razem z leaderem
        /// (po przesunięciu labela linia rozkładu rysowała się tylko od połowy).
        /// Rozpoznajemy ją geometrycznie: kierunek osi rozkładu + długość jak w BuildDistLineAndDots.
        /// </summary>
        private static bool IsDistributionLine(Line ln, BarData bar)
        {
            if (ln == null || bar == null) return false;
            bool horizontal   = bar.Direction == "X";
            double lineExt    = (bar.Count >= 1 && bar.Count <= 3) ? Scaled(DotRadius, bar) : 0.0;
            double lastBarPos = (bar.Count - 1) * bar.Spacing;

            Point3d baseStart = horizontal
                ? new Point3d(bar.SkewStart, -lineExt, 0)
                : new Point3d(-lineExt, bar.SkewStart, 0);
            Point3d baseEnd = horizontal
                ? new Point3d(bar.SkewEnd, lastBarPos + lineExt, 0)
                : new Point3d(lastBarPos + lineExt, bar.SkewEnd, 0);

            Vector3d fallback = horizontal ? Vector3d.YAxis : Vector3d.XAxis;
            bool hasSkew = NeedsElbow(bar);
            Vector3d axisDir = hasSkew && (baseEnd - baseStart).Length > 1e-9
                ? (baseEnd - baseStart).GetNormal() : fallback;
            Point3d finalEnd = hasSkew ? baseEnd + axisDir * DrawnExtOf(bar) : baseEnd;
            double expectedLen = (finalEnd - baseStart).Length;

            var v = ln.EndPoint - ln.StartPoint;
            double len = v.Length;
            if (len < 1e-6 || expectedLen < 1e-6) return false;
            if (Math.Abs(len - expectedLen) > 0.5) return false;
            // równoległa do osi rozkładu (w obie strony)
            double cross = Math.Abs(v.X * axisDir.Y - v.Y * axisDir.X) / len;
            return cross < 1e-3;
        }

        private static void BuildDistLineAndDots(
            Transaction tr, BlockTableRecord btr,
            BarData bar, string ltName, Vector3d offset = default)
        {
            bool horizontal  = bar.Direction == "X";
            double lineExt   = (bar.Count >= 1 && bar.Count <= 3) ? Scaled(DotRadius, bar) : 0.0;
            double lastBarPos = (bar.Count - 1) * bar.Spacing;
            double ox = offset.X, oy = offset.Y;

            Point3d baseStart = horizontal
                ? new Point3d(bar.SkewStart + ox, -lineExt + oy,             0)
                : new Point3d(-lineExt + ox,      bar.SkewStart + oy,        0);
            Point3d baseEnd = horizontal
                ? new Point3d(bar.SkewEnd + ox,   lastBarPos + lineExt + oy, 0)
                : new Point3d(lastBarPos + lineExt + ox, bar.SkewEnd + oy,   0);

            bool hasSkew = Math.Abs(bar.SkewEnd - bar.SkewStart) > 1e-6;
            Vector3d fallback = horizontal ? Vector3d.YAxis : Vector3d.XAxis;
            Vector3d axisDir;
            Point3d  finalStart = baseStart;
            Point3d  finalEnd;
            if (NeedsElbow(bar))
            {
                axisDir  = (baseEnd - baseStart).Length > 1e-9
                    ? (baseEnd - baseStart).GetNormal() : fallback;
                // Przedłużenie linii rozkładu TYLKO po stronie załamania leadera (tekstu) — wcześniej zawsze
                // na końcu, więc gdy opis był przy początku, linia sterczała z drugiej strony prętów.
                bool atStart = false;
                var lp = DecodeLeaderPoints(bar.LeaderPoints);
                if (lp.Count >= 2)
                {
                    var mid = new Point3d((baseStart.X + baseEnd.X) / 2, (baseStart.Y + baseEnd.Y) / 2, 0);
                    var tip = lp.Count >= 3 ? lp[1] : lp[lp.Count - 1];
                    atStart = (tip - mid).DotProduct(axisDir) < 0;
                }
                double drawnExt = IsOblique(bar.Angle) ? 0.0 : DrawnExtOf(bar);   // obrócony: odcinek do załamania rysuje leader
                if (atStart) { finalStart = baseStart - axisDir * drawnExt; finalEnd = baseEnd; }
                else           finalEnd = baseEnd + axisDir * drawnExt;
            }
            else
            {
                axisDir  = fallback;
                finalEnd = baseEnd;
            }

            var dl = new Line(finalStart, finalEnd)
            {
                Layer      = LayerManager.LeaderLayer,
                ColorIndex = 256,
                LineWeight = LineWeight.LineWeight018,
                Linetype   = ltName
            };
            btr.AppendEntity(dl);
            tr.AddNewlyCreatedDBObject(dl, true);

            var visSet = BarBlockEngine.GetVisibleIndicesPublic(bar.VisibilityMode, bar.VisibleIndices, bar.Count);
            for (int i = 0; i < bar.Count; i++)
            {
                bool   isFirst = (i == 0);
                bool   isLast  = (i == bar.Count - 1);
                double frac    = bar.Count > 1 ? (double)i / (bar.Count - 1) : 0.0;
                double skewOff = bar.SkewStart + frac * (bar.SkewEnd - bar.SkewStart);

                if (bar.Count > 3 && isFirst)
                {
                    Point3d tip = horizontal
                        ? new Point3d(bar.SkewStart + ox, oy,                 0)
                        : new Point3d(ox,                 bar.SkewStart + oy, 0);
                    AddArrow(tr, btr, tip, -axisDir, halfWidth: Scaled(22.5, bar), height: Scaled(151, bar));
                }
                else if (bar.Count > 3 && isLast)
                {
                    Point3d tip = horizontal
                        ? new Point3d(bar.SkewEnd + ox,   lastBarPos + oy, 0)
                        : new Point3d(lastBarPos + ox, bar.SkewEnd + oy,   0);
                    AddArrow(tr, btr, tip, axisDir, halfWidth: Scaled(22.5, bar), height: Scaled(151, bar));
                }
                else if (bar.Count <= 3 || visSet.Contains(i))
                {
                    Point3d center = horizontal
                        ? new Point3d(skewOff + ox,           i * bar.Spacing + oy, 0)
                        : new Point3d(i * bar.Spacing + ox, skewOff + oy,           0);
                    AddDot(tr, btr, center, Scaled(DotRadius, bar));
                }
            }

            if (bar.Count > 3)
            {
                double   tickLen  = Scaled(DotRadius, bar) * 3;
                Vector3d perpAxis = horizontal ? Vector3d.XAxis : Vector3d.YAxis;
                Point3d  tick0    = horizontal
                    ? new Point3d(bar.SkewStart + ox, oy,                 0)
                    : new Point3d(ox,                 bar.SkewStart + oy, 0);
                Point3d  tick1    = horizontal
                    ? new Point3d(bar.SkewEnd + ox,   lastBarPos + oy,    0)
                    : new Point3d(lastBarPos + ox,    bar.SkewEnd + oy,   0);
                AddEndTick(tr, btr, tick0, perpAxis, tickLen);
                AddEndTick(tr, btr, tick1, perpAxis, tickLen);
            }
        }

        // ----------------------------------------------------------------
        // GetArmMidY — odczytuje aktualną pozycję Y linii arm z BTR bloku
        // ----------------------------------------------------------------

        public static double GetArmMidY(BlockReference br)
        {
            var bar = ReadAnnotXData(br);
            if (bar != null && !double.IsNaN(bar.ArmMidY))
                return bar.ArmMidY;

            using var tr = br.Database.TransactionManager.StartTransaction();
            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            foreach (ObjectId oid in btr)
            {
                if (oid.IsErased) continue;
                var obj = tr.GetObject(oid, OpenMode.ForRead);
                if (obj is Line ln
                    && Math.Abs(ln.StartPoint.X) < 1.0
                    && Math.Abs(ln.StartPoint.Y - ln.EndPoint.Y) < 1.0
                    && Math.Abs(ln.EndPoint.X) > 1.0)
                { tr.Commit(); return ln.StartPoint.Y; }
            }
            tr.Commit();
            return bar?.BarsSpan / 2.0 ?? 0;
        }

        // ----------------------------------------------------------------
        // UpdateArmInBlock — grip arm-top
        // ----------------------------------------------------------------

        // ----------------------------------------------------------------
        // UpdateLeaderInBlock — przebudowuje MLeader z zaktualizowanymi punktami
        // ----------------------------------------------------------------
        public static void UpdateLeaderInBlock(
            BlockReference br, Point3d newTextPosWCS)
        {
            var db = br.Database;
            using var tr = db.TransactionManager.StartTransaction();

            var brRw = tr.GetObject(br.ObjectId, OpenMode.ForWrite) as BlockReference;
            if (brRw == null) { tr.Commit(); return; }

            var bar = ReadAnnotXData(brRw);
            if (bar == null) { tr.Commit(); return; }
            bar.Angle = brRw.Rotation;

            // Przelicz nową pozycję tekstu do lokalnego BTR
            var inv     = brRw.BlockTransform.Inverse();
            var localPt = newTextPosWCS.TransformBy(inv);

            // Zaktualizuj ostatni punkt w LeaderPoints
            var pts = DecodeLeaderPoints(bar.LeaderPoints);
            if (pts.Count > 0)
                pts[pts.Count - 1] = localPt;
            else
                pts.Add(localPt);

            // Rescale leadera — pts[0] na środek dist line (spójny z grip[0] UI)
            if (pts.Count >= 2)
            {
                // [p282] pts[0] aktualizuje się wyłącznie przez RebuildDistLineInBtr (Site 5),
                // które uwzględnia localOffset. Snap do target bez localOffset psuje pts[0] post-MOVE annot.
                var target = CalcDistLineCenter(bar);
                if (bar.Direction == "X")
                {
                    double deltaX = target.X - pts[0].X;
                    for (int k = 0; k < pts.Count; k++) pts[k] = new Point3d(pts[k].X + deltaX, pts[k].Y, 0);
                    // pts[0].Y bez zmian — set przez RebuildDistLineInBtr z localOffset
                }
                else
                {
                    double deltaY = target.Y - pts[0].Y;
                    for (int k = 0; k < pts.Count; k++) pts[k] = new Point3d(pts[k].X, pts[k].Y + deltaY, 0);
                    // pts[0].X bez zmian — set przez RebuildDistLineInBtr z localOffset
                }
            }

            bar.LeaderPoints = EncodeLeaderPoints(pts);

            // Kasuj stare: Line (Continuous, ColorIndex=7) i DBText
            var btr = (BlockTableRecord)tr.GetObject(
                brRw.BlockTableRecord, OpenMode.ForWrite);
            var idsToErase = new List<ObjectId>();
            foreach (ObjectId eid in btr)
            {
                var ent = tr.GetObject(eid, OpenMode.ForRead);
                if (ent is Line ln && ln.Linetype == "Continuous" && !IsDistributionLine(ln, bar))
                    idsToErase.Add(eid);
                if (ent is DBText)
                    idsToErase.Add(eid);
            }
            foreach (var eid in idsToErase)
            {
                var ent = tr.GetObject(eid, OpenMode.ForWrite);
                ent.Erase();
            }

            // Odbuduj geometrię z zaktualizowanymi punktami
            BuildMLeaderInBtr(tr, btr, db, bar, pts);

            // Zapisz XData
            WriteAnnotXData(brRw, bar);
            tr.Commit();
        }

        // ----------------------------------------------------------------
        public static void UpdateLastSegmentWithShift(
            BlockReference br, Vector3d perpShiftWCS, Vector3d alongShiftWCS)
        {
            var db = br.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var brRw = tr.GetObject(br.ObjectId, OpenMode.ForWrite) as BlockReference;
            if (brRw == null) { tr.Commit(); return; }

            var bar = ReadAnnotXData(brRw);
            if (bar == null) { tr.Commit(); return; }
            bar.Angle = brRw.Rotation;
            bool oblique = IsOblique(bar.Angle);

            var inv = brRw.BlockTransform.Inverse();
            var perpLocal  = perpShiftWCS.TransformBy(inv);
            var alongLocal = alongShiftWCS.TransformBy(inv);

            var pts = DecodeLeaderPoints(bar.LeaderPoints);
            if (pts.Count < 2) { tr.Commit(); return; }

            // Rozkład skośny z ramieniem wzdłuż osi rozkładu: kink przypięty do końca linii rozkładu
            // (nie ślizga się po skosie), ostatni odcinek zostaje ściśle pionowy/poziomy.
            bool skewArm = oblique;   // obrócony ukośnie: załamanie liczone od nowa (ApplyElbow niżej)
            if (!oblique && pts.Count >= 3 && Math.Abs(bar.SkewEnd - bar.SkewStart) > 1e-6)
            {
                var segL = pts[pts.Count - 1] - pts[pts.Count - 2];
                skewArm = bar.Direction == "X" ? Math.Abs(segL.X) < 1e-3 : Math.Abs(segL.Y) < 1e-3;
            }

            // Kink (pts[N-2]): ślizganie wzdłuż arm (pts[0]→kink) — zachowuje kąt arm
            // lastPt (pts[N-1]): pełen offset użytkownika
            if (oblique && pts.Count >= 4)
            {
                // obrócony z dodatkowymi załamaniami: przedostatni punkt ślizga się wzdłuż swojego odcinka
                var segV = pts[pts.Count - 2] - pts[pts.Count - 3];
                if (segV.Length > 1e-9)
                {
                    var segD = segV.GetNormal();
                    var dT = perpLocal + alongLocal;
                    pts[pts.Count - 2] = pts[pts.Count - 2] + segD * (dT.X * segD.X + dT.Y * segD.Y);
                }
            }
            if (pts.Count >= 3 && !skewArm)
            {
                var armVec = pts[pts.Count - 2] - pts[0];
                double armLen = armVec.Length;
                if (armLen > 1e-9)
                {
                    var armDir = new Vector3d(armVec.X / armLen, armVec.Y / armLen, 0.0);
                    var deltaTotal = perpLocal + alongLocal;
                    double slide = deltaTotal.X * armDir.X + deltaTotal.Y * armDir.Y;
                    pts[pts.Count - 2] = pts[pts.Count - 2] + armDir * slide;
                }
            }
            pts[pts.Count - 1] = pts[pts.Count - 1] + perpLocal + alongLocal;
            if (skewArm && !oblique)
            {
                var kinkL = pts[pts.Count - 2];
                var lastL = pts[pts.Count - 1];
                pts[pts.Count - 1] = bar.Direction == "X"
                    ? new Point3d(kinkL.X, lastL.Y, 0)
                    : new Point3d(lastL.X, kinkL.Y, 0);
            }

            // Rescale leadera — pts[0] na środek dist line (spójny z grip[0] UI)
            if (pts.Count >= 2)
            {
                // [p282] pts[0] aktualizuje się wyłącznie przez RebuildDistLineInBtr (Site 5),
                // które uwzględnia localOffset. Snap do target bez localOffset psuje pts[0] post-MOVE annot.
                var target = CalcDistLineCenter(bar);
                if (bar.Direction == "X")
                {
                    double deltaX = target.X - pts[0].X;
                    for (int k = 0; k < pts.Count; k++) pts[k] = new Point3d(pts[k].X + deltaX, pts[k].Y, 0);
                    // pts[0].Y bez zmian — set przez RebuildDistLineInBtr z localOffset
                }
                else
                {
                    double deltaY = target.Y - pts[0].Y;
                    for (int k = 0; k < pts.Count; k++) pts[k] = new Point3d(pts[k].X, pts[k].Y + deltaY, 0);
                    // pts[0].X bez zmian — set przez RebuildDistLineInBtr z localOffset
                }
            }

            // Obrócony ukośnie: ramię poziome/pionowe w układzie rysunku od końca linii rozkładu
            // (przeciągnięcie w bok może przełączyć ramię z pionowego na poziome i odwrotnie).
            if (oblique && pts.Count >= 2)
                pts = ApplyElbow(bar, pts, pts[0] - CalcDistLineCenter(bar));

            bar.LeaderPoints = EncodeLeaderPoints(pts);

            var btr = (BlockTableRecord)tr.GetObject(
                brRw.BlockTableRecord, OpenMode.ForWrite);
            var idsToErase = new List<ObjectId>();
            foreach (ObjectId eid in btr)
            {
                if (eid.IsErased) continue;
                var ent = tr.GetObject(eid, OpenMode.ForRead);
                bool willErase = false;
                if (ent is Line ln)
                {
                    willErase = ln.Linetype == "Continuous" && !IsDistributionLine(ln, bar);
                    if (willErase) idsToErase.Add(eid);
                }
                else if (ent is DBText tx)
                {
                    idsToErase.Add(eid);
                }
            }
            foreach (var eid in idsToErase)
            {
                var ent = tr.GetObject(eid, OpenMode.ForWrite);
                ent.Erase();
            }

            BuildMLeaderInBtr(tr, btr, db, bar, pts);
            WriteAnnotXData(brRw, bar);
            tr.Commit();
        }

        // ----------------------------------------------------------------
        // BuildMLeaderInBtr — budowa leadera w BTR: Line segmenty + DBText + landing
        // Używana przez BuildHorizontal, BuildVertical i UpdateLeaderInBlock.
        // ----------------------------------------------------------------
        private static void BuildMLeaderInBtr(
            Transaction tr, BlockTableRecord btr, Database db,
            BarData bar, List<Point3d> leaderPts)
        {
            if (leaderPts.Count < 2) return;

            // 1. Rysuj segmenty leadera (Line od punktu do punktu)
            for (int i = 0; i < leaderPts.Count - 1; i++)
            {
                var seg = new Line(leaderPts[i], leaderPts[i + 1])
                {
                    Layer      = LayerManager.LeaderLayer,
                    ColorIndex = 256,
                    LineWeight = LineWeight.LineWeight018,
                    Linetype   = "Continuous"
                };
                btr.AppendEntity(seg);
                tr.AddNewlyCreatedDBObject(seg, true);
            }

            // 2. Oblicz kierunek ostatniego segmentu (dla tekstu i landing)
            var lastPt     = leaderPts[leaderPts.Count - 1];
            var prevPt     = leaderPts[leaderPts.Count - 2];
            var diff = lastPt - prevPt;
            if (diff.Length < 1e-6)
            {
                // Zdegenerowany segment (identyczne punkty) — nie rysuj leadera,
                // ale NIE rzucaj wyjątku żeby nie rollbackować całej transakcji SyncAnnotation.
                return;
            }
            var lastDir    = diff.GetNormal();
            // 3. Kąt tekstu = kąt ostatniego segmentu (dokładny, nie snap do 0/90°)
            // Czytelność oceniamy w układzie RYSUNKU (blok annotacji może być obrócony o bar.Angle)
            double rawAngle = Math.Atan2(lastDir.Y, lastDir.X);
            double rawW = rawAngle + bar.Angle;
            while (rawW >  Math.PI) rawW -= 2 * Math.PI;
            while (rawW <= -Math.PI) rawW += 2 * Math.PI;
            double normW = rawW;
            // Normalizuj żeby tekst był czytelny (nie do góry nogami)
            if (normW > Math.PI / 2.0 + 1e-6) normW -= Math.PI;
            else if (normW <= -Math.PI / 2.0 + 1e-6) normW += Math.PI;
            double textAngle = rawAngle + (normW - rawW);

            var textDir = new Vector3d(Math.Cos(textAngle), Math.Sin(textAngle), 0);
            var perpDir = new Vector3d(-Math.Sin(textAngle), Math.Cos(textAngle), 0);

            // 4. Tymczasowy DBText żeby zmierzyć textLen
            string textString = $"{bar.EffectiveCount} {bar.Mark}";
            double scaledTextH = Scaled(DefaultTextHeight, bar);
            double textLen;
            {
                var tmpText = new DBText
                {
                    TextString  = textString,
                    Height      = scaledTextH,
                    TextStyleId = GetTextStyleId(db)
                };
                btr.AppendEntity(tmpText);
                tr.AddNewlyCreatedDBObject(tmpText, true);
                try
                {
                    var ext = tmpText.GeometricExtents;
                    textLen = Math.Max(
                        Math.Abs(ext.MaxPoint.X - ext.MinPoint.X),
                        Math.Abs(ext.MaxPoint.Y - ext.MinPoint.Y));
                    if (textLen <= 0) textLen = textString.Length * scaledTextH * 0.65;
                }
                catch { textLen = textString.Length * scaledTextH * 0.65; }
                tmpText.Erase();
            }

            // 5. Landing: zawsze kończy się w lastPt
            double scaledTextArmOffset = Scaled(TextArmOffset, bar);
            Vector3d landingDir = lastDir;
            var landingStart = lastPt - landingDir * (textLen + scaledTextArmOffset);
            var landingEnd   = lastPt;
            var landingLine  = new Line(landingStart, landingEnd)
            {
                Layer      = LayerManager.LeaderLayer,
                ColorIndex = 256,
                LineWeight = LineWeight.LineWeight018,
                Linetype   = "Continuous"
            };
            btr.AppendEntity(landingLine);
            tr.AddNewlyCreatedDBObject(landingLine, true);

            // 6. textPos: gdy textAngle był flipowany, anchor = lastPt; gdy nie — landingStart
            bool textFlipped  = Math.Abs(textAngle - rawAngle) > 1e-6;
            Point3d textAnchor = textFlipped ? lastPt : landingStart;
            Point3d textPos    = textAnchor + perpDir * scaledTextArmOffset;
            var dbText = new DBText
            {
                TextString     = textString,
                Layer          = LayerManager.AnnotLayer,
                Height         = scaledTextH,
                ColorIndex     = 2,
                Position       = textPos,
                Rotation       = textAngle,
                TextStyleId    = GetTextStyleId(db),
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode   = TextVerticalMode.TextBase
            };
            btr.AppendEntity(dbText);
            tr.AddNewlyCreatedDBObject(dbText, true);

            bar.ArmTotalLen = (lastPt - prevPt).Length;
            bar.TextLen     = textLen;
        }

        public static void UpdateArmInBlock(BlockReference br, double newArmTotalLen, double newMidY = double.NaN)
        {
            var bar = ReadAnnotXData(br);
            if (bar == null || bar.BarsSpan <= 0) return;

            double clampedNew = Math.Max(50.0, newArmTotalLen);
            bool   xHoriz     = bar.Direction == "X" && bar.LeaderHorizontal;
            bool   yVert      = bar.Direction == "Y" && !bar.LeaderHorizontal;  // Y-bars, arm pionowy
            bool   yHoriz     = bar.Direction == "Y" && bar.LeaderHorizontal;   // Y-bars, arm ze złamaniem
            double midY       = !double.IsNaN(newMidY) ? newMidY : (!double.IsNaN(bar.ArmMidY) ? bar.ArmMidY : bar.BarsSpan / 2.0);

            // textLen pochodzi z XData (stała długość tekstu) — PRZED nadpisaniem ArmTotalLen
            double textLen = bar.TextLen > 0 ? bar.TextLen : (bar.ArmTotalLen - ArmLength);

            bar.ArmTotalLen = clampedNew;
            if (xHoriz) bar.ArmMidY = midY;
            if (yHoriz)
            {
                double midXVal = !double.IsNaN(newMidY) ? newMidY
                               : (!double.IsNaN(bar.ArmMidY) ? bar.ArmMidY
                               : bar.BarsSpan / 2.0 + ArmLength);
                bar.ArmMidY = midXVal;
            }

            // Otwieramy br przez własną transakcję (ForWrite) — bezpośredni zapis br.XData
            // na parametrze z systemu gripów może nie trafić do trwałej transakcji BricsCAD.
            var objId = br.ObjectId;
            var db    = br.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var brRw = (BlockReference)tr.GetObject(objId, OpenMode.ForWrite);
            WriteAnnotXData(brRw, bar);

            var btr = (BlockTableRecord)tr.GetObject(brRw.BlockTableRecord, OpenMode.ForRead);

            Line   armLine = null;
            DBText armText = null;
            foreach (ObjectId oid in btr)
            {
                if (oid.IsErased) continue;
                var obj = tr.GetObject(oid, OpenMode.ForRead);
                if (obj is Line ln && armLine == null)
                {
                    if (xHoriz)
                    {
                        // Arm poziome X-bars: Start.X=0, pozioma (Start.Y==End.Y), EndPoint.X!=0
                        // (dist line jest pionowa — Start.X=End.X=0; arm ma EndPoint.X != 0)
                        if (Math.Abs(ln.StartPoint.X) < 1.0
                            && Math.Abs(ln.StartPoint.Y - ln.EndPoint.Y) < 1.0
                            && Math.Abs(ln.EndPoint.X) > 1.0)
                            armLine = ln;
                    }
                    else if (bar.Direction == "X")
                    {
                        // Arm pionowe: x=0 na obu końcach; góra EndY > barsSpan, dół EndY < barsSpan/2
                        if (Math.Abs(ln.StartPoint.X) < 1.0
                            && Math.Abs(ln.EndPoint.X) < 1.0
                            && (ln.EndPoint.Y > bar.BarsSpan + 1.0
                                || ln.EndPoint.Y < bar.BarsSpan / 2.0 - 1.0))
                            armLine = ln;
                    }
                    else if (yVert)
                    {
                        // Arm poziomy Y-bars (prosty): StartPoint.Y ≈ 0, EndPoint.Y ≈ 0
                        // StartPoint.X ≈ barsSpan/2 (środek dist line), EndPoint.X różni się
                        if (Math.Abs(ln.StartPoint.Y) < 1.0
                            && Math.Abs(ln.EndPoint.Y) < 1.0
                            && Math.Abs(ln.StartPoint.X - bar.BarsSpan / 2.0) < 5.0
                            && Math.Abs(ln.EndPoint.X - bar.BarsSpan / 2.0) > 5.0)
                            armLine = ln;
                    }
                    else if (yHoriz)
                    {
                        // Arm pionowe Y-bars ze złamaniem: StartPoint.Y ≈ 0, EndPoint.Y ≠ 0
                        // Nie może być dist line (X=0→barsSpan) ani tick mark (StartPoint≈EndPoint.X)
                        if (Math.Abs(ln.StartPoint.Y) < 1.0
                            && Math.Abs(ln.EndPoint.Y) > 1.0
                            && Math.Abs(ln.StartPoint.X) > 5.0                      // nie przy X=0
                            && Math.Abs(ln.StartPoint.X - bar.BarsSpan) > 5.0)      // nie przy X=barsSpan
                            armLine = ln;
                    }
                    else
                    {
                        // fallback
                        if (Math.Abs(ln.StartPoint.Y) < 1.0 && ln.StartPoint.X > 1.0)
                            armLine = ln;
                    }
                }
                else if (obj is DBText txt && armText == null)
                    armText = txt;
                if (armLine != null && armText != null) break;
            }


            if (armLine != null)
            {
                armLine.UpgradeOpen();
                if (xHoriz)
                {
                    double hDir        = bar.LeaderRight ? 1.0 : -1.0;
                    // arm poziomy: długość = clampedNew (zmienia się przez X drag)
                    armLine.StartPoint = new Point3d(0, midY, 0);
                    armLine.EndPoint   = new Point3d(hDir * clampedNew, midY, 0);
                }
                else if (bar.Direction == "X")
                {
                    armLine.StartPoint = new Point3d(0, bar.BarsSpan / 2.0, 0);
                    armLine.EndPoint   = bar.LeaderUp
                        ? new Point3d(0, bar.BarsSpan + clampedNew, 0)
                        : new Point3d(0, bar.BarsSpan / 2.0 - clampedNew, 0);
                }
                else if (yVert)
                {
                    // Arm poziomy: od (barsSpan/2, 0) w prawo lub lewo
                    armLine.StartPoint = new Point3d(bar.BarsSpan / 2.0, 0, 0);
                    armLine.EndPoint   = bar.LeaderRight
                        ? new Point3d(bar.BarsSpan + clampedNew, 0, 0)
                        : new Point3d(bar.BarsSpan / 2.0 - clampedNew, 0, 0);
                }
                else if (yHoriz)
                {
                    // Y-bars ze złamaniem: arm pionowy od (midXVal,0) do (midXVal, vDir*clampedNew)
                    double vDir    = bar.LeaderUp ? 1.0 : -1.0;
                    double midXVal = !double.IsNaN(newMidY) ? newMidY
                                   : (!double.IsNaN(bar.ArmMidY) ? bar.ArmMidY : bar.BarsSpan / 2.0 + ArmLength);
                    armLine.StartPoint = new Point3d(midXVal, 0,                  0);
                    armLine.EndPoint   = new Point3d(midXVal, vDir * clampedNew,  0);
                }
                else
                {
                    // fallback poziomy
                    armLine.StartPoint = new Point3d(bar.BarsSpan, 0, 0);
                    armLine.EndPoint   = new Point3d(bar.BarsSpan + clampedNew, 0, 0);
                }
            }

            if (armText != null)
            {
                armText.UpgradeOpen();
                if (xHoriz)
                {
                    double hDir    = bar.LeaderRight ? 1.0 : -1.0;
                    // hDir=+1: TextLeft  → Position      = lewa krawędź (armEnd - textLen od kink)
                    // hDir=-1: TextRight → AlignmentPoint = prawa krawędź (ArmLength od kink)
                    var newTextPos = new Point3d(hDir * (clampedNew - textLen), midY + TextArmOffset, 0);
                    if (hDir > 0)
                        armText.Position = newTextPos;
                    else
                        armText.AlignmentPoint = newTextPos;
                }
                else if (bar.Direction == "X")
                {
                    Point3d newTextPos;
                    if (bar.LeaderUp)
                    {
                        // Góra: tekst zaczyna się ArmLength nad barsSpan, rośnie w górę
                        newTextPos = new Point3d(-TextArmOffset, bar.BarsSpan + clampedNew - textLen, 0);
                    }
                    else
                    {
                        // Dół: tekst przy końcu arm, rośnie w górę (od arm.End do arm.End + textLen)
                        newTextPos = new Point3d(-TextArmOffset, bar.BarsSpan / 2.0 - clampedNew, 0);
                    }
                    armText.Position = newTextPos;
                }
                else if (yVert)
                {
                    // Arm poziomy Y-bars: tekst poziomy (Rotation=0°), nad armem
                    double textStartX = bar.LeaderRight
                        ? bar.BarsSpan + clampedNew - textLen   // prawo: tekst przy końcu arma
                        : bar.BarsSpan / 2.0 - clampedNew;      // lewo: tekst od końca arma w prawo
                    armText.Position = new Point3d(textStartX, TextArmOffset, 0);
                }
                else if (yHoriz)
                {
                    // Y-bars ze złamaniem: tekst pionowy (Rotation=90°), odsunięty od arma w -X
                    double vDir = bar.LeaderUp ? 1.0 : -1.0;
                    // Tekst (Rotation=90°) zawsze rośnie w +Y od Position.
                    // Musi sięgać dokładnie do końca arma:
                    // vDir= 1 (góra): arm kończy się na +clampedNew → tekst od (clampedNew - textLen) do clampedNew
                    // vDir=-1 (dół):  arm kończy się na -clampedNew → tekst od -clampedNew do -(clampedNew - textLen)
                    double midXVal    = !double.IsNaN(bar.ArmMidY) ? bar.ArmMidY : bar.BarsSpan / 2.0 + ArmLength;
                    double textStartY = vDir > 0
                        ? clampedNew - textLen   // góra: tekst kończy się przy końcu arma
                        : -clampedNew;           // dół: tekst zaczyna się przy końcu arma i rośnie w górę
                    armText.Position = new Point3d(midXVal - TextArmOffset, textStartY, 0);
                }
                else
                {
                    // fallback poziomy
                    double textStartX = bar.BarsSpan + clampedNew - textLen;
                    var newTextPos    = new Point3d(textStartX, TextArmOffset, 0);
                    armText.Position  = newTextPos;
                }
            }
            else
            {
            }

            // Zaktualizuj kółko złamania — usuń stare (jeśli istnieje) i dodaj nowe
            if (xHoriz || yHoriz)
            {
                var btrForCircles = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);

                // Zbierz i usuń kółka złamania (nie-prętowe)
                var circlesToErase = new List<Circle>();
                foreach (ObjectId oid in btrForCircles)
                {
                    if (oid.IsErased) continue;
                    var obj = tr.GetObject(oid, OpenMode.ForRead);
                    if (obj is Circle c)
                    {
                        bool isBarCircle = false;
                        if (xHoriz)
                        {
                            // Kółka prętów: center.X ≈ 0, center.Y = i*spacing (wielokrotność spacing)
                            // Kółko złamania: center.X ≈ 0, center.Y = midY (dowolna wartość)
                            double rem = bar.Spacing > 0 ? c.Center.Y % bar.Spacing : -1;
                            isBarCircle = Math.Abs(rem) < 5.0 || Math.Abs(rem - bar.Spacing) < 5.0;
                        }
                        else // yHoriz
                        {
                            double rem = bar.Spacing > 0 ? c.Center.X % bar.Spacing : -1;
                            isBarCircle = Math.Abs(rem) < 5.0 || Math.Abs(rem - bar.Spacing) < 5.0;
                        }
                        if (!isBarCircle)
                            circlesToErase.Add(c);
                    }
                }
                foreach (var c in circlesToErase)
                {
                    c.UpgradeOpen();
                    c.Erase();
                }

                // Dodaj nowe kółko złamania tylko gdy midY/midX w zakresie dist line
                if (xHoriz && midY >= 0 && midY <= bar.BarsSpan)
                    AddKinkCircle(tr, btrForCircles, new Point3d(0, midY, 0), DotRadius);
                else if (yHoriz)
                {
                    double midXK = !double.IsNaN(newMidY) ? newMidY
                                 : (!double.IsNaN(bar.ArmMidY) ? bar.ArmMidY : bar.BarsSpan / 2.0 + ArmLength);
                    if (midXK >= 0 && midXK <= bar.BarsSpan)
                        AddKinkCircle(tr, btrForCircles, new Point3d(midXK, 0, 0), DotRadius);
                }
            }

            // Pionowy stem — od środka dist line (barsSpan/2) do punktu zagięcia (midY).
            // Dotyczy tylko xHoriz. Zawsze obecny (nie tylko gdy midY poza dist line).
            if (xHoriz)
            {
                double barsSpanLocal = bar.BarsSpan;
                Line stemLine = null;
                foreach (ObjectId oid in btr)
                {
                    if (oid.IsErased) continue;
                    var obj = tr.GetObject(oid, OpenMode.ForRead);
                    if (obj is Line ln
                        && Math.Abs(ln.StartPoint.X) < 1.0
                        && Math.Abs(ln.EndPoint.X) < 1.0
                        && Math.Abs(ln.StartPoint.Y - ln.EndPoint.Y) > 1.0  // pionowa
                        && !(Math.Abs(ln.StartPoint.Y) < 1.0 && Math.Abs(ln.EndPoint.Y - barsSpanLocal) < 1.0)  // nie dist line
                        && !(Math.Abs(ln.StartPoint.Y - barsSpanLocal) < 1.0 && Math.Abs(ln.EndPoint.Y) < 1.0)) // nie dist line odwrotnie
                    {
                        stemLine = ln;
                        break;
                    }
                }

                var btrWriteStem = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
                if (stemLine != null)
                {
                    stemLine.UpgradeOpen();
                    stemLine.StartPoint = new Point3d(0, barsSpanLocal / 2.0, 0);
                    stemLine.EndPoint   = new Point3d(0, midY, 0);
                }
                else
                {
                    var stem = new Line(new Point3d(0, barsSpanLocal / 2.0, 0), new Point3d(0, midY, 0))
                    {
                        Layer      = LayerManager.LeaderLayer,
                        LineWeight = LineWeight.LineWeight018,
                        Linetype   = "Continuous"
                    };
                    btrWriteStem.AppendEntity(stem);
                    tr.AddNewlyCreatedDBObject(stem, true);
                }
            }

            if (yHoriz)
            {
                double midX        = !double.IsNaN(bar.ArmMidY) ? bar.ArmMidY : bar.BarsSpan / 2.0 + ArmLength;
                double barsSpanLoc = bar.BarsSpan;
                Line stemLine = null;
                foreach (ObjectId oid in btr)
                {
                    if (oid.IsErased) continue;
                    var obj = tr.GetObject(oid, OpenMode.ForRead);
                    if (obj is Line ln
                        && Math.Abs(ln.StartPoint.Y) < 1.0      // pozioma
                        && Math.Abs(ln.EndPoint.Y)   < 1.0
                        && Math.Abs(ln.StartPoint.X - ln.EndPoint.X) > 1.0   // nie punkt
                        && !(Math.Abs(ln.StartPoint.X) < 5.0
                             && Math.Abs(ln.EndPoint.X - barsSpanLoc) < 5.0) // wykluczamy dist line (0→barsSpan)
                        && !(Math.Abs(ln.EndPoint.X) < 5.0
                             && Math.Abs(ln.StartPoint.X - barsSpanLoc) < 5.0) // wykluczamy dist line odwrotnie
                        && !(Math.Abs(ln.StartPoint.X - barsSpanLoc / 2.0) < 5.0
                             && Math.Abs(ln.EndPoint.X - barsSpanLoc / 2.0) < 5.0)) // wykluczamy linie symetryczne (nie stem)
                    {
                        stemLine = ln;
                        break;
                    }
                }

                var btrWriteStem2 = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
                if (stemLine != null)
                {
                    stemLine.UpgradeOpen();
                    stemLine.StartPoint = new Point3d(barsSpanLoc / 2.0, 0, 0);
                    stemLine.EndPoint   = new Point3d(midX,               0, 0);
                }
                else
                {
                    var stem = new Line(
                        new Point3d(barsSpanLoc / 2.0, 0, 0),
                        new Point3d(midX,               0, 0))
                    {
                        Layer      = LayerManager.LeaderLayer,
                        LineWeight = LineWeight.LineWeight018,
                        Linetype   = "Continuous"
                    };
                    btrWriteStem2.AppendEntity(stem);
                    tr.AddNewlyCreatedDBObject(stem, true);
                }
            }

            tr.Commit();

        }

        // ----------------------------------------------------------------
        // SyncAnnotation — wywolywane po zmianie Count/BarsSpan w RC_BAR_BLOCK
        // Szuka bloku RC_ANNOT_nnn z pasujacym Markiem i przebudowuje jego BTR.
        // ----------------------------------------------------------------

        public static void SyncAnnotation(Database db, BarData updatedBar)
        {
            // Szukaj po AnnotHandle (unikalne dla kazdego rozkladu) — bez handle fallback na Mark
            var annotId = !string.IsNullOrEmpty(updatedBar.AnnotHandle)
                ? FindAnnotationIdByHandle(db, updatedBar.AnnotHandle)
                : FindAnnotationIdByMark  (db, updatedBar.Mark);
            if (annotId == ObjectId.Null) return;

            using var tr = db.TransactionManager.StartTransaction();
            var annotBr = (BlockReference)tr.GetObject(annotId, OpenMode.ForWrite);

            // Zachowaj ArmTotalLen z istniejacych XData (uzytkownik mogl zmienic grip)
            var existingAnnot = ReadAnnotXData(annotBr);
            if (existingAnnot != null)
            {
                // Zachowaj całą geometrię leadera — użytkownik mógł ją zmieniać gripami
                updatedBar.ArmTotalLen      = existingAnnot.ArmTotalLen;
                updatedBar.LeaderHorizontal = existingAnnot.LeaderHorizontal;
                updatedBar.LeaderRight      = existingAnnot.LeaderRight;
                updatedBar.LeaderUp         = existingAnnot.LeaderUp;
                updatedBar.ArmMidY          = existingAnnot.ArmMidY;
                updatedBar.TextLen          = existingAnnot.TextLen;
                updatedBar.ElbowExt         = existingAnnot.ElbowExt;
                // Przefiltruj LeaderPoints — usuń zdegenerowane segmenty (duplikaty)
                if (!string.IsNullOrEmpty(existingAnnot.LeaderPoints))
                {
                    var pts = DecodeLeaderPoints(existingAnnot.LeaderPoints);
                    var cleanPts = new List<Point3d>();
                    foreach (var p in pts)
                    {
                        if (cleanPts.Count == 0 || cleanPts[cleanPts.Count - 1].DistanceTo(p) > 1e-6)
                            cleanPts.Add(p);
                    }
                    updatedBar.LeaderPoints = cleanPts.Count >= 2
                        ? EncodeLeaderPoints(cleanPts)
                        : "";  // Za mało punktów — BuildH/V użyje domyślnego leadera
                }
                else
                    updatedBar.LeaderPoints = "";
            }

            // Propaguj AnnotScale z bloku-źródła + oblicz offset dist line
            var localOffset = new Vector3d(0, 0, 0);
            if (existingAnnot != null && !string.IsNullOrEmpty(existingAnnot.SourceBlockHandle))
            {
                try
                {
                    long hVal = Convert.ToInt64(existingAnnot.SourceBlockHandle.TrimStart('0').PadLeft(1, '0'), 16);
                    if (db.TryGetObjectId(new Handle(hVal), out ObjectId srcId) && !srcId.IsNull && !srcId.IsErased)
                    {
                        var srcBr = tr.GetObject(srcId, OpenMode.ForRead) as BlockReference;
                        var sourceBlockBarData = srcBr != null ? BarBlockEngine.ReadXData(srcBr) : null;
                        if (sourceBlockBarData != null && srcBr != null)
                        {
                            updatedBar.AnnotScale = sourceBlockBarData.AnnotScale;
                            localOffset = DistLineLocalOffset(annotBr, srcBr.Position, updatedBar.Direction == "X");
                        }
                    }
                }
                catch { }
            }

            updatedBar.Angle = annotBr.Rotation;   // geometria leadera/tekstu zależy od obrotu bloku annotacji
            var btr = (BlockTableRecord)tr.GetObject(annotBr.BlockTableRecord, OpenMode.ForWrite);

            // Wymazanie calej zawartosci BTR
            var ids = new List<ObjectId>();
            foreach (ObjectId oid in btr)
                if (!oid.IsErased) ids.Add(oid);
            foreach (var oid in ids)
                ((DBObject)tr.GetObject(oid, OpenMode.ForWrite)).Erase();

            // Przebudowa — BuildH/V remierzy tekst i ustawia TextLen
            string ltName      = ResolveLinetype(db, tr, "_DOT", "CENTER");
            double armTotalLen;

            if (updatedBar.Direction == "X")
                armTotalLen = BuildHorizontal(tr, btr, db, updatedBar, ltName,
                    updatedBar.LeaderHorizontal, updatedBar.LeaderRight, updatedBar.LeaderUp,
                    localOffset);
            else
                armTotalLen = BuildVertical(tr, btr, db, updatedBar, ltName,
                    leaderVertical: updatedBar.LeaderHorizontal,
                    leaderRight:    updatedBar.LeaderRight,
                    leaderUp:       updatedBar.LeaderUp,
                    offset:         localOffset);

            updatedBar.ArmTotalLen = armTotalLen;
            WriteAnnotXData(annotBr, updatedBar);

            tr.Commit();
            try { annotBr.RecordGraphicsModified(true); } catch { }
        }

        // ----------------------------------------------------------------
        // RebuildDistLineInBtr — p258 ETAP 2: full rebuild dist line + leader + text.
        // Kasuje dist line + doty/strzałki/ticki + leader (ColorIndex=7) + DBText,
        // odbudowuje dist line + leader z nowym pts[0] (anchor na dist line po offsecie).
        // Wywoływane po MOVE/COPY block żeby dist line trzymała się prętów.
        // ----------------------------------------------------------------

        /// <summary>
        /// Full rebuild: usuwa wszystkie entities z BTR annot i odbudowuje
        /// dist line + leader z pts[0] zaktualizowanym do nowej pozycji dist line.
        /// </summary>
        public static void RebuildDistLineInBtr(BlockReference annotBr, BarData barData, Database db,
            Point3d? blockPos = null)
        {
            if (annotBr == null || barData == null || db == null) return;

            using var tr = db.TransactionManager.StartTransaction();
            var btr = tr.GetObject(annotBr.BlockTableRecord, OpenMode.ForWrite) as BlockTableRecord;
            if (btr == null) { tr.Commit(); return; }

            // 1. Kasuj dist line, doty, strzałki, ticki, leader, text — cały BTR.
            var toErase = new List<ObjectId>();
            foreach (ObjectId entId in btr)
            {
                if (entId.IsErased) continue;
                var ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                if (ent is Line || ent is Circle || ent is Hatch || ent is Solid || ent is DBText)
                    toErase.Add(entId);
            }
            foreach (var id in toErase)
            {
                var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                ent?.Erase();
            }

            // 2. Odbuduj dist line + doty/strzałki/ticki
            Vector3d localOffset = new Vector3d(0, 0, 0);
            if (blockPos.HasValue)
                localOffset = DistLineLocalOffset(annotBr, blockPos.Value, barData.Direction == "X");
            barData.Angle = annotBr.Rotation;
            var annotPre = ReadAnnotXData(annotBr);
            if (annotPre != null) barData.ElbowExt = annotPre.ElbowExt;
            string ltName = ResolveLinetype(db, tr, "_DOT", "CENTER");
            BuildDistLineAndDots(tr, btr, barData, ltName, localOffset);

            // p268 — Leader anchor update: pts[0] = nowa pozycja na dist line po offsecie
            var annotBar = ReadAnnotXData(annotBr);
            if (annotBar != null && !string.IsNullOrEmpty(annotBar.LeaderPoints))
            {
                var pts = DecodeLeaderPoints(annotBar.LeaderPoints);
                if (pts != null && pts.Count >= 2)
                {
                    Point3d target = CalcDistLineCenter(barData);
                    pts[0] = new Point3d(target.X + localOffset.X, target.Y + localOffset.Y, 0);
                    annotBar.Angle = annotBr.Rotation;
                    if (NeedsElbow(barData))
                        pts = ApplyElbow(barData, pts, localOffset);   // załamanie zostaje na końcu linii rozkładu
                    annotBar.LeaderPoints = EncodeLeaderPoints(pts);
                    WriteAnnotXData(annotBr, annotBar);
                    BuildMLeaderInBtr(tr, btr, db, annotBar, pts);
                }
            }

            tr.Commit();
        }

        /// <summary>
        /// Przesunięcie linii rozkładu w BTR annotacji: wektor (pozycja bloku prętów − pozycja annotacji)
        /// w UKŁADZIE BLOKU annotacji (uwzględnia obrót), składowa wzdłuż rozkładu.
        /// </summary>
        private static Vector3d DistLineLocalOffset(BlockReference annotBr, Point3d blockPos, bool horizontal)
        {
            var d = (blockPos - annotBr.Position).RotateBy(-annotBr.Rotation, Vector3d.ZAxis);
            return horizontal ? new Vector3d(0, d.Y, 0) : new Vector3d(d.X, 0, 0);
        }

        /// <summary>
        /// Szuka annotacji po hex-stringu handle'a ObjectId — O(1), unikalny klucz.
        /// Uzywane gdy RC_BAR_BLOCK ma zapisany AnnotHandle (od momentu utworzenia annotacji).
        /// </summary>
        private static ObjectId FindAnnotationIdByHandle(Database db, string handleHex)
        {
            try
            {
                long val = Convert.ToInt64(handleHex.TrimStart('0').PadLeft(1, '0'), 16);
                var  h   = new Handle(val);
                if (db.TryGetObjectId(h, out ObjectId id) && !id.IsNull && !id.IsErased)
                    return id;
            }
            catch { }
            return ObjectId.Null;
        }

        private static ObjectId FindAnnotationIdByMark(Database db, string mark)
        {
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId oid in space)
            {
                if (oid.IsErased) continue;
                if (!(tr.GetObject(oid, OpenMode.ForRead) is BlockReference br)) continue;
                var data = ReadAnnotXData(br);
                if (data != null && data.Mark == mark) return oid;
            }
            return ObjectId.Null;
        }

        // ----------------------------------------------------------------
        // AppId
        // ----------------------------------------------------------------

        public static void EnsureAppIdRegistered(Database db)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var regTable = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!regTable.Has(AnnotAppName))
            {
                regTable.UpgradeOpen();
                var rec = new RegAppTableRecord { Name = AnnotAppName };
                regTable.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }
            tr.Commit();
        }

        // ----------------------------------------------------------------
        // XData
        // [0]AppName [1]Mark [2]LayerCode [3]Count [4]Diameter
        // [5]Spacing [6]Direction [7]Position [8]LengthA
        // [9]BarsSpan [10]ArmTotalLen [11]TextLen [12]LeaderHorizontal [13]LeaderRight
        // [14]ArmMidY [15]LeaderUp [16]SourceBlockHandle
        // ----------------------------------------------------------------

        internal static void WriteAnnotXData(Entity entity, BarData bar)
        {
            // Jeśli bar.SourceBlockHandle jest pusty (stary format annotacji bez tego slotu),
            // zachowaj istniejącą wartość z XData zamiast nadpisywać pustym stringiem.
            string sourceHandle = bar.SourceBlockHandle;
            if (string.IsNullOrEmpty(sourceHandle))
            {
                var existing = entity.GetXDataForApplication(AnnotAppName);
                if (existing != null)
                {
                    var ev = existing.AsArray();
                    if (ev.Length >= 17)
                        sourceHandle = XLink.Read(ev[16]);
                }
            }

            entity.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName,  AnnotAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Mark),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.LayerCode),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)bar.Count),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)bar.Diameter),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.Spacing),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Direction),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Position),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.LengthA),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.BarsSpan),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.ArmTotalLen),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.TextLen),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)(bar.LeaderHorizontal ? 1 : 0)),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)(bar.LeaderRight ? 1 : 0)),
                new TypedValue((int)DxfCode.ExtendedDataReal,        !double.IsNaN(bar.ArmMidY) ? bar.ArmMidY : bar.BarsSpan / 2.0),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)(bar.LeaderUp ? 1 : 0)),
                XLink.Write(sourceHandle),                                          // [16] handle 1005
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.LeaderPoints ?? ""), // [17] — punkty leadera "x1,y1;x2,y2;..."
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.SkewEnd),            // [18]
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.SkewStart),          // [19]
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)(bar.CountDisplay ?? -1)), // [20] CountDisplay (-1 = null)
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)(bar.IsLabelManual ? 1 : 0)), // [21] IsLabelManual
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.ElbowExt > 0 ? bar.ElbowExt : -1.0) // [22] ElbowExt (-1 = domyślny)
            );
        }

        public static BarData ReadAnnotXData(Entity entity)
        {
            var xdata = entity.GetXDataForApplication(AnnotAppName);
            if (xdata == null) return null;
            var v = xdata.AsArray();
            if (v.Length < 9) return null;
            var bd = new BarData
            {
                Mark      = (string)v[1].Value,
                LayerCode = (string)v[2].Value,
                Count     = (short)v[3].Value,
                Diameter  = (short)v[4].Value,
                Spacing   = (double)v[5].Value,
                Direction = (string)v[6].Value,
                Position  = (string)v[7].Value,
                LengthA   = (double)v[8].Value
            };
            if (v.Length >= 11)
            {
                bd.BarsSpan    = (double)v[9].Value;
                bd.ArmTotalLen = (double)v[10].Value;
            }
            if (v.Length >= 12) bd.TextLen = (double)v[11].Value;
            if (v.Length >= 13) bd.LeaderHorizontal = (short)v[12].Value == 1;
            if (v.Length >= 14) bd.LeaderRight       = (short)v[13].Value == 1;
            if (v.Length >= 15) bd.ArmMidY   = (double)v[14].Value;
            if (v.Length >= 16) bd.LeaderUp           = (short)v[15].Value == 1;
            if (v.Length >= 17) bd.SourceBlockHandle  = XLink.Read(v[16]);
            if (v.Length >= 18) bd.LeaderPoints       = (string)v[17].Value ?? "";
            if (v.Length >= 19) { try { bd.SkewEnd   = Convert.ToDouble(v[18].Value); } catch { bd.SkewEnd   = 0.0; } }
            if (v.Length >= 20) { try { bd.SkewStart = Convert.ToDouble(v[19].Value); } catch { bd.SkewStart = 0.0; } }
            if (v.Length >= 21)
            {
                try
                {
                    short cd = Convert.ToInt16(v[20].Value);
                    bd.CountDisplay = cd == -1 ? (int?)null : (int)cd;
                }
                catch { bd.CountDisplay = null; }
            }
            else bd.CountDisplay = null;

            if (v.Length >= 22)
            {
                try { bd.IsLabelManual = (short)v[21].Value != 0; }
                catch { bd.IsLabelManual = false; }
            }
            else bd.IsLabelManual = false;
            if (v.Length >= 23)
            {
                try { double e = Convert.ToDouble(v[22].Value); bd.ElbowExt = e > 0 ? e : double.NaN; }
                catch { bd.ElbowExt = double.NaN; }
            }

            return bd;
        }

        public static bool IsAnnotation(Entity entity)
            => entity.GetXDataForApplication(AnnotAppName) != null;

        // ----------------------------------------------------------------
        // UpdateBarLabelCount — suma prętów ze wszystkich rozkładów powiązanych z prętem
        // ----------------------------------------------------------------

        /// <summary>
        /// Aktualizuje tekst MLeadera pręta na podstawie sumy prętów ze wszystkich powiązanych rozkładów.
        /// </summary>
        public static void UpdateBarLabelCount(Database db, string sourceBarHandle,
                                               string markOverride = null)
        {
            if (string.IsNullOrEmpty(sourceBarHandle)) return;

            try
            {
                // Krok 1 — znajdź polilnię pręta
                if (!long.TryParse(sourceBarHandle,
                        System.Globalization.NumberStyles.HexNumber,
                        null, out long srcHVal)) return;
                var srcHandle = new Handle(srcHVal);
                if (!db.TryGetObjectId(srcHandle, out ObjectId srcId) || srcId.IsErased) return;

                using var tr = db.TransactionManager.StartTransaction();

                var srcPline = tr.GetObject(srcId, OpenMode.ForRead)
                    as Teigha.DatabaseServices.Polyline;
                var srcBar = srcPline != null ? SingleBarEngine.ReadBarXData(srcPline) : null;
                if (srcBar == null || string.IsNullOrEmpty(srcBar.LabelHandle))
                { tr.Commit(); return; }

                // Krok 2 — zsumuj pręty ze wszystkich rozkładów powiązanych z tym prętem
                int totalCount = 0;
                var modelSpace = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

                foreach (ObjectId oid in modelSpace)
                {
                    if (oid.IsErased) continue;
                    var br2 = tr.GetObject(oid, OpenMode.ForRead) as BlockReference;
                    if (br2 == null) continue;
                    var barBlock = BarBlockEngine.ReadXData(br2);
                    if (barBlock == null) continue;
                    if (XLink.Same(barBlock.SourceBarHandle, sourceBarHandle))
                    {
                        if (!BarBlockEngine.IsAnnotAlive(db, barBlock.AnnotHandle)) continue;
                        totalCount += barBlock.EffectiveCount;
                    }
                }

                // Krok 3 — zaktualizuj MLeadera
                if (!long.TryParse(srcBar.LabelHandle,
                        System.Globalization.NumberStyles.HexNumber,
                        null, out long lblHVal))
                { tr.Commit(); return; }
                var lblHandle = new Handle(lblHVal);
                if (!db.TryGetObjectId(lblHandle, out ObjectId lblId) || lblId.IsErased)
                { tr.Commit(); return; }

                var ml = tr.GetObject(lblId, OpenMode.ForRead) as MLeader;
                // Etykieta musi należeć do tego pręta (back-link) — kopia pręta bez etykiety
                // nie może nadpisywać liczby na etykiecie oryginału.
                if (ml == null || !XLink.Same(SingleBarEngine.ReadBarHandleFromLabel(ml), srcId.Handle.Value.ToString("X8")))
                { tr.Commit(); return; }
                ml.UpgradeOpen();
                if (ml.ContentType == ContentType.MTextContent)
                {
                    var mt = ml.MText?.Clone() as MText;
                    if (mt != null)
                    {
                        // Wyciągnij pierwsze dwa człony Mark (bez spacing): "H12-01-200" → "H12-01"
                        string rawMark = !string.IsNullOrEmpty(markOverride)
                            ? markOverride
                            : srcBar.Mark;
                        var parts = rawMark.Split('-');
                        string markForLabel = parts.Length >= 2
                            ? $"{parts[0]}-{parts[1]}"
                            : rawMark;

                        mt.Contents = totalCount > 0
                            ? $"{totalCount} {markForLabel}"
                            : markForLabel;
                        ml.MText = mt;
                    }
                }

                tr.Commit();
            }
            catch { }
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private static string ResolveLinetype(Database db, Transaction tr, params string[] preferred)
        {
            var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            foreach (var n in preferred)
                if (lt.Has(n)) return n;
            return "Continuous";
        }

        private static ObjectId GetTextStyleId(Database db)
        {
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var st = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            return st.Has(LayerManager.AnnotTextStyle) ? st[LayerManager.AnnotTextStyle] : db.Textstyle;
        }

        // ----------------------------------------------------------------
        // LeaderPoints — enkodowanie/dekodowanie punktów leadera
        // ----------------------------------------------------------------

        /// <summary>Enkoduje listę punktów do stringa "x1,y1;x2,y2;..."</summary>
        public static string EncodeLeaderPoints(IEnumerable<Point3d> pts)
            => string.Join(";", pts.Select(p =>
                p.X.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "," +
                p.Y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)));

        /// <summary>Dekoduje string "x1,y1;x2,y2;..." do listy Point3d (Z=0).</summary>
        public static List<Point3d> DecodeLeaderPoints(string s)
        {
            var result = new List<Point3d>();
            if (string.IsNullOrEmpty(s)) return result;
            foreach (var seg in s.Split(';'))
            {
                var parts = seg.Split(',');
                // Zgodność wstecz: stare rysunki zapisane na polskim locale mają
                // "1234,5000,678,0000" (przecinek dziesiętny) — 4 części zamiast 2.
                if (parts.Length == 4)
                    parts = new[] { parts[0] + "." + parts[1], parts[2] + "." + parts[3] };
                if (parts.Length >= 2
                    && double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out double x)
                    && double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out double y))
                    result.Add(new Point3d(x, y, 0));
            }
            return result;
        }

    }
}
