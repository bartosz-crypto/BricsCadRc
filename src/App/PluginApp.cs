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
            doc?.Editor.WriteMessage($"\n[{ProductInfo.Name}] Plugin zaladowany. Build: {built}\n");
            // Plan C, krok 3f: format nowych prętów (RC_BAR_FORMAT, %APPDATA%\NaSmartRc\barformat.txt)
            try { doc?.Editor.WriteMessage($"[{ProductInfo.Name}] Nowe pręty: {BarFormat.DisplayName(BarFormat.Current)}\n"); }
            catch (System.Exception ex) { Log.Error("PluginApp.BarFormat", ex); }

            // PICKSTYLE=1 — zaznaczanie calych grup przy kliknieciu na czlon grupy.
            // Bez tego klik na linie/kropke zaznacza tylko ten element, nie cala grupe ANNOT.
            Application.SetSystemVariable("PICKSTYLE", 1);

            // Ogranicz ruch blokow RC_ANNOT_nnn do osi kierunku zbrojenia (X lub Y)
            AnnotMoveOverrule.Register();

            // W2: mapa klonowania oryginał → klon (BeginDeepCloneTranslation / InsertMappingAvailable) — PRZED BarCopyWatcher
            CloneMap.Register();

            // Remap handle'ów block↔annot po kopiowaniu (COPY/MIRROR/ARRAY)
            BarCopyWatcher.Register();

            // Auto-aktualizacja rozkładów po rozciągnięciu polilinii pręta (FEATURE E)
            BarGeometryWatcher.Register();

            // Plan C — gripy pręta-bloku (BlockReference, RC_SINGLE_BAR [14]=2); krok 3f: długość 00/99 z propagacją
            SingleBarGripOverrule.Register();

            // PLAN C ETAP 3e2 — SCALE pręta-bloku: skala ±1 i obrys z danych pręta po zakończeniu komendy
            SingleBarBlockScaleWatcher.Register();

            // Snap grotu MLeadera (etykieta RC_BAR) z powrotem na pręt po edycji
            RcMLeaderOverrule.Register();

            BarBlockHighlightManager.Register();

            // Opóźniona aktualizacja etykiet prętów po ERASE — w każdym otwartym rysunku
            DocumentWatch.Subscribe("PluginApp",
                d => { d.CommandEnded += OnCommandEnded;  d.CommandCancelled += OnCommandCancelled; },
                d => { d.CommandEnded -= OnCommandEnded;  d.CommandCancelled -= OnCommandCancelled; });

            // Bezpiecznik przekrojów (etap 2): wydruk / PDF / PUBLISH (DocumentLockModeChanged) i komunikat przy otwarciu rysunku
            SectionGuard.Register();

            RibbonBuilder.Build();
        }

        public void Terminate()
        {
            BarBlockHighlightManager.Unregister();
            DocumentWatch.Unsubscribe("PluginApp");
            SectionGuard.Unregister();

            SingleBarGripOverrule.Unregister();
            SingleBarBlockScaleWatcher.Unregister();
            RcMLeaderOverrule.Unregister();
            BarGeometryWatcher.Unregister();
            BarCopyWatcher.Unregister();
            CloneMap.Unregister();
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

            // MIRROR w miejscu (usunięcie źródła = Tak): rozkład i opis jak kopia po MIRROR (2026-10-22)
            try
            {
                using (doc.LockDocument())
                    BarBlockTransformOverrule.ApplyPendingMirrors(doc.Database);
            }
            catch (System.Exception ex) { Log.Error("PluginApp.ApplyPendingMirrors", ex); }

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
