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
        /// RC_GENERATE — Mesh (cała siatka) / B1 / B2 / T1 / T2 / UB1 / UB2 / UBNib (UB 03 w nibie).
        /// Ostatni wybór jest domyślny (Enter).
        /// </summary>
        [CommandMethod("RC_GENERATE", CommandFlags.Modal)]
        public void Generate()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var opts = new PromptKeywordOptions($"\nCo wygenerować [Mesh/B1/B2/T1/T2/UB1/UB2/UBNib] <{_lastGenerate}>: ")
            {
                AllowNone = true
            };
            foreach (var k in new[] { "Mesh", "B1", "B2", "T1", "T2", "UB1", "UB2", "UBNib" })
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
