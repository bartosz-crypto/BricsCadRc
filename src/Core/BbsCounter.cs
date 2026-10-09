using System.Collections.Generic;
using System.Globalization;
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
        /// Skanuje caly rysunek i zwraca wyniki BBS:
        ///   • stare prety z XData RC_BAR (kazda linia = 1 pret) — jak dotad,
        ///   • POPRAWKI 26: prety RC_SINGLE_BAR (polilinie i prety-bloki: AutoRebar, reczne, 501/502) z iloscia z rozkladow
        ///     RC_BAR_BLOCK z zywym opisem — to samo zrodlo co RC_BBS (BbsDrawingReader.ReadBars), wiec tonaz = BBS
        ///     (dlugosc ciecia BS 8666, dlugosc nadpisana ma pierwszenstwo).
        /// Encja z RC_SINGLE_BAR / RC_BAR_BLOCK nie jest liczona drugi raz jako RC_BAR.
        /// </summary>
        public static List<BbsRow> CountAll(Database db) => CountAll(db, null);

        /// <summary>Jak <see cref="CountAll(Database)"/>; <paramref name="warnings"/> (opcjonalnie) — ostrzezenia odczytu BBS.</summary>
        public static List<BbsRow> CountAll(Database db, List<string> warnings)
        {
            var results = new Dictionary<string, BbsRow>();

            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                // przestrzeń modelu — jak BbsDrawingReader (nowe pręty); dawniej bieżąca przestrzeń (w arkuszu: 0 prętów RC_BAR)
                var space = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

                foreach (ObjectId id in space)
                {
                    var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (entity == null) continue;

                    // POPRAWKI 26: pret / rozklad nowego formatu liczy BbsDrawingReader (bez podwojnego liczenia)
                    if (HasApp(entity, SingleBarEngine.XAppName) || HasApp(entity, BarBlockEngine.XAppName)) continue;

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
                        // W5: długość cięcia jak zestawienie / BBS (formuła kształtu, w górę do 25 mm), nie A
                        try
                        {
                            row.CuttingLength = BarLengthRules.CuttingLength(bar.ShapeCode ?? "00", bar.Diameter,
                                bar.LengthA, bar.LengthB, bar.LengthC, bar.LengthD, bar.LengthE);
                        }
                        catch (System.Exception ex)
                        {
                            // 47 bez C, 64, długość <= 0 — wiersz „brak długości”, masa 0, reszta dalej
                            Log.Error("BbsCounter.CountAll.CuttingLength", ex);
                            row.CuttingLength = double.NaN;
                            row.LengthError   = ex.Message;
                        }
                        results[bar.Mark] = row;
                    }

                    // Kazda linia w rysunku to JEDEN pret (niezaleznie od Count w XData)
                    row.BarLinesInDrawing++;
                }
                tr.Commit();
            }

            // Oblicz tonaz dla kazdego wiersza — W5: z dlugosci ciecia (prety giete — nie z A); brak dlugosci → 0
            foreach (var row in results.Values)
                row.TotalMassKg = BbsCountRules.MassKg(row.BarLinesInDrawing, row.CuttingLength, BarData.GetLinearMass(row.Diameter));

            var rows = results.Values.ToList();

            // POPRAWKI 26: pręty AutoRebar / ręczne — wiersze RC_BBS (ta sama długość cięcia i masa co arkusz BBS)
            try
            {
                var bbs = BbsDrawingReader.ReadBars(db);
                foreach (var r in bbs.Rows)
                    rows.Add(BbsCountRules.FromBbs(r.BarMark, r.Diameter, r.ShapeCode, r.Layer == BbsLayer.Top, r.Dims[0],
                                                   r.Length, r.Total, r.Note, r.LinearMass));
                warnings?.AddRange(bbs.Warnings);
            }
            catch (System.Exception ex)
            {
                Log.Error("BbsCounter.CountAll.ReadBars", ex);
                warnings?.Add("Odczyt prętów RC_SINGLE_BAR / rozkładów nie powiódł się: " + ex.Message);
            }

            return rows.OrderBy(r => r.LayerCode).ThenBy(r => r.Mark).ToList();
        }

        private static bool HasApp(Entity e, string app)
        {
            using (var rb = e.GetXDataForApplication(app)) return rb != null;
        }

        /// <summary>Wypisuje wyniki do konsoli BricsCAD.</summary>
        public static void PrintToConsole(List<BbsRow> rows)
        {
            var editor = Application.DocumentManager.MdiActiveDocument?.Editor;
            if (editor == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("\n=== RC SLAB - BBS (wg BS8666) ===");
            // W5: L = dlugosc ciecia (jak zestawienie / BBS), z niej masa
            sb.AppendLine($"{"Mark",-18} {"Pos",-5} {"Shape",-7} {"Dia",-5} {"L [mm]",-10} {"Szt",-6} {"Masa [kg]",-12}");
            sb.AppendLine(new string('-', 70));

            double totalKg = 0;
            foreach (var r in rows)
            {
                string len = double.IsNaN(r.CuttingLength) ? "brak dl." : r.CuttingLength.ToString("F0", CultureInfo.InvariantCulture);
                sb.AppendLine($"{r.Mark,-18} {r.Position,-5} {r.ShapeCode,-7} Ø{r.Diameter,-4} {len,-10} {r.BarLinesInDrawing,-6} {r.TotalMassKg,-12:F2}");
                totalKg += r.TotalMassKg;
            }
            foreach (var r in rows.Where(x => x.LengthError != null))
                sb.AppendLine($"[RC SLAB] UWAGA: {r.Mark} (ksztalt {r.ShapeCode}): brak dlugosci — {r.LengthError}");
            foreach (var r in rows.Where(x => x.LengthError == null && !string.IsNullOrEmpty(x.Note)))
                sb.AppendLine($"[RC SLAB] {r.Mark} (ksztalt {r.ShapeCode}): {r.Note}");

            sb.AppendLine(new string('-', 70));
            sb.AppendLine($"RAZEM: {totalKg:F2} kg  ({totalKg / 1000.0:F3} t)");

            editor.WriteMessage(sb.ToString());
        }
    }
}
