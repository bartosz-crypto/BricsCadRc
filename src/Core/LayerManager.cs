using Teigha.Colors;
using Teigha.DatabaseServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Tworzy i zarzadza warstwami oraz stylami tekstu RC SLAB w rysunku.
    /// Kolory warstw zgodne z konwencja ASD (AutoCAD Structural Detailing).
    /// </summary>
    public static class LayerManager
    {
        // Nazwy warstw
        public const string BotLayer1 = "RC-SLAB-BOT-01";   // B1 — dolna kierunek 1
        public const string BotLayer2 = "RC-SLAB-BOT-02";   // B2 — dolna kierunek 2
        public const string TopLayer1 = "RC-SLAB-TOP-01";   // T1 — gorna kierunek 1
        public const string TopLayer2 = "RC-SLAB-TOP-02";   // T2 — gorna kierunek 2
        public const string AnnotLayer = "RC-SLAB-ANNOT";   // etykiety pretow (zolty — jak ASD)
        public const string DimsLayer  = "RC-SLAB-DIMS";    // wymiarowanie (cyjan)

        /// <summary>Nazwa stylu tekstu zgodna z ASD ("style1", font romans.shx).</summary>
        public const string AnnotTextStyle = "style1";

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

        /// <summary>
        /// Tworzy wszystkie warstwy i style tekstu RC SLAB jesli nie istnieja.
        /// Wywolywac przed kazdym zapisem geometrii do rysunku.
        /// </summary>
        public static void EnsureLayersExist(Database db)
        {
            using var tr = db.TransactionManager.StartTransaction();

            var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            // Kolory pretow jak w ASD — preta na niebiesko, etykiety zolto
            EnsureLayer(tr, layerTable, BotLayer1,  5);   // niebieski  (B1 — ACI 5)
            EnsureLayer(tr, layerTable, BotLayer2,  4);   // cyjan      (B2 — ACI 4)
            EnsureLayer(tr, layerTable, TopLayer1,  1);   // czerwony   (T1 — ACI 1)
            EnsureLayer(tr, layerTable, TopLayer2,  6);   // magenta    (T2 — ACI 6)
            EnsureLayer(tr, layerTable, AnnotLayer, 2);   // zolty      (etykiety — ACI 2, jak ASD)
            EnsureLayer(tr, layerTable, DimsLayer,  4);   // cyjan      (wymiary)

            // Styl tekstu "style1" z fontem romans.shx — identyczny jak ASD
            EnsureTextStyle(tr, db);

            tr.Commit();
        }

        // ----------------------------------------------------------------
        // Warstwy
        // ----------------------------------------------------------------

        private static void EnsureLayer(Transaction tr, LayerTable table, string name, short colorIndex)
        {
            if (table.Has(name)) return;

            table.UpgradeOpen();
            var layer = new LayerTableRecord
            {
                Name  = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex)
            };
            table.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
        }

        // ----------------------------------------------------------------
        // Style tekstu
        // ----------------------------------------------------------------

        /// <summary>
        /// Tworzy styl tekstu "style1" z fontem romans.shx (jak ASD).
        /// Jesli romans.shx nie jest dostepny, uzywa domyslnego fonta BricsCAD.
        /// </summary>
        private static void EnsureTextStyle(Transaction tr, Database db)
        {
            var styleTable = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (styleTable.Has(AnnotTextStyle)) return;

            styleTable.UpgradeOpen();

            var style = new TextStyleTableRecord
            {
                Name     = AnnotTextStyle,
                FileName = "romans.shx",   // font ASD — musi byc dostepny w BricsCAD
                TextSize = 0.0,            // 0 = wysokosc ustawiana per-obiekt
                XScale   = 0.85,           // lekko skondensowany jak w ASD
            };

            styleTable.Add(style);
            tr.AddNewlyCreatedDBObject(style, true);
        }
    }
}
