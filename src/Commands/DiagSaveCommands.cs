using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using BricsCadRc.Core;

namespace BricsCadRc.Commands
{
    /// <summary>
    /// RC_DIAG_SAVE (2026-10-20, „Failed to save the file”): szuka obiektów, przez które rysunku nie da się zapisać — bez zgadywania.
    /// Kopie (WBLOCK) do plików tymczasowych w %TEMP%\NaSmartRc_diag: najpierw cały rysunek, potem obiekty modelu i każdego arkusza
    /// osobno, a gdy zapis się nie udaje — połowienie, aż do pojedynczych obiektów. Rysunku NIE zmienia. Wynik: linia poleceń + log.
    /// </summary>
    public static class DiagSaveCommands
    {
        private const string T = "[RC DIAG]";
        private const int MaxBad = 15;

        [CommandMethod("RC_DIAG_SAVE", CommandFlags.Modal)]
        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        public static void DiagSave()
        {
            // osobna metoda: błąd ładowania / JIT (brak metody API w BricsCAD) też trafia do logu i linii poleceń
            try { Run(); }
            catch (System.Exception ex)
            {
                Log.Error("RC_DIAG_SAVE.Outer", ex);
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n{T} Błąd: {ex.GetType().Name}: {ex.Message}\n");
            }
        }

        private static void Run()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;
            string dir = Path.Combine(Path.GetTempPath(), "NaSmartRc_diag");
            try
            {
                Directory.CreateDirectory(dir);
                void Say(string m) { ed.WriteMessage($"\n{T} {m}"); Log.Info($"RC_DIAG_SAVE: {m}"); }

                Say("start — kopie do " + dir);
                // 1. cały rysunek
                using (doc.LockDocument())
                {
                    string whole = Path.Combine(dir, "caly.dwg");
                    string err = SaveWhole(db, whole);
                    Say(err == null ? "kopia całego rysunku zapisuje się (WBLOCK) — problem może być w samym pliku / ustawieniach zapisu"
                                    : $"kopia całego rysunku NIE zapisuje się: {err}");

                    // 2. przestrzenie: model i arkusze
                    var spaces = new List<(string name, List<ObjectId> ids)>();
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        var ld = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                        foreach (DBDictionaryEntry e in ld)
                        {
                            var lay = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
                            var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForRead);
                            var ids = new List<ObjectId>();
                            foreach (ObjectId id in btr)
                                if (!id.IsErased && !(tr.GetObject(id, OpenMode.ForRead) is Viewport)) ids.Add(id);
                            spaces.Add((lay.LayoutName, ids));
                        }
                        tr.Commit();
                    }

                    int file = 0;
                    var bad = new List<ObjectId>();
                    foreach (var (name, ids) in spaces)
                    {
                        if (ids.Count == 0) continue;
                        if (TrySave(db, ids, dir, ref file, out string e1)) { Say($"{name}: {ids.Count} obiektów — zapis OK"); continue; }
                        Say($"{name}: {ids.Count} obiektów — zapis NIE ({e1}); szukam winnych…");
                        Bisect(db, ids, dir, ref file, bad);
                        if (bad.Count >= MaxBad) break;
                    }

