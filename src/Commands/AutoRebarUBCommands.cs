using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Bricscad.EditorInput;
using Teigha.Runtime;
using BricsCadRc.Core;

namespace BricsCadRc.Commands
{
    /// <summary>
    /// Auto-rebar UB (U-bar shape 21) commands.
    /// Etap 1: RC_GENERUJ_UB_B1.
    /// </summary>
    public class AutoRebarUBCommands
    {
        private const string SlabLayer = "SD-PILED-RAFT";

        [CommandMethod("RC_GENERUJ_UB_B1", CommandFlags.Modal)]
        public void GenerateUBB1()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            // Prompt: slab thickness
            var thickOpts = new PromptKeywordOptions("\nGrubość płyty [225/300] <300>: ")
                { AllowNone = true };
            thickOpts.Keywords.Add("225");
            thickOpts.Keywords.Add("300");
            thickOpts.Keywords.Default = "300";
            var thickRes = ed.GetKeywords(thickOpts);
            if (thickRes.Status == PromptStatus.Cancel) return;

            int thickness = 300;
            if (thickRes.Status == PromptStatus.OK)
            {
                if (!int.TryParse(thickRes.StringResult, out thickness)) thickness = 300;
            }

            // Prompt: slab polyline
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
                        if (pl.Layer == SlabLayer) layerOk = true;
                        else ed.WriteMessage(
                            $"\nPolilinia na warstwie '{pl.Layer}', wymagana '{SlabLayer}'.\n");
                    }
                    tr.Commit();
                }
                if (!layerOk) continue;

                try
                {
                    AutoRebarEngine.GenerateUBLayer(doc, res.ObjectId,
                        sourceLayer:    "rebar_bottom",
                        layerCode:      "B1",
                        slabThickness:  thickness);
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[AutoRebar UB] Błąd: {ex.Message}\n");
                }
                return;
            }
        }
    }
}
