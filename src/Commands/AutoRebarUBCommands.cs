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

            var slabId = SlabPicker.PickOrDraw(doc, SlabLayer, out bool isDrawn);
            if (slabId.IsNull) return;

            try
            {
                AutoRebarEngine.GenerateUBLayer(doc, slabId,
                    sourceLayer: "rebar_bottom",
                    layerCode: "B1",
                    slabThickness: thickness,
                    filterDirection: "X");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[AutoRebar UB] Blad: {ex.Message}\n");
            }
            finally
            {
                if (isDrawn) SlabPicker.Cleanup(db, slabId);
            }
        }

        [CommandMethod("RC_GENERUJ_UB_B2", CommandFlags.Modal)]
        public void GenerateUBB2()
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

            var slabId = SlabPicker.PickOrDraw(doc, SlabLayer, out bool isDrawn);
            if (slabId.IsNull) return;

            try
            {
                AutoRebarEngine.GenerateUBLayer(doc, slabId,
                    sourceLayer: "rebar_bottom",
                    layerCode: "B2",
                    slabThickness: thickness,
                    filterDirection: "Y");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[AutoRebar UB] Blad: {ex.Message}\n");
            }
            finally
            {
                if (isDrawn) SlabPicker.Cleanup(db, slabId);
            }
        }
    }
}
