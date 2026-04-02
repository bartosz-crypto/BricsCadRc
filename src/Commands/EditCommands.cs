using System;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
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

            // Lokalna metoda pomocnicza — przebudowuje blok + Mark + annotację
            void ApplyPreview(int count, double spacing, double cover)
            {
                BarBlockEngine.RebuildWithNewLayout(db, blockRefId, count, spacing, cover);

                using var trP = db.TransactionManager.StartTransaction();
                var brP = trP.GetObject(blockRefId, OpenMode.ForWrite) as BlockReference;
                if (brP != null)
                {
                    var bP = BarBlockEngine.ReadXData(brP);
                    if (bP != null)
                    {
                        var mp = bar.Mark.Split(' ');
                        var cp = mp[0].Split('-');
                        string sfx = mp.Length > 1 ? " " + string.Join(" ", mp, 1, mp.Length - 1) : "";
                        bP.Mark    = cp.Length >= 2 ? $"{cp[0]}-{cp[1]}-{(int)spacing}{sfx}" : bar.Mark;
                        bP.Count   = count;
                        bP.Spacing = spacing;
                        BarBlockEngine.WriteXData(brP, bP);
                    }
                }
                trP.Commit();

                using var trS = db.TransactionManager.StartTransaction();
                var brS = trS.GetObject(blockRefId, OpenMode.ForRead) as BlockReference;
                var bS  = brS != null ? BarBlockEngine.ReadXData(brS) : null;
                trS.Commit();
                if (bS != null) AnnotationEngine.SyncAnnotation(db, bS);

                try { doc.SendStringToExecute("REGEN\n", false, false, false); } catch { }
            }

            // Zachowaj oryginalne wartości do ewentualnego przywrócenia
            int    origCount   = bar.Count;
            double origSpacing = bar.Spacing;
            double origCover   = bar.Cover;

            // Krok 2 — dialog z live preview
            var dlg = new EditDistributionDialog(bar.Mark, bar.Count, bar.Spacing, bar.Cover);
            dlg.OnPreview = (c, sp, cv) => ApplyPreview(c, sp, cv);

            bool confirmed = Application.ShowModalWindow(dlg) == true;

            if (!confirmed)
            {
                if (dlg.PreviewApplied)
                {
                    ApplyPreview(origCount, origSpacing, origCover);
                }
                return;
            }

            // Krok 3 — przebuduj blok z wartościami z dialogu
            BarBlockEngine.RebuildWithNewLayout(db, blockRefId,
                dlg.ResultCount, dlg.ResultSpacing, dlg.ResultCover);

            // Krok 3b — zaktualizuj Mark jeśli zmienił się rozstaw (format H{dia}-{posNr}-{spacing})
            if (Math.Abs(dlg.ResultSpacing - bar.Spacing) > 0.1 || dlg.ResultCount != bar.Count)
            {
                using var trMark = db.TransactionManager.StartTransaction();
                var brMark = trMark.GetObject(blockRefId, OpenMode.ForWrite) as BlockReference;
                if (brMark != null)
                {
                    var barMark = BarBlockEngine.ReadXData(brMark);
                    if (barMark != null)
                    {
                        // Wyodrębnij H{dia}-{posNr} z aktualnego Mark (przed spacing)
                        // Format: "H12-01-200" lub "H12-01-200 B1"
                        var markParts  = bar.Mark.Split(' ');
                        var coreParts  = markParts[0].Split('-');  // ["H12","01","200"]
                        string suffix  = markParts.Length > 1
                            ? " " + string.Join(" ", markParts, 1, markParts.Length - 1)
                            : "";
                        string baseNew = coreParts.Length >= 2
                            ? $"{coreParts[0]}-{coreParts[1]}-{(int)dlg.ResultSpacing}"
                            : bar.Mark;
                        barMark.Mark    = baseNew + suffix;
                        barMark.Count   = dlg.ResultCount;
                        barMark.Spacing = dlg.ResultSpacing;
                        BarBlockEngine.WriteXData(brMark, barMark);
                    }
                }
                trMark.Commit();
            }

            // Krok 3c — zaktualizuj etykietę pręta źródłowego
            using (var trSrc = db.TransactionManager.StartTransaction())
            {
                var brSrc = trSrc.GetObject(blockRefId, OpenMode.ForRead) as BlockReference;
                if (brSrc != null)
                {
                    var barSrc = BarBlockEngine.ReadXData(brSrc);
                    if (barSrc != null && !string.IsNullOrEmpty(barSrc.SourceBarHandle))
                    {
                        if (long.TryParse(barSrc.SourceBarHandle,
                                System.Globalization.NumberStyles.HexNumber,
                                null, out long srcHVal))
                        {
                            var srcHandle = new Handle(srcHVal);
                            if (db.TryGetObjectId(srcHandle, out ObjectId srcId) && !srcId.IsErased)
                            {
                                var srcPline = trSrc.GetObject(srcId, OpenMode.ForRead) as Polyline;
                                var srcBar   = srcPline != null ? SingleBarEngine.ReadBarXData(srcPline) : null;
                                if (srcBar != null && !string.IsNullOrEmpty(srcBar.LabelHandle))
                                {
                                    if (long.TryParse(srcBar.LabelHandle,
                                            System.Globalization.NumberStyles.HexNumber,
                                            null, out long lblHVal))
                                    {
                                        var lblHandle = new Handle(lblHVal);
                                        if (db.TryGetObjectId(lblHandle, out ObjectId lblId) && !lblId.IsErased)
                                        {
                                            var ml = trSrc.GetObject(lblId, OpenMode.ForWrite) as MLeader;
                                            if (ml?.ContentType == ContentType.MTextContent)
                                            {
                                                var mt = ml.MText?.Clone() as MText;
                                                if (mt != null)
                                                {
                                                    var updMark = BarBlockEngine.ReadXData(
                                                        trSrc.GetObject(blockRefId, OpenMode.ForRead) as BlockReference);
                                                    mt.Contents = $"{dlg.ResultCount} {updMark?.Mark ?? srcBar.Mark}";
                                                    ml.MText    = mt;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                trSrc.Commit();
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
                $"\nRozkład {bar.Mark} zaktualizowany: {dlg.ResultCount} szt. co {dlg.ResultSpacing:F0} mm.\n");
            try { doc.SendStringToExecute("REGEN\n", false, false, false); } catch { }
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

            // Krok 3 — otwórz dialog
            var dlg = new EditLabelDialog(bar.Count, bar.Mark, bar.Diameter, bar.Spacing);
            if (Application.ShowModalWindow(dlg) != true) return;

            // Krok 4 — zaktualizuj XData i DBText w BTR
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = tr.GetObject(selRes.ObjectId, OpenMode.ForWrite) as BlockReference;
                if (br == null) { tr.Abort(); return; }

                bar.Count   = dlg.ResultCount;
                bar.Mark    = dlg.ResultMark;
                bar.Spacing = dlg.ResultSpacing;
                // bar.Diameter — nie zmieniamy, pochodzi z pręta

                AnnotationEngine.WriteAnnotXData(br, bar);

                // Zaktualizuj TextString w DBText wewnątrz BTR
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                foreach (ObjectId oid in btr)
                {
                    if (oid.IsErased) continue;
                    var obj = tr.GetObject(oid, OpenMode.ForRead);
                    if (obj is DBText txt)
                    {
                        txt.UpgradeOpen();
                        txt.TextString = $"{bar.Count} {bar.Mark}";
                        break;  // tylko pierwszy DBText
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage($"\nEtykieta zaktualizowana: {dlg.ResultCount} {dlg.ResultMark}\n");

            // DEBUG — odczyt po tr.Commit() żeby sprawdzić czy TextString faktycznie się zapisał
            using (var trDbg = db.TransactionManager.StartTransaction())
            {
                var brDbg = trDbg.GetObject(selRes.ObjectId, OpenMode.ForRead) as BlockReference;
                if (brDbg != null)
                {
                    var btrDbg = (BlockTableRecord)trDbg.GetObject(brDbg.BlockTableRecord, OpenMode.ForRead);
                    string foundText = null;
                    int    txtCount  = 0;
                    foreach (ObjectId oid in btrDbg)
                    {
                        if (oid.IsErased) continue;
                        var obj = trDbg.GetObject(oid, OpenMode.ForRead);
                        if (obj is DBText txt)
                        {
                            txtCount++;
                            if (foundText == null) foundText = txt.TextString;
                        }
                    }
                    ed.WriteMessage($"\n[DEBUG RC_EDIT_LABEL] DBText w BTR: {txtCount}  " +
                                    $"pierwszy TextString=\"{foundText ?? "(null)"}\"");
                }
                trDbg.Commit();
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
                            double newArmTotalLen = AnnotationEngine.ArmLength + newTextLen;

                            // Zaktualizuj TextLen w XData — UpdateArmInBlock czyta go do pozycjonowania tekstu
                            barArm.TextLen = newTextLen;
                            using (var trTextLen = db.TransactionManager.StartTransaction())
                            {
                                var brTextLen = trTextLen.GetObject(selRes.ObjectId, OpenMode.ForWrite) as BlockReference;
                                if (brTextLen != null)
                                    AnnotationEngine.WriteAnnotXData(brTextLen, barArm);
                                trTextLen.Commit();
                            }

                            trArm.Commit();
                            AnnotationEngine.UpdateArmInBlock(brArm, newArmTotalLen);
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

        /// <summary>
        /// RC_SHOW_ALL_BARS — przywraca widocznosc wszystkich pretow RC SLAB.
        /// </summary>
        [CommandMethod("RC_SHOW_ALL_BARS", CommandFlags.Modal)]
        public void ShowAllBars()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            using var tr = db.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            int count = 0;

            foreach (ObjectId id in space)
            {
                var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (entity == null) continue;

                var bar = XDataHelper.Read(entity);
                if (bar == null) continue;

                if (!entity.Visible)
                {
                    entity.UpgradeOpen();
                    entity.Visible = true;
                    bar.RepresentativeFlag = 0;
                    XDataHelper.Write(entity, bar);
                    count++;
                }
            }

            tr.Commit();
            ed.WriteMessage($"\n[RC SLAB] Przywrocono widocznosc {count} pretow.\n");
        }
    }
}
