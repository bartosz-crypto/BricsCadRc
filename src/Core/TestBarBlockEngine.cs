// PLAN C ETAP 1 — TEST; od etapu 3 cienka nakładka na SingleBarBlock

using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// PLAN C ETAP 1 — pręt testowy RC_TEST_BARBLOCK (H12, kształt 00) jako BlockReference.
    /// Od etapu 3 silnik to SingleBarBlock (wszystkie kształty); tu zostaje: TestMark, tworzenie pręta testowego
    /// od p1 do p2, rozpoznanie v2 i opis (Info) — sygnatury bez zmian (testy dymne etapów 1–2 przez refleksję).
    ///
    /// Świadome odstępstwa próby: mark stały "H12-T" (bez PositionCounter), bez etykiety MLeader,
    /// pręt poza numeracją i BBS (ExtractPosNr("H12-T") = 0).
    /// </summary>
    internal static class TestBarBlockEngine
    {
        internal const string TestMark = "H12-T"; // ExtractPosNr → 0, poza numeracją

        /// <summary>BlockReference z RC_SINGLE_BAR i [14] = 2 (Int16).</summary>
        internal static bool IsV2(Entity e)
        {
            try
            {
                return BarEntity.KindOf(e) == BarKind.Block;
            }
            catch (System.Exception ex)
            {
                Log.Error("TestBarBlockEngine.IsV2", ex);
                return false;
            }
        }

        /// <summary>[0]–[13] przez BarXData.Write, [14] = 2 podmienione albo dopisane (nigdy zdublowane).</summary>
        internal static void WriteXDataV2(Entity e, BarData bar)
            => BarXData.Write(e, bar, BarRecordRules.BlockFormatVersion);

        /// <summary>
        /// Wstawia pręt testowy H12-T od p1 do p2 (WCS) przez SingleBarBlock.CreateCore
        /// (BTR *U albo nazwany RC_BAR_TEST_nnn). Długość = odległość zaokrąglona do 1 mm, min. max(2d, 50).
        /// Zwraca id, nazwę BTR i długość.
        /// </summary>
        internal static (ObjectId id, string btrName, double length) Create(Database db, Point3d p1, Point3d p2, bool anonymous)
        {
            SingleBarEngine.EnsureAppIdRegistered(db);
            LayerManager.EnsureLayersExist(db);

            const int diameter = 12;
            p1 = new Point3d(p1.X, p1.Y, 0);
            p2 = new Point3d(p2.X, p2.Y, 0);
            double dist  = p1.DistanceTo(p2);
            double len   = Math.Max(BarLengthRules.MinSegment(diameter), Math.Round(dist, MidpointRounding.AwayFromZero));
            double angle = dist > 1e-9 ? Math.Atan2(p2.Y - p1.Y, p2.X - p1.X) : 0.0;

            var bar = new BarData
            {
                Mark        = TestMark,
                Diameter    = diameter,
                ShapeCode   = "00",
                LengthA     = len,
                // W4g (BS 8666:2020 kl. 7.2): długość całkowita wg formuły 00 — A w górę do 25 mm (jak po gripie)
                TotalLength = BarLengthRules.TotalLengthFor("00", new[] { len }, diameter),
                LayerCode   = "B1",
                Position    = "BOT",
            };

            // Kształt 00: LocalStart = (0,0) → punkt wstawienia = początek osi = p1
            var id = SingleBarBlock.CreateCore(db, bar, p1, angle, anonymous, out string btrName);
            return (id, btrName, len);
        }

        /// <summary>Opis pręta-bloku do linii poleceń (opcja Info).</summary>
        internal static string Describe(Transaction tr, BlockReference br) => SingleBarBlock.Describe(tr, br);

        internal static string F(double d) => SingleBarBlock.F(d);
    }
}
