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
        // RC_EDIT_BAR (legacy Line/DBText + BarPropertiesDialog) usunięte —
        // komenda była zarejestrowana dwa razy. Aktualna wersja: BarCommands.EditBar.

        /// <summary>
        /// RC_EDIT_DISTRIBUTION dla rozkładu Any (decyzja 2026-10-14): okno opisu (liczba, rozstaw, sufiks) — w bloku zostaje jeden pręt
        /// (Count = 1), liczba w CountDisplay (opis, zestawienie), rozstaw w Spacing i w opisie.
        /// </summary>
        private static void EditAnyDistribution(Database db, Editor ed, ObjectId blockRefId, BarData bar)
        {
            int posNr = SingleBarEngine.ExtractPosNr(bar.Mark);
            string mark0 = (bar.Mark ?? "").Trim();
            int sp = mark0.IndexOf(' ');
            string suffix0 = sp > 0 ? mark0.Substring(sp + 1).Trim() : "";
            string baseMark = BarData.FormatMark(bar.Diameter, posNr, bar.Spacing, bar.EffectiveCount);
            var dlg = new DistributionDescDialog(bar, bar.EffectiveCount, bar.Spacing > 0 ? bar.Spacing : 200, baseMark, suffix0);
            if (Application.ShowModalWindow(dlg) != true) return;

            string newBase = BarData.FormatMark(bar.Diameter, posNr, dlg.BarSpacing, 2);   // Any: rozstaw zawsze w opisie
            string mark = string.IsNullOrEmpty(dlg.Suffix) ? newBase : $"{newBase} {dlg.Suffix}";
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(blockRefId, OpenMode.ForWrite) is BlockReference br && BarBlockEngine.ReadXData(br) is BarData b)
                {
                    b.Mark = mark;
                    b.Spacing = dlg.BarSpacing;
                    b.CountDisplay = dlg.BarCount != b.Count ? (int?)dlg.BarCount : null;
                    BarBlockEngine.WriteXData(br, b);
                    bar = b;
                }
                tr.Commit();
            }
            AnnotationEngine.SyncAnnotation(db, bar);
            AnnotationEngine.UpdateBarLabelCount(db, bar.SourceBarHandle ?? "", markOverride: mark);
            ed.WriteMessage($"\n[RC] Rozkład Any: {bar.EffectiveCount} prętów, rozstaw {bar.Spacing:F0} — {mark}\n");
            try { Application.DocumentManager.MdiActiveDocument?.SendStringToExecute("REGEN\n", false, false, false); }
            catch (System.Exception logEx) { Log.Error("EditCommands.EditAnyDistribution", logEx); }
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

            // Rozkład Any (decyzja 2026-10-14): jeden pręt w bloku — edycja tylko liczby / rozstawu / sufiksu opisu
            if (BarBlockEngine.IsAny(bar))
            {
                EditAnyDistribution(db, ed, blockRefId, bar);
                return;
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

                try { doc.SendStringToExecute("REGEN\n", false, false, false); } catch (System.Exception logEx) { Log.Error("EditCommands.EditDistribution", logEx); }
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

            // Zgłoszenie kolegi 2026-10-16: kopia rozkładu bez opisu z nieaktualnym [11] (opis oryginału) — opis nie wskazuje
            // [16] z powrotem na ten rozkład. Wtedy „brak opisu” (opcja Dodaj opis) i [11] czyszczone — przebudowa nie rusza
            // opisu oryginału.
            if (!annotMissing)
            {
                try
                {
                    using var trL = db.TransactionManager.StartTransaction();
                    ObjectId aId = ObjectId.Null;
                    if (long.TryParse(bar.AnnotHandle, System.Globalization.NumberStyles.HexNumber, null, out long aH))
                        db.TryGetObjectId(new Handle(aH), out aId);
                    var brL = trL.GetObject(blockRefId, OpenMode.ForWrite) as BlockReference;
                    if (!aId.IsNull && brL != null && !BarEraseCascade.AnnotBelongsTo(trL, aId, brL.Handle.Value.ToString("X8")))
                    {
                        annotMissing = true;
                        bar.AnnotHandle = "";
                        var xd = BarBlockEngine.ReadXData(brL);
                        if (xd != null) { xd.AnnotHandle = ""; BarBlockEngine.WriteXData(brL, xd); }
                        ed.WriteMessage("\n[RC] Opis wskazywany przez ten rozkład należy do innego rozkładu (kopia bez opisu) — " +
                                        "rozkład odłączony; użyj „Dodaj opis”.\n");
                    }
                    trL.Commit();
                }
                catch (System.Exception ex) { Log.Error("EditCommands.EditDistribution.Link", ex); }
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
                catch (System.Exception logEx) { Log.Error("EditCommands.EditDistribution", logEx); }
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
                    var bt     = brRe.BlockTransform;
                    double signX = Vector3d.XAxis.TransformBy(bt).X >= 0 ? 1.0 : -1.0;
                    double signY = Vector3d.YAxis.TransformBy(bt).Y >= 0 ? 1.0 : -1.0;
                    double dx    = signX * (bar.Direction == "X" ? bar.LengthA : bar.BarsSpan);
                    double dy    = signY * (bar.Direction == "X" ? bar.BarsSpan : bar.LengthA);
                    barResult = new BarBlockEngine.BarBlockResult
                    {
                        BlockRefId = blockRefId,
                        BarOrigin  = insertWCS,
                        MinPoint   = new Point3d(
                            dx >= 0 ? insertWCS.X        : insertWCS.X + dx,
                            dy >= 0 ? insertWCS.Y        : insertWCS.Y + dy,
                            0),
                        MaxPoint   = new Point3d(
                            dx >= 0 ? insertWCS.X + dx   : insertWCS.X,
                            dy >= 0 ? insertWCS.Y + dy   : insertWCS.Y,
                            0)
                    };
                }
                trRe.Commit();

                string baseMark = BarData.FormatMark(bar.Diameter, SingleBarEngine.ExtractPosNr(bar.Mark), bar.Spacing, bar.Count);
                BarCommands.RunAnnotationFlow(doc, db, bar, barResult, bar.Direction == "X",
                    bar.Spacing, bar.Count, baseMark, blockRefId, askVisibility: true);

                try { doc.SendStringToExecute("REGEN\n", false, false, false); } catch (System.Exception logEx) { Log.Error("EditCommands.EditDistribution", logEx); }
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
                                    catch (System.Exception logEx) { Log.Error("EditCommands.EditDistribution", logEx); }
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

            try { doc.SendStringToExecute("REGEN\n", false, false, false); } catch (System.Exception logEx) { Log.Error("EditCommands.EditDistribution", logEx); }
        }

        // ----------------------------------------------------------------

        [CommandMethod("RC_BAR_END", CommandFlags.Modal)]
        public void BarEnd()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db  = doc.Database;
            var ed  = doc.Editor;

            // 1. Wybór rozkładu (RC_BAR_BLOCK) — symbol końca jest cechą rozkładu (poprawka #10);
            //    pręt (polilinia / pręt-blok) nie ma go w danych RC_SINGLE_BAR
            var selOpts = new PromptEntityOptions("\nWybierz rozkład: ");
            selOpts.SetRejectMessage("\n[RC] To nie jest rozkład RC.");
            selOpts.AddAllowedClass(typeof(Polyline),        true);
            selOpts.AddAllowedClass(typeof(BlockReference),  true);
            var selRes = ed.GetEntity(selOpts);
            if (selRes.Status != PromptStatus.OK) return;

            BarData  barData    = null;
            ObjectId targetId   = ObjectId.Null;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = tr.GetObject(selRes.ObjectId, OpenMode.ForRead) as Entity;
                var kind = BarEntity.KindOf(ent);
                if (kind == BarKind.Polyline || kind == BarKind.Block)
                {
                    tr.Commit();
                    ed.WriteMessage("\n[RC] Symbol końca ustawia się na rozkładzie — wskaż rozkład, nie pręt.");
                    return;
                }
                if (ent is BlockReference)
                {
                    targetId = ResolveBarBlock(ent, tr);
                    if (!targetId.IsNull)
                    {
                        var brT = tr.GetObject(targetId, OpenMode.ForRead) as BlockReference;
                        barData = brT != null ? BarBlockEngine.ReadXData(brT) : null;
                    }
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
            BarBlockEngine.RebuildBarEndStyle(db, targetId,
                dlg.SymbolType, dlg.SymbolSide, dlg.SymbolDirection);

            try { doc.SendStringToExecute("REGEN\n", false, false, false); } catch (System.Exception logEx) { Log.Error("EditCommands.BarEnd", logEx); }
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
        /// RC_SET_REPR_BAR (wstążka „Single Bar View”) — wygasza pręty na wskazanych płytach jak RC_GENERATE (decyzja
        /// użytkownika 2026-10-16; dawna wersja dla linii RC SLAB nie działała z rozkładami): w każdym rozkładzie jeden
        /// widoczny pręt, odcinki w pasie przesunięte o rozstaw, potem odsunięcie od linii rozkładów. Odwrotność RC_SHOW_ALL_BARS.
        /// </summary>
        [CommandMethod("RC_SET_REPR_BAR", CommandFlags.Modal)]
        public void SetRepresentativeBar()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            var opts = new PromptSelectionOptions { MessageForAdding = "\nWybierz obrysy płyt (rzut dolny / górny): " };
            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE") });
            var sel = ed.GetSelection(opts, filter);
            if (sel.Status != PromptStatus.OK) return;

            var slabs = new System.Collections.Generic.HashSet<ObjectId>();
            foreach (var id in sel.Value.GetObjectIds())
            {
                try { slabs.Add(NibDetector.OuterOf(db, id)); }
                catch (System.Exception ex) { Log.Error("EditCommands.SetRepresentativeBar.Outer", ex); slabs.Add(id); }
            }
            int dists = 0, hidden = 0, moved = 0;
            foreach (var slabId in slabs)
            {
                try
                {
                    var r = AutoRebarEngine.ApplyRepresentativeOnSlab(doc, slabId);
                    dists += r.Dists; hidden += r.Hidden; moved += r.Moved;
                }
                catch (System.Exception ex)
                {
                    Log.Error("EditCommands.SetRepresentativeBar", ex);
                    ed.WriteMessage($"\n[RC] Błąd wygaszania: {ex.Message}");
                }
            }
            ed.WriteMessage($"\n[RC] Single Bar View: płyty {slabs.Count}, rozkłady {dists} — wygaszone {hidden}, " +
                            $"przesunięte (czytelność) {moved}. Wszystkie pręty z powrotem: RC_SHOW_ALL_BARS.\n");
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

            // Zgłoszenie kolegi 2026-10-16: opis skopiowany osobno ma [16] rozkładu oryginału — zmiany widoczności trafiały w
            // oryginał. Opis, którego rozkład [16] nie wskazuje [11] z powrotem na ten opis — odłączony (tylko tekst opisu).
            bool unlinkedAnnot = false;
            if (!string.IsNullOrEmpty(bar.SourceBlockHandle))
            {
                try
                {
                    using var trL = db.TransactionManager.StartOpenCloseTransaction();
                    bool linked = false;
                    if (long.TryParse(bar.SourceBlockHandle, System.Globalization.NumberStyles.HexNumber, null, out long sH)
                        && db.TryGetObjectId(new Handle(sH), out ObjectId sId) && !sId.IsErased
                        && trL.GetObject(sId, OpenMode.ForRead) is BlockReference sBr)
                    {
                        var sXd = BarBlockEngine.ReadXData(sBr);
                        linked = sXd != null && XLink.Same(sXd.AnnotHandle, selRes.ObjectId.Handle.Value.ToString("X8"));
                    }
                    trL.Commit();
                    if (!linked)
                    {
                        bar.SourceBlockHandle = "";
                        unlinkedAnnot = true;
                        ed.WriteMessage("\n[RC] Ten opis nie jest powiązany z rozkładem (np. skopiowany osobno) — zmienisz tylko tekst; " +
                                        "wygaszanie prętów: RC_EDIT_DISTRIBUTION → Dodaj opis na właściwym rozkładzie.\n");
                    }
                }
                catch (System.Exception ex) { Log.Error("EditCommands.EditLabel.Link", ex); }
            }

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
                catch (System.Exception logEx) { Log.Error("EditCommands.EditLabel", logEx); /* fallback true */ }
            }

            var dlg = new EditLabelDialog(bar.EffectiveCount, bar.Mark, bar.Diameter, bar.Spacing,
                                          bar.VisibilityMode, showSpacing, countText: bar.CountText);
            if (Application.ShowModalWindow(dlg) != true) return;

            // Obsługa Manual — user klika pręty
            string newVisibleIndices = bar.VisibleIndices ?? "";
            if (dlg.ResultVisibility == BarVisibilityMode.Manual && !unlinkedAnnot)
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
                    catch (System.Exception logEx) { Log.Error("EditCommands.EditLabel", logEx); }
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
                bar.CountMult      = dlg.ResultMult;   // „2x5” (2026-10-23)
                bar.Mark           = dlg.ResultMark;
                bar.Spacing        = dlg.ResultSpacing;
                bar.VisibilityMode = unlinkedAnnot ? bar.VisibilityMode : dlg.ResultVisibility;   // opis bez rozkładu — widoczność bez zmian
                bar.VisibleIndices = newVisibleIndices;
                // bar.Diameter — nie zmieniamy, pochodzi z pręta
                bar.IsLabelManual  = true;  // p334: manual override after RC_EDIT_LABEL

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
                                    barBlock.Mark          = bar.Mark;
                                    barBlock.CountDisplay  = bar.CountDisplay;
                                    barBlock.CountMult     = bar.CountMult;
                                    barBlock.ShowSpacing   = dlg.ResultShowSpacing;
                                    barBlock.IsLabelManual = true;  // p334: manual override sync
                                    BarBlockEngine.WriteXData(brBlock, barBlock);
                                }
                            }
                            trMarkSync.Commit();
                        }
                    }
                }

                // Zaktualizuj TextString w DBText wewnątrz BTR (własny BTR — opis skopiowany nie zmienia oryginału)
                var btr = BarBlockEngine.OwnBtrForWrite(tr, br);
                foreach (ObjectId oid in btr)
                {
                    if (oid.IsErased) continue;
                    var obj = tr.GetObject(oid, OpenMode.ForRead);
                    if (obj is DBText txt)
                    {
                        txt.UpgradeOpen();
                        txt.TextString = $"{bar.CountText} {bar.Mark}";
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
                catch (System.Exception logEx) { Log.Error("EditCommands.EditLabel", logEx); }
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

            try { doc.SendStringToExecute("REGEN\n", false, false, false); } catch (System.Exception logEx) { Log.Error("EditCommands.EditLabel", logEx); }
        }

        /// <summary>
        /// Widoczność prętów po dodaniu opisu (RC_EDIT_DISTRIBUTION → „Dodaj opis rozkładu”, prośba Bartka 2026-10-28) — jak RC_EDIT_LABEL:
        /// Manualny — wszystkie pręty widoczne na czas klikania, wybór klikanych (Enter — koniec; nic nie wybrane — poprzedni stan);
        /// pozostałe tryby — od razu. Rozkład przebudowany, opis (kropki na linii rozkładu) zsynchronizowany.
        /// </summary>
        internal static void ApplyDistributionVisibility(Document doc, Database db, ObjectId annotId, ObjectId blockRefId, BarVisibilityMode mode)
        {
            try
            {
                if (annotId.IsNull || blockRefId.IsNull) return;
                string existing;
                int count;
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var bb = (tr.GetObject(blockRefId, OpenMode.ForRead) as BlockReference) is BlockReference brb ? BarBlockEngine.ReadXData(brb) : null;
                    tr.Commit();
                    if (bb == null) return;
                    existing = bb.VisibleIndices ?? "";
                    count = bb.Count;
                }
                string indices = "";
                if (mode == BarVisibilityMode.Manual)
                {
                    BarBlockEngine.RebuildVisibility(db, blockRefId, BarVisibilityMode.All, "");
                    var pick = new BarData { SourceBlockHandle = blockRefId.Handle.Value.ToString("X"), Count = count };
                    indices = SelectVisibleBarsManually(doc, db, annotId, pick, existing) ?? (existing.Length > 0 ? existing : "0");
                }
                BarBlockEngine.RebuildVisibility(db, blockRefId, mode, indices);
                BarData fresh = null;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    if (tr.GetObject(blockRefId, OpenMode.ForRead) is BlockReference brf) fresh = BarBlockEngine.ReadXData(brf);
                    if (tr.GetObject(annotId, OpenMode.ForWrite) is BlockReference ab && AnnotationEngine.ReadAnnotXData(ab) is BarData ad)
                    {
                        ad.VisibilityMode = mode;
                        ad.VisibleIndices = indices;
                        AnnotationEngine.WriteAnnotXData(ab, ad);
                    }
                    tr.Commit();
                }
                if (fresh != null) AnnotationEngine.SyncAnnotation(db, fresh);
            }
            catch (System.Exception ex) { Log.Error("EditCommands.ApplyDistributionVisibility", ex); }
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

            // Podświetlenie zaznaczonych prętów (decyzja użytkownika 2026-10-16): gruba czerwona polilinia na pręcie,
            // tymczasowa — usuwana po zakończeniu wyboru
            using var marks = new BarPickMarks(db, bar.SourceBlockHandle);
            foreach (int i in selectedIndices) marks.Set(i, true);
            marks.Flush(doc);

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
                        // indeks w układzie ROZKŁADU (bloku prętów); bez rozkładu — dawniej, w układzie opisu
                        int nearest = marks.IndexAt(res.Value);
                        if (nearest < 0)
                        {
                            var local = res.Value.TransformBy(annotBr.BlockTransform.Inverse());
                            double coord = barData.Direction == "X" ? local.Y : local.X;
                            nearest = Math.Max(0, Math.Min(barData.Count - 1, (int)Math.Round(coord / barData.Spacing)));
                        }

                        if (selectedIndices.Contains(nearest))
                            selectedIndices.Remove(nearest);
                        else
                            selectedIndices.Add(nearest);

                        ed.WriteMessage($"\n  Pręt {nearest + 1} {(selectedIndices.Contains(nearest) ? "zaznaczony" : "odznaczony")}. Zaznaczonych: {selectedIndices.Count}");
                    }
                }
                tr.Commit();
                foreach (int i in Enumerable.Range(0, Math.Max(marks.Count, bar.Count))) marks.Set(i, selectedIndices.Contains(i));
                marks.Flush(doc);
            }

            if (selectedIndices.Count == 0) return null;
            var sorted = new System.Collections.Generic.List<int>(selectedIndices);
            sorted.Sort();
            return string.Join(",", sorted);
        }

        /// <summary>
        /// Rozkład klikany w RC_EDIT_LABEL (wygaszanie ręczne): indeks pręta pod kliknięciem i tymczasowe znaczniki (gruba czerwona
        /// polilinia wzdłuż pręta). Wszystko w układzie ROZKŁADU (BlockTransform bloku prętów: pręt i = i · rozstaw) — zgłoszenie
        /// kolegi 2026-10-16: indeks liczony w układzie opisu wypadał o kilka prętów obok, gdy opis był przesunięty / dodany osobno.
        /// Dispose usuwa znaczniki.
        /// </summary>
        private sealed class BarPickMarks : IDisposable
        {
            private readonly Database _db;
            private readonly Dictionary<int, ObjectId> _ids = new Dictionary<int, ObjectId>();
            private readonly bool _barsX;
            private readonly Matrix3d _xf, _inv;
            private readonly double _sp, _lo, _hi, _width;
            public bool Ok { get; }
            public int Count { get; }

            public BarPickMarks(Database db, string distHandle)
            {
                _db = db;
                try
                {
                    if (string.IsNullOrEmpty(distHandle)
                        || !long.TryParse(distHandle, System.Globalization.NumberStyles.HexNumber, null, out long h)
                        || !db.TryGetObjectId(new Handle(h), out ObjectId distId) || distId.IsErased) return;
                    using var tr = db.TransactionManager.StartOpenCloseTransaction();
                    if (!(tr.GetObject(distId, OpenMode.ForRead) is BlockReference br)) return;
                    var d = BarBlockEngine.ReadXData(br);
                    if (d == null || d.Spacing <= 0) return;
                    _barsX = d.Direction == "X";
                    _xf = br.BlockTransform;
                    _inv = _xf.Inverse();
                    _sp = d.Spacing;
                    Count = Math.Max(1, d.Count);
                    _lo = Math.Min(0, Math.Min(d.SkewStart, d.SkewEnd));
                    _hi = d.LengthA + Math.Max(0, Math.Max(d.SkewStart, d.SkewEnd));
                    _width = Math.Max(30.0, Math.Min(80.0, 0.3 * _sp));
                    Ok = _hi - _lo > 1.0;
                }
                catch (System.Exception ex) { Log.Error("EditCommands.BarPickMarks", ex); }
            }

            /// <summary>Indeks pręta najbliższego klikniętemu punktowi (układ rozkładu); -1 — brak rozkładu.</summary>
            public int IndexAt(Point3d wcs)
            {
                if (!Ok) return -1;
                var l = wcs.TransformBy(_inv);
                double c = _barsX ? l.Y : l.X;
                return Math.Max(0, Math.Min(Count - 1, (int)Math.Round(c / _sp)));
            }

            public void Set(int i, bool on)
            {
                if (!Ok) return;
                bool has = _ids.TryGetValue(i, out ObjectId id);
                if (on == has) return;
                try
                {
                    using var tr = _db.TransactionManager.StartTransaction();
                    if (on)
                    {
                        double c = i * _sp;
                        var pl = new Polyline();
                        pl.SetDatabaseDefaults(_db);
                        pl.AddVertexAt(0, _barsX ? new Point2d(_lo, c) : new Point2d(c, _lo), 0, _width, _width);
                        pl.AddVertexAt(1, _barsX ? new Point2d(_hi, c) : new Point2d(c, _hi), 0, _width, _width);
                        pl.ColorIndex = 1;
                        pl.TransformBy(_xf);
                        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);
                        _ids[i] = ms.AppendEntity(pl);
                        tr.AddNewlyCreatedDBObject(pl, true);
                    }
                    else
                    {
                        if (!id.IsErased && tr.GetObject(id, OpenMode.ForWrite) is Entity e) e.Erase();
                        _ids.Remove(i);
                    }
                    tr.Commit();
                }
                catch (System.Exception ex) { Log.Error("EditCommands.BarPickMarks.Set", ex); }
            }

            public void Flush(Document doc)
            {
                try { doc.TransactionManager.QueueForGraphicsFlush(); doc.TransactionManager.FlushGraphics(); doc.Editor.UpdateScreen(); }
                catch (System.Exception ex) { Log.Error("EditCommands.BarPickMarks.Flush", ex); }
            }

            public void Dispose()
            {
                foreach (int i in _ids.Keys.ToList()) Set(i, false);
            }
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
        /// Skala 1:50 = factor 1.0 (domyślna). Factor = mianownik / 50: 1:25 = 0.5 (opis o połowę mniejszy w modelu — na papierze 1:25 ten sam), 1:100 = 2.0.
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
            catch (System.Exception logEx) { Log.Error("EditCommands.RcScaleAnnot", logEx); }

            // 4c. Przebuduj anotację (BuildH/V używają Scaled(...) dla tekstu/dotów/strzałek)
            AnnotationEngine.SyncAnnotation(db, barData);

            ed.Regen();
            ed.WriteMessage($"\n[RC] Skala opisu ustawiona na 1:{denominator:F0} (factor={newFactor:F3}).");
        }
    }
}
