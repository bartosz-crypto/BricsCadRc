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
