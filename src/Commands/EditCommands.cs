using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using BricsCadRc.Core;
using Teigha.DatabaseServices;
using Teigha.Runtime;

namespace BricsCadRc.Commands
{
    public class EditCommands
    {
        /// <summary>
        /// RC_EDIT_BAR — uzytkownik klika pret, pojawia sie dialog edycji.
        /// </summary>
        [CommandMethod("RC_EDIT_BAR", CommandFlags.Modal)]
        public void EditBar()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            // Wybierz pret
            var selOpts = new PromptEntityOptions("\nWybierz pret do edycji: ");
            selOpts.SetRejectMessage("\nTo nie jest pret RC SLAB.");
            selOpts.AddAllowedClass(typeof(Line), true);
            var selResult = ed.GetEntity(selOpts);
            if (selResult.Status != PromptStatus.OK) return;

            using var tr = db.TransactionManager.StartTransaction();
            var entity = (Entity)tr.GetObject(selResult.ObjectId, OpenMode.ForRead);

            var bar = XDataHelper.Read(entity);
            if (bar == null)
            {
                ed.WriteMessage("\nWybrana linia nie jest pretem RC SLAB.\n");
                tr.Abort();
                return;
            }

            ed.WriteMessage($"\n[RC SLAB] Pret: {bar.Mark} | Ø{bar.Diameter} | rozstaw: {bar.Spacing} mm | szt: {bar.Count}\n");

            // Na razie prosta edycja przez konsole — pozniej zastapi to dialog WPF
            var diaOpts = new PromptIntegerOptions($"\nNowa srednica [mm] <{bar.Diameter}>: ")
            {
                DefaultValue = bar.Diameter,
                AllowNone = true
            };
            var diaResult = ed.GetInteger(diaOpts);
            if (diaResult.Status == PromptStatus.OK)
                bar.Diameter = diaResult.Value;

            var spacOpts = new PromptDistanceOptions($"\nNowy rozstaw [mm] <{bar.Spacing}>: ")
            {
                DefaultValue = bar.Spacing,
                AllowNone = true
            };
            var spacResult = ed.GetDistance(spacOpts);
            if (spacResult.Status == PromptStatus.OK)
                bar.Spacing = spacResult.Value;

            // Zapisz zaktualizowane dane
            entity.UpgradeOpen();
            XDataHelper.Write(entity, bar);
            tr.Commit();

            ed.WriteMessage($"\n[RC SLAB] Zaktualizowano pret: {bar.Mark}\n");
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