                    if (bad.Count == 0) { Say("nie znaleziono pojedynczego winnego obiektu."); }
                    else
                    {
                        Say($"obiekty, przez które zapis się nie udaje ({bad.Count}{(bad.Count >= MaxBad ? "+" : "")}):");
                        using var tr = db.TransactionManager.StartOpenCloseTransaction();
                        foreach (var id in bad)
                            Say("  " + Describe(tr, id));
                        tr.Commit();
                    }
                }
                ed.WriteMessage($"\n{T} Gotowe. Wynik także w logu (%LOCALAPPDATA%\\NaSmartRc\\log.txt). Rysunek bez zmian.\n");
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_DIAG_SAVE", ex);
                ed.WriteMessage($"\n{T} Błąd diagnostyki: {ex.Message}\n");
            }
        }

        /// <summary>Kopia całego rysunku; błąd także natywny (naruszenie pamięci w BricsCAD) — zwracany jako tekst.</summary>
        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        private static string SaveWhole(Database db, string path)
        {
            try { using (var copy = db.Wblock()) copy.SaveAs(path, DwgVersion.Current); return null; }
            catch (System.Exception ex) { Log.Error("RC_DIAG_SAVE.Whole", ex); return ex.GetType().Name + ": " + ex.Message; }
        }

        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        private static bool TrySave(Database db, List<ObjectId> ids, string dir, ref int file, out string error)
        {
            error = null;
            string path = Path.Combine(dir, $"czesc_{file++:D4}.dwg");
            try
            {
                var coll = new ObjectIdCollection(ids.ToArray());
                using (var part = db.Wblock(coll, Point3d.Origin))
                    part.SaveAs(path, DwgVersion.Current);
                try { File.Delete(path); } catch (System.Exception ex) { Log.Error("RC_DIAG_SAVE.Delete", ex); }
                return true;
            }
            catch (System.Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return false; }
        }

        /// <summary>Połowienie listy obiektów, aż do pojedynczych, których kopii nie da się zapisać.</summary>
        private static void Bisect(Database db, List<ObjectId> ids, string dir, ref int file, List<ObjectId> bad)
        {
            if (bad.Count >= MaxBad) return;
            if (ids.Count == 1) { bad.Add(ids[0]); return; }
            var a = ids.Take(ids.Count / 2).ToList();
            var b = ids.Skip(ids.Count / 2).ToList();
            bool okA = TrySave(db, a, dir, ref file, out _);
            bool okB = TrySave(db, b, dir, ref file, out _);
            if (!okA) Bisect(db, a, dir, ref file, bad);
            if (!okB) Bisect(db, b, dir, ref file, bad);
            if (okA && okB) bad.AddRange(new[] { a[0] });   // tylko razem — zgłaszamy pierwszy jako wskazówkę
        }

        private static string Describe(Transaction tr, ObjectId id)
        {
            try
            {
                var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                string apps = "";
                using (var rb = e.XData)
                    if (rb != null)
                        apps = string.Join(",", rb.AsArray().Where(v => v.TypeCode == (int)DxfCode.ExtendedDataRegAppName).Select(v => v.Value.ToString()));
                string extra = e is BlockReference br ? $" blok „{br.Name}”" : e is Hatch h ? $" wzór {h.PatternName}" : "";
                return $"{e.Handle} {e.GetType().Name} warstwa „{e.Layer}”{extra}{(apps.Length > 0 ? " XData: " + apps : "")}";
            }
            catch (System.Exception ex) { return $"{id.Handle} (opis niedostępny: {ex.Message})"; }
        }

        /// <summary>
        /// RC_DIAG_TEMPLATE (2026-10-20): wstawia z szablonu wtyczki TYLKO jedną część tego, co robi Prepare GA — [Arkusz] layout z A1-BL,
        /// [PH] detale PH1–PH9, [Tytuly] tytuły rzutów — żeby po QSAVE sprawdzić, która część psuje zapis. Uruchamiać w pustym,
        /// zapisanym rysunku; każdą część w osobnym pliku.
        /// </summary>
        [CommandMethod("RC_DIAG_TEMPLATE", CommandFlags.Modal)]
        public static void DiagTemplate()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            try
            {
                var ko = new Bricscad.EditorInput.PromptKeywordOptions("\nCo wstawić z szablonu? [Arkusz/PH/Tytuly]: ", "Arkusz PH Tytuly");
                var kr = ed.GetKeywords(ko);
                if (kr.Status != Bricscad.EditorInput.PromptStatus.OK) return;
                var rep = new RcTemplateEngine.Report();
                using (var tpl = RcTemplateEngine.OpenTemplate())
                {
                    if (tpl == null) { ed.WriteMessage($"\n{T} Szablon niedostępny.\n"); return; }
                    using (doc.LockDocument())   // jak Prepare GA
                    {
                        if (kr.StringResult == "Arkusz") RcTemplateEngine.CreateSheet(doc.Database, tpl, "TEST_ARKUSZ", rep);
                        else if (kr.StringResult == "PH") RcTemplateEngine.EnsurePh(doc.Database, tpl, 0, 0, rep);
                        else RcTemplateEngine.EnsureTitles(doc.Database, tpl, rep);
                    }
                }
                foreach (var m in rep.Messages) ed.WriteMessage($"\n{T} {m}.");
                ed.WriteMessage($"\n{T} Wstawiono: {kr.StringResult}. Teraz zapisz (QSAVE) i napisz, czy się zapisało.\n");
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_DIAG_TEMPLATE", ex);
                ed.WriteMessage($"\n{T} Błąd: {ex.Message}\n");
            }
        }
    }
}
