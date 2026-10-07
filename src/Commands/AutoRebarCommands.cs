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

        /// <summary>
        /// RC_GENERUJ_SIATKA — cała siatka płyty jednym poleceniem, na tych samych silnikach
        /// co RC_GENERUJ_B1/B2/T1/T2 i RC_GENERUJ_UB_B1/B2:
        ///   1. grubość płyty (dla U-barów),
        ///   2. obrys rzutu DOLNEGO → B1, B2,
        ///   3. obrys rzutu GÓRNEGO (zawsze osobny rzut) → T1, T2 — zakłady góry liczone względem
        ///      właśnie wygenerowanego dołu (≥ 750 mm w świetle),
        ///   4. UB B1, UB B2 (i UB NIB) na rzucie dolnym.
        /// W rozkładach widoczny jest tylko pręt reprezentatywny (RC_SHOW_ALL_BARS pokazuje wszystkie).
        /// </summary>
        [CommandMethod("RC_GENERUJ_SIATKA", CommandFlags.Modal)]
        public void GenerateMesh()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            // 1. Grubość płyty (U-bary)
            var thickOpts = new PromptKeywordOptions("\nGrubość płyty [225/300] <300>: ") { AllowNone = true };
            thickOpts.Keywords.Add("225");
            thickOpts.Keywords.Add("300");
            thickOpts.Keywords.Default = "300";
            var thickRes = ed.GetKeywords(thickOpts);
            if (thickRes.Status == PromptStatus.Cancel) return;
            int thickness = 300;
            if (thickRes.Status == PromptStatus.OK && !int.TryParse(thickRes.StringResult, out thickness))
                thickness = 300;

            // 2. Obrysy — oba wskazywane NA POCZĄTKU, potem generowanie wszystkich warstw
            ed.WriteMessage("\n[RC SIATKA] Obrys siatki DOLNEJ (B1, B2, U-bary):");
            var bottomId = SlabPicker.PickOrDraw(doc, SlabLayer, out bool bottomDrawn);
            if (bottomId.IsNull) return;

            // Góra i dół zawsze na osobnych rzutach (dwa obrysy tej samej płyty)
            ed.WriteMessage("\n[RC SIATKA] Obrys siatki GÓRNEJ (T1, T2):");
            ObjectId topId = SlabPicker.PickOrDraw(doc, SlabLayer, out bool topDrawn);
            if (topId.IsNull || topId == bottomId)
            {
                if (topDrawn && !topId.IsNull) SlabPicker.Cleanup(db, topId);
                if (bottomDrawn) SlabPicker.Cleanup(db, bottomId);
                ed.WriteMessage(topId == bottomId && !topId.IsNull
                    ? "\n[RC SIATKA] Obrys góry musi być innym obrysem niż dół (osobny rzut) — przerwano.\n"
                    : "\n[RC SIATKA] Anulowano — nic nie wygenerowano.\n");
                return;
            }

            int nB1 = 0, nB2 = 0, nU1 = 0, nU2 = 0, nU3 = 0, nT1 = 0, nT2 = 0;
            bool hasNib = NibDetector.Detect(db, bottomId) != null;
            try
            {
                nB1 = Run(ed, "B1", () => AutoRebarEngine.GenerateLayer(doc, bottomId, "rebar_bottom", "X", "B1",
                                               diameter: 10, representativeOnly: true));
                nB2 = Run(ed, "B2", () => AutoRebarEngine.GenerateLayer(doc, bottomId, "rebar_bottom", "Y", "B2",
                                               diameter: 10, representativeOnly: true));
                // Góra liczy zakłady względem właśnie wygenerowanego dołu (≥ 750 mm)
                nT1 = Run(ed, "T1", () => AutoRebarEngine.GenerateLayer(doc, topId, "rebar_top", "X", "T1",
                                               diameter: 12, representativeOnly: true));
                nT2 = Run(ed, "T2", () => AutoRebarEngine.GenerateLayer(doc, topId, "rebar_top", "Y", "T2",
                                               diameter: 12, representativeOnly: true));
                // UB (dół) — po górze
                nU1 = Run(ed, "UB B1", () => AutoRebarEngine.GenerateUBLayer(doc, bottomId, "rebar_bottom", "B1",
                                               thickness, "X", representativeOnly: true));
                nU2 = Run(ed, "UB B2", () => AutoRebarEngine.GenerateUBLayer(doc, bottomId, "rebar_bottom", "B2",
                                               thickness, "Y", representativeOnly: true));
                // Nib: UB 03 przy krawędzi zewnętrznej (UB 01/02 stoją wtedy na uskoku)
                if (hasNib)
                    nU3 = Run(ed, "UB NIB", () => AutoRebarEngine.GenerateNibUBLayer(doc, bottomId, "rebar_bottom",
                                                   representativeOnly: true));

                // Czytelność: widoczne pręty odsunięte od linii rozkładów i innych widocznych prętów
                Run(ed, "kolizje dół", () => AutoRebarEngine.ResolveRepresentativeCollisions(doc, bottomId));
                Run(ed, "kolizje góra", () => AutoRebarEngine.ResolveRepresentativeCollisions(doc, topId));
            }
            finally
            {
                // Obrysy rysowane usuwamy dopiero na końcu: góra szuka dołu po przystającym obrysie
                if (topDrawn && topId != bottomId) SlabPicker.Cleanup(db, topId);
                if (bottomDrawn) SlabPicker.Cleanup(db, bottomId);
            }

            ed.WriteMessage($"\n[RC SIATKA] Gotowe. Rozkłady: B1={Pos(nB1)}, B2={Pos(nB2)}, " +
                            $"UB B1={Pos(nU1)}, UB B2={Pos(nU2)}" + (hasNib ? $", UB NIB={Pos(nU3)}" : "") +
                            $", T1={Pos(nT1)}, T2={Pos(nT2)}" + (hasNib ? " (z T IN NIB)" : "") + ". " +
                            "Widoczny pręt reprezentatywny — wszystkie: RC_SHOW_ALL_BARS.\n");
        }

        /// <summary>
        /// RC_DETAL_OTWORU — detal otworu: obramówka na planie (kolor 10, DASHED, skala 25) z opisem
        /// DETAIL 'n' oraz w wskazanym miejscu rysunek detalu: pręty dodatkowe H16 (2 dołem + 2 górą
        /// przy każdej krawędzi, ≥ 650 mm poza otwór, długość co 250) i U-bary przy krawędziach
        /// otworu (rozmiar wg grubości płyty). Pręty liczą się w BBS.
        /// </summary>
        [CommandMethod("RC_DETAL_OTWORU", CommandFlags.Modal)]
        public void HoleDetail()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            var thickOpts = new PromptKeywordOptions("\nGrubość płyty [225/300] <300>: ") { AllowNone = true };
            thickOpts.Keywords.Add("225");
            thickOpts.Keywords.Add("300");
            thickOpts.Keywords.Default = "300";
            var thickRes = ed.GetKeywords(thickOpts);
            if (thickRes.Status == PromptStatus.Cancel) return;
            int thickness = 300;
            if (thickRes.Status == PromptStatus.OK && !int.TryParse(thickRes.StringResult, out thickness))
                thickness = 300;

            var selOpts = new PromptEntityOptions("\nWskaż otwór (prostokąt z krzyżykiem): ");
            selOpts.SetRejectMessage("\nTo nie jest polilinia.");
            selOpts.AddAllowedClass(typeof(Polyline), true);
            var selRes = ed.GetEntity(selOpts);
            if (selRes.Status != PromptStatus.OK) return;
            if (!AutoRebarEngine.TryGetHole(doc.Database, selRes.ObjectId, out _, out _, out _, out _, out _))
            {
                ed.WriteMessage("\n[RC DETAL] Wskazana polilinia nie jest otworem (brak krzyżyka po przekątnych).\n");
                return;
            }

            var ptRes = ed.GetPoint("\nWskaż miejsce detalu (środek) w wolnej części rysunku: ");
            if (ptRes.Status != PromptStatus.OK) return;

            try { AutoRebarEngine.GenerateHoleDetail(doc, selRes.ObjectId, ptRes.Value, thickness); }
            catch (System.Exception ex)
            {
                Log.Error("RC_DETAL_OTWORU", ex);
                ed.WriteMessage($"\n*** ERROR *** [RC DETAL] {ex.Message}\n");
            }
        }

        private static int Pos(int n) => n < 0 ? 0 : n;

        /// <summary>Jedna warstwa siatki — błąd jednej warstwy nie przerywa pozostałych.</summary>
        private static int Run(Editor ed, string name, System.Func<int> generate)
        {
            try { return generate(); }
            catch (System.Exception ex)
            {
                Log.Error($"RC_GENERUJ_SIATKA {name}", ex);
                ed.WriteMessage($"\n*** ERROR *** [RC SIATKA] {name}: {ex.Message}\n");
                return 0;
            }
        }

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
