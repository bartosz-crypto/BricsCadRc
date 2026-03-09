using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Zlicza prety w rysunku i oblicza tonaz wg BS8666.
    /// </summary>
    public static class BbsCounter
    {
        /// <summary>
        /// Skanuje caly rysunek, zbiera wszystkie prety z XData RC_BAR
        /// i zwraca pogrupowane wyniki BBS.
        /// </summary>
        public static List<BbsRow> CountAll(Database db)
        {
            var results = new Dictionary<string, BbsRow>();

            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

            foreach (ObjectId id in space)
            {
                var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (entity == null) continue;

                var bar = XDataHelper.Read(entity);
                if (bar == null) continue;

                // Grupuj wg Mark (oznaczenia preta)
                if (!results.TryGetValue(bar.Mark, out var row))
                {
                    row = new BbsRow
                    {
                        Mark      = bar.Mark,
                        Diameter  = bar.Diameter,
                        ShapeCode = bar.ShapeCode,
                        Position  = bar.Position,
                        LayerCode = bar.LayerCode,
                        LengthA   = bar.LengthA
                    };
                    results[bar.Mark] = row;
                }

                // Kazda linia w rysunku to JEDEN pret (niezaleznie od Count w XData)
                row.BarLinesInDrawing++;
            }

            // Oblicz tonaz dla kazdego wiersza
            foreach (var row in results.Values)
            {
                row.TotalMassKg = row.BarLinesInDrawing
                    * (row.LengthA / 1000.0)
                    * BarData.GetLinearMass(row.Diameter);
            }

            return results.Values.OrderBy(r => r.LayerCode).ThenBy(r => r.Mark).ToList();
        }

        /// <summary>Wypisuje wyniki do konsoli BricsCAD.</summary>
        public static void PrintToConsole(List<BbsRow> rows)
        {
            var editor = Application.DocumentManager.MdiActiveDocument?.Editor;
            if (editor == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("\n=== RC SLAB - BBS (wg BS8666) ===");
            sb.AppendLine($"{"Mark",-18} {"Pos",-5} {"Shape",-7} {"Dia",-5} {"L [mm]",-10} {"Szt",-6} {"Masa [kg]",-12}");
            sb.AppendLine(new string('-', 70));

            double totalKg = 0;
            foreach (var r in rows)
            {
                sb.AppendLine($"{r.Mark,-18} {r.Position,-5} {r.ShapeCode,-7} Ø{r.Diameter,-4} {r.LengthA,-10:F0} {r.BarLinesInDrawing,-6} {r.TotalMassKg,-12:F2}");
                totalKg += r.TotalMassKg;
            }

            sb.AppendLine(new string('-', 70));
            sb.AppendLine($"RAZEM: {totalKg:F2} kg  ({totalKg / 1000.0:F3} t)");

            editor.WriteMessage(sb.ToString());
        }
    }

    public class BbsRow
    {
        public string Mark { get; set; }
        public int Diameter { get; set; }
        public string ShapeCode { get; set; }
        public string Position { get; set; }
        public string LayerCode { get; set; }
        public double LengthA { get; set; }
        public int BarLinesInDrawing { get; set; }
        public double TotalMassKg { get; set; }
    }
}
