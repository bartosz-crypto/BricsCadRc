using System;
using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using BricsCadRc.Commands;
using BricsCadRc.Core;
using BricsCadRc.Dialogs;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using Polyline = Teigha.DatabaseServices.Polyline;

namespace BricsCadRc.Commands
{
    public class EditCommands
    {
        /// <summary>
        /// RC_EDIT_BAR — uzytkownik klika pret lub annotacje, otwiera sie dialog WPF.
        /// </summary>
        [CommandMethod("RC_EDIT_BAR", CommandFlags.Modal)]
        public void EditBar()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            // Pozwol wybrac: linie (pret) lub tekst (annotacje)
            var selOpts = new PromptEntityOptions("\nWybierz pret lub opis zbrojenia: ");
            selOpts.SetRejectMessage("\nTo nie jest obiekt RC SLAB.");
            selOpts.AddAllowedClass(typeof(Line),   true);
            selOpts.AddAllowedClass(typeof(DBText), true);
            var selResult = ed.GetEntity(selOpts);
            if (selResult.Status != PromptStatus.OK) return;

            using var tr = db.TransactionManager.StartTransaction();
            var entity = (Entity)tr.GetObject(selResult.ObjectId, OpenMode.ForRead);
            tr.Commit();

            BarData bar      = null;
            ObjectId annotId = ObjectId.Null;

            // Sprawdz czy to annotacja
            using (var tr2 = db.TransactionManager.StartTransaction())
            {
                var ent2 = (Entity)tr2.GetObject(selResult.ObjectId, OpenMode.ForRead);

                if (ent2 is DBText)
                {
                    bar = AnnotationEngine.ReadAnnotXData(ent2);
                    if (bar != null)
                        annotId = selResult.ObjectId;
                }
                else
                {
                    bar = XDataHelper.Read(ent2);
                }

                tr2.Commit();
            }

            if (bar == null)
            {
                ed.WriteMessage("\nWybrany obiekt nie jest pretem ani opisem RC SLAB.\n");
                return;
            }

            // Jesli kliknieto pret (nie annotacje) — sprobuj znalezc annotacje z tej samej grupy
            if (annotId.IsNull)
            {
                annotId = FindAnnotationByMark(db, bar.Mark);
            }

            // Otworz dialog WPF
            var dlg = new BarPropertiesDialog(bar, annotId, db);
            Application.ShowModalWindow(dlg);
        }

