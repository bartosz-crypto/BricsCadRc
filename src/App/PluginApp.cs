using Bricscad.ApplicationServices;
using BricsCadRc.Core;
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

            // Auto-aktualizacja rozkładów po rozciągnięciu polilinii pręta (FEATURE E)
            BarGeometryWatcher.Register();

            // Snap grotu MLeadera (etykieta RC_BAR) z powrotem na pręt po edycji
            RcMLeaderOverrule.Register();

            // Opóźniona aktualizacja etykiet prętów po ERASE
            if (doc != null)
                doc.CommandEnded += OnCommandEnded;

            RibbonBuilder.Build();
        }

        public void Terminate()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
                doc.CommandEnded -= OnCommandEnded;

            RcMLeaderOverrule.Unregister();
            BarGeometryWatcher.Unregister();
            AnnotMoveOverrule.Unregister();
        }

        static void OnCommandEnded(object sender, CommandEventArgs e)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc?.Database != null)
                PendingLabelUpdates.FlushAll(doc.Database);
        }
    }
}
