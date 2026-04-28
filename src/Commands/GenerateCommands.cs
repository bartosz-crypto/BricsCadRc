using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using BricsCadRc.Core;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.Commands
{
    public class GenerateCommands
    {
        // ----------------------------------------------------------------
        // RC_GENERATE_SLAB — generuje prety z polilinii obrysu plyty
        // ----------------------------------------------------------------

        [CommandMethod("RC_GENERATE_SLAB", CommandFlags.Modal)]
        public void GenerateSlab()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            // 1. Wybierz polilinie obrysu
            var selOpts = new PromptEntityOptions("\nWybierz obrys plyty (zamknieta polilinia): ");
            selOpts.SetRejectMessage("\nTo nie jest polilinia.");
            selOpts.AddAllowedClass(typeof(Polyline), true);
            var selResult = ed.GetEntity(selOpts);
            if (selResult.Status != PromptStatus.OK) return;

            // 2. Pozycja (BOT / TOP)
            var kwPos = new PromptKeywordOptions("\nPozycja zbrojenia [BOT/TOP] <BOT>: ");
            kwPos.Keywords.Add("BOT");
            kwPos.Keywords.Add("TOP");
            kwPos.Keywords.Default = "BOT";
            kwPos.AllowNone = true;
            var posResult = ed.GetKeywords(kwPos);
            string position = (posResult.Status == PromptStatus.OK)
                ? posResult.StringResult : "BOT";

            // 3. Kod warstwy (B1/B2 dla BOT, T1/T2 dla TOP)
            string layer1 = position == "BOT" ? "B1" : "T1";
            string layer2 = position == "BOT" ? "B2" : "T2";
            var kwLayer = new PromptKeywordOptions($"\nKod warstwy [{layer1}/{layer2}] <{layer1}>: ");
            kwLayer.Keywords.Add(layer1);
            kwLayer.Keywords.Add(layer2);
            kwLayer.Keywords.Default = layer1;
            kwLayer.AllowNone = true;
            var layerResult = ed.GetKeywords(kwLayer);
            string layerCode = (layerResult.Status == PromptStatus.OK)
                ? layerResult.StringResult : layer1;

            // 4. Kierunek pretow
            var kwDir = new PromptKeywordOptions("\nKierunek pretow [X/Y] <X>: ");
            kwDir.Keywords.Add("X");
            kwDir.Keywords.Add("Y");
            kwDir.Keywords.Default = "X";
            kwDir.AllowNone = true;
            var dirResult = ed.GetKeywords(kwDir);
            bool horizontal = (dirResult.Status != PromptStatus.OK)
                || dirResult.StringResult == "X";

            // 5. Srednica
            var diaOpts = new PromptIntegerOptions("\nSrednica preta [mm] <12>: ")
            {
                DefaultValue = 12,
                AllowNone    = true,
                LowerLimit   = 6,
                UpperLimit   = 50
            };
            var diaResult = ed.GetInteger(diaOpts);
            int diameter = diaResult.Status == PromptStatus.OK ? diaResult.Value : 12;

            // 6. Rozstaw
            var spacOpts = new PromptDistanceOptions("\nRozstaw pretow [mm] <200>: ")
            {
                DefaultValue    = 200,
                AllowNone       = true,
                AllowNegative   = false,
                AllowZero       = false
            };
            var spacResult = ed.GetDistance(spacOpts);
            double spacing = spacResult.Status == PromptStatus.OK ? spacResult.Value : 200;

            // 7. Otulina
            double defaultCover = position == "BOT" ? SlabGenerator.CoverBot : SlabGenerator.CoverTop;
            var covOpts = new PromptDistanceOptions($"\nOtulina [mm] <{defaultCover}>: ")
            {
                DefaultValue  = defaultCover,
                AllowNone     = true,
                AllowNegative = false,
                AllowZero     = false
            };
            var covResult = ed.GetDistance(covOpts);
            double cover = covResult.Status == PromptStatus.OK ? covResult.Value : defaultCover;

            // 8. Numer pozycji — Peek bez zapisu; CommitUsed po pomyślnym wygenerowaniu
            int posNr = PositionCounter.Peek(db);

            // 9. Zbuduj BarData (Mark w formacie ASD: H12-01-200)
            var bar = new BarData
            {
                Diameter  = diameter,
                Spacing   = spacing,
                ShapeCode = "00",
                Position  = position,
                LayerCode = layerCode,
                Direction = horizontal ? "X" : "Y"
            };
            bar.Mark = BarData.FormatMark(diameter, posNr, spacing, 2);

            // 10. Modul pretow — RC_SLAB_BARS_nnn (prety only, styl ASD RBCR_EN_CONSTLINEMODULE)
            var barResult = BarBlockEngine.Generate(db, selResult.ObjectId, bar, horizontal, cover, posNr);

            if (!barResult.IsValid)
            {
                ed.WriteMessage("\n[RC SLAB] Nie wygenerowano zadnych pretow — sprawdz rozmiar polilinii i otuline.\n");
                return;
            }

            PositionCounter.CommitUsed(db, posNr);

            // 11. Modul annotacji — RC_ANNOT_nnn (dist line + romby + ramie + tekst)
            //     Romby w pozycjach wzglednych (i*spacing) — niezalezne od world coords pretow.
            //     Mozna przesuwac annotacje wzd. X bez utraty wyrownania z pretami.
            AnnotationEngine.CreateLeader(db, barResult, bar, horizontal, posNr);

            ed.WriteMessage($"\n[RC SLAB] Wygenerowano {bar.Count} pretow. Mark: {bar.Mark}\n");
            ed.WriteMessage($"[RC SLAB] Warstwa: {LayerManager.GetLayerName(layerCode)} | Kierunek: {(horizontal ? "X" : "Y")} | Rozstaw: {spacing} mm | Otulina: {cover} mm\n");
            ed.WriteMessage($"[RC SLAB] Bloki: RC_SLAB_BARS_{posNr:D3} + RC_ANNOT_{posNr:D3} | Opis: {bar.Count} {bar.Mark} {bar.LayerCode}\n");
        }

        // ----------------------------------------------------------------
        // Stare komendy (prostokat — zachowane dla kompatybilnosci)
        // ----------------------------------------------------------------

        [CommandMethod("RC_GENERATE_BOT", CommandFlags.Modal)]
        public void GenerateBottom()
        {
            GenerateBars("BOT");
        }

        [CommandMethod("RC_GENERATE_TOP", CommandFlags.Modal)]
        public void GenerateTop()
        {
            GenerateBars("TOP");
        }

        private void GenerateBars(string position)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            ed.WriteMessage($"\n[RC SLAB] Generowanie pretow — warstwa {position}\n");

            // 1. Pobierz obszar (dwa punkty narozne)
            var ptOpts1 = new PromptPointOptions("\nPunkt startowy obszaru: ");
            var ptResult1 = ed.GetPoint(ptOpts1);
            if (ptResult1.Status != PromptStatus.OK) return;

            var ptOpts2 = new PromptCornerOptions("\nPunkt koncowy obszaru: ", ptResult1.Value);
            var ptResult2 = ed.GetCorner(ptOpts2);
            if (ptResult2.Status != PromptStatus.OK) return;

            Point3d p0 = ptResult1.Value;
            Point3d p1 = ptResult2.Value;

            // 2. Zapytaj o srednice
            var diaOpts = new PromptIntegerOptions("\nSrednica preta [mm] <16>: ")
            {
                DefaultValue = 16,
                AllowNone = true
            };
            var diaResult = ed.GetInteger(diaOpts);
            int diameter = diaResult.Status == PromptStatus.OK ? diaResult.Value : 16;

            // 3. Zapytaj o rozstaw
            var spacOpts = new PromptDistanceOptions("\nRozstaw pretow [mm] <200>: ")
            {
                DefaultValue = 200,
                AllowNone = true
            };
            var spacResult = ed.GetDistance(spacOpts);
            double spacing = spacResult.Status == PromptStatus.OK ? spacResult.Value : 200;

            // 4. Kierunek pretow
            var kw = new PromptKeywordOptions("\nKierunek pretow [Poziome/Pionowe] <P>: ");
            kw.Keywords.Add("Poziome");
            kw.Keywords.Add("Pionowe");
            kw.Keywords.Default = "Poziome";
            kw.AllowNone = true;
            var kwResult = ed.GetKeywords(kw);
            bool horizontal = kwResult.Status != PromptStatus.OK
                || kwResult.StringResult != "Pionowe";

            // 5. Kod warstwy
            // B1/T1 = kierunek 1, B2/T2 = kierunek 2
            string layer1 = position == "BOT" ? "B1" : "T1";
            string layer2 = position == "BOT" ? "B2" : "T2";

            var kw2 = new PromptKeywordOptions($"\nKod warstwy [{layer1}/{layer2}] <{layer1}>: ");
            kw2.Keywords.Add(layer1);
            kw2.Keywords.Add(layer2);
            kw2.Keywords.Default = layer1;
            kw2.AllowNone = true;
            var kwResult2 = ed.GetKeywords(kw2);
            string layerCode = kwResult2.Status == PromptStatus.OK
                ? kwResult2.StringResult
                : layer1;

            // 6. Dlugosc preta (A)
            double lengthA = horizontal
                ? System.Math.Abs(p1.X - p0.X)
                : System.Math.Abs(p1.Y - p0.Y);

            // 7. Zbuduj BarData
            var bar = new BarData
            {
                Diameter  = diameter,
                Spacing   = spacing,
                ShapeCode = "00",
                LengthA   = lengthA,
                Position  = position,
                LayerCode = layerCode
            };
            bar.Mark = $"H{diameter}-{(int)spacing}-{layerCode}";

            // 8. Generuj
            var ids = BarGenerator.GenerateLayout(db, bar, p0, p1, horizontal);

            ed.WriteMessage($"\n[RC SLAB] Wygenerowano {ids.Count} pretow. Mark: {bar.Mark}\n");
            ed.WriteMessage($"[RC SLAB] Tonaz ukladu: {bar.TotalMassKg:F2} kg ({bar.TotalMassKg / 1000.0:F3} t)\n");
        }
    }
}