        /// <summary>Szuka annotacji RC SLAB z podanym markiem w przestrzeni modelu.</summary>
        private static ObjectId FindAnnotationByMark(Database db, string mark)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);

            foreach (ObjectId id in space)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as DBText;
                if (ent == null) continue;

                var annot = AnnotationEngine.ReadAnnotXData(ent);
                if (annot != null && annot.Mark == mark)
                {
                    tr.Commit();
                    return id;
                }
            }

            tr.Commit();
            return ObjectId.Null;
        }

        /// <summary>
        /// RC_EDIT_DISTRIBUTION — edytuje istniejący rozkład prętów (RC_BAR_BLOCK).
        /// Otwiera dialog z polami Count/Spacing/Cover i przebudowuje blok.
        /// </summary>
        [CommandMethod("RC_EDIT_DISTRIBUTION", CommandFlags.Modal)]
        public void EditDistribution()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            // Krok 1 — wybierz rozkład (dowolny entity z BTR RC_SLAB_BARS_*)
            ObjectId blockRefId = ObjectId.Null;
            BarData  bar        = null;

            while (true)
            {
                var selOpts = new PromptEntityOptions("\nWybierz rozkład do edycji: ");
                var selResult = ed.GetEntity(selOpts);
                if (selResult.Status != PromptStatus.OK) return;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ent = tr.GetObject(selResult.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent != null) blockRefId = ResolveBarBlock(ent, tr);
                    tr.Commit();
                }

                if (!blockRefId.IsNull)
                {
                    using (var tr2 = db.TransactionManager.StartTransaction())
                    {
                        var br2 = tr2.GetObject(blockRefId, OpenMode.ForRead) as BlockReference;
                        bar = BarBlockEngine.ReadXData(br2);
                        tr2.Commit();
                    }
                }

                if (bar != null) break;

                ed.WriteMessage("\nTo nie jest rozkład RC — kliknij na linię rozkładu prętów.\n");
                blockRefId = ObjectId.Null;
            }

            // Lokalna metoda pomocnicza — przebudowuje blok + annotację (Mark sticky — nie ruszamy)
            void ApplyPreview(int count, double spacing, double cover, double barsSpan)
            {
                if (count < 1) count = 1;
                BarBlockEngine.RebuildWithNewLayout(db, blockRefId, count, spacing, cover, newBarsSpan: barsSpan);

                using var trS = db.TransactionManager.StartTransaction();
                var brS = trS.GetObject(blockRefId, OpenMode.ForRead) as BlockReference;
                var bS  = brS != null ? BarBlockEngine.ReadXData(brS) : null;
                trS.Commit();
                if (bS != null) AnnotationEngine.SyncAnnotation(db, bS);

                BarBlockHighlightManager.RefreshOutlineForBlock(blockRefId);

                try { doc.SendStringToExecute("REGEN\n", false, false, false); } catch { }
            }

            // Zachowaj oryginalne wartości do ewentualnego przywrócenia
            int    origCount    = bar.Count;
            double origBarsSpan = bar.BarsSpan;
            double origSpacing  = bar.Spacing;
            double origCover    = bar.Cover;

            // Sprawdź czy annotacja istnieje
            bool annotMissing = false;
            if (string.IsNullOrEmpty(bar.AnnotHandle))
            {
                annotMissing = true;
            }
            else if (long.TryParse(bar.AnnotHandle,
                    System.Globalization.NumberStyles.HexNumber,
                    null, out long checkHVal))
            {
                var checkHandle = new Handle(checkHVal);
                if (!db.TryGetObjectId(checkHandle, out ObjectId checkId)
                    || checkId.IsNull || checkId.IsErased)
                    annotMissing = true;
            }

            // Krok 2 — dialog z live preview
            var dlg = new EditDistributionDialog(bar, (cnt, sp, cv, span) => ApplyPreview(cnt, sp, cv, span), annotMissing);

            BarBlockHighlightManager.ShowOutlineFor(blockRefId);
            bool confirmed;
            try
            {
                confirmed = Application.ShowModalWindow(dlg) == true;
            }
            finally
            {
                BarBlockHighlightManager.HideAllOutlines();
                try
                {
                    var docF = Application.DocumentManager.MdiActiveDocument;
                    docF?.Editor.SetImpliedSelection(new ObjectId[0]);
                }
                catch { }
            }

            if (!confirmed)
            {
                if (dlg.PreviewApplied)
                {
                    ApplyPreview(origCount, origSpacing, origCover, origBarsSpan);
                }
                return;
            }

            if (annotMissing && dlg.ResultAddAnnotation)
            {
                using var trRe = db.TransactionManager.StartTransaction();
                var brRe = trRe.GetObject(blockRefId, OpenMode.ForRead) as BlockReference;
                if (brRe == null) { trRe.Commit(); return; }
                // Re-read XData — RebuildWithNewLayout (ApplyPreview) pisze Count/Spacing/BarsSpan/LengthA do DB;
                // lokalny `bar` z początku komendy jest stale (przed-dialogowy).
                var freshBar = BarBlockEngine.ReadXData(brRe);
                if (freshBar != null) bar = freshBar;
                bar.Angle = brRe.Rotation;
                var insertWCS = brRe.Position;
                BarBlockEngine.BarBlockResult barResult;
                if (Math.Abs(bar.Angle) > 1e-6)
                {
                    // Dla obróconego bloku — oblicz MaxPoint w kierunku obrotu (nie AABB w WCS)
                    double cos       = Math.Cos(bar.Angle);
                    double sin       = Math.Sin(bar.Angle);
                    double barsSpanR = bar.BarsSpan;
                    double lengthR   = bar.LengthA;
                    barResult = new BarBlockEngine.BarBlockResult
                    {
                        BlockRefId = blockRefId,
                        BarOrigin  = insertWCS,
                        MinPoint   = insertWCS,
                        MaxPoint   = new Point3d(
                            insertWCS.X + lengthR * cos + barsSpanR * (-sin),
                            insertWCS.Y + lengthR * sin + barsSpanR * cos,
                            0)
                    };
                }
                else
                {
                    barResult = new BarBlockEngine.BarBlockResult
                    {
                        BlockRefId = blockRefId,
                        BarOrigin  = insertWCS,
                        MinPoint   = insertWCS,
                        MaxPoint   = new Point3d(
                            insertWCS.X + (bar.Direction == "X" ? bar.LengthA : bar.BarsSpan),
                            insertWCS.Y + (bar.Direction == "X" ? bar.BarsSpan : bar.LengthA),
                            0)
                    };
                }
                trRe.Commit();

                string baseMark = BarData.FormatMark(bar.Diameter, SingleBarEngine.ExtractPosNr(bar.Mark), bar.Spacing, bar.Count);
                BarCommands.RunAnnotationFlow(doc, db, bar, barResult, bar.Direction == "X",
                    bar.Spacing, bar.Count, baseMark, blockRefId);

                try { doc.SendStringToExecute("REGEN\n", false, false, false); } catch { }
                return;
            }

            // Krok 3 — przebuduj blok z wartościami z dialogu (+ opcjonalnie viewLength/ViewingDir)
            double? newLengthA = null;
            string  newViewDir = dlg.ResultViewingDirection;
            int     newSegIdx  = -1;

            if (newViewDir == "Manual" || newViewDir == "Auto")
            {
                // Rozwiąż polilnię pręta-źródłowego przez SourceBarHandle
                ObjectId polyId = ObjectId.Null;
                BarData  barSrc = null;
                using (var trPoly = db.TransactionManager.StartTransaction())
                {
                    var brPoly  = trPoly.GetObject(blockRefId, OpenMode.ForRead) as BlockReference;
                    var barPoly = brPoly != null ? BarBlockEngine.ReadXData(brPoly) : null;
                    if (barPoly != null && !string.IsNullOrEmpty(barPoly.SourceBarHandle))
                    {
                        if (long.TryParse(barPoly.SourceBarHandle,
                                System.Globalization.NumberStyles.HexNumber, null, out long hv))
                            db.TryGetObjectId(new Handle(hv), out polyId);
                    }
                    if (!polyId.IsNull)
                    {
                        var polyEnt = trPoly.GetObject(polyId, OpenMode.ForRead) as Entity;
                        barSrc = polyEnt != null ? SingleBarEngine.ReadBarXData(polyEnt) : null;
                    }
                    trPoly.Commit();
                }
                barSrc ??= new BarData();

                if (newViewDir == "Manual" && !polyId.IsNull)
                {
                    double segLen = BarCommands.GetViewLengthManual(ed, db, polyId, barSrc);
                    if (segLen > 0)
                    {
                        newLengthA = segLen;
                        newSegIdx  = barSrc.ViewSegmentIndex;
                    }
                    else
                    {
                        // ESC z Manual → cofnij do Auto
                        newViewDir = "Auto";
                        newLengthA = BarCommands.GetViewLength(barSrc);
                    }
                }
                else if (newViewDir == "Auto")
                {
                    // Auto zawsze przelicza z ShapeCode — niezależnie od poprzedniego stanu
                    newLengthA = BarCommands.GetViewLength(barSrc);
                }
            }
            // "Any" lub bez zmiany ViewingDir → newLengthA = null (zachowaj obecne LengthA)

            int    newCount    = dlg.ResultCount;
            double newSpacing  = dlg.ResultSpacing;
            double newCover    = dlg.ResultCover;
            double newBarsSpan = dlg.ResultBarsSpan;

            BarBlockEngine.RebuildWithNewLayout(
                db, blockRefId,
                newCount, newSpacing, newCover,
                newBarsSpan:   newBarsSpan,
                newLengthA:    newLengthA,
                newViewingDir: newViewDir,
                newViewSegIdx: newSegIdx);

            // Krok 3c — zaktualizuj etykietę pręta źródłowego
            {
                using var trSrc = db.TransactionManager.StartTransaction();
                var brSrc = trSrc.GetObject(blockRefId, OpenMode.ForRead) as BlockReference;
                var barSrc = brSrc != null ? BarBlockEngine.ReadXData(brSrc) : null;
                trSrc.Commit();
                AnnotationEngine.UpdateBarLabelCount(db, barSrc?.SourceBarHandle ?? "", markOverride: barSrc?.Mark);
            }

            // Krok 4 — zaktualizuj annotację (nowy BarsSpan)
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = tr.GetObject(blockRefId, OpenMode.ForRead) as BlockReference;
                if (br != null)
                {
                    var updatedBar = BarBlockEngine.ReadXData(br);
                    if (updatedBar != null)
                        AnnotationEngine.SyncAnnotation(db, updatedBar);
                }
                tr.Commit();
            }

            ed.WriteMessage(
                $"\nRozkład {bar.Mark} zaktualizowany: {newCount} szt. co {newSpacing:F0} mm.\n");

            // Krok 5 — opcjonalna pętla jig3: przebuduj leader
            if (dlg.ResultRebuildLeader)
            {
                // Rozwiąż ObjectId annotacji z aktualnego XData bloku
                ObjectId annotBlockRefId = ObjectId.Null;
                using (var trA = db.TransactionManager.StartTransaction())
                {
                    var brA = trA.GetObject(blockRefId, OpenMode.ForRead) as BlockReference;
                    var barA = brA != null ? BarBlockEngine.ReadXData(brA) : null;
                    if (barA != null && !string.IsNullOrEmpty(barA.AnnotHandle)
                        && long.TryParse(barA.AnnotHandle,
                            System.Globalization.NumberStyles.HexNumber, null, out long annotHv))
                        db.TryGetObjectId(new Handle(annotHv), out annotBlockRefId);
                    trA.Commit();
                }

                if (!annotBlockRefId.IsNull)
                {
                    // Startowy punkt = środek dist line annotacji w WCS
                    var leaderWcsPts = new List<Point3d>();
                    using (var trB = db.TransactionManager.StartTransaction())
                    {
                        var brB = trB.GetObject(annotBlockRefId, OpenMode.ForRead) as BlockReference;
                        if (brB != null)
                        {
                            var barB = AnnotationEngine.ReadAnnotXData(brB);
                            if (barB != null)
                            {
                                Point3d startLocal = barB.Direction == "X"
                                    ? new Point3d(0, barB.BarsSpan / 2.0, 0)
                                    : new Point3d(barB.BarsSpan / 2.0, 0, 0);
                                leaderWcsPts.Add(startLocal.TransformBy(brB.BlockTransform));
                            }
                        }
                        trB.Commit();
                    }

                    if (leaderWcsPts.Count > 0)
                    {
                        // Pętla kliknięć jig3 z live preview
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

                                if (ptRes.Status == PromptStatus.Cancel) { drawer.Clear(); return; }
                                if (ptRes.Status != PromptStatus.OK) break;
                                leaderWcsPts.Add(ptRes.Value);
                            }
                            drawer.Clear();
                        }

                        if (leaderWcsPts.Count >= 2)
                        {
                            using (var trC = db.TransactionManager.StartTransaction())
                            {
                                var brC = trC.GetObject(annotBlockRefId, OpenMode.ForWrite) as BlockReference;
                                if (brC != null)
                                {
                                    var barC = AnnotationEngine.ReadAnnotXData(brC);
                                    if (barC != null)
                                    {
                                        var inv = brC.BlockTransform.Inverse();
                                        var localPts = leaderWcsPts.Select(p => p.TransformBy(inv)).ToList();

                                        // Detekcja zawracania — usuń punkty kąta ostrego
                                        for (int k = localPts.Count - 2; k >= 1; k--)
                                        {
                                            var d1 = (localPts[k] - localPts[k - 1]).GetNormal();
                                            var d2 = (localPts[k + 1] - localPts[k]).GetNormal();
                                            if (d1.DotProduct(d2) < -0.1)
                                                localPts.RemoveAt(k);
                                        }

                                        barC.LeaderPoints = AnnotationEngine.EncodeLeaderPoints(localPts);
                                        AnnotationEngine.WriteAnnotXData(brC, barC);
                                        AnnotationEngine.SyncAnnotation(db, barC);
                                    }
                                }
                                trC.Commit();
                            }
                        }
                    }
                }
            }

            try { doc.SendStringToExecute("REGEN\n", false, false, false); } catch { }
        }

        // ----------------------------------------------------------------

        [CommandMethod("RC_BAR_END", CommandFlags.Modal)]
        public void BarEnd()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db  = doc.Database;
            var ed  = doc.Editor;

            // 1. Wybór RC_BAR_BLOCK lub RC_SINGLE_BAR
            var selOpts = new PromptEntityOptions("\nWybierz pręt lub rozkład: ");
            selOpts.SetRejectMessage("\nTo nie jest pręt RC.");
            selOpts.AddAllowedClass(typeof(Polyline),        true);
            selOpts.AddAllowedClass(typeof(BlockReference),  true);
            var selRes = ed.GetEntity(selOpts);
            if (selRes.Status != PromptStatus.OK) return;

            BarData  barData    = null;
            ObjectId targetId   = ObjectId.Null;
            bool     isBarBlock = false;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = tr.GetObject(selRes.ObjectId, OpenMode.ForRead) as Entity;
                if (ent is BlockReference br)
                {
                    targetId   = ResolveBarBlock(ent, tr);
                    if (!targetId.IsNull)
                    {
                        var brT = tr.GetObject(targetId, OpenMode.ForRead) as BlockReference;
                        barData    = brT != null ? BarBlockEngine.ReadXData(brT) : null;
                        isBarBlock = true;
                    }
                }
                else if (ent is Polyline poly)
                {
                    barData  = SingleBarEngine.ReadBarXData(poly);
                    targetId = selRes.ObjectId;
                }
                tr.Commit();
            }

            if (barData == null || targetId.IsNull)
            {
                ed.WriteMessage("\nWybrany obiekt nie zawiera danych pręta RC.");
                return;
            }

            // 2. Dialog z aktualnymi wartościami
            var dlg = new BarEndStyleDialog(
                barData.SymbolType      ?? "Auto",
                barData.SymbolSide      ?? "Right",
                barData.SymbolDirection ?? "Up");

            if (Application.ShowModalWindow(dlg) != true) return;

            // 3. Aktualizuj
            if (isBarBlock)
            {
                BarBlockEngine.RebuildBarEndStyle(db, targetId,
                    dlg.SymbolType, dlg.SymbolSide, dlg.SymbolDirection);
            }
            else
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var poly = tr.GetObject(targetId, OpenMode.ForWrite) as Polyline;
                    if (poly != null)
                    {
                        barData.SymbolType      = dlg.SymbolType;
                        barData.SymbolSide      = dlg.SymbolSide;
                        barData.SymbolDirection = dlg.SymbolDirection;
                        SingleBarEngine.WriteXData(poly, barData);
                    }
                    tr.Commit();
                }
            }

            try { doc.SendStringToExecute("REGEN\n", false, false, false); } catch { }
            ed.WriteMessage("\n[RC] Symbol końca pręta zaktualizowany.");
        }

        // ----------------------------------------------------------------
        // ResolveBarBlock — z dowolnie klikniętego entity zwraca ObjectId
        // BlockReference z XData RC_BAR_BLOCK (lub ObjectId.Null jeśli nie znaleziono).
        //
        // Przypadki:
        //   1. Użytkownik kliknął samego BlockReference (RC_BAR_BLOCK w model space)
        //   2. Użytkownik kliknął Line/Circle wewnątrz BTR RC_SLAB_BARS_* — szukamy
        //      BlockReference przez GetBlockReferenceIds na rodzicu BTR
        // ----------------------------------------------------------------
        private static ObjectId ResolveBarBlock(Entity selectedEnt, Transaction tr)
        {
            // Przypadek 1: bezpośrednio BlockReference z RC_BAR_BLOCK XData
            if (selectedEnt is BlockReference directBr)
            {
                if (BarBlockEngine.ReadXData(directBr) != null)
                    return directBr.ObjectId;
            }

            // Przypadek 2: entity wewnątrz BTR RC_SLAB_BARS_*
            var ownerBtr = tr.GetObject(selectedEnt.OwnerId, OpenMode.ForRead) as BlockTableRecord;
            if (ownerBtr == null || !ownerBtr.Name.StartsWith("RC_SLAB_BARS_"))
                return ObjectId.Null;

            var refs = ownerBtr.GetBlockReferenceIds(true, false);
            foreach (ObjectId refId in refs)
            {
                var br = tr.GetObject(refId, OpenMode.ForRead) as BlockReference;
                if (br != null && BarBlockEngine.ReadXData(br) != null)
                    return refId;
            }

            return ObjectId.Null;
        }

        /// <summary>
        /// RC_SET_REPR_BAR — oznacza wybrany pret jako reprezentatywny,
        /// pozostale prety w tej samej grupie (ten sam Mark) sa ukrywane.
        /// </summary>
        [CommandMethod("RC_SET_REPR_BAR", CommandFlags.Modal)]
        public void SetRepresentativeBar()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            var selOpts = new PromptEntityOptions("\nWybierz pret reprezentatywny: ");
            selOpts.AddAllowedClass(typeof(Line), true);
            var selResult = ed.GetEntity(selOpts);
            if (selResult.Status != PromptStatus.OK) return;

            using var tr = db.TransactionManager.StartTransaction();
            var selectedEntity = (Entity)tr.GetObject(selResult.ObjectId, OpenMode.ForRead);
            var selectedBar = XDataHelper.Read(selectedEntity);

            if (selectedBar == null)
            {
                ed.WriteMessage("\nTo nie jest pret RC SLAB.\n");
                tr.Abort();
                return;
            }

            string targetMark = selectedBar.Mark;

            // Przejdz przez wszystkie obiekty i zarzadzaj widocznoscia
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            int hidden = 0, shown = 0;

            foreach (ObjectId id in space)
            {
                var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (entity == null) continue;

                var bar = XDataHelper.Read(entity);
                if (bar == null || bar.Mark != targetMark) continue;

                entity.UpgradeOpen();

                if (id == selResult.ObjectId)
                {
                    // Ten pret — reprezentatywny, widoczny
                    bar.RepresentativeFlag = 1;
                    entity.Visible = true;
                    shown++;
                }
                else
                {
                    // Pozostale — ukryte
                    bar.RepresentativeFlag = 0;
                    entity.Visible = false;
                    hidden++;
                }
                XDataHelper.Write(entity, bar);
            }

            tr.Commit();
            ed.WriteMessage($"\n[RC SLAB] Ustawiono pret reprezentatywny dla grupy '{targetMark}'. Ukryto: {hidden}, widocznych: {shown}\n");
        }

        /// <summary>
        /// RC_EDIT_LABEL — edytuje treść etykiety rozkładu (Count, Mark, Diameter, Spacing).
        /// Aktualizuje XData RC_BAR_ANNOT i TextString DBText wewnątrz BTR.
        /// </summary>
        [CommandMethod("RC_EDIT_LABEL", CommandFlags.Modal)]
        public void EditLabel()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db  = doc.Database;
            var ed  = doc.Editor;

            // Krok 1 — wybierz blok annotacji
            var selOpts = new PromptEntityOptions("\nWybierz etykietę rozkładu: ");
            selOpts.SetRejectMessage("\nTo nie jest etykieta rozkładu RC.");
            selOpts.AddAllowedClass(typeof(BlockReference), false);
            var selRes = ed.GetEntity(selOpts);
            if (selRes.Status != PromptStatus.OK) return;

            // Krok 2 — odczytaj XData RC_BAR_ANNOT
            BarData bar;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = tr.GetObject(selRes.ObjectId, OpenMode.ForRead) as BlockReference;
                if (br == null) { tr.Abort(); return; }
                bar = AnnotationEngine.ReadAnnotXData(br);
                tr.Commit();
            }
            if (bar == null)
            { ed.WriteMessage("\nNie znaleziono danych etykiety."); return; }

            // Krok 3 — odczytaj ShowSpacing z RC_BAR_BLOCK (flaga nie jest w RC_BAR_ANNOT)
            bool showSpacing = true;
            if (!string.IsNullOrEmpty(bar.SourceBlockHandle) &&
                long.TryParse(bar.SourceBlockHandle, System.Globalization.NumberStyles.HexNumber,
                              null, out long sbHValShow) &&
                db.TryGetObjectId(new Handle(sbHValShow), out ObjectId sbIdShow) && !sbIdShow.IsErased)
            {
                try
                {
                    using var trShow = db.TransactionManager.StartTransaction();
                    var brShow = trShow.GetObject(sbIdShow, OpenMode.ForRead) as BlockReference;
                    if (brShow != null)
                    {
                        var barShow = BarBlockEngine.ReadXData(brShow);
                        if (barShow != null) showSpacing = barShow.ShowSpacing;
                    }
                    trShow.Commit();
                }
                catch { /* fallback true */ }
            }

            var dlg = new EditLabelDialog(bar.EffectiveCount, bar.Mark, bar.Diameter, bar.Spacing,
                                          bar.VisibilityMode, showSpacing);
            if (Application.ShowModalWindow(dlg) != true) return;

            // Obsługa Manual — user klika pręty
            string newVisibleIndices = bar.VisibleIndices ?? "";
            if (dlg.ResultVisibility == BarVisibilityMode.Manual)
            {
                // Tymczasowo pokaż WSZYSTKIE pręty — user musi widzieć co klika
                if (!string.IsNullOrEmpty(bar.SourceBlockHandle))
                {
                    try
                    {
                        long hValPrev = Convert.ToInt64(
                            bar.SourceBlockHandle.TrimStart('0').PadLeft(1, '0'), 16);
                        if (db.TryGetObjectId(new Handle(hValPrev), out ObjectId prevBlockId)
                            && !prevBlockId.IsNull)
                            BarBlockEngine.RebuildVisibility(
                                db, prevBlockId, BarVisibilityMode.All, "");
                    }
                    catch { }
                }

                // User klika pręty — widzi wszystkie, toggle'uje wybrane
                newVisibleIndices = SelectVisibleBarsManually(doc, db, selRes.ObjectId, bar,
                                        existingIndices: bar.VisibleIndices ?? "")
                                    ?? bar.VisibleIndices ?? "";
                // Finalne RebuildVisibility z wybranym zestawem wywoływane niżej (~linia 520)
            }

            // Krok 4 — zaktualizuj XData i DBText w BTR
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = tr.GetObject(selRes.ObjectId, OpenMode.ForWrite) as BlockReference;
                if (br == null) { tr.Abort(); return; }

                bar.CountDisplay   = (dlg.ResultCount != bar.Count) ? (int?)dlg.ResultCount : null;
                bar.Mark           = dlg.ResultMark;
                bar.Spacing        = dlg.ResultSpacing;
                bar.VisibilityMode = dlg.ResultVisibility;
                bar.VisibleIndices = newVisibleIndices;
                // bar.Diameter — nie zmieniamy, pochodzi z pręta

                AnnotationEngine.WriteAnnotXData(br, bar);

                // Zsynchronizuj Mark i Count do RC_BAR_BLOCK
                if (!string.IsNullOrEmpty(bar.SourceBlockHandle))
                {
                    if (long.TryParse(bar.SourceBlockHandle,
                            System.Globalization.NumberStyles.HexNumber,
                            null, out long sbHVal2))
                    {
                        var sbHandle2 = new Handle(sbHVal2);
                        if (db.TryGetObjectId(sbHandle2, out ObjectId sbId2) && !sbId2.IsErased)
                        {
                            using var trMarkSync = db.TransactionManager.StartTransaction();
                            var brBlock = trMarkSync.GetObject(sbId2, OpenMode.ForWrite) as BlockReference;
                            if (brBlock != null)
                            {
                                var barBlock = BarBlockEngine.ReadXData(brBlock);
                                if (barBlock != null)
                                {
                                    barBlock.Mark         = bar.Mark;
                                    barBlock.CountDisplay = bar.CountDisplay;
                                    barBlock.ShowSpacing  = dlg.ResultShowSpacing;
                                    BarBlockEngine.WriteXData(brBlock, barBlock);
                                }
                            }
                            trMarkSync.Commit();
                        }
                    }
                }

                // Zaktualizuj TextString w DBText wewnątrz BTR
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                foreach (ObjectId oid in btr)
                {
                    if (oid.IsErased) continue;
                    var obj = tr.GetObject(oid, OpenMode.ForRead);
                    if (obj is DBText txt)
                    {
                        txt.UpgradeOpen();
                        txt.TextString = $"{bar.EffectiveCount} {bar.Mark}";
                        break;  // tylko pierwszy DBText
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage($"\nEtykieta zaktualizowana: {dlg.ResultCount} {dlg.ResultMark}\n");

            // Zaktualizuj MLeader RC_SINGLE_BAR — honoruje CountDisplay override
            {
                string sourceBarHandle = null;
                if (!string.IsNullOrEmpty(bar.SourceBlockHandle) &&
                    long.TryParse(bar.SourceBlockHandle,
                        System.Globalization.NumberStyles.HexNumber, null, out long sbhvU) &&
                    db.TryGetObjectId(new Handle(sbhvU), out ObjectId sbIdU) && !sbIdU.IsErased)
                {
                    using var trU = db.TransactionManager.StartTransaction();
                    var brU = trU.GetObject(sbIdU, OpenMode.ForRead) as BlockReference;
                    var barU = brU != null ? BarBlockEngine.ReadXData(brU) : null;
                    sourceBarHandle = barU?.SourceBarHandle;
                    trU.Commit();
                }
                if (!string.IsNullOrEmpty(sourceBarHandle))
                    AnnotationEngine.UpdateBarLabelCount(db, sourceBarHandle, markOverride: bar.Mark);
            }

            // Przebuduj blok prętów jeśli znamy SourceBlockHandle (visibility mogła się zmienić)
            if (!string.IsNullOrEmpty(bar.SourceBlockHandle))
            {
                // Gdy Manual z pustym indicesem → zachowaj poprzedni stan zamiast resetować do pręta 0
                string safeIndices = newVisibleIndices;
                if (dlg.ResultVisibility == BarVisibilityMode.Manual
                    && string.IsNullOrEmpty(safeIndices))
                {
                    safeIndices = bar.VisibleIndices ?? "0";
                }

                try
                {
                    long hVal = Convert.ToInt64(bar.SourceBlockHandle.TrimStart('0').PadLeft(1, '0'), 16);
                    if (db.TryGetObjectId(new Handle(hVal), out ObjectId barBlockId) && !barBlockId.IsNull)
                        BarBlockEngine.RebuildVisibility(db, barBlockId, dlg.ResultVisibility, safeIndices);
                }
                catch { }
            }

            // Przebuduj annotację (kółka na dist line) z nową widocznością
            using (var trSync = db.TransactionManager.StartTransaction())
            {
                // Znajdź RC_BAR_BLOCK przez SourceBlockHandle z annotacji
                var annotBrSync = trSync.GetObject(selRes.ObjectId, OpenMode.ForRead) as BlockReference;
                if (annotBrSync != null)
                {
                    var barAnnotSync = AnnotationEngine.ReadAnnotXData(annotBrSync);
                    if (barAnnotSync != null && !string.IsNullOrEmpty(barAnnotSync.SourceBlockHandle))
                    {
                        if (long.TryParse(barAnnotSync.SourceBlockHandle,
                                System.Globalization.NumberStyles.HexNumber,
                                null, out long sbHVal))
                        {
                            var sbHandle = new Handle(sbHVal);
                            if (db.TryGetObjectId(sbHandle, out ObjectId sbId) && !sbId.IsErased)
                            {
                                var barBlockBr   = trSync.GetObject(sbId, OpenMode.ForRead) as BlockReference;
                                var barBlockData = barBlockBr != null ? BarBlockEngine.ReadXData(barBlockBr) : null;
                                if (barBlockData != null)
                                {
                                    trSync.Commit();
                                    AnnotationEngine.SyncAnnotation(db, barBlockData);
                                }
                                else trSync.Commit();
                            }
                            else trSync.Commit();
                        }
                        else trSync.Commit();
                    }
                    else trSync.Commit();
                }
                else trSync.Commit();
            }

            // Wymusz odświeżenie renderowania bloku
            using (var trRefresh = db.TransactionManager.StartTransaction())
            {
                var brRefresh = trRefresh.GetObject(selRes.ObjectId, OpenMode.ForWrite) as BlockReference;
                brRefresh?.RecordGraphicsModified(true);
                trRefresh.Commit();
            }

            // Przelicz armTotalLen na podstawie nowej długości tekstu i zaktualizuj arm
            using (var trArm = db.TransactionManager.StartTransaction())
            {
                var brArm = trArm.GetObject(selRes.ObjectId, OpenMode.ForRead) as BlockReference;
                if (brArm != null)
                {
                    var barArm = AnnotationEngine.ReadAnnotXData(brArm);
                    if (barArm != null)
                    {
                        var btrArm = (BlockTableRecord)trArm.GetObject(brArm.BlockTableRecord, OpenMode.ForRead);
                        double newTextLen = 0;
                        foreach (ObjectId oid in btrArm)
                        {
                            if (oid.IsErased) continue;
                            var obj = trArm.GetObject(oid, OpenMode.ForRead);
                            if (obj is DBText txt)
                            {
                                try
                                {
                                    var ext = txt.GeometricExtents;
                                    if (System.Math.Abs(txt.Rotation - System.Math.PI / 2.0) < 0.01)
                                        newTextLen = System.Math.Abs(ext.MaxPoint.Y - ext.MinPoint.Y);
                                    else
                                        newTextLen = System.Math.Abs(ext.MaxPoint.X - ext.MinPoint.X);
                                }
                                catch
                                {
                                    newTextLen = txt.TextString.Length * AnnotationEngine.TextCharWidth;
                                }
                                break;
                            }
                        }

                        if (newTextLen > 0)
                        {
                            barArm.TextLen = newTextLen;
                            using (var trTextLen = db.TransactionManager.StartTransaction())
                            {
                                var brTextLen = trTextLen.GetObject(selRes.ObjectId, OpenMode.ForWrite) as BlockReference;
                                if (brTextLen != null)
                                    AnnotationEngine.WriteAnnotXData(brTextLen, barArm);
                                trTextLen.Commit();
                            }

                            trArm.Commit();
                        }
                        else
                        {
                            trArm.Commit();
                        }
                    }
                    else trArm.Commit();
                }
                else trArm.Commit();
            }

            try { doc.SendStringToExecute("REGEN\n", false, false, false); } catch { }
        }

        private static string SelectVisibleBarsManually(
            Document doc, Database db, ObjectId annotId, BarData bar,
            string existingIndices = "")
        {
            var ed = doc.Editor;
            ed.WriteMessage("\nKliknij pręty które mają być widoczne (toggle). Enter = zatwierdź.");

            // Prefill z poprzedniego stanu — user widzi co już jest zaznaczone i może toggle'ować
            var selectedIndices = new System.Collections.Generic.HashSet<int>();
            if (!string.IsNullOrEmpty(existingIndices))
            {
                foreach (var part in existingIndices.Split(','))
                    if (int.TryParse(part.Trim(), out int idx))
                        selectedIndices.Add(idx);
            }

            while (true)
            {
                var opts = new PromptPointOptions("\nKliknij pręt (Enter = koniec): ");
                opts.AllowNone = true;
                var res = ed.GetPoint(opts);
                if (res.Status == PromptStatus.None || res.Status == PromptStatus.Cancel) break;
                if (res.Status != PromptStatus.OK) break;

                using var tr = db.TransactionManager.StartTransaction();
                var annotBr = tr.GetObject(annotId, OpenMode.ForRead) as BlockReference;
                if (annotBr != null)
                {
                    var barData = AnnotationEngine.ReadAnnotXData(annotBr);
                    if (barData != null && barData.Spacing > 0)
                    {
                        var inv   = annotBr.BlockTransform.Inverse();
                        var local = res.Value.TransformBy(inv);

                        double coord   = barData.Direction == "X" ? local.Y : local.X;
                        int    nearest = (int)Math.Round(coord / barData.Spacing);
                        nearest = Math.Max(0, Math.Min(barData.Count - 1, nearest));

                        if (selectedIndices.Contains(nearest))
                            selectedIndices.Remove(nearest);
                        else
                            selectedIndices.Add(nearest);

                        ed.WriteMessage($"\n  Pręt {nearest + 1} {(selectedIndices.Contains(nearest) ? "zaznaczony" : "odznaczony")}. Zaznaczonych: {selectedIndices.Count}");
                    }
                }
                tr.Commit();
            }

            if (selectedIndices.Count == 0) return null;
            var sorted = new System.Collections.Generic.List<int>(selectedIndices);
            sorted.Sort();
            return string.Join(",", sorted);
        }

        /// <summary>
        /// RC_SHOW_ALL_BARS — przywraca widoczność (All) wszystkich rozkładów RC_BAR_BLOCK.
        /// </summary>
        [CommandMethod("RC_SHOW_ALL_BARS", CommandFlags.Modal)]
        public void ShowAllBars()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db  = doc.Database;
            var ed  = doc.Editor;

            var toRebuild = new System.Collections.Generic.List<(ObjectId id, BarData bar)>();

            // Krok 1 — zbierz rozkłady które nie są w trybie All
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(
                    db.CurrentSpaceId, OpenMode.ForRead);

                foreach (ObjectId id in space)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (ent == null || ent.IsErased) continue;

                    var barBlock = BarBlockEngine.ReadXData(ent);
                    if (barBlock == null) continue;

                    if (barBlock.VisibilityMode != BarVisibilityMode.All)
                        toRebuild.Add((id, barBlock));
                }
                tr.Commit();
            }

            // Krok 2 — przebuduj każdy rozkład poza transakcją iteracji
            // (RebuildVisibility i SyncAnnotation tworzą własne transakcje wewnętrznie)
            foreach (var (id, bar) in toRebuild)
            {
                // 1. Przebuduj symbole prętów w RC_BAR_BLOCK (kółka/strzałki)
                BarBlockEngine.RebuildVisibility(db, id, BarVisibilityMode.All, "");

                // 2. Przebuduj geometrię RC_BAR_ANNOT (doty na dist line, ramię leadera)
                bar.VisibilityMode = BarVisibilityMode.All;
                bar.VisibleIndices = "";
                if (!string.IsNullOrEmpty(bar.AnnotHandle))
                    AnnotationEngine.SyncAnnotation(db, bar);
            }

            ed.WriteMessage($"\n[RC] Zaktualizowano {toRebuild.Count} rozkładów → widok: Wszystkie.\n");
        }

        /// <summary>
        /// RC_SCALE_ANNOT — ustawia wizualną skalę opisu rozkładu (tekst, doty, strzałki, zakończenia prętów).
        /// Skala 1:50 = factor 1.0 (domyślna). 1:25 = 2.0 (dwa razy większy). 1:100 = 0.5.
        /// </summary>
        [CommandMethod("RC_SCALE_ANNOT", CommandFlags.Modal)]
        public void RcScaleAnnot()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var ed = doc.Editor;

            // 1. Wybierz obiekt (RC_BAR_BLOCK lub RC_BAR_ANNOT)
            var opts = new PromptEntityOptions("\nWybierz opis rozkładu (RC_BAR_BLOCK lub RC_BAR_ANNOT): ");
            opts.SetRejectMessage("\nTo nie jest RC_BAR_BLOCK ani RC_BAR_ANNOT.");
            opts.AddAllowedClass(typeof(BlockReference), true);
            var selRes = ed.GetEntity(opts);
            if (selRes.Status != PromptStatus.OK) return;

            // 2. Resolve do RC_BAR_BLOCK (source of truth dla AnnotScale)
            ObjectId blockId = ObjectId.Null;
            double currentScale = 1.0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = tr.GetObject(selRes.ObjectId, OpenMode.ForRead) as BlockReference;
                if (br == null) { ed.WriteMessage("\nNieprawidłowy obiekt."); tr.Commit(); return; }

                if (BarBlockEngine.IsBarBlock(br))
                {
                    blockId = br.ObjectId;
                    var bd = BarBlockEngine.ReadXData(br);
                    if (bd != null) currentScale = bd.AnnotScale;
                }
                else if (AnnotationEngine.IsAnnotation(br))
                {
                    var annotBd = AnnotationEngine.ReadAnnotXData(br);
                    if (annotBd == null || string.IsNullOrEmpty(annotBd.SourceBlockHandle))
                    {
                        ed.WriteMessage("\nAnotacja nie ma powiązanego RC_BAR_BLOCK.");
                        tr.Commit();
                        return;
                    }
                    long h;
                    try { h = Convert.ToInt64(annotBd.SourceBlockHandle, 16); }
                    catch { ed.WriteMessage("\nNieprawidłowy SourceBlockHandle."); tr.Commit(); return; }
                    if (!db.TryGetObjectId(new Handle(h), out var bId))
                    {
                        ed.WriteMessage("\nNie znaleziono powiązanego RC_BAR_BLOCK.");
                        tr.Commit();
                        return;
                    }
                    blockId = bId;
                    var blkBr = tr.GetObject(blockId, OpenMode.ForRead) as BlockReference;
                    if (blkBr == null) { tr.Commit(); return; }
                    var bd = BarBlockEngine.ReadXData(blkBr);
                    if (bd != null) currentScale = bd.AnnotScale;
                }
                else
                {
                    ed.WriteMessage("\nWybrany obiekt nie jest RC_BAR_BLOCK ani RC_BAR_ANNOT.");
                    tr.Commit();
                    return;
                }
                tr.Commit();
            }

            if (blockId.IsNull) return;

            // 3. Zapytaj o skalę rysunku (mianownik: 50 = 1:50, 25 = 1:25 itd.)
            double currentDenominator = 50.0 * (currentScale > 0 ? currentScale : 1.0);
            var dOpts = new PromptDoubleOptions($"\nSkala rysunku 1: <{(int)Math.Round(currentDenominator)}>: ");
            dOpts.AllowNone = true;
            dOpts.AllowNegative = false;
            dOpts.AllowZero = false;
            dOpts.DefaultValue = currentDenominator;
            dOpts.UseDefaultValue = true;
            var dRes = ed.GetDouble(dOpts);
            if (dRes.Status != PromptStatus.OK && dRes.Status != PromptStatus.None) return;

            double denominator = dRes.Status == PromptStatus.None ? currentDenominator : dRes.Value;
            if (denominator <= 0) { ed.WriteMessage("\nSkala musi być dodatnia."); return; }

            double newFactor = denominator / 50.0;

            // 4a. Zapisz nowy AnnotScale do RC_BAR_BLOCK XData
            BarData barData;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var blockBr = tr.GetObject(blockId, OpenMode.ForWrite) as BlockReference;
                if (blockBr == null) { tr.Commit(); return; }
                barData = BarBlockEngine.ReadXData(blockBr);
                if (barData == null) { tr.Commit(); return; }
                barData.AnnotScale = newFactor;
                BarBlockEngine.WriteXData(blockBr, barData);
                tr.Commit();
            }

            // 4b. Przebuduj zakończenia prętów (SymR/HookLen skalują się przez AnnotScale)
            try
            {
                BarBlockEngine.RebuildBarEndStyle(db, blockId,
                    barData.SymbolType      ?? "Auto",
                    barData.SymbolSide      ?? "Right",
                    barData.SymbolDirection ?? "Up");
            }
            catch { }

            // 4c. Przebuduj anotację (BuildH/V używają Scaled(...) dla tekstu/dotów/strzałek)
            AnnotationEngine.SyncAnnotation(db, barData);

            ed.Regen();
            ed.WriteMessage($"\n[RC] Skala opisu ustawiona na 1:{denominator:F0} (factor={newFactor:F3}).");
        }
    }
}
