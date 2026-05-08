using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Runtime;
using BricsCadRc.Core;

namespace BricsCadRc.Commands
{
    /// <summary>
    /// Auto-rebar generation commands — distribute RC_BAR templates onto slab polylines.
    /// Etap 1: RC_GENERUJ_B1 (horizontal bottom layer B1).
    /// Etap 2+: RC_GENERUJ_B2 / RC_GENERUJ_T1 / RC_GENERUJ_T2 — see comments below.
    /// </summary>
    public class AutoRebarCommands
    {
        private const string SlabLayer = "SD-PILED-RAFT";

        [CommandMethod("RC_GENERUJ_B1", CommandFlags.Modal)]
        public void GenerateB1()
        {
            GenerateForLayer(
                sourceLayer:     "rebar_bottom",
                filterDirection: "X",
                layerCode:       "B1");
        }

        // Etap 2+ — uncomment when engine support is confirmed:
        // [CommandMethod("RC_GENERUJ_B2", CommandFlags.Modal)]
        // public void GenerateB2() => GenerateForLayer("rebar_bottom", "Y", "B2");
        //
        // [CommandMethod("RC_GENERUJ_T1", CommandFlags.Modal)]
        // public void GenerateT1() => GenerateForLayer("rebar_top", "X", "T1");
        //
        // [CommandMethod("RC_GENERUJ_T2", CommandFlags.Modal)]
        // public void GenerateT2() => GenerateForLayer("rebar_top", "Y", "T2");

        private void GenerateForLayer(
            string sourceLayer,
            string filterDirection,
            string layerCode)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            while (true)
            {
                var opts = new PromptEntityOptions(
                    $"\nWybierz obrys płyty (LWPOLYLINE na warstwie {SlabLayer}):");
                opts.SetRejectMessage("\nMusi być to LWPOLYLINE.\n");
                opts.AddAllowedClass(typeof(Polyline), true);

                var res = ed.GetEntity(opts);
                if (res.Status == PromptStatus.Cancel) return;
                if (res.Status != PromptStatus.OK) continue;

                bool layerOk = false;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    if (tr.GetObject(res.ObjectId, OpenMode.ForRead) is Polyline pl)
                    {
                        if (pl.Layer == SlabLayer)
                        {
                            layerOk = true;
                        }
                        else
                        {
                            ed.WriteMessage(
                                $"\nPolilinia jest na warstwie '{pl.Layer}', wymagana '{SlabLayer}'. Spróbuj ponownie.\n");
                        }
                    }
                    tr.Commit();
                }
                if (!layerOk) continue;

                try
                {
                    AutoRebarEngine.GenerateLayer(
                        doc, res.ObjectId,
                        sourceLayer:     sourceLayer,
                        filterDirection: filterDirection,
                        layerCode:       layerCode);
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[AutoRebar] Błąd: {ex.Message}\n");
                }
                return;
            }
        }
    }
}
