using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bricscad.ApplicationServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Bezpiecznik przekrojów przed wydaniem dokumentów (etap 1: RC_BBS). Sprawdza przekroje rysunku (SectionStore.CheckAll,
    /// bez starych tytułów; rysunek bez RegApp przekrojów — szybkie wyjście), synchronizuje chmurki „NIEAKTUALNY” i — gdy są
    /// przekroje nieaktualne / do sprawdzenia — pyta Tak / Nie (okno w KOMENDZIE, nie w handlerze zdarzenia). Tryb bez okien
    /// (ScriptMode): tylko komunikat w linii poleceń, dalej bez pytania. Wydruk / PDF / PUBLISH — etap 2.
    /// </summary>
    public static class SectionGuard
    {
        public const string Prefix = "[RC SECTION]";

        /// <summary>Komunikaty ostatniego wywołania (testy BRX).</summary>
        public static List<string> LastMessages { get; } = new List<string>();

        /// <summary>
        /// true = kontynuować (brak nieaktualnych przekrojów, użytkownik wybrał „Tak” albo tryb bez okien). what — co wydajemy
        /// (np. „BBS”). Wołać z komendy (zablokowany dokument — synchronizacja chmurek zapisuje).
        /// </summary>
        public static bool ConfirmBeforeOutput(Document doc, string what)
        {
            LastMessages.Clear();
            if (doc == null) return true;
            var db = doc.Database;
            try
            {
                var sw = Stopwatch.StartNew();
                if (!SectionStore.HasRegApps(db)) return true;
                var infos = SectionStore.CheckAll(db, includeLegacy: false);
                SectionStore.SyncClouds(db, infos);
                sw.Stop();
                Log.Info($"SectionGuard.ConfirmBeforeOutput({what}): przekrojów {infos.Count}, {sw.ElapsedMilliseconds} ms.");

                var bad = infos.Where(s => s.Status == SectionStatus.Nieaktualny || s.Status == SectionStatus.Osierocony
                                           || s.Status == SectionStatus.Uszkodzony || s.Status == SectionStatus.Kopia).ToList();
                if (bad.Count == 0) return true;

                var lines = bad.Select(s => $"{s.Label}: {SectionStore.StatusText(s.Status)}" +
                                            (s.Reasons.Count > 0 ? " — " + string.Join("; ", s.Reasons.Take(3)) + (s.Reasons.Count > 3 ? "; …" : "") : ""))
                               .ToList();
                string head = $"Przekroje nieaktualne / do sprawdzenia ({bad.Count}) — sprawdź RC_SECTION_CHECK:";
                LastMessages.Add(head);
                LastMessages.AddRange(lines);

                if (ScriptMode.IsNonInteractive())
                {
                    doc.Editor.WriteMessage($"\n{Prefix} UWAGA: {head}");
                    foreach (var l in lines) doc.Editor.WriteMessage($"\n{Prefix}   {l}");
                    doc.Editor.WriteMessage($"\n{Prefix} {what}: tryb bez okien — dalej bez pytania.\n");
                    LastMessages.Add(what + ": tryb bez okien — dalej bez pytania.");
                    return true;
                }
                // okno modalne z właścicielem = główne okno BricsCAD (ShowModalWindow), jak inne potwierdzenia wtyczki
                var dlg = new BricsCadRc.Dialogs.TextConfirmDialog("Przekroje nieaktualne",
                    head + "\n\n" + string.Join("\n", lines) + $"\n\nKontynuować ({what})?", "Tak, kontynuuj", "Nie") { Width = 620, Height = 360 };
                bool go = Application.ShowModalWindow(dlg) == true;
                if (!go) doc.Editor.WriteMessage($"\n{Prefix} {what}: przerwano — przekroje nieaktualne.\n");
                return go;
            }
            catch (System.Exception ex)
            {
                // bezpiecznik nie może zablokować wydania dokumentów — błąd do logu, dalej
                Log.Error("SectionGuard.ConfirmBeforeOutput", ex);
                return true;
            }
        }
    }
}
