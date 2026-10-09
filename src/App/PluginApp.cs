using Bricscad.ApplicationServices;
using BricsCadRc.Core;
using Teigha.DatabaseServices;
using Teigha.Runtime;

[assembly: ExtensionApplication(typeof(BricsCadRc.App.PluginApp))]

namespace BricsCadRc.App
{
    public class PluginApp : IExtensionApplication
    {
        public void Initialize()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            string built = "?";
            try { built = System.IO.File.GetLastWriteTime(typeof(PluginApp).Assembly.Location).ToString("yyyy-MM-dd HH:mm"); } catch (System.Exception logEx) { Log.Error("PluginApp.Initialize", logEx); }
            doc?.Editor.WriteMessage($"\n[RC SLAB] Plugin zaladowany. Build: {built}\n");

            // PICKSTYLE=1 — zaznaczanie calych grup przy kliknieciu na czlon grupy.
            // Bez tego klik na linie/kropke zaznacza tylko ten element, nie cala grupe ANNOT.
            Application.SetSystemVariable("PICKSTYLE", 1);

            // Ogranicz ruch blokow RC_ANNOT_nnn do osi kierunku zbrojenia (X lub Y)
            AnnotMoveOverrule.Register();

            // Remap handle'ów block↔annot po kopiowaniu (COPY/MIRROR/ARRAY)
            BarCopyWatcher.Register();

            // Auto-aktualizacja rozkładów po rozciągnięciu polilinii pręta (FEATURE E)
            BarGeometryWatcher.Register();

            // TODO Plan C: reaktywacja po refactorze SingleBar na BlockReference
            // SingleBarGripOverrule.Register();

            // Snap grotu MLeadera (etykieta RC_BAR) z powrotem na pręt po edycji
            RcMLeaderOverrule.Register();

            BarBlockHighlightManager.Register();

            // Opóźniona aktualizacja etykiet prętów po ERASE — w każdym otwartym rysunku
            DocumentWatch.Subscribe("PluginApp",
                d => { d.CommandEnded += OnCommandEnded;  d.CommandCancelled += OnCommandCancelled; },
                d => { d.CommandEnded -= OnCommandEnded;  d.CommandCancelled -= OnCommandCancelled; });

            RibbonBuilder.Build();
        }

        public void Terminate()
        {
            BarBlockHighlightManager.Unregister();
            DocumentWatch.Unsubscribe("PluginApp");

            // SingleBarGripOverrule.Unregister();
            RcMLeaderOverrule.Unregister();
            BarGeometryWatcher.Unregister();
            BarCopyWatcher.Unregister();
            AnnotMoveOverrule.Unregister();
        }

        static void OnCommandEnded(object sender, CommandEventArgs e)
        {
            var doc = sender as Document ?? Application.DocumentManager.MdiActiveDocument;
            if (doc?.Database == null) { AnnotGripOverrule.ResetDragState(); return; }

            // Zakończony grip-drag: zapisz zmiany rozkładu raz, po puszczeniu gripa.
            try
            {
                using (doc.LockDocument())
                    AnnotGripOverrule.ApplyPendingGripEdits(doc.Database);
            }
            catch (System.Exception ex) { Log.Error("PluginApp.ApplyPendingGripEdits", ex); }
            finally { AnnotGripOverrule.ResetDragState(); }

            // Po U/UNDO/REDO niczego nie poprawiamy — każda modyfikacja kasuje stos REDO.
            if (DocumentWatch.IsUndoCommand(e.GlobalCommandName))
            {
                BarBlockTransformOverrule.DiscardPendingRotations();
                PendingLabelUpdates.Discard(doc.Database);
                return;
            }

            // ROTATE prętów: obróć i przebuduj powiązane opisy
            try
            {
                using (doc.LockDocument())
                    BarBlockTransformOverrule.ApplyPendingRotations(doc.Database);
            }
            catch (System.Exception ex) { Log.Error("PluginApp.ApplyPendingRotations", ex); }

            try { PendingLabelUpdates.FlushAll(doc.Database); }
            catch (System.Exception ex) { Log.Error("PluginApp.OnCommandEnded", ex); }
        }

        static void OnCommandCancelled(object sender, CommandEventArgs e)
        {
            // ESC w trakcie grip-dragu: podgląd był tylko transientem, w bazie nic się nie
            // zmieniło — wystarczy sprzątnąć transienty i stan dragu (dawny PendingAnnotRestore
            // nie jest już potrzebny).
            AnnotGripOverrule.ResetDragState();
            BarBlockTransformOverrule.DiscardPendingRotations();

            var doc = sender as Document ?? Application.DocumentManager.MdiActiveDocument;
            if (doc?.Database == null) return;

            // Anulowana komenda też mogła zakolejkować aktualizacje etykiet (np. ERASE w trakcie)
            try { PendingLabelUpdates.FlushAll(doc.Database); }
            catch (System.Exception ex) { Log.Error("PluginApp.OnCommandCancelled", ex); }
        }
    }
}
