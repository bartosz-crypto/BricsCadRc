using System;
using System.Collections.Generic;
using System.Linq;
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

            // Krok 1 — oblicz sugerowany wolny numer pozycji
            var usedNrs   = PositionCounter.GetUsedPositionNumbers(db);
            int suggested = usedNrs.Count == 0 ? 1 : usedNrs.Max() + 1;

            // Krok 2 — dialog z kształtem, średnicą, wymiarami i numerem pozycji
            var elevDlg = new BarElevationDialog(suggested);
            if (Application.ShowModalWindow(elevDlg) != true) return;
            var barData = elevDlg.Result;
            if (barData == null) return;

            // Krok 3 — punkt wstawienia
            var ptOpts   = new PromptPointOptions("\nClick insertion point for bar: ");
            var ptResult = ed.GetPoint(ptOpts);
            if (ptResult.Status != PromptStatus.OK) return;
            Point3d insertPt = ptResult.Value;

            // Walidacja unikalności numeru pozycji
            if (!int.TryParse(elevDlg.ResultPosNr, out int posNr)) posNr = suggested;
            int safePosNr = PositionCounter.GetNextFreeFrom(usedNrs, posNr);
            if (safePosNr != posNr)
            {
                var conflictDlg = new BarPositionConflictDialog(posNr, safePosNr);
                if (Application.ShowModalWindow(conflictDlg) != true) return;
                posNr = conflictDlg.ResultPosNr;
                // Upewnij się że wybrany numer też jest wolny
                posNr = PositionCounter.GetNextFreeFrom(usedNrs, posNr);
            }

            barData.Mark = BarData.FormatMark(barData.Diameter, posNr, 0, 1);

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

            string labelText = barData.Mark;  // np. "H12-01"
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
                // Odczytaj kąt z geometrii polilinii (uwzględnia ROTATE)
                if (sourceBar != null && ent is Polyline barPline)
                    sourceBar.Angle = SingleBarEngine.GetBarAngle(barPline);
                tr.Commit();
            }
            // Zapisz handle polilinii pręta — potrzebny do resetu etykiety przy usunięciu rozkładu
            if (sourceBar != null)
                sourceBar.SourceBarHandle = selResult.ObjectId.Handle.Value.ToString("X8");

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
                else
                {
                    sourceBar.ViewingDirection = "Manual";
                }
            }
            else
            {
                sourceBar.ViewingDirection = "Auto";
                viewLength = GetViewLength(sourceBar);
            }

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
            // Dla prostych rozkładów (krawędź H lub V) obrót bloku = 0.
            // BuildHorizontal/BuildVertical już obsługuje orientację prętów.
            // GetBarAngle (linia 142) może zwrócić ±90° gdy polilinia rysowana "od góry" —
            // to niepoprawnie ustawia blockRef.Rotation i obraca pręty w złym kierunku.
            sourceBar.Angle = 0.0;
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

            // Numer pozycji: z marka preta ("H12-13" → 13), lub nowy (Peek — nie inkrementuj przed OK)
            int posNr        = SingleBarEngine.ExtractPosNr(sourceBar.Mark);
            bool newPosAlloc = posNr <= 0;
            if (newPosAlloc) posNr = PositionCounter.Peek(db);

            // Usuń live preview — zaraz pojawią się finalne pręty
            ClearBarPreview(liveTransients);

            // Dla trybu Any — dialog z liczbą prętów i rozstawem (po ustaleniu geometrii i spacingu)
            // Krok 7: Generuj blok rozkładu prętów
            sourceBar.Direction = horizontal ? "X" : "Y";
            sourceBar.Cover     = cover;
            sourceBar.Pt1X      = x0;
            sourceBar.Pt1Y      = y0;
            sourceBar.BarsSpan  = rawSpan;
            sourceBar.Spacing   = spacing;
            sourceBar.Flipped   = isFlipped;

            var barResult = BarBlockEngine.GenerateFromBounds(
                db, x0, y0, x1Bound, y1Bound,
                sourceBar, horizontal, posNr);

            if (!barResult.IsValid)
            {
                ed.WriteMessage("\n[RC SLAB] Failed to generate distribution — check range and spacing.\n");
                return;
            }

            // Krok 6, 8–9 — dialog + jigy + CreateLeader
            string baseMark = BarData.FormatMark(sourceBar.Diameter, posNr, spacing, autoCount);

            bool annOk = RunAnnotationFlow(doc, db, sourceBar, barResult, horizontal,
                spacing, autoCount, baseMark, barResult.BlockRefId);
            if (!annOk) return;

            if (newPosAlloc) PositionCounter.CommitUsed(db, posNr);

            // Zaktualizuj Mark w RC_BAR_BLOCK — RunAnnotationFlow zbudowało pełny mark; przepisz do XData bloku
            if (!string.IsNullOrEmpty(sourceBar.Mark) && barResult.IsValid)
            {
                using (var trMark = db.TransactionManager.StartTransaction())
                {
                    var brMark = trMark.GetObject(barResult.BlockRefId, OpenMode.ForWrite)
                                 as BlockReference;
                    if (brMark != null)
                    {
                        var barXd = BarBlockEngine.ReadXData(brMark);
                        if (barXd != null)
                        {
                            barXd.Mark = sourceBar.Mark;
                            BarBlockEngine.WriteXData(brMark, barXd);
                        }
                    }
                    trMark.Commit();
                }
            }

            AnnotationEngine.UpdateBarLabelCount(
                db, sourceBar.SourceBarHandle ?? "", markOverride: sourceBar.Mark);

            ed.WriteMessage($"\n[RC SLAB] Distribution created: {sourceBar.Count} bars  {sourceBar.Mark}");
            try { doc.SendStringToExecute("REGEN\n", false, false, false); } catch { }
        }

        // ================================================================
        //  Wspólny flow annotacji — dialog + JIG + CreateLeader
        // ================================================================

        /// <summary>
        /// Wspólny flow annotacji rozkładu: dialog DistributionDescDialog + 3-etapowy JIG + CreateLeader.
        /// Wywoływany z RC_DISTRIBUTION i RC_EDIT_DISTRIBUTION (re-annotacja).
        /// </summary>
        internal static bool RunAnnotationFlow(
            Document doc,
            Database db,
            BarData sourceBar,
            BarBlockEngine.BarBlockResult barResult,
            bool horizontal,
            double finalSpacing,
            int autoCount,
            string baseMark,
            ObjectId blockRefId)
        {
            var ed = doc.Editor;

            // Krok 6 — Dialog DistributionDescDialog
            var descDlg = new DistributionDescDialog(sourceBar, autoCount, finalSpacing, baseMark);
            if (Application.ShowModalWindow(descDlg) != true) return false;

            int    finalCount = descDlg.BarCount;
            string suffix     = descDlg.Suffix ?? "";
            finalSpacing      = descDlg.BarSpacing;

            // Odbuduj mark z finalSpacing/finalCount (baseMark ma command-line spacing — nie nadpisuj)
            string newBaseMark = BarData.FormatMark(
                sourceBar.Diameter,
                SingleBarEngine.ExtractPosNr(sourceBar.Mark),
                finalSpacing, finalCount);
            string mark = string.IsNullOrEmpty(suffix) ? newBaseMark : $"{newBaseMark} {suffix}";
            sourceBar.Mark         = mark;
            sourceBar.CountDisplay = (finalCount != sourceBar.Count) ? (int?)finalCount : null;

            // Geometria fizycznego rozkładu BEZ ZMIAN — label-override semantics.
            // Zaktualizuj XData RC_BAR_BLOCK: tylko Mark i CountDisplay. Count/Spacing canonical bez zmian.
            using (var trXData = db.TransactionManager.StartTransaction())
            {
                var brXData = trXData.GetObject(blockRefId, OpenMode.ForWrite) as BlockReference;
                if (brXData != null)
                {
                    var barXd = BarBlockEngine.ReadXData(brXData);
                    if (barXd != null)
                    {
                        barXd.Mark         = sourceBar.Mark;
                        barXd.CountDisplay = sourceBar.CountDisplay;
                        BarBlockEngine.WriteXData(brXData, barXd);
                    }
                }
                trXData.Commit();
            }

            // Krok 8 — 3-etapowy JIG
            double barsSpan  = sourceBar.BarsSpan;
            double minFixed, basePos;

            if (Math.Abs(sourceBar.Angle) > 1e-6)
            {
                // Dla obróconych bloków — lokalne współrzędne (przed obrotem)
                // barResult.MinPoint = punkt wstawienia (lokalne 0,0)
                // horizontal=true: dist line wzdłuż Y lokalnie (0..barsSpan), pręty wzdłuż X (0..barLength)
                // horizontal=false: dist line wzdłuż X lokalnie
                minFixed = 0.0;        // lokalne origin zawsze 0
                basePos  = sourceBar.LengthA;
            }
            else
            {
                minFixed = horizontal ? barResult.BarOrigin.Y : barResult.BarOrigin.X;
                basePos  = horizontal ? barResult.MaxPoint.X : barResult.MaxPoint.Y;
            }

            Point3d  kinkPt           = horizontal
                ? new Point3d(barResult.MinPoint.X, barResult.MinPoint.Y + barsSpan / 2.0, 0)
                : new Point3d(barResult.MinPoint.X + barsSpan / 2.0, barResult.MinPoint.Y, 0);
            bool     userClickedInJig3 = false;
            Point3d  jig3CursorPt      = kinkPt;
            bool     leaderHorizontal  = false;
            bool     leaderRight       = true;
            bool     leaderUp          = true;
            Point3d? insertOverride    = null;
            double   labelPos          = 0;

            // Dla nieobróconego bloku rotCenter=(0,0,0) — LocalToWCS działa jako identity
            // Dla obróconego rotCenter=barResult.MinPoint — punkt wstawienia bloku w WCS
            Point3d jigRotCenter = Math.Abs(sourceBar.Angle) > 1e-6
                ? barResult.MinPoint
                : Point3d.Origin;

            // ETAP 1
            var jig1 = new AnnotLabelPositionJig(
                minFixed, basePos, barsSpan, finalSpacing, finalCount,
                horizontal, AnnotationEngine.DotRadius,
                sourceBar.Angle, jigRotCenter);
            var res1 = ed.Drag(jig1);
            jig1.ClearTransients();
            if (res1.Status == PromptStatus.Cancel) return false;

            if (res1.Status == PromptStatus.OK || res1.Status == PromptStatus.None)
            {
                labelPos = jig1.LabelPos;
                insertOverride = horizontal
                    ? new Point3d(labelPos, minFixed, 0)
                    : new Point3d(minFixed, labelPos, 0);

                if (Math.Abs(sourceBar.Angle) > 1e-6)
                {
                    // insertOverride = WCS pozycja gdzie dist line przecina pierwszy pret (lokalny Y=0)
                    // labelPos = lokalna X (gdzie wzdluz preta stoi dist line)
                    // kinkPt po ETAP 2 daje lokalną Y (gdzie na dist line jest punkt docelowy)
                    // Ale insertOverride musi być przy Y=0 (pierwszy pret) — bez kinkPt
                    if (horizontal)
                        insertOverride = Point3dRotate.LocalToWCS(jigRotCenter, sourceBar.Angle, labelPos, 0);
                    else
                        insertOverride = Point3dRotate.LocalToWCS(jigRotCenter, sourceBar.Angle, 0, labelPos);
                    // Przesuniecie wzdluz dist line jest kodowane w ArmMidY, nie w insertOverride
                }

                Point3d distCenter;
                if (Math.Abs(sourceBar.Angle) > 1e-6)
                {
                    // Dla obróconych: distCenter w WCS = LocalToWCS(rotCenter, angle, localX, localY)
                    if (horizontal)
                        distCenter = Point3dRotate.LocalToWCS(jigRotCenter, sourceBar.Angle, labelPos, barsSpan / 2.0);
                    else
                        distCenter = Point3dRotate.LocalToWCS(jigRotCenter, sourceBar.Angle, barsSpan / 2.0, labelPos);
                }
                else
                {
                    distCenter = horizontal
                        ? new Point3d(labelPos, minFixed + barsSpan / 2.0, 0)
                        : new Point3d(minFixed + barsSpan / 2.0, labelPos, 0);
                }
                kinkPt = distCenter;

                // ETAP 2
                var jig2 = new AnnotLabelDirectionJig(
                    distCenter, labelPos, minFixed, barsSpan,
                    horizontal, AnnotationEngine.DotRadius, finalCount, finalSpacing,
                    sourceBar.Angle, jigRotCenter);
                var res2 = ed.Drag(jig2);
                jig2.ClearTransients();
                if (res2.Status == PromptStatus.Cancel) return false;
                if (res2.Status == PromptStatus.OK)
                {
                    var direction = jig2.Direction;
                    kinkPt           = jig2.KinkPt;
                    leaderHorizontal = horizontal
                        ? direction == LabelDirection.Left || direction == LabelDirection.Right
                        : direction == LabelDirection.Up   || direction == LabelDirection.Down;
                    leaderRight = direction == LabelDirection.Right;
                    leaderUp    = direction == LabelDirection.Up;

                    // ETAP 3 — wielosegmentowy leader: pętla GetPoint z live preview
                    var leaderWcsPts = new List<Point3d> { kinkPt };  // start = kinkPt z jig2

                    using (var drawer = new LeaderTransientDrawer())
                    {
                        while (true)
                        {
                            var ptOpts = new PromptPointOptions(
                                "\nKliknij punkt leadera [Enter=zatwierdź]: ");
                            ptOpts.AllowNone = true;
                            ptOpts.UseBasePoint = true;
                            ptOpts.BasePoint = leaderWcsPts[leaderWcsPts.Count - 1];

                            PointMonitorEventHandler monitor = (s, ev) =>
                            {
                                try
                                {
                                    drawer.UpdatePreview(leaderWcsPts, ev.Context.ComputedPoint);
                                    Application.UpdateScreen();
                                }
                                catch { }
                            };
                            ed.PointMonitor += monitor;

                            PromptPointResult ptRes;
                            try { ptRes = ed.GetPoint(ptOpts); }
                            finally { ed.PointMonitor -= monitor; }

                            if (ptRes.Status == PromptStatus.Cancel) { drawer.Clear(); return false; }
                            if (ptRes.Status != PromptStatus.OK) break;
                            leaderWcsPts.Add(ptRes.Value);
                        }
                        drawer.Clear();
                    }

                    // insertPt = punkt wstawienia RC_BAR_BLOCK (origin BTR w WCS)
                    Point3d insertPt;
                    if (insertOverride.HasValue)
                        insertPt = insertOverride.Value;
                    else
                    {
                        using (var trIns = db.TransactionManager.StartTransaction())
                        {
                            var brIns = trIns.GetObject(barResult.BlockRefId, OpenMode.ForRead)
                                        as BlockReference;
                            insertPt = brIns?.Position ?? Point3d.Origin;
                            trIns.Commit();
                        }
                    }

                    // Przelicz punkty WCS → układ lokalny BTR
                    var localPts = leaderWcsPts.Select(pt => {
                        double dx = pt.X - insertPt.X;
                        double dy = pt.Y - insertPt.Y;
                        if (Math.Abs(sourceBar.Angle) < 1e-6)
                            return new Point3d(dx, dy, 0);
                        double cos = Math.Cos(-sourceBar.Angle);
                        double sin = Math.Sin(-sourceBar.Angle);
                        return new Point3d(dx * cos - dy * sin, dx * sin + dy * cos, 0);
                    }).ToList();

                    // Pierwszy punkt leadera zawsze na dist line (środek spanu w lokalnym BTR)
                    Point3d distLinePt = horizontal
                        ? new Point3d(0, sourceBar.BarsSpan / 2.0, 0)   // X-bars: dist line wzdłuż Y
                        : new Point3d(sourceBar.BarsSpan / 2.0, 0, 0);  // Y-bars: dist line wzdłuż X
                    localPts.Insert(0, distLinePt);

                    // Detekcja "zawracania" — usuń punkty pośrednie które tworzą kąt ostry
                    for (int k = localPts.Count - 2; k >= 1; k--)
                    {
                        var before = localPts[k - 1];
                        var mid    = localPts[k];
                        var after  = localPts[k + 1];
                        var d1 = (mid   - before).GetNormal();
                        var d2 = (after - mid).GetNormal();
                        if (d1.DotProduct(d2) < -0.1)
                            localPts.RemoveAt(k);
                    }

                    sourceBar.LeaderPoints = AnnotationEngine.EncodeLeaderPoints(localPts);

                    if (leaderWcsPts.Count >= 2)
                    {
                        // User kliknął w jig3 → wielosegmentowy leader
                        leaderHorizontal = true;
                        var lastMsDir = leaderWcsPts[leaderWcsPts.Count - 1]
                                      - leaderWcsPts[leaderWcsPts.Count - 2];
                        if (horizontal)
                            leaderRight = lastMsDir.X >= 0;
                        else
                            leaderUp = lastMsDir.Y >= 0;
                    }
                    // else: Enter od razu → zachowaj leaderHorizontal/leaderRight/leaderUp z jig2
                }
                else
                {
                    kinkPt            = jig2.LastCursorPt;
                }
            }

            // Dla obróconych bloków — przelicz ArmMidY, leaderRight, leaderUp do układu lokalnego
            if (Math.Abs(sourceBar.Angle) > 1e-6 && insertOverride.HasValue)
            {
                double cos = Math.Cos(sourceBar.Angle);
                double sin = Math.Sin(sourceBar.Angle);
                var ins = insertOverride.Value;

                // Przelicz kinkPt do lokalnego układu bloku (od insertOverride)
                double dkx = kinkPt.X - ins.X;
                double dky = kinkPt.Y - ins.Y;
                // lokalX = wzdłuż pręta (cos,sin), lokalY = wzdłuż dist line (-sin,cos)
                double kinkLocalX = dkx * cos + dky * sin;
                double kinkLocalY = -dkx * sin + dky * cos;

                // ArmMidY = lokalna pozycja Y na dist line gdzie przymocowany jest arm
                if (leaderHorizontal)
                {
                    // Użyj jig3CursorPt jeśli user kliknął w ETAP 3 — daje dokładną pozycję złamania
                    if (userClickedInJig3)
                    {
                        double dcx3 = jig3CursorPt.X - ins.X;
                        double dcy3 = jig3CursorPt.Y - ins.Y;
                        double cursor3LocalY = -dcx3 * sin + dcy3 * cos;
                        sourceBar.ArmMidY = cursor3LocalY;
                    }
                    else
                    {
                        sourceBar.ArmMidY = kinkLocalY;
                    }
                }

                // leaderRight = po której stronie pręta user umieścił kursor w jig3
                if (leaderHorizontal && userClickedInJig3)
                {
                    // Przelicz CursorPt jig3 do układu lokalnego
                    double dcx = jig3CursorPt.X - ins.X;
                    double dcy = jig3CursorPt.Y - ins.Y;
                    double cursorLocalX = dcx * cos + dcy * sin;
                    leaderRight = cursorLocalX >= 0;
                }
                else if (leaderHorizontal)
                {
                    // Fallback — użyj kinkPt
                    leaderRight = kinkLocalX >= 0;
                }

                // leaderUp = arm idzie w kierunku +lokalY (wzdłuż dist line)
                if (!leaderHorizontal)
                    leaderUp = kinkLocalY >= 0;
            }

            // Krok 9 — CreateLeader
            int posNr = SingleBarEngine.ExtractPosNr(sourceBar.Mark);
            var annotResult = AnnotationEngine.CreateLeader(
                db, barResult, sourceBar, leaderHorizontal, posNr,
                insertOverride, barsHorizontal: horizontal,
                leaderRight: leaderRight, leaderUp: leaderUp);

            if (annotResult.BlockRefId == ObjectId.Null) return false;

            BarBlockEngine.LinkAnnotation(db, blockRefId, annotResult.BlockRefId);
            AnnotationEngine.UpdateBarLabelCount(db, sourceBar.SourceBarHandle ?? "", markOverride: sourceBar.Mark);

            return true;
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

                // U-bar shape 21 — zwraca A (szerokość widoku)
                case "21":
                    return p[0];

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

                newLength = pline != null ? SingleBarEngine.GetStraightBarAxisLength(pline) : 0.0;
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

        // ================================================================
        //  RC_EDIT_BAR — edycja shape code i/lub parametrów istniejącego pręta
        // ================================================================

        [CommandMethod("RC_EDIT_BAR", CommandFlags.Modal)]
        public void EditBar()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db  = doc.Database;
            var ed  = doc.Editor;

            // Krok 1 — wybierz pręt
            var selOpts = new PromptEntityOptions("\nWybierz pręt do edycji: ");
            selOpts.SetRejectMessage("\nTo nie jest pręt RC.");
            selOpts.AddAllowedClass(typeof(Polyline), false);
            var selRes = ed.GetEntity(selOpts);
            if (selRes.Status != PromptStatus.OK) return;

            BarData  bar    = null;
            ObjectId editId = selRes.ObjectId;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pline = tr.GetObject(selRes.ObjectId, OpenMode.ForRead) as Polyline;
                if (pline == null) { tr.Abort(); return; }

                bar = SingleBarEngine.ReadBarXData(pline);
                if (bar == null)
                {
                    // Może to być towarzysząca poly — szukaj głównej przez RC_BAR_LINK
                    var primaryId = SingleBarEngine.ResolvePrimaryId(pline);
                    if (!primaryId.IsNull)
                    {
                        editId = primaryId;
                        var primary = tr.GetObject(primaryId, OpenMode.ForRead) as Polyline;
                        if (primary != null) bar = SingleBarEngine.ReadBarXData(primary);
                    }
                }
                tr.Commit();
            }

            if (bar == null)
            {
                ed.WriteMessage("\nNie znaleziono danych pręta.");
                return;
            }

            // Krok 2 — otwórz dialog z aktualnymi wartościami
            var dlg = new BarElevationDialog();
            dlg.LoadExisting(bar);
            if (Application.ShowModalWindow(dlg) != true) return;
            var updated = dlg.Result;

            // Zachowaj niezmienne pola z oryginału
            // Przebuduj Mark z nową średnicą i posNr z dialogu — format H{dia}-{posNr}
            string posNrStr = dlg.ResultPosNr;
            updated.Mark    = BarData.FormatMark(updated.Diameter, int.Parse(posNrStr), 0, 1);
            updated.LayerCode   = bar.LayerCode;
            updated.Position    = bar.Position;
            updated.Direction   = bar.Direction;
            updated.LabelHandle = bar.LabelHandle;

            // Krok 3 — zapisz XData i przebuduj geometrię
            using (var tr2 = db.TransactionManager.StartTransaction())
            {
                var plineRw = tr2.GetObject(editId, OpenMode.ForWrite) as Polyline;
                if (plineRw == null) { tr2.Abort(); return; }
                SingleBarEngine.WriteXData(plineRw, updated);
                tr2.Commit();
            }

            SingleBarEngine.RebuildCompanions(db, editId, updated);

            // Krok 3b — napraw grot strzałki etykiety pręta (geometria się zmieniła)
            using (var trFix = db.TransactionManager.StartTransaction())
            {
                var plineFix = trFix.GetObject(editId, OpenMode.ForRead) as Polyline;
                if (plineFix != null && !string.IsNullOrEmpty(updated.LabelHandle))
                {
                    if (long.TryParse(updated.LabelHandle,
                            System.Globalization.NumberStyles.HexNumber,
                            null, out long lblHVal))
                    {
                        var lblHandle = new Handle(lblHVal);
                        if (db.TryGetObjectId(lblHandle, out ObjectId lblId)
                            && !lblId.IsNull && !lblId.IsErased)
                        {
                            var ml = trFix.GetObject(lblId, OpenMode.ForWrite) as MLeader;
                            if (ml != null)
                            {
                                try
                                {
                                    var leaderIdxs = ml.GetLeaderIndexes();
                                    if (leaderIdxs != null && leaderIdxs.Count > 0)
                                    {
                                        int li       = (int)leaderIdxs[0];
                                        var lineIdxs = ml.GetLeaderLineIndexes(li);
                                        if (lineIdxs != null && lineIdxs.Count > 0)
                                        {
                                            int lni        = (int)lineIdxs[0];
                                            var currentTip = ml.GetFirstVertex(lni);
                                            var newTip     = plineFix.GetClosestPointTo(currentTip, false);
                                            ml.SetFirstVertex(lni, newTip);
                                        }
                                    }
                                }
                                catch { /* nie przerywaj jeśli MLeader nie ma leaderów */ }
                            }
                        }
                    }
                }
                trFix.Commit();
            }

            // Krok 3c — zaktualizuj etykietę pręta (MLeader) z poprawnym count ze wszystkich rozkładów
            AnnotationEngine.UpdateBarLabelCount(db,
                editId.Handle.Value.ToString("X8"), markOverride: updated.Mark);

            // Krok 4 — propaguj zmiany (Mark, Diameter, LengthA) do powiązanych rozkładów.
            // Szukamy po SourceBarHandle (nie po posNr) — handle pręta nie zmienia się gdy user
            // zmienia posNr lub diameter, więc zawsze znajdziemy właściwe rozkłady.
            {
                string myHandle  = editId.Handle.Value.ToString("X8");
                int    newPosNr  = SingleBarEngine.ExtractPosNr(updated.Mark);
                var    toRebuild = new System.Collections.Generic.List<(ObjectId id, BarData bar)>();

                using (var trDist = db.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)trDist.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                    foreach (ObjectId oid in ms)
                    {
                        if (oid.IsErased) continue;
                        var brDist  = trDist.GetObject(oid, OpenMode.ForRead) as BlockReference;
                        if (brDist == null) continue;
                        var barDist = BarBlockEngine.ReadXData(brDist);
                        if (barDist == null) continue;
                        if (!string.Equals(barDist.SourceBarHandle, myHandle,
                                StringComparison.OrdinalIgnoreCase)) continue;

                        // Odbuduj Mark: zachowaj spacing i suffix, podmień prefix H{dia}-{posNr}
                        var    mp     = barDist.Mark.Split(' ');
                        var    cp     = mp[0].Split('-');
                        string sfx    = mp.Length > 1
                            ? " " + string.Join(" ", mp, 1, mp.Length - 1) : "";
                        int    distSp = cp.Length >= 3
                            && int.TryParse(cp[2], out int spParsed)
                            ? spParsed : (int)barDist.Spacing;
                        string newMark = BarData.FormatMark(
                            updated.Diameter, newPosNr, distSp, barDist.Count) + sfx;

                        brDist.UpgradeOpen();
                        barDist.Mark     = newMark;
                        barDist.Diameter = updated.Diameter;
                        barDist.LengthA  = updated.LengthA;
                        BarBlockEngine.WriteXData(brDist, barDist);
                        toRebuild.Add((oid, barDist));
                    }
                    trDist.Commit();
                }

                foreach (var (id, bd) in toRebuild)
                {
                    BarBlockEngine.UpdateBarLength(db, id, updated.LengthA);
                    AnnotationEngine.SyncAnnotation(db, bd);
                }
            }

            ed.WriteMessage($"\nPręt {updated.Mark} zaktualizowany. Shape: {updated.ShapeCode}\n");
            try { doc.SendStringToExecute("REGEN\n", true, false, false); } catch { }
        }

        // ================================================================
        //  RC_SCHEDULE — zestawienie prętów (BBS) wg BS 8666:2020
        // ================================================================

        [CommandMethod("RC_SCHEDULE", CommandFlags.Modal)]
        public void ShowSchedule()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db  = doc.Database;
            var ed  = doc.Editor;

            ed.WriteMessage("\n[RC_SCHEDULE] Budowanie zestawienia prętów...\n");

            List<BarScheduleEntry> entries;
            try
            {
                entries = BarScheduleEngine.BuildSchedule(db);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[RC_SCHEDULE] Błąd: {ex.Message}\n");
                return;
            }

            if (entries.Count == 0)
            {
                ed.WriteMessage("\n[RC_SCHEDULE] Brak prętów z rozkładami w rysunku.\n");
                return;
            }

            ed.WriteMessage($"\n[RC_SCHEDULE] Znaleziono {entries.Count} pozycji.\n");

            var dlg = new BarScheduleDialog(entries);
            Application.ShowModelessWindow(dlg);
        }
    }
}
