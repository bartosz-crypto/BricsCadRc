using System;
using System.IO;
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
        public const string AnnotLayer  = "RC-SLAB-ANNOT";    // etykiety pretow (zolty — jak ASD)
        public const string DimsLayer   = "RC-SLAB-DIMS";    // wymiarowanie (cyjan)
        public const string LeaderLayer = "RC-SLAB-LEADERS"; // linie prowadzace + doty (bialy — ACI 7)

        /// <summary>Nazwa stylu tekstu annotacji ("StandardNarrow", font txt.shx, XScale=0.70).</summary>
        public const string AnnotTextStyle = "StandardNarrow";

        public static string GetLayerName(string layerCode)
        {
            return layerCode switch
            {
                "B1"  => BotLayer1,
                "B2"  => BotLayer2,
                "T1"  => TopLayer1,
                "T2"  => TopLayer2,
                "BOT" => BotLayer1,   // nowy format — Bottom Layer
                "TOP" => TopLayer1,   // nowy format — Top Layer
                _     => BotLayer1
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
            EnsureLayer(tr, layerTable, BotLayer2,  5);   // niebieski  (B2 — ACI 5, jak B1 — ASD konwencja)
            EnsureLayer(tr, layerTable, TopLayer1,  1);   // czerwony   (T1 — ACI 1)
            EnsureLayer(tr, layerTable, TopLayer2,  1);   // czerwony   (T2 — ACI 1, jak T1)
            EnsureLayer(tr, layerTable, AnnotLayer,  2);   // zolty      (etykiety — ACI 2, jak ASD)
            EnsureLayer(tr, layerTable, DimsLayer,   4);   // cyjan      (wymiary)
            EnsureLayer(tr, layerTable, LeaderLayer, 7);   // bialy      (linie prowadzace — ACI 7)

            // Styl tekstu "style1" z fontem romans.shx — identyczny jak ASD
            EnsureTextStyle(tr, db);

            // Linetype _DOT dla linii prowadzacych
            EnsureLinetype(tr, db, "_DOT");

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
        // Linetypes
        // ----------------------------------------------------------------

        /// <summary>
        /// Laduje linetype z pliku acad.lin (lub acadiso.lin) jesli nie jest jeszcze zaladowany.
        /// Fallback: CENTER, potem CONTINUOUS (zawsze dostepny w BricsCAD).
        /// </summary>
        public static void EnsureLinetype(Transaction tr, Database db, string ltName)
        {
            var ltTable = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltTable.Has(ltName)) return;

            // Sprobuj zaladowac z pliku definicji linetype
            string[] candidates = { "acad.lin", "acadiso.lin" };
            foreach (var fileName in candidates)
            {
                string path = FindSupportFile(db, fileName);
                if (path == null) continue;
                try
                {
                    db.LoadLineTypeFile(ltName, path);
                    return;
                }
                catch { /* nie znaleziono w tym pliku — proba kolejnego */ }
            }

            // Fallback 1: CENTER (standardowy w BricsCAD)
            if (!ltTable.Has("CENTER"))
            {
                foreach (var fileName in candidates)
                {
                    string path = FindSupportFile(db, fileName);
                    if (path == null) continue;
                    try { db.LoadLineTypeFile("CENTER", path); break; }
                    catch { }
                }
            }
            // Fallback 2: nic — CONTINUOUS bedzie uzyte przez AnnotationEngine
        }

        /// <summary>Szuka pliku w katalogu dokumentu, potem w PATH.</summary>
        private static string FindSupportFile(Database db, string fileName)
        {
            // Katalog pliku rysunku
            if (!string.IsNullOrEmpty(db.Filename))
            {
                string dir = Path.GetDirectoryName(db.Filename);
                if (!string.IsNullOrEmpty(dir))
                {
                    string candidate = Path.Combine(dir, fileName);
                    if (File.Exists(candidate)) return candidate;
                }
            }

            // Standardowe lokalizacje BricsCAD / AutoCAD Support
            string[] searchDirs =
            {
                @"C:\Program Files\Bricsys\BricsCAD V24 en_US\Support",
                @"C:\Program Files\Bricsys\BricsCAD V23 en_US\Support",
                @"C:\Program Files\Bricsys\BricsCAD V25 en_US\Support",
                @"C:\Program Files\AutoCAD 2024\Support",
                @"C:\Program Files\AutoCAD 2023\Support",
            };
            foreach (var dir in searchDirs)
            {
                string path = Path.Combine(dir, fileName);
                if (File.Exists(path)) return path;
            }

            return null;
        }

        // ----------------------------------------------------------------
        // Style tekstu
        // ----------------------------------------------------------------

        /// <summary>
        /// Tworzy styl tekstu "StandardNarrow" (font txt.shx, XScale=0.70).
        /// Jesli txt.shx nie jest dostepny, uzywa domyslnego fonta BricsCAD.
        /// </summary>
        private static void EnsureTextStyle(Transaction tr, Database db)
        {
            var styleTable = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (styleTable.Has(AnnotTextStyle)) return;

            styleTable.UpgradeOpen();

            var style = new TextStyleTableRecord
            {
                Name     = AnnotTextStyle,
                FileName = "txt.shx",   // standardowy font CAD (wbudowany w BricsCAD)
                TextSize = 0.0,         // 0 = wysokosc ustawiana per-obiekt
                XScale   = 0.70,        // waski — Standard Narrow
            };

            styleTable.Add(style);
            tr.AddNewlyCreatedDBObject(style, true);
        }
    }
}
