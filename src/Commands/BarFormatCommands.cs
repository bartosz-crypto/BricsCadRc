using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using BricsCadRc.Core;
using Teigha.Runtime;

namespace BricsCadRc.Commands
{
    /// <summary>
    /// Plan C, krok 3f — RC_BAR_FORMAT [Polilinia/Blok]: format NOWYCH prętów (RC_BAR, AutoRebar, przebicie).
    /// Ustawienie użytkownika zapisywane w %APPDATA%\NaSmartRc\barformat.txt (BarFormat). Domyślnie Polilinia.
    /// Istniejące pręty bez zmian — rysunek może mieć pręty obu formatów.
    /// </summary>
    public class BarFormatCommands
    {
        [CommandMethod("RC_BAR_FORMAT", CommandFlags.Modal)]
        public void RC_BAR_FORMAT()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            try
            {
                var cur = BarFormat.Current;
                string def = cur == BarFormatKind.Block ? "Blok" : "Polilinia";
                ed.WriteMessage($"\n[RC] Nowe pręty: {BarFormat.DisplayName(cur)}.");

                var pko = new PromptKeywordOptions($"\n[RC] Format nowych prętów [Polilinia/Blok] <{def}>: ");
                pko.Keywords.Add("Polilinia");
                pko.Keywords.Add("Blok");
                pko.Keywords.Default = def;
                pko.AppendKeywordsToMessage = false;
                pko.AllowNone = true;
                var kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return;

                var kind = BarFormat.Parse(kr.StringResult) ?? cur;
                if (BarFormat.Set(kind, persist: true))
                    ed.WriteMessage($"\n[RC] Nowe pręty: {BarFormat.DisplayName(kind)} (zapisane).\n");
                else
                    ed.WriteMessage($"\n[RC] Nowe pręty: {BarFormat.DisplayName(kind)}. " +
                                    "Nie zapisano ustawienia — obowiązuje do końca sesji.\n");
                string env = BarFormat.EnvOverride();
                if (env != null)
                    ed.WriteMessage($"[RC] Uwaga: ustawiona zmienna środowiskowa {BarFormat.EnvVar}={env} — " +
                                    "po ponownym uruchomieniu ma pierwszeństwo przed tym ustawieniem.\n");
                if (kind == BarFormatKind.Block)
                    ed.WriteMessage("[RC] Pręt-blok: grip końca / STRETCH pręta prostego zmienia długość, numer pozycji, rozkłady " +
                                    "i opis; pręty gięte — tylko przesunięcie (długość przez RC_EDIT_BAR). " +
                                    "Rysunki dla starej wtyczki (BricsCadRc) — format Polilinia.\n");
                Log.Info($"[RC] RC_BAR_FORMAT: {kind}");
            }
            catch (System.Exception ex)
            {
                Log.Error("BarFormatCommands.RC_BAR_FORMAT", ex);
                ed.WriteMessage($"\n[RC] Błąd: {ex.Message}\n");
            }
        }

        /// <summary>
        /// RC_OPISY_CIAGLE (decyzja 2026-10-15): linie rozkładów w istniejących opisach — ciągłe (dawniej _DOT / CENTER,
        /// kreskowane w rzutniach). Nowe opisy od razu mają linię ciągłą.
        /// </summary>
        [CommandMethod("RC_OPISY_CIAGLE", CommandFlags.Modal)]
        public void RC_OPISY_CIAGLE()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            try
            {
                int n;
                using (doc.LockDocument())
                    n = AnnotationEngine.MakeAnnotLinesContinuous(doc.Database);
                ed.Regen();
                ed.WriteMessage($"\n[RC] Linie opisów zmienione na ciągłe: {n}.\n");
            }
            catch (System.Exception ex)
            {
                Log.Error("BarFormatCommands.RC_OPISY_CIAGLE", ex);
                ed.WriteMessage($"\n[RC] Błąd: {ex.Message}\n");
            }
        }
    }
}
