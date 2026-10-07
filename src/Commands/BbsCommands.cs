using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Bricscad.ApplicationServices;
using Teigha.Runtime;
using BricsCadRc.Core;
using BricsCadRc.Dialogs;

namespace BricsCadRc.Commands
{
    public static class BbsCommands
    {
        /// <summary>
        /// RC_BBS — BBS (.xls, szablon Speedeck) prosto z rysunku: pozycje z rozkładów z opisem, podgląd z ostrzeżeniami,
        /// przypisanie layoutów A1-BL do BOTTOM/TOP, nagłówek z bloku tytułowego, akcesoria z SLAB AREA.
        /// </summary>
        [CommandMethod("RC_BBS")]
        public static void GenerateBbs()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;
            const string T = "[RC BBS]";

            try
            {
                // 1) pręty z rysunku
                var bars = BbsDrawingReader.ReadBars(db);
                if (bars.Rows.Count == 0)
                {
                    ed.WriteMessage($"\n{T} Brak prętów z rozkładami (z opisem) w rysunku.");
                    foreach (var w in bars.Warnings.Take(10)) ed.WriteMessage($"\n  {w}");
                    ed.WriteMessage("\n");
                    return;
                }
                ed.WriteMessage($"\n{T} Pozycji: {bars.Rows.Count} (z {bars.Distributions} rozkładów)" +
                                (bars.Warnings.Count > 0 ? $", uwag: {bars.Warnings.Count}" : "") + ".");

                // 2) layouty i nagłówek
                var layouts = BbsDrawingReader.ReadLayouts(db);
                var ctx = BbsGenerationContext.BuildInitial(layouts);
                if (layouts.Count == 0)
                {
                    // Rysunek bez arkuszy A1-BL — jeden „wirtualny” layout, żeby dało się wygenerować BBS
                    ctx.Assignments.Add(new BbsLayoutAssignment
                    {
                        Layout = new BbsLayoutInfo { LayoutName = "(brak A1-BL)", DrawingNumber = "" },
                        Assignment = BbsLayerAssignment.BottomAndTop
                    });
                    bars.Warnings.Add("Brak layoutów z blokiem A1-BL — nagłówek (Contract, adres, Drg) uzupełnij ręcznie.");
                }

                // 3) akcesoria z notatek płyty (jak ASD: 1 szt. / 2 m² + 10 %; DK z notatki albo z grubości)
                string hint;
                var notes = BbsDrawingReader.ReadSlabNotes(db);
                if (notes.HystoolsType != null) ctx.HystoolsType = notes.HystoolsType;
                else if (notes.ThicknessMm == 225) ctx.HystoolsType = "DK90";
                else if (notes.ThicknessMm == 300) ctx.HystoolsType = "DK165";
                if (notes.Areas.Count > 0)
                {
                    ctx.SlabAreaM2 = notes.Areas[0];
                    int? q = BbsGenerationContext.SuggestAccessoryQty(ctx.SlabAreaM2);
                    if (q.HasValue) ctx.TricTrakQty = ctx.HystoolsQty = q.Value.ToString(CultureInfo.InvariantCulture);
                    hint = string.Format(CultureInfo.InvariantCulture,
                        "Proponowane {0} szt. z SLAB AREA = {1:0.00} m² (1 szt. / 2 m² + 10 %) — można zmienić.", q, ctx.SlabAreaM2);
                    if (notes.Areas.Count > 1)
                        hint += " Uwaga: w rysunku jest kilka wartości SLAB AREA (" +
                                string.Join(", ", notes.Areas.Select(a => a.ToString("0.00", CultureInfo.InvariantCulture))) + ") — przyjęto pierwszą.";
                }
                else hint = "Nie znaleziono SLAB AREA w notatkach — wpisz ilości ręcznie.";
                hint += " HYSTOOLS: " + (notes.HystoolsType != null ? "typ z notatki rysunku."
                                        : notes.ThicknessMm.HasValue ? $"typ z grubości płyty {notes.ThicknessMm} mm." : "typ domyślny.");

                // 4) dialog
                string suggested = BbsXlsGenerator.SuggestOutputPath(doc.Name);
                var dlg = new BbsGeneratorDialog(ctx, bars.Rows, bars.Warnings, suggested, BbsSettings.TemplatePath, hint);
                if (Application.ShowModalWindow(dlg) != true) { ed.WriteMessage($"\n{T} Anulowano.\n"); return; }

                BbsSettings.TemplatePath = dlg.TemplatePath;

                // 5) zapis
                BbsGenerateResult res;
                try
                {
                    res = BbsXlsGenerator.Generate(dlg.Result, bars.Rows, dlg.TemplatePath, dlg.OutputPath);
                }
                catch (IOException ex)
                {
                    Log.Error("RC_BBS.Write", ex);
                    ed.WriteMessage($"\n{T} Nie można zapisać pliku (otwarty w Excelu?): {ex.Message}\n");
                    System.Windows.MessageBox.Show("Nie można zapisać pliku BBS — zamknij go w Excelu i spróbuj ponownie.\n\n" + ex.Message,
                        "BBS", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                if (!res.Success)
                {
                    ed.WriteMessage($"\n{T} {res.Message}\n");
                    System.Windows.MessageBox.Show(res.Message, "BBS", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                ed.WriteMessage($"\n{T} Zapisano: {res.OutputPath}\n{T} {res.Message}\n");
                if (System.Windows.MessageBox.Show($"Zapisano BBS:\n{res.OutputPath}\n\n{res.Message}\n\nOtworzyć plik?",
                        "BBS", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information) == System.Windows.MessageBoxResult.Yes)
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(res.OutputPath) { UseShellExecute = true }); }
                    catch (System.Exception ex) { Log.Error("RC_BBS.Open", ex); }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_BBS", ex);
                ed.WriteMessage($"\n{T} Błąd: {ex.Message}\n");
            }
        }
    }
}
