using System;
using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using BricsCadRc.Core;
using BricsCadRc.Dialogs;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Teigha.Runtime;
using Polyline = Teigha.DatabaseServices.Polyline;

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

            // --- Walidacja: czy numer pozycji jest wolny ---
            var usedNrs = PositionCounter.GetUsedPositionNumbers(db);
            while (usedNrs.Contains(posNr))
            {
                int nextFree = PositionCounter.GetNextFreeFrom(usedNrs, posNr + 1);
                ed.WriteMessage($"\n[RC SLAB] Position {posNr} is already in use. Next free: {nextFree}.");

                var nrOpts = new PromptIntegerOptions(
                    $"\nAccept position {nextFree} [Enter] or enter new number: ")
                {
                    AllowNone  = true,
                    LowerLimit = 1,
                    UpperLimit = 9999
                };
                var nrResult = ed.GetInteger(nrOpts);
                if (nrResult.Status == PromptStatus.Cancel) return;
                posNr = nrResult.Status == PromptStatus.None ? nextFree : nrResult.Value;
            }

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

            // --- Krok 3: Zakres rozkładu — pierwszy punkt krawędzi ---
            var pt1Opts = new PromptPointOptions("\nFirst point along edge: ");
            var pt1Result = ed.GetPoint(pt1Opts);
            if (pt1Result.Status != PromptStatus.OK) return;
            Point3d pt1 = pt1Result.Value;

            // FEATURE F: Drugi punkt z podglądem prętów (TransientManager, nie prostokąt)
            var jig = new DistributionJig(pt1, sourceBar.LengthA);
            var jigResult = ed.Drag(jig);
            jig.ClearTransients();   // wyczyść transienty jiga — flip-preview przejmuje ekran
            if (jigResult.Status != PromptStatus.OK) return;
            Point3d pt2         = jig.SecondPoint;
            bool edgeHorizontal = jig.EdgeHorizontal;

            // FEATURE G: flip jako osobny krok po kliknięciu pt2
            // Pokazujemy podgląd z aktualnym stanem, S toggleuje, Enter zatwierdza
            bool isFlipped = false;
            var flipTransients = new List<Line>();
            while (true)
            {
                ClearBarPreview(flipTransients);
                DrawFlipPreview(pt1, pt2, edgeHorizontal, isFlipped,
                    sourceBar.LengthA, flipTransients);

                var flipOpts = new PromptKeywordOptions(
                    isFlipped ? "\nFlip: pręty po drugiej stronie [S=cofnij/Enter=zatwierdź]: "
                              : "\nFlip: [S=odwróć stronę/Enter=zatwierdź]: ")
                {
                    AllowNone = true
                };
                flipOpts.Keywords.Add("S");
                var flipRes = ed.GetKeywords(flipOpts);

                if (flipRes.Status == PromptStatus.Cancel)
                {
                    ClearBarPreview(flipTransients);
                    return;
                }
                if (flipRes.Status != PromptStatus.OK) break;   // Enter = zatwierdź
                if (flipRes.StringResult == "S") isFlipped = !isFlipped;
            }
            ClearBarPreview(flipTransients);

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

            // Kierunek prętów + granice (zależne od cover, NIE od spacing — obliczane tu)
            bool horizontal = !edgeHorizontal;
            double x0, y0, x1Bound, y1Bound;
            if (edgeHorizontal)
            {
                double edgeY = (pt1.Y + pt2.Y) / 2.0;
                x0      = Math.Min(pt1.X, pt2.X) + cover;
                x1Bound = Math.Max(pt1.X, pt2.X) - cover;
                if (isFlipped) { y0 = edgeY - sourceBar.LengthA; y1Bound = edgeY; }
                else           { y0 = edgeY;                     y1Bound = edgeY + sourceBar.LengthA; }
            }
            else
            {
                double edgeX = (pt1.X + pt2.X) / 2.0;
                y0      = Math.Min(pt1.Y, pt2.Y) + cover;
                y1Bound = Math.Max(pt1.Y, pt2.Y) - cover;
                if (isFlipped) { x0 = edgeX - sourceBar.LengthA; x1Bound = edgeX; }
                else           { x0 = edgeX;                     x1Bound = edgeX + sourceBar.LengthA; }
            }

            if (x0 >= x1Bound || y0 >= y1Bound)
            {
                ed.WriteMessage("\nRange is too small for given cover. Aborted.\n");
                return;
            }

            // --- Krok 5: Rozstaw z live preview (FEATURE H) ---

            double spacing = 200.0;
            var spacingTransients = new List<Line>();

            // Pętla: rysuj → zapytaj → jeśli Enter (brak wartości) → wyjdź do dialogu
            // UWAGA: nie ustawiamy DefaultValue — BricsCAD zwraca OK z defaultem zamiast None,
            //        co powoduje nieskończoną pętlę. Brak DefaultValue → Enter = PromptStatus.None.
            while (true)
            {
                // Rysuj preview z aktualnym spacingiem (na początku pętli jak w pseudokodzie)
                ClearBarPreview(spacingTransients);
                DrawBarPreview(horizontal, x0, y0, x1Bound, y1Bound, spacing, spacingTransients);

                var spacOpts = new PromptDistanceOptions($"\nSpacing (mm) <{(int)spacing}>: ")
                {
                    AllowNone     = true,
                    AllowNegative = false,
                    AllowZero     = false
                    // Brak DefaultValue — Enter zwraca None, nie OK-z-defaultem
                };
                var spacResult = ed.GetDistance(spacOpts);

                if (spacResult.Status == PromptStatus.Cancel)
                {
                    ClearBarPreview(spacingTransients);
                    return;
                }
                if (spacResult.Status != PromptStatus.OK) break;  // Enter (None) = zatwierdź

                spacing = spacResult.Value;  // nowa wartość → pętla rysuje od nowa
            }
            ClearBarPreview(spacingTransients);

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
            var annotResult = AnnotationEngine.CreateLeader(db, barResult, sourceBar, horizontal, posNr);

            // Zapisz handle annotacji w XData bloku pretow — unikalny klucz dla SyncAnnotation
            if (annotResult.BlockRefId != ObjectId.Null)
                BarBlockEngine.LinkAnnotation(db, barResult.BlockRefId, annotResult.BlockRefId);

            ed.WriteMessage(
                $"\n[RC SLAB] Distribution created: {finalCount} bars  {sourceBar.Mark}\n" +
                $"[RC SLAB] Layer: {LayerManager.GetLayerName(sourceBar.LayerCode)} | " +
                $"Direction: {(horizontal ? "X" : "Y")} | " +
                $"Spacing: {finalSpacing} mm | Cover: {cover} mm\n");
        }

        // ================================================================
        //  Helpers — TransientManager preview
        // ================================================================

        /// <summary>
        /// FEATURE G: rysuje podgląd flip — linie prostopadłe do krawędzi
        /// po stronie wybranej przez isFlipped. Używane przez pętlę flip po jigu.
        /// </summary>
        private static void DrawFlipPreview(
            Point3d pt1, Point3d pt2, bool edgeHorizontal, bool flipped,
            double barLength, List<Line> transients)
        {
            const double cover = 40.0, spacing = 200.0;
            var tm    = TransientManager.CurrentTransientManager;
            var vpIds = new IntegerCollection();

            if (edgeHorizontal)
            {
                double edgeY   = (pt1.Y + pt2.Y) / 2.0;
                double x0      = Math.Min(pt1.X, pt2.X) + cover;
                double x1Bound = Math.Max(pt1.X, pt2.X) - cover;
                double yStart  = flipped ? edgeY - barLength : edgeY;
                double yEnd    = flipped ? edgeY             : edgeY + barLength;
                if (x0 >= x1Bound) return;
                for (double x = x0; x <= x1Bound + 0.5; x += spacing)
                {
                    double xc = Math.Min(x, x1Bound);
                    AddPreviewLine(tm, vpIds, new Point3d(xc, yStart, 0), new Point3d(xc, yEnd, 0), transients);
                    if (xc >= x1Bound) break;
                }
            }
            else
            {
                double edgeX   = (pt1.X + pt2.X) / 2.0;
                double y0      = Math.Min(pt1.Y, pt2.Y) + cover;
                double y1Bound = Math.Max(pt1.Y, pt2.Y) - cover;
                double xStart  = flipped ? edgeX - barLength : edgeX;
                double xEnd    = flipped ? edgeX             : edgeX + barLength;
                if (y0 >= y1Bound) return;
                for (double y = y0; y <= y1Bound + 0.5; y += spacing)
                {
                    double yc = Math.Min(y, y1Bound);
                    AddPreviewLine(tm, vpIds, new Point3d(xStart, yc, 0), new Point3d(xEnd, yc, 0), transients);
                    if (yc >= y1Bound) break;
                }
            }
            try { Application.UpdateScreen(); } catch { }
        }

        private static void DrawBarPreview(bool horizontal,
            double x0, double y0, double x1, double y1,
            double spacing, List<Line> transients)
        {
            var tm    = TransientManager.CurrentTransientManager;
            var vpIds = new IntegerCollection();

            if (horizontal)
            {
                // Pręty poziome: linie od x0 do x1, rozmieszczone co spacing w Y
                for (double y = y0; y <= y1 + 0.5; y += spacing)
                {
                    double yc = Math.Min(y, y1);
                    AddPreviewLine(tm, vpIds, new Point3d(x0, yc, 0), new Point3d(x1, yc, 0), transients);
                    if (yc >= y1) break;
                }
            }
            else
            {
                // Pręty pionowe: linie od y0 do y1, rozmieszczone co spacing w X
                for (double x = x0; x <= x1 + 0.5; x += spacing)
                {
                    double xc = Math.Min(x, x1);
                    AddPreviewLine(tm, vpIds, new Point3d(xc, y0, 0), new Point3d(xc, y1, 0), transients);
                    if (xc >= x1) break;
                }
            }
            try { Application.UpdateScreen(); } catch { }
        }

        private static void AddPreviewLine(TransientManager tm, IntegerCollection vpIds,
            Point3d p1, Point3d p2, List<Line> transients)
        {
            var line = new Line(p1, p2) { ColorIndex = 5 };
            try
            {
                tm.AddTransient(line, TransientDrawingMode.DirectTopmost, 128, vpIds);
                transients.Add(line);
            }
            catch { line.Dispose(); }
        }

        private static void ClearBarPreview(List<Line> transients)
        {
            if (transients.Count == 0) return;
            var tm    = TransientManager.CurrentTransientManager;
            var vpIds = new IntegerCollection();
            foreach (var line in transients)
            {
                try { tm.EraseTransient(line, vpIds); line.Dispose(); } catch { }
            }
            transients.Clear();
        }

        // ================================================================
        //  RC_UPDATE_BAR — aktualizuje długość prętów po edycji polilinii
        // ================================================================

        [CommandMethod("RC_UPDATE_BAR", CommandFlags.Modal)]
        public void UpdateBar()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            // Wybierz polilinie preta
            var selOpts = new PromptEntityOptions("\nSelect bar polyline to update (RC_BAR): ");
            selOpts.SetRejectMessage("\nNot a polyline — select a bar created with RC_BAR.");
            selOpts.AddAllowedClass(typeof(Polyline), true);
            var selResult = ed.GetEntity(selOpts);
            if (selResult.Status != PromptStatus.OK) return;

            // Odczytaj geometrie i XData
            BarData bar      = null;
            double newLength = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pline = tr.GetObject(selResult.ObjectId, OpenMode.ForRead) as Polyline;
                if (pline == null) { ed.WriteMessage("\nNot a polyline.\n"); tr.Commit(); return; }
                bar = SingleBarEngine.ReadBarXData(pline);
                if (bar == null) { ed.WriteMessage("\nNo RC bar data on selected polyline.\n"); tr.Commit(); return; }
                newLength = pline.Length;
                tr.Commit();
            }

            double oldLength = bar.LengthA;
            ed.WriteMessage($"\n[RC_UPDATE_BAR] Mark={bar.Mark}  old={oldLength:F0} mm  new={newLength:F0} mm\n");

            // Zaktualizuj XData na polilinii
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pline = tr.GetObject(selResult.ObjectId, OpenMode.ForWrite) as Polyline;
                bar.LengthA = newLength;
                SingleBarEngine.WriteXData(pline, bar);
                tr.Commit();
            }

            // Znajdz i przerysuj powiazane rozklady
            int posNr = SingleBarEngine.ExtractPosNr(bar.Mark);
            var distIds = BarBlockEngine.FindDistributionsByPosNr(db, posNr);
            ed.WriteMessage($"[RC_UPDATE_BAR] Found {distIds.Count} distribution(s) for pos {posNr}.\n");

            int updated = 0;
            foreach (var id in distIds)
                if (BarBlockEngine.UpdateBarLength(db, id, newLength)) updated++;

            try { doc.SendStringToExecute("REGEN\n", true, false, false); } catch { }
            ed.WriteMessage($"[RC_UPDATE_BAR] Done: {updated}/{distIds.Count} distributions updated. LengthA: {oldLength:F0} → {newLength:F0} mm\n");
        }
    }
}
