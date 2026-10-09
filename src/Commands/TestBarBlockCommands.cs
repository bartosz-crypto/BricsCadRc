// TEMPORARY — PLAN C ETAP 1
// PLAN C ETAP 1 — TEST, do usunięcia/zastąpienia w etapie 3

using System;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using BricsCadRc.Core;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.Commands
{
    /// <summary>
    /// TEMPORARY — PLAN C ETAP 1. Komenda RC_TEST_BARBLOCK: pręt prosty H12-T jako BlockReference
    /// z własnymi gripami (SingleBarGripOverrule). Pręt testowy — poza numeracją i BBS.
    /// Krok 3f: opcja Info opisuje KAŻDY pręt-blok (także z RC_BAR / AutoRebar w formacie Blok).
    /// </summary>
    public class TestBarBlockCommands
    {
        // Wariant BTR pamiętany w sesji: false = nazwany RC_BAR_TEST_nnn, true = anonimowy *U
        private static bool _anonymous = false;

        [CommandMethod("RC_TEST_BARBLOCK", CommandFlags.Modal)]
        public void RC_TEST_BARBLOCK()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            try
            {
                while (true)
                {
                    ed.WriteMessage($"\n[RC TEST] Typ BTR: {(_anonymous ? "Anonimowy (*U)" : "Nazwany (RC_BAR_TEST_nnn)")}.");
                    var ppo = new PromptPointOptions("\n[RC TEST] Początek pręta lub [Info/Typ]: ");
                    ppo.Keywords.Add("Info");
                    ppo.Keywords.Add("Typ");
                    ppo.AppendKeywordsToMessage = false;
                    var pr = ed.GetPoint(ppo);

                    if (pr.Status == PromptStatus.Keyword)
                    {
                        if (pr.StringResult == "Typ") AskType(ed);
                        else if (pr.StringResult == "Info") ShowInfo(ed, db);
                        continue;
                    }
                    if (pr.Status != PromptStatus.OK) return;

                    var ucs = ed.CurrentUserCoordinateSystem;
                    var p1u = pr.Value;
                    var p2o = new PromptPointOptions("\n[RC TEST] Koniec pręta: ")
                    {
                        UseBasePoint  = true,
                        BasePoint     = p1u,
                        UseDashedLine = true,
                    };
                    var pr2 = ed.GetPoint(p2o);
                    if (pr2.Status != PromptStatus.OK) return;

                    var p1 = p1u.TransformBy(ucs);
                    var p2 = pr2.Value.TransformBy(ucs);
                    var (id, btrName, len) = TestBarBlockEngine.Create(db, p1, p2, _anonymous);

                    string handle = id.Handle.ToString();
                    ed.WriteMessage($"\n[RC TEST] Wstawiono pręt {TestBarBlockEngine.TestMark} L={TestBarBlockEngine.F(len)} " +
                                    $"(BTR {btrName}, uchwyt {handle}). Pręt testowy — poza numeracją i BBS.\n");
                    Log.Info($"[RC TEST] RC_TEST_BARBLOCK: wstawiono {handle}, BTR {btrName}, L={TestBarBlockEngine.F(len)}");
                    return;
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("TestBarBlockCommands.RC_TEST_BARBLOCK", ex);
                ed.WriteMessage($"\n[RC TEST] Błąd: {ex.Message}\n");
            }
        }

        private static void AskType(Editor ed)
        {
            var pko = new PromptKeywordOptions("\n[RC TEST] Typ BTR [Nazwany/Anonimowy]: ");
            pko.Keywords.Add("Nazwany");
            pko.Keywords.Add("Anonimowy");
            pko.Keywords.Default = _anonymous ? "Anonimowy" : "Nazwany";
            pko.AppendKeywordsToMessage = false;
            pko.AllowNone = true;
            var kr = ed.GetKeywords(pko);
            if (kr.Status != PromptStatus.OK || string.IsNullOrEmpty(kr.StringResult)) return;
            _anonymous = kr.StringResult == "Anonimowy";
        }

        private static void ShowInfo(Editor ed, Database db)
        {
            var peo = new PromptEntityOptions("\n[RC TEST] Wskaż pręt-blok: ");
            peo.SetRejectMessage("\n[RC TEST] To nie jest blok.");
            peo.AddAllowedClass(typeof(BlockReference), true);
            var per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            using var tr = db.TransactionManager.StartTransaction();
            var br = tr.GetObject(per.ObjectId, OpenMode.ForRead) as BlockReference;
            if (br == null || !TestBarBlockEngine.IsV2(br))
                ed.WriteMessage("\n[RC TEST] To nie jest pręt-blok (brak RC_SINGLE_BAR [14]=2).");
            else
                ed.WriteMessage(TestBarBlockEngine.Describe(tr, br) + "\n");
            tr.Commit();
        }
    }
}
