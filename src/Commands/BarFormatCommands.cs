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

            // decyzja Bartka 2026-10-20: pręty zawsze jak dawniej (polilinia); przełącznik wyłączony, stary plik ustawienia usuwany
            try
            {
                string path = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                                                     App.ProductInfo.FolderName, BarFormat.FileName);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                BarFormat.Set(BarFormatKind.Polyline, persist: false);
                ed.WriteMessage("\n[RC] Nowe pręty: zawsze polilinia (jak dawniej). Format blokowy wyłączony.\n");
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
