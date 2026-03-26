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
        /// Otwiera dialog "Reinforcement detailing" z aktualnymi wartościami,
        /// pozwala zmienić Viewing Direction i przebudowuje blok.
        /// </summary>
        [CommandMethod("RC_EDIT_DISTRIBUTION", CommandFlags.Modal)]
        public void EditDistribution()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            // Krok 1: wybierz rozkład — dowolny entity, potem ResolveBarBlock przez XData/OwnerId
            // Nie używamy AddAllowedClass (powoduje "Use SetRejectMessage first!" w BRX gdy typ nie pasuje)
            ObjectId blockRefId = ObjectId.Null;
            BarData  bar        = null;

            while (true)
            {
                var selOpts = new PromptEntityOptions("\nSelect distribution to edit: ");
                var selResult = ed.GetEntity(selOpts);

                if (selResult.Status != PromptStatus.OK) return;

                using var tr = db.TransactionManager.StartTransaction();
                var ent = tr.GetObject(selResult.ObjectId, OpenMode.ForRead) as Entity;
                if (ent != null)
                    blockRefId = ResolveBarBlock(ent, tr);
                tr.Commit();

                if (!blockRefId.IsNull)
                {
                    using var tr2 = db.TransactionManager.StartTransaction();
                    var br2 = tr2.GetObject(blockRefId, OpenMode.ForRead) as BlockReference;
                    bar = BarBlockEngine.ReadXData(br2);
                    tr2.Commit();
                }

                if (bar != null) break;

                ed.WriteMessage("\nTo nie jest rozkład RC — kliknij na linię rozkładu prętów.\n");
                blockRefId = ObjectId.Null;
            }

            // Krok 2: dialog z aktualnymi wartościami
            var dlg = new ReinfDetailingDialog(bar.ViewingDirection ?? "Auto");
            if (Application.ShowModalWindow(dlg) != true) return;

            // Krok 3: oblicz nową długość widoku
            double newBarLength;
            string newViewDir;
            int    newSegIdx;

            if (dlg.IsManualViewingDirection)
            {
                // Manual: użytkownik wskazuje pręt źródłowy i klika segment
                var barSelOpts = new PromptEntityOptions(
                    "\nSelect source bar polyline for manual segment [Esc=Auto]: ")
                    { AllowNone = true };
                var barSel = ed.GetEntity(barSelOpts);

                if (barSel.Status == PromptStatus.OK)
                {
                    newBarLength = BarCommands.GetViewLengthManual(ed, db, barSel.ObjectId, bar);
                    if (newBarLength <= 0)
                    {
                        // Escape podczas kliknięcia segmentu → fallback Auto
                        newBarLength = BarCommands.GetViewLength(bar);
                        newViewDir   = "Auto";
                        newSegIdx    = -1;
                    }
                    else
                    {
                        newViewDir = "Manual";
                        newSegIdx  = bar.ViewSegmentIndex;
                    }
                }
                else
                {
                    // Escape przy wyborze pręta → fallback Auto
                    newBarLength = BarCommands.GetViewLength(bar);
                    newViewDir   = "Auto";
                    newSegIdx    = -1;
                }
            }
            else
            {
                newBarLength = BarCommands.GetViewLength(bar);
                newViewDir   = "Auto";
                newSegIdx    = -1;
            }

            // Krok 4: przebuduj blok
            BarBlockEngine.RebuildWithNewViewLength(db, blockRefId, newBarLength, newViewDir, newSegIdx);

            ed.WriteMessage(
                $"\n[RC SLAB] Distribution updated: {bar.Count} bars {bar.Mark}" +
                $"  viewLength={newBarLength:F0}mm  viewingDir={newViewDir}\n");
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
