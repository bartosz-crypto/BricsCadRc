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

            // --- Krok 5: Wstaw polilinię pręta ---
            ObjectId barId = SingleBarEngine.PlaceBar(db, barData, insertPt);

            // --- Krok 6: MLeader — użytkownik klika pozycję tekstu etykiety ---
            // Prompt z bazą w centrum bbox pręta; smart arrowTip obliczany po kliknięciu
            Point3d barCenter;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pline = tr.GetObject(barId, OpenMode.ForRead) as Teigha.DatabaseServices.Polyline;
                barCenter = pline != null
                    ? pline.GetPointAtParameter(pline.EndParam / 2.0)
                    : insertPt;
                tr.Commit();
            }

            Point3d defaultTextPt = new Point3d(barCenter.X - 50, barCenter.Y + 150, 0);
            var lblOpts = new PromptPointOptions("\nClick label position [Enter=auto]: ")
            {
                AllowNone     = true,
                UseBasePoint  = true,
                BasePoint     = barCenter,
                UseDashedLine = true
            };
            var lblResult = ed.GetPoint(lblOpts);
            Point3d textPt = lblResult.Status == PromptStatus.OK
                ? lblResult.Value
                : defaultTextPt;

            // Smart arrowTip — krawędź pręta najbliżej tekstu
            Point3d arrowTip;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                arrowTip = SingleBarEngine.GetBarArrowTip(barId, barData, textPt, tr);
                tr.Commit();
            }

            string labelText = $"{posNr:D2} {barData.Mark}";
            ObjectId labelId = SingleBarEngine.PlaceBarLabel(db, arrowTip, textPt, labelText, barId);

            // Zapisz handle MLeadera w XData pręta (umożliwia RC_FIX_LABEL)
            if (!labelId.IsNull)
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var barEnt = tr.GetObject(barId, OpenMode.ForWrite) as Entity;
                    barData.LabelHandle = labelId.Handle.ToString();
                    SingleBarEngine.WriteXData(barEnt, barData);
                    tr.Commit();
                }
            }

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

            // --- Krok 2: Dialog "Reinforcement detailing" ---
            var detailDlg = new ReinfDetailingDialog();
            if (Application.ShowModalWindow(detailDlg) != true) return;

            // viewLength — Auto z GetViewLength lub Manual z kliknięcia segmentu
            double viewLength;
            if (detailDlg.IsManualViewingDirection)
            {
                viewLength = GetViewLengthManual(ed, db, selResult.ObjectId, sourceBar);
                if (viewLength <= 0)   // Escape → fallback do Auto
                {
                    sourceBar.ViewingDirection = "Auto";
                    viewLength = GetViewLength(sourceBar);
                }
            }
            else
            {
                viewLength = GetViewLength(sourceBar);
            }

            ed.WriteMessage(
                $"\n[RC_DIST] ShapeCode={sourceBar.ShapeCode}" +
                $"  ParamValues=[{sourceBar.LengthA:F0},{sourceBar.LengthB:F0}," +
                $"{sourceBar.LengthC:F0},{sourceBar.LengthD:F0},{sourceBar.LengthE:F0}]" +
                $"  TotalLength={sourceBar.TotalLength:F0}" +
                $"  viewLength={viewLength:F0}  viewingDir={sourceBar.ViewingDirection}\n");

            // --- Krok 3: Zakres rozkładu — pierwszy punkt krawędzi ---
            var pt1Opts = new PromptPointOptions("\nFirst point along edge: ");
            var pt1Result = ed.GetPoint(pt1Opts);
            if (pt1Result.Status != PromptStatus.OK) return;
            Point3d pt1 = pt1Result.Value;

            // FEATURE F: Drugi punkt z podglądem prętów (TransientManager, nie prostokąt)
            var jig = new DistributionJig(pt1, viewLength);
            var jigResult = ed.Drag(jig);
            if (jigResult.Status != PromptStatus.OK)
            {
                jig.ClearTransients();   // Escape podczas jigu
                return;
            }
            Point3d pt2         = jig.SecondPoint;
            bool edgeHorizontal = jig.EdgeHorizontal;

            // liveTransients — widoczne od flip aż do GenerateFromBounds (FEATURE F)
            var liveTransients = new List<Line>();

            // FEATURE G: flip jako osobny krok po kliknięciu pt2
            bool isFlipped = false;

            // Inicjalizacja: narysuj najpierw, potem usuń transienty jiga → brak luki wizualnej
            DrawFlipPreview(pt1, pt2, edgeHorizontal, isFlipped, viewLength, liveTransients);
            jig.ClearTransients();

            while (true)
            {
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
                    ClearBarPreview(liveTransients);
                    return;
                }
                if (flipRes.Status != PromptStatus.OK) break;   // Enter = zatwierdź

                if (flipRes.StringResult == "S")
                {
                    isFlipped = !isFlipped;
                    // Double-buffer: nowe linie dodane PRZED usunięciem starych → brak flicker
                    var oldTransients = liveTransients;
                    liveTransients = new List<Line>();
                    DrawFlipPreview(pt1, pt2, edgeHorizontal, isFlipped, viewLength, liveTransients);
                    ClearBarPreview(oldTransients);
                }
            }
            // liveTransients widoczne — NIE czyścimy

            // --- Krok 3b: Prompty dla symboli końcowych (L-bar / U-bar) ---
            // Pobieramy kategorię kształtu raz, przed pytaniami.
            string symbolSide = "Right";
            string symbolDir  = "Up";
            var symCat = BarBlockEngine.GetSymbolCategory(sourceBar.ShapeCode);

            if (symCat == BarBlockEngine.BarSymbolCategory.LBar)
            {
                // Strona haczyka
                var sideOpts = new PromptKeywordOptions("\nHook side [Left/Right] <Right>: ")
                    { AllowNone = true };
                sideOpts.Keywords.Add("Left");
                sideOpts.Keywords.Add("Right");
                var sideRes = ed.GetKeywords(sideOpts);
                if (sideRes.Status == PromptStatus.Cancel)
                    { ClearBarPreview(liveTransients); return; }
                if (sideRes.Status == PromptStatus.OK)
                    symbolSide = sideRes.StringResult;

                // Kierunek haczyka (prostopadle do linii rozkładu)
                var dirOpts = new PromptKeywordOptions("\nHook direction [Up/Down] <Up>: ")
                    { AllowNone = true };
                dirOpts.Keywords.Add("Up");
                dirOpts.Keywords.Add("Down");
                var dirRes = ed.GetKeywords(dirOpts);
                if (dirRes.Status == PromptStatus.Cancel)
                    { ClearBarPreview(liveTransients); return; }
                if (dirRes.Status == PromptStatus.OK)
                    symbolDir = dirRes.StringResult;
            }
            else if (symCat == BarBlockEngine.BarSymbolCategory.UBar)
            {
                // Domyślna strona: shape 21 (symetryczny U) → Both; pozostałe → Left
                symbolSide = (sourceBar.ShapeCode == "21") ? "Both" : "Left";

                var sideOpts = new PromptKeywordOptions(
                    $"\nCircle side [Left/Right/Both] <{symbolSide}>: ")
                    { AllowNone = true };
                sideOpts.Keywords.Add("Left");
                sideOpts.Keywords.Add("Right");
                sideOpts.Keywords.Add("Both");
                var sideRes = ed.GetKeywords(sideOpts);
                if (sideRes.Status == PromptStatus.Cancel)
                    { ClearBarPreview(liveTransients); return; }
                if (sideRes.Status == PromptStatus.OK)
                    symbolSide = sideRes.StringResult;
            }

            sourceBar.SymbolSide      = symbolSide;
            sourceBar.SymbolDirection = symbolDir;

            if (BarBlockEngine.GetSymbolCategory(sourceBar.ShapeCode) == BarBlockEngine.BarSymbolCategory.LBar)
                ed.WriteMessage(
                    $"\n[DEBUG L-BAR] shapeCode={sourceBar.ShapeCode}" +
                    $" ParamValues=[{sourceBar.LengthA:F0},{sourceBar.LengthB:F0},{sourceBar.LengthC:F0}]" +
                    $" viewLength={viewLength:F0} symbolSide={symbolSide}\n");

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
                if (isFlipped) { y0 = edgeY - viewLength; y1Bound = edgeY; }
                else           { y0 = edgeY;              y1Bound = edgeY + viewLength; }
            }
            else
            {
                double edgeX = (pt1.X + pt2.X) / 2.0;
                y0      = Math.Min(pt1.Y, pt2.Y) + cover;
                y1Bound = Math.Max(pt1.Y, pt2.Y) - cover;
                if (isFlipped) { x0 = edgeX - viewLength; x1Bound = edgeX; }
                else           { x0 = edgeX;              x1Bound = edgeX + viewLength; }
            }

            if (x0 >= x1Bound || y0 >= y1Bound)
            {
                ClearBarPreview(liveTransients);
                ed.WriteMessage("\nRange is too small for given cover. Aborted.\n");
                return;
            }

            // --- Krok 5: Rozstaw z live preview ---
            // UWAGA: brak DefaultValue — Enter zwraca None, nie OK-z-defaultem (nieskończona pętla)
            double spacing = 200.0;

            // Inicjalizacja: narysuj pierwszy podgląd spacing, usuń flip preview (double-buffer)
            {
                var old = liveTransients;
                liveTransients = new List<Line>();
                DrawBarPreview(horizontal, x0, y0, x1Bound, y1Bound, spacing, liveTransients);
                ClearBarPreview(old);
            }

            while (true)
            {
                var spacOpts = new PromptDistanceOptions($"\nSpacing (mm) <{(int)spacing}>: ")
                {
                    AllowNone     = true,
                    AllowNegative = false,
                    AllowZero     = false
                };
                var spacResult = ed.GetDistance(spacOpts);

                if (spacResult.Status == PromptStatus.Cancel)
                {
                    ClearBarPreview(liveTransients);
                    return;
                }
                if (spacResult.Status != PromptStatus.OK) break;  // Enter (None) = zatwierdź

                // Nowy spacing: double-buffer → brak flicker
                spacing = spacResult.Value;
                var oldT = liveTransients;
                liveTransients = new List<Line>();
                DrawBarPreview(horizontal, x0, y0, x1Bound, y1Bound, spacing, liveTransients);
                ClearBarPreview(oldT);
            }
            // liveTransients widoczne — NIE czyścimy

            // Auto count
            double rawSpan = horizontal ? (y1Bound - y0) : (x1Bound - x0);
            int    autoCount = Math.Max(1, (int)(rawSpan / spacing) + 1);

            // Numer pozycji: z marka preta ("H12-13" → 13), lub nowy
            int posNr = SingleBarEngine.ExtractPosNr(sourceBar.Mark);
            if (posNr <= 0) posNr = PositionCounter.GetNext(db);

            string baseMark = $"H{sourceBar.Diameter}-{posNr:D2}-{(int)spacing}";

            // --- Krok 6: Dialog "Reinforcement description" (liveTransients widoczne w tle) ---
            sourceBar.Spacing = spacing;
            var descDlg = new DistributionDescDialog(sourceBar, autoCount, spacing, baseMark);
            if (Application.ShowModalWindow(descDlg) != true)
            {
                ClearBarPreview(liveTransients);
                return;
            }

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

            // Usuń live preview — zaraz pojawią się finalne pręty
            ClearBarPreview(liveTransients);

            // --- Krok 7: Generuj blok rozkladu pretow ---
            var barResult = BarBlockEngine.GenerateFromBounds(
                db, x0, y0, x1Bound, y1Bound,
                sourceBar, horizontal, posNr);

            if (!barResult.IsValid)
            {
                ed.WriteMessage("\n[RC SLAB] Failed to generate distribution — check range and spacing.\n");
                return;
            }

            // --- Krok 8: 3-stage label placement ---
            double barsSpan = (double)(finalCount - 1) * finalSpacing;

            bool     leaderHorizontal = false;
            bool     leaderRight      = true;
            bool     leaderUp         = true;
            Point3d? insertOverride   = null;

            // ── 3-etapowy JIG — wspólna logika dla X-bars i Y-bars ──────────
            // X-bars (horizontal=true):  minFixed=minY, basePos=maxX, freeAxis=X
            // Y-bars (horizontal=false): minFixed=minX, basePos=maxY, freeAxis=Y
            {
                double minFixed = horizontal ? barResult.MinPoint.Y : barResult.MinPoint.X;
                double basePos  = horizontal ? barResult.MaxPoint.X : barResult.MaxPoint.Y;
                double dotR     = AnnotationEngine.DotRadius;

                // ── ETAP 1: pozycja dist line ──────────────────────────────
                var jig1 = new AnnotLabelPositionJig(
                    minFixed, basePos, barsSpan, finalSpacing, finalCount, horizontal, dotR);
                var res1 = ed.Drag(jig1);
                jig1.ClearTransients();
                if (res1.Status == PromptStatus.Cancel) return;

                if (res1.Status == PromptStatus.OK)
                {
                    double  labelPos   = jig1.LabelPos;
                    Point3d distCenter = horizontal
                        ? new Point3d(labelPos,                        minFixed + barsSpan / 2.0, 0)
                        : new Point3d(minFixed + barsSpan / 2.0, labelPos,                        0);
                    insertOverride = horizontal
                        ? new Point3d(labelPos, minFixed, 0)
                        : new Point3d(minFixed,  labelPos, 0);

                    // ── ETAP 2: kierunek etykiety ──────────────────────────
                    var jig2 = new AnnotLabelDirectionJig(
                        distCenter, labelPos, minFixed, barsSpan, horizontal, dotR, finalCount, finalSpacing);
                    var res2 = ed.Drag(jig2);
                    jig2.ClearTransients();
                    if (res2.Status == PromptStatus.Cancel) return;

                    if (res2.Status == PromptStatus.OK)
                    {
                        var direction = jig2.Direction;
                        var kinkPt    = jig2.KinkPt;
                        leaderUp = (direction != LabelDirection.Down);

                        // "Prosty" kierunek = prostopadle do dist line — brak ETAP 3
                        // X-bars: Left/Right są prostopadłe do pionowej dist line
                        // Y-bars: Up/Down są prostopadłe do poziomej dist line
                        bool isSimple = horizontal
                            ? (direction == LabelDirection.Left  || direction == LabelDirection.Right)
                            : (direction == LabelDirection.Up    || direction == LabelDirection.Down);

                        if (isSimple)
                        {
                            leaderHorizontal  = true;
                            leaderRight       = horizontal
                                ? (direction == LabelDirection.Right)
                                : true;  // dla Y-bars: nieużywane przy leaderVertical=true
                            sourceBar.ArmMidY = barsSpan / 2.0;
                        }
                        else // wzdłuż dist line: ETAP 3 — złamanie lub prosta
                        {
                            // Dla Y-bars wzdłuż dist line: ustaw leaderRight z kierunku Left/Right
                            if (!horizontal)
                                leaderRight = (direction == LabelDirection.Right);

                            var jig3 = new AnnotLabelBendJig(
                                distCenter, kinkPt,
                                labelPos, minFixed, barsSpan, horizontal, dotR, finalCount, finalSpacing);
                            var res3 = ed.Drag(jig3);
                            jig3.ClearTransients();
                            if (res3.Status == PromptStatus.Cancel) return;

                            if (res3.Status == PromptStatus.OK)
                            {
                                // Klik = złamanie
                                leaderHorizontal = true;
                                if (horizontal)
                                {
                                    // X-bars: kink pionowy, ramię poziome → leaderRight z X kursora
                                    leaderRight       = jig3.CursorPt.X >= labelPos;
                                    sourceBar.ArmMidY = kinkPt.Y - insertOverride.Value.Y;
                                }
                                else
                                {
                                    // Y-bars: kink poziomy, ramię pionowe → leaderUp z Y kursora
                                    leaderUp          = jig3.CursorPt.Y >= labelPos;
                                    sourceBar.ArmMidY = kinkPt.X - insertOverride.Value.X;
                                }
                            }
                            // else Enter = prosta wzdłuż osi (leaderHorizontal=false)
                        }
                    }
                    else
                    {
                        // Enter po ETAP 2 = prosta etykieta bez kierunku
                        leaderHorizontal = false;
                    }
                }
                // else Enter po ETAP 1 = auto placement (insertOverride=null, leaderHorizontal=false)
            }

            // --- Krok 9: Annotacja ---
            var annotResult = AnnotationEngine.CreateLeader(
                db, barResult, sourceBar, leaderHorizontal, posNr, insertOverride,
                barsHorizontal: horizontal, leaderRight: leaderRight, leaderUp: leaderUp);

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

        // ----------------------------------------------------------------
        // GetViewLength — długość linii w rzucie z góry zależna od shape code.
        //
        // Auto mode:
        //   00, 99                          → A (cała długość)
        //   51, 63, 34-47 (linki)           → A (bok prostokąta)
        //   11, 12, 14, 15 (L-shape)        → max(A, B) = dłuższe ramię
        //   21           (U-bar)            → A (tymczasowo — wymaga weryfikacji)
        //   13, 22-26    (U-bar, crank)     → A
        //   wszystkie pozostałe             → ParamValues[0] = A (domyślnie)
        //
        // Jeśli kiedykolwiek dany kształt wymaga innego parametru niż A,
        // rozszerzaj tylko to miejsce.
        // ----------------------------------------------------------------

        internal static double GetViewLength(BarData bar)
        {
            double[] p = bar.ParamValues;   // [A, B, C, D, E]
            string code = bar.ShapeCode ?? "00";

            switch (code)
            {
                // Linki zamknięte — bok A (szerokość)
                case "51": case "63":
                case "34": case "35": case "36":
                case "41": case "46": case "47":
                // Prosta
                case "00": case "99":
                    return p[0];

                // L-shape — dłuższe z ramion max(A, B)
                case "11": case "12": case "14": case "15":
                    return Math.Max(p[0], p[1]);

                // U-bar shape 21 — debug: pokaż A i B, zwróć A (do weryfikacji)
                case "21":
                {
                    double vl = p[0];   // tymczasowo A — do zweryfikowania w BricsCAD
                    try
                    {
                        Bricscad.ApplicationServices.Application
                            .DocumentManager.MdiActiveDocument?.Editor
                            .WriteMessage(
                                $"\n[DEBUG U-BAR 21] ParamValues=[A={p[0]:F0},B={p[1]:F0},C={p[2]:F0}]" +
                                $"  viewLength={vl:F0} (używa A)\n");
                    }
                    catch { }
                    return vl;
                }

                // U-bar / crank — bok A
                case "13": case "22": case "23":
                case "24": case "25": case "26":
                    return p[0];

                default:
                    // Pozostałe → najdłuższy parametr
                    return Math.Max(p[0], Math.Max(p[1], Math.Max(p[2], Math.Max(p[3], p[4]))));
            }
        }

        // ----------------------------------------------------------------
        // GetViewLengthManual — użytkownik klika segment polilinii pręta.
        // Zwraca długość wybranego segmentu, lub ≤0 jeśli Escape.
        // Ustawia bar.ViewingDirection / ViewSegmentIndex.
        // ----------------------------------------------------------------

        internal static double GetViewLengthManual(
            Editor ed, Database db, ObjectId polyId, BarData bar)
        {
            while (true)
            {
                var ptOpts = new PromptPointOptions(
                    "\nClick on bar segment to show in plan view [Esc=Auto]: ")
                    { AllowNone = false };
                var ptResult = ed.GetPoint(ptOpts);

                if (ptResult.Status != PromptStatus.OK)
                    return -1;   // Escape → caller wraca do Auto

                Point3d clickedPt = ptResult.Value;

                double segLen = -1;
                int    segIdx = -1;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var pl = tr.GetObject(polyId, OpenMode.ForRead) as Polyline;
                    if (pl != null && pl.NumberOfVertices >= 2)
                    {
                        double minDist = double.MaxValue;
                        int    nv      = pl.NumberOfVertices;
                        for (int i = 0; i < nv - 1; i++)
                        {
                            Point2d a2 = pl.GetPoint2dAt(i);
                            Point2d b2 = pl.GetPoint2dAt(i + 1);
                            double d = DistPointToSegment(
                                clickedPt,
                                new Point3d(a2.X, a2.Y, 0),
                                new Point3d(b2.X, b2.Y, 0));
                            if (d < minDist) { minDist = d; segIdx = i; }
                        }

                        if (minDist <= 100.0)
                        {
                            Point2d sa = pl.GetPoint2dAt(segIdx);
                            Point2d sb = pl.GetPoint2dAt(segIdx + 1);
                            double dx  = sb.X - sa.X;
                            double dy  = sb.Y - sa.Y;
                            segLen = Math.Sqrt(dx * dx + dy * dy);
                        }
                    }
                    tr.Commit();
                }

                if (segLen <= 0)
                {
                    ed.WriteMessage("\nKliknij bezpośrednio na segment pręta (za daleko od polilinii).\n");
                    continue;
                }

                bar.ViewingDirection = "Manual";
                bar.ViewSegmentIndex = segIdx;
                ed.WriteMessage($"\n[RC_DIST] Manual view: segment {segIdx}, length={segLen:F0}mm\n");
                return segLen;
            }
        }

        private static double DistPointToSegment(Point3d p, Point3d a, Point3d b)
        {
            double abx = b.X - a.X, aby = b.Y - a.Y;
            double lenSq = abx * abx + aby * aby;
            if (lenSq < 1e-18) return p.DistanceTo(a);
            double t = ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / lenSq;
            t = Math.Max(0, Math.Min(1, t));
            double px = a.X + t * abx - p.X;
            double py = a.Y + t * aby - p.Y;
            return Math.Sqrt(px * px + py * py);
        }

        // ================================================================
        //  RC_UPDATE_BAR — aktualizuje długość prętów po edycji polilinii
        // ================================================================

        // ================================================================
        //  RC_FIX_LABEL — przesuwa grot MLeadera na najbliższy punkt pręta
        // ================================================================

        [CommandMethod("RC_FIX_LABEL", CommandFlags.Modal)]
        public void FixLabel()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            // Krok 1: użytkownik wskazuje MLeader
            var selOpts = new PromptEntityOptions("\nWskaż etykietę pręta RC: ");
            selOpts.SetRejectMessage("\nWskaż etykietę pręta RC");
            selOpts.AddAllowedClass(typeof(MLeader), true);
            var selResult = ed.GetEntity(selOpts);
            if (selResult.Status != PromptStatus.OK) return;

            using var tr = db.TransactionManager.StartTransaction();

            var ml = tr.GetObject(selResult.ObjectId, OpenMode.ForRead) as MLeader;
            if (ml == null)
            {
                ed.WriteMessage("\nWskaż etykietę pręta RC\n");
                tr.Commit();
                return;
            }

            // Krok 2: odczytaj handle pręta z XData MLeadera
            string barHandle = SingleBarEngine.ReadBarHandleFromLabel(ml);
            if (string.IsNullOrEmpty(barHandle))
            {
                ed.WriteMessage("\nTa etykieta nie jest powiązana z prętem RC\n");
                tr.Commit();
                return;
            }

            // Krok 3: znajdź pręt po handle
            ObjectId barId = SingleBarEngine.HandleToObjectId(db, barHandle);
            if (barId.IsNull)
            {
                ed.WriteMessage("\nPręt powiązany z etykietą nie istnieje\n");
                tr.Commit();
                return;
            }

            Entity barEnt;
            try { barEnt = tr.GetObject(barId, OpenMode.ForRead) as Entity; }
            catch
            {
                ed.WriteMessage("\nPręt powiązany z etykietą nie istnieje\n");
                tr.Commit();
                return;
            }
            if (barEnt == null || barEnt.IsErased)
            {
                ed.WriteMessage("\nPręt powiązany z etykietą nie istnieje\n");
                tr.Commit();
                return;
            }

            // Krok 4: pobierz aktualny grot MLeadera (first vertex pierwszej linii lidera)
            // BRX: GetLeaderIndexes() / GetLeaderLineIndexes(int) zwracają ArrayList
            System.Collections.ArrayList leaderIdxs;
            try { leaderIdxs = ml.GetLeaderIndexes(); }
            catch { leaderIdxs = new System.Collections.ArrayList(); }

            if (leaderIdxs.Count == 0)
            {
                ed.WriteMessage("\nEtykieta nie ma linii lidera\n");
                tr.Commit();
                return;
            }

            System.Collections.ArrayList lineIdxs;
            try { lineIdxs = ml.GetLeaderLineIndexes((int)leaderIdxs[0]); }
            catch { lineIdxs = new System.Collections.ArrayList(); }

            if (lineIdxs.Count == 0)
            {
                ed.WriteMessage("\nEtykieta nie ma linii lidera\n");
                tr.Commit();
                return;
            }
            int lni = (int)lineIdxs[0];
            Point3d currentTip = ml.GetFirstVertex(lni);

            // Krok 5: najbliższy punkt na pręcie
            Point3d newTip;
            try
            {
                var curve = barEnt as Curve;
                newTip = curve != null
                    ? curve.GetClosestPointTo(currentTip, false)
                    : currentTip;
            }
            catch
            {
                ed.WriteMessage("\nNie można wyznaczyć punktu na pręcie\n");
                tr.Commit();
                return;
            }

            // Krok 6: przesuń grot MLeadera
            ml.UpgradeOpen();
            ml.SetFirstVertex(lni, newTip);

            tr.Commit();
            ed.WriteMessage($"\n[RC_FIX_LABEL] Grot przesunięty na ({newTip.X:F0}, {newTip.Y:F0})\n");
        }

        [CommandMethod("RC_UPDATE_BAR", CommandFlags.Modal)]
        public void UpdateBar()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            var selOpts = new PromptEntityOptions("\nSelect bar polyline to update (RC_BAR): ");
            selOpts.SetRejectMessage("\nNot a polyline — select a bar created with RC_BAR.");
            selOpts.AddAllowedClass(typeof(Polyline), true);
            var selResult = ed.GetEntity(selOpts);
            if (selResult.Status != PromptStatus.OK) return;

            // Odczytaj geometrię i XData; obsłuż przypadek kliknięcia na poly2 (RC_BAR_LINK)
            BarData  bar       = null;
            double   newLength = 0;
            ObjectId primaryId = selResult.ObjectId;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pline = tr.GetObject(selResult.ObjectId, OpenMode.ForRead) as Polyline;
                if (pline == null) { ed.WriteMessage("\nNot a polyline.\n"); tr.Commit(); return; }

                bar = SingleBarEngine.ReadBarXData(pline);

                if (bar == null)
                {
                    // Może to być towarzysząca poly2 → szukaj głównej przez RC_BAR_LINK
                    var resolvedId = SingleBarEngine.ResolvePrimaryId(pline);
                    if (resolvedId != ObjectId.Null)
                    {
                        primaryId = resolvedId;
                        var primary = tr.GetObject(resolvedId, OpenMode.ForRead) as Entity;
                        if (primary != null) bar = SingleBarEngine.ReadBarXData(primary);
                        pline = tr.GetObject(resolvedId, OpenMode.ForRead) as Polyline;
                    }
                }

                if (bar == null)
                {
                    ed.WriteMessage("\nNo RC bar data on selected polyline.\n");
                    tr.Commit();
                    return;
                }

                newLength = pline?.Length ?? 0;
                tr.Commit();
            }

            double oldLength = bar.LengthA;
            string shapeCode = bar.ShapeCode ?? "00";

            // Tylko shape 00/99 można aktualizować przez pline.Length.
            // Dla innych kształtów pline.Length = całkowita długość łuków ≠ parametr A.
            if (shapeCode != "00" && shapeCode != "99")
            {
                ed.WriteMessage(
                    $"\n[RC_UPDATE_BAR] Shape {shapeCode} — aktualizacja przez geometrię polilinii " +
                    $"nieobsługiwana. Zmień parametry w dialogu RC_BAR.\n");
                return;
            }

            ed.WriteMessage($"\n[RC_UPDATE_BAR] Mark={bar.Mark}  old={oldLength:F0} mm  new={newLength:F0} mm\n");

            // Zaktualizuj XData na głównej polilinii
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pline = tr.GetObject(primaryId, OpenMode.ForWrite) as Polyline;
                bar.LengthA = newLength;
                SingleBarEngine.WriteXData(pline, bar);
                tr.Commit();
            }

            // Przebuduj towarzyszące encje (poly2 + łuki końcowe)
            SingleBarEngine.RebuildCompanions(db, primaryId, bar);

            // Znajdź i przerysuj powiązane rozkłady
            int posNr   = SingleBarEngine.ExtractPosNr(bar.Mark);
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
