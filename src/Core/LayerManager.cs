using Teigha.Colors;
using Teigha.DatabaseServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Tworzy i zarzadza warstwami RC SLAB w rysunku.
    /// </summary>
    public static class LayerManager
    {
        // Nazwy warstw
        public const string BotLayer1 = "RC-SLAB-BOT-01";   // B1
        public const string BotLayer2 = "RC-SLAB-BOT-02";   // B2
        public const string TopLayer1 = "RC-SLAB-TOP-01";   // T1
        public const string TopLayer2 = "RC-SLAB-TOP-02";   // T2
        public const string AnnotLayer = "RC-SLAB-ANNOT";   // etykiety
        public const string DimsLayer  = "RC-SLAB-DIMS";    // wymiarowanie

        public static string GetLayerName(string layerCode)
        {
            return layerCode switch
            {
                "B1" => BotLayer1,
                "B2" => BotLayer2,
                "T1" => TopLayer1,
                "T2" => TopLayer2,
                _    => BotLayer1
            };
        }

        /// <summary>Tworzy wszystkie warstwy RC SLAB jesli nie istnieja.</summary>
        public static void EnsureLayersExist(Database db)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            EnsureLayer(tr, layerTable, BotLayer1, 3);   // zielony
            EnsureLayer(tr, layerTable, BotLayer2, 4);   // cyjan
            EnsureLayer(tr, layerTable, TopLayer1, 1);   // czerwony
            EnsureLayer(tr, layerTable, TopLayer2, 6);   // magenta
            EnsureLayer(tr, layerTable, AnnotLayer, 7);  // bialy
            EnsureLayer(tr, layerTable, DimsLayer,  2);  // zolty

            tr.Commit();
        }

        private static void EnsureLayer(Transaction tr, LayerTable table, string name, short colorIndex)
        {
            if (table.Has(name)) return;

            table.UpgradeOpen();
            var layer = new LayerTableRecord
            {
                Name = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex)
            };
            table.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
        }
    }
}
