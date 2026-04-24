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
            doc?.Editor.WriteMessage("\n[RC SLAB] Plugin zaladowany. Wersja 0.1\n");

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

            // Opóźniona aktualizacja etykiet prętów po ERASE
            if (doc != null)
            {
                doc.CommandEnded      += OnCommandEnded;
                doc.CommandCancelled  += OnCommandCancelled;
            }

            RibbonBuilder.Build();
        }

        public void Terminate()
        {
            BarBlockHighlightManager.Unregister();
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                doc.CommandEnded     -= OnCommandEnded;
                doc.CommandCancelled -= OnCommandCancelled;
            }

            // SingleBarGripOverrule.Unregister();
            RcMLeaderOverrule.Unregister();
            BarGeometryWatcher.Unregister();
            BarCopyWatcher.Unregister();
            AnnotMoveOverrule.Unregister();
        }

        static void OnCommandEnded(object sender, CommandEventArgs e)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc?.Database != null)
                PendingLabelUpdates.FlushAll(doc.Database);
        }

        static void OnCommandCancelled(object sender, CommandEventArgs e)
        {
            if (e.GlobalCommandName != "GRIP_STRETCH") return;
            if (AnnotGripOverrule.PendingAnnotRestore.Count == 0) return;

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc?.Database == null) return;
            var db = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var kvp in AnnotGripOverrule.PendingAnnotRestore)
                {
                    try
                    {
                        var annotBr = tr.GetObject(kvp.Key, OpenMode.ForWrite) as BlockReference;
                        if (annotBr != null)
                            annotBr.Position = kvp.Value;
                    }
                    catch { }
                }
                tr.Commit();
            }

            AnnotGripOverrule.PendingAnnotRestore.Clear();
        }
    }
}
