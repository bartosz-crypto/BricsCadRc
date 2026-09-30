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
        public void GenerateB1() => GenerateForLayer("rebar_bottom", "X", "B1", diameter: 10);

        [CommandMethod("RC_GENERUJ_B2", CommandFlags.Modal)]
        public void GenerateB2() => GenerateForLayer("rebar_bottom", "Y", "B2", diameter: 10);

        // Góra: te same parametry co dół (s=200, c=40), tylko Ø12.
        // Zakłady przesunięte względem B1/B2 tego samego kierunku (≥ 750 mm w świetle).
        [CommandMethod("RC_GENERUJ_T1", CommandFlags.Modal)]
        public void GenerateT1() => GenerateForLayer("rebar_top", "X", "T1", diameter: 12);

        [CommandMethod("RC_GENERUJ_T2", CommandFlags.Modal)]
        public void GenerateT2() => GenerateForLayer("rebar_top", "Y", "T2", diameter: 12);

        private void GenerateForLayer(
            string sourceLayer,
            string filterDirection,
            string layerCode,
            int    diameter)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            var slabId = SlabPicker.PickOrDraw(doc, SlabLayer, out bool isDrawn);
            if (slabId.IsNull) return;

            try
            {
                AutoRebarEngine.GenerateLayer(doc, slabId, sourceLayer, filterDirection, layerCode, diameter);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[AutoRebar] Blad: {ex.Message}\n");
            }
            finally
            {
                if (isDrawn) SlabPicker.Cleanup(db, slabId);
            }
        }
    }
}
