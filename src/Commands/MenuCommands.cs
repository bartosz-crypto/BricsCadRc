using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.Runtime;

namespace BricsCadRc.Commands
{
    /// <summary>
    /// Angielskie komendy wstążki. RC_GENERATE zastępuje osobne przyciski generowania: wybór warstwy w linii poleceń.
    /// Stare nazwy (RC_GENERUJ_*, RC_PRZYGOTUJ_GA, RC_GA_TEKSTY, RC_DETAL_OTWORU, RC_SIATKA_3D) działają dalej.
    /// </summary>
    public class MenuCommands
    {
        private static string _lastGenerate = "Mesh";

        /// <summary>
        /// RC_GENERATE — Mesh (cała siatka) / B1 / B2 / T1 / T2 / UB1 / UB2 / UBNib (UB 03 w nibie) / B3 (B3 ADD w narożnikach wklęsłych) /
        /// Add (dozbrojenie dołem B1 / B2 ADD z zaimportowanych map) /
        /// Beam (pręty belek / pogrubień — tylko na żądanie, Mesh ich nie tworzy; RC_GENERATE_BEAM) /
        /// Cantilever (pręty wsporników w detalu 1:25 — RC_GENERATE_CANTILEVER, 2026-10-21).
        /// Ostatni wybór jest domyślny (Enter).
        /// </summary>
        [CommandMethod("RC_GENERATE", CommandFlags.Modal)]
        public void Generate()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var opts = new PromptKeywordOptions($"\nCo wygenerować [Mesh/B1/B2/T1/T2/UB1/UB2/UBNib/B3/Add/Beam/Cantilever] <{_lastGenerate}>: ")
            {
                AllowNone = true
            };
            foreach (var k in new[] { "Mesh", "B1", "B2", "T1", "T2", "UB1", "UB2", "UBNib", "B3", "Add", "Beam", "Cantilever" })
                opts.Keywords.Add(k);
            opts.Keywords.Default = _lastGenerate;

            var res = doc.Editor.GetKeywords(opts);
            if (res.Status == PromptStatus.Cancel || res.Status == PromptStatus.Error) return;
            string choice = res.Status == PromptStatus.OK && !string.IsNullOrEmpty(res.StringResult) ? res.StringResult : _lastGenerate;
            _lastGenerate = choice;

            var rebar = new AutoRebarCommands();
            var ub = new AutoRebarUBCommands();
            switch (choice)
            {
                case "Mesh":  rebar.GenerateMesh(); break;
                case "B1":    rebar.GenerateB1(); break;
                case "B2":    rebar.GenerateB2(); break;
                case "T1":    rebar.GenerateT1(); break;
                case "T2":    rebar.GenerateT2(); break;
                case "UB1":   ub.GenerateUBB1(); break;
                case "UB2":   ub.GenerateUBB2(); break;
                case "UBNib": ub.GenerateUBNib(); break;
                case "B3":    rebar.GenerateB3(); break;
                case "Add":   GenerateAdd(); break;
                case "Beam":  new BeamCommands().GenerateBeam(); break;
                case "Cantilever": CantileverCommands.GenerateCantilever(); break;
            }
        }

        /// <summary>
        /// RC_GENERATE_ADD — dozbrojenie dołem z map B1 / B2 (RC_IMPORT_MAP) na wskazanej płycie (rzut dolny):
        /// rozkłady „… B1 ADD” / „… B2 ADD” (cyan), wymiary od pali, H12 / H16 przy dużym As — komunikat.
        /// </summary>
        [CommandMethod("RC_GENERATE_ADD", CommandFlags.Modal)]
        public void GenerateAdd()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            const string T = "[RC ADD]";
            doc.Editor.WriteMessage($"\n{T} Wskaż obrys płyty na rzucie DOLNYM (mapy B1 / B2 → B ADD; T1 / T2 → UB ADD przy krawędzi).");
            var slabId = BricsCadRc.Core.SlabPicker.PickOrDraw(doc, "SD-PILED-RAFT", out bool isDrawn);
            if (slabId.IsNull) return;
            try
            {
                // grupa płyt (uwaga Bartka 2026-10-26, PLOT 357-362): ADD wszystkich płyt grupy jednym poleceniem
                var others = isDrawn ? new System.Collections.Generic.List<(Teigha.DatabaseServices.ObjectId bottom, Teigha.DatabaseServices.ObjectId top, string member)>()
                                     : GroupPick.Others(doc.Editor, doc.Database, slabId, T);
                if (others == null) return;
                var r = BricsCadRc.Core.AutoRebarEngine.GenerateAddFromMaps(doc, slabId);
                foreach (var g in others)
                {
                    doc.Editor.WriteMessage($"\n{T} ── {g.member} ──");
                    var rg = BricsCadRc.Core.AutoRebarEngine.GenerateAddFromMaps(doc, g.bottom);
                    r.Distributions += rg.Distributions; r.UbAdd += rg.UbAdd; r.Zones += rg.Zones;
                    foreach (var m in rg.Messages) r.Messages.Add($"{g.member}: {m}");
                }
                string head = $"Dozbrojenie dołem: {r.Distributions} rozkład(y), UB ADD: {r.UbAdd} — z {r.Zones} stref map.";
                doc.Editor.WriteMessage($"\n{T} {head}\n");
                foreach (var m in r.Messages) doc.Editor.WriteMessage($"\n{T} {m}");
                if (r.Messages.Count > 0)
                    System.Windows.MessageBox.Show(head + "\n\n• " + string.Join("\n• ", r.Messages), "Dozbrojenie (ADD)",
                        System.Windows.MessageBoxButton.OK,
                        r.Messages.Exists(m => m.Contains("→ H")) ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                BricsCadRc.Core.Log.Error("RC_GENERATE_ADD", ex);
                doc.Editor.WriteMessage($"\n{T} Błąd: {ex.Message}\n");
            }
            finally
            {
                if (isDrawn) BricsCadRc.Core.SlabPicker.Cleanup(doc.Database, slabId);
            }
        }

        [CommandMethod("RC_PREPARE_GA")]
        public void PrepareGa() => GaImportCommands.PrepareFromGa();

        [CommandMethod("RC_GA_TEXTS")]
        public void GaTexts() => GaImportCommands.CopyTextsFromGa();

        [CommandMethod("RC_OPENING_DETAIL", CommandFlags.Modal)]
        public void OpeningDetail() => new AutoRebarCommands().HoleDetail();

        [CommandMethod("RC_MODEL_3D", CommandFlags.Modal)]
        public void Model3d() => new Rebar3dCommands().Generate3d();
    }
}
