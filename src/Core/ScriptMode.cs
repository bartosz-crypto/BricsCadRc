using System;
using System.Globalization;
using System.Linq;
using Bricscad.ApplicationServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Tryb bez okien (decyzja D5): okna pytające wtyczki (np. „identyczny pręt” w RC_DISTRIBUTION) są pomijane,
    /// gdy BricsCAD działa jako automat albo komenda idzie ze skryptu — zamiast okna komunikat w linii poleceń.
    /// Warunki (dowolny): argument procesu <c>/automation</c>, zmienna środowiskowa <c>RC_NO_DIALOGS=1</c>,
    /// bit 4 zmiennej <c>CMDACTIVE</c> (skrypt .scr; LISP / makra NIE są wykrywane — pokażą okno).
    /// </summary>
    internal static class ScriptMode
    {
        public static bool IsNonInteractive()
        {
            try
            {
                if (Environment.GetCommandLineArgs().Any(a =>
                        string.Equals((a ?? "").Trim(), "/automation", StringComparison.OrdinalIgnoreCase)))
                    return true;
                if (Environment.GetEnvironmentVariable("RC_NO_DIALOGS") == "1") return true;
                int cmdActive = Convert.ToInt32(Application.GetSystemVariable("CMDACTIVE"), CultureInfo.InvariantCulture);
                return (cmdActive & 4) != 0;
            }
            catch (System.Exception ex)
            {
                Log.Error("ScriptMode.IsNonInteractive", ex);
                return false;
            }
        }
    }
}
