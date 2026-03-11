using System;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using BricsCadRc.Core;
using BricsCadRc.Dialogs;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.Commands
{
    /// <summary>
    /// RC_BAR        — FLOW 1: tworzenie pojedynczego preta w widoku elewacji.
    /// RC_DISTRIBUTION — FLOW 2: rozkladanie preta w planie (rzut z gory).
    /// </summary>
    public class BarCommands
    {
        // ================================================================
        //  FLOW 1 — RC_BAR
        // ================================================================

        [CommandMethod("RC_BAR", CommandFlags.Modal)]
        public void CreateBar()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            // --- Krok 1: Dialog "Reinforcement – Elevation" ---
            var elevDlg = new BarElevationDialog();
            if (Application.ShowModalWindow(elevDlg) != true) return;
            var barData = elevDlg.Result;
            if (barData == null) return;

            // --- Krok 2 & 3: Wstaw pret w Model Space ---
            var ptOpts = new PromptPointOptions("\nClick insertion point for bar: ");
            var ptResult = ed.GetPoint(ptOpts);
            if (ptResult.Status != PromptStatus.OK) return;
            Point3d insertPt = ptResult.Value;

            // --- Krok 4: Dialog "Reinforcement description" (numer pozycji) ---
            int suggestedNr = PositionCounter.GetNext(db);
            var posDlg = new BarPositionDialog(barData, suggestedNr);
            if (Application.ShowModalWindow(posDlg) != true) return;

            int posNr = posDlg.PositionNumber;
            barData.Mark = $"H{barData.Diameter}-{posNr:D2}";

            // --- Krok 5: Wstaw polilinie i etykiete ---
            SingleBarEngine.PlaceBar(db, barData, insertPt);

            ed.WriteMessage($"\n[RC SLAB] Bar placed: {barData.Mark}  (pos {posNr}, Ø{barData.Diameter}, shape {barData.ShapeCode}, A={barData.LengthA:F0} mm)\n");
        }

        // ================================================================
        //  FLOW 2 — RC_DISTRIBUTION
        // ================================================================

        [CommandMethod("RC_DISTRIBUTION", CommandFlags.Modal)]
        public void Distribution()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            // --- Krok 1: Wybierz pret (polilinia RC_SINGLE_BAR lub BlockReference RC_BAR_BLOCK) ---
            var selOpts = new PromptEntityOptions("\nSelect bar to distribute (RC_BAR): ");
            selOpts.SetRejectMessage("\nNot a reinforcement bar.");
            selOpts.AddAllowedClass(typeof(Polyline), true);
            var selResult = ed.GetEntity(selOpts);
            if (selResult.Status != PromptStatus.OK) return;

            BarData sourceBar = null;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = (Entity)tr.GetObject(selResult.ObjectId, OpenMode.ForRead);
                sourceBar = SingleBarEngine.ReadBarXData(ent);
                tr.Commit();
            }

            if (sourceBar == null)
            {
                ed.WriteMessage("\nSelected entity has no RC bar data. Use RC_BAR command to create bars.\n");
                return;
            }

            // --- Krok 2: Dialog "Reinforcement detailing" (statyczny) ---
            var detailDlg = new ReinfDetailingDialog();
            if (Application.ShowModalWindow(detailDlg) != true) return;

            // --- Krok 3: Zakres rozkładu (dwa punkty) ---
            var pt1Opts = new PromptPointOptions("\nFirst point of distribution range: ");
            var pt1Result = ed.GetPoint(pt1Opts);
            if (pt1Result.Status != PromptStatus.OK) return;
            Point3d pt1 = pt1Result.Value;

            var pt2Opts = new PromptCornerOptions("\nSecond point of distribution range: ", pt1);
            var pt2Result = ed.GetCorner(pt2Opts);
            if (pt2Result.Status != PromptStatus.OK) return;
            Point3d pt2 = pt2Result.Value;

            double minX = Math.Min(pt1.X, pt2.X), maxX = Math.Max(pt1.X, pt2.X);
            double minY = Math.Min(pt1.Y, pt2.Y), maxY = Math.Max(pt1.Y, pt2.Y);

            // --- Krok 4: Pozycja pierwszego preta (otulina) ---
            var covOpts = new PromptDistanceOptions("\nPosition of first bar (mm) <40>: ")
            {
                DefaultValue  = 40,
                AllowNone     = true,
                AllowNegative = false,
                AllowZero     = false
            };
            var covResult = ed.GetDistance(covOpts);
            double cover = covResult.Status == PromptStatus.OK ? covResult.Value : 40;

            // --- Krok 5: Rozstaw ---
            var spacOpts = new PromptDistanceOptions("\nSpacing (mm) <200>: ")
            {
                DefaultValue  = 200,
                AllowNone     = true,
                AllowNegative = false,
                AllowZero     = false
            };
            var spacResult = ed.GetDistance(spacOpts);
            double spacing = spacResult.Status == PromptStatus.OK ? spacResult.Value : 200;

            // Kierunek: B1/T1 → poziome (X), B2/T2 → pionowe (Y)
            bool horizontal = (sourceBar.LayerCode == "B1" || sourceBar.LayerCode == "T1");

            // Granice po otulinie (cover tylko w kierunku rozkladania, dlugosc preta = pelna szerokosc)
            double x0, y0, x1Bound, y1Bound;
            if (horizontal)
            {
                x0      = minX;            // dlugosc preta — bez otuliny
                x1Bound = maxX;
                y0      = minY + cover;    // pierwsze pret na +cover od krawedzi
                y1Bound = maxY - cover;    // ostatni pret na -cover od krawedzi
            }
            else
            {
                x0      = minX + cover;
                x1Bound = maxX - cover;
                y0      = minY;
                y1Bound = maxY;
            }

            if (x0 >= x1Bound || y0 >= y1Bound)
            {
                ed.WriteMessage("\nRange is too small for given cover. Aborted.\n");
                return;
            }

            // Auto count
            double rawSpan = horizontal ? (y1Bound - y0) : (x1Bound - x0);
            int    autoCount = Math.Max(1, (int)(rawSpan / spacing) + 1);

            // Numer pozycji: z marka preta ("H12-13" → 13), lub nowy
            int posNr = SingleBarEngine.ExtractPosNr(sourceBar.Mark);
            if (posNr <= 0) posNr = PositionCounter.GetNext(db);

            string baseMark = $"H{sourceBar.Diameter}-{posNr:D2}-{(int)spacing}";

            // --- Krok 6: Dialog "Reinforcement description" dla rozkładu ---
            sourceBar.Spacing = spacing;
            var descDlg = new DistributionDescDialog(sourceBar, autoCount, spacing, baseMark);
            if (Application.ShowModalWindow(descDlg) != true) return;

            // Zastosuj nadpisania z dialogu
            int    finalCount   = descDlg.BarCount;
            double finalSpacing = descDlg.BarSpacing;
            string suffix       = descDlg.Suffix;

            // Zaktualizuj barsData
            sourceBar.Spacing   = finalSpacing;
            sourceBar.Count     = finalCount;
            sourceBar.Direction = horizontal ? "X" : "Y";
            sourceBar.Mark      = baseMark + (string.IsNullOrEmpty(suffix) ? "" : " " + suffix);
            sourceBar.Cover     = cover;

            // --- Krok 7: Pozycja etykiety (opcjonalna — Enter = auto) ---
            var lblOpts = new PromptPointOptions("\nClick label position (Enter = automatic): ")
            {
                AllowNone = true
            };
            var lblResult = ed.GetPoint(lblOpts);
            // lblResult.Status == PromptStatus.None = Enter (auto pozycja), OK = kliknieto

            // --- Krok 8: Generuj blok rozkladu pretow ---
            var barResult = BarBlockEngine.GenerateFromBounds(
                db, x0, y0, x1Bound, y1Bound,
                sourceBar, horizontal, posNr);

            if (!barResult.IsValid)
            {
                ed.WriteMessage("\n[RC SLAB] Failed to generate distribution — check range and spacing.\n");
                return;
            }

            // --- Krok 9: Annotacja ---
            AnnotationEngine.CreateLeader(db, barResult, sourceBar, horizontal, posNr);

            ed.WriteMessage(
                $"\n[RC SLAB] Distribution created: {finalCount} bars  {sourceBar.Mark}\n" +
                $"[RC SLAB] Layer: {LayerManager.GetLayerName(sourceBar.LayerCode)} | " +
                $"Direction: {(horizontal ? "X" : "Y")} | " +
                $"Spacing: {finalSpacing} mm | Cover: {cover} mm\n");
        }
    }
}
