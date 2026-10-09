using System;
using System.IO;

namespace BricsCadRc.Core
{
    /// <summary>Format nowych prętów (Plan C, etap 3): polilinia (jak dotąd) albo pręt-blok (BlockReference, [14] = 2).</summary>
    internal enum BarFormatKind { Polyline, Block }

    /// <summary>
    /// Przełącznik formatu nowych prętów — jedyny punkt rozgałęzienia w SingleBarEngine.PlaceBar.
    /// Domyślnie Polyline (do akceptacji etapów 3–5). Źródła, w kolejności:
    /// 1) Set(kind, …) w tej sesji (testy: refleksja z RegressionDump / smoke),
    /// 2) zmienna środowiskowa RC_REG_BARFORMAT = Block | Polyline (tylko testy; bez zapisu do pliku),
    /// 3) %APPDATA%\NaSmartRc\barformat.txt (fallback: folder starej wtyczki, jak BbsSettings).
    /// Krok 3f: użytkownik przełącza format komendą RC_BAR_FORMAT [Polilinia/Blok] (zapis do pliku);
    /// komunikat przy starcie wtyczki (PluginApp) pokazuje bieżący format.
    /// </summary>
    internal static class BarFormat
    {
        internal const string EnvVar   = "RC_REG_BARFORMAT";
        internal const string FileName = "barformat.txt";

        // Krok 3f (0-POPRAWKI p.1): ustawienie użytkownika (plik) czytane i zapisywane (RC_BAR_FORMAT).
        // Testy przełączają format bez zapisu (Set(kind, false) / zmienna środowiskowa).
        internal static readonly bool UserSettingEnabled = true;

        private static bool _loaded;
        private static BarFormatKind _current = BarFormatKind.Polyline;

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), App.ProductInfo.FolderName, FileName);

        private static string LegacyFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), App.ProductInfo.LegacyFolderName, FileName);

        /// <summary>Bieżący format nowych prętów (leniwy odczyt przy pierwszym użyciu).</summary>
        public static BarFormatKind Current
        {
            get { Load(); return _current; }
        }

        /// <summary>Ustawia format; persist = zapis do %APPDATA%\NaSmartRc\barformat.txt.</summary>
        /// <returns>false, gdy zapisu nie udało się wykonać (format obowiązuje wtedy do końca sesji).</returns>
        public static bool Set(BarFormatKind kind, bool persist)
        {
            _loaded  = true;
            _current = kind;
            return !(persist && UserSettingEnabled) || Save();
        }

        /// <summary>Wartość zmiennej środowiskowej RC_REG_BARFORMAT (testy) albo null — ma pierwszeństwo przed plikiem.</summary>
        internal static string EnvOverride()
        {
            string v = Environment.GetEnvironmentVariable(EnvVar);
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }

        /// <summary>Nazwa formatu do komunikatów: „polilinia” / „blok”.</summary>
        internal static string DisplayName(BarFormatKind kind)
            => kind == BarFormatKind.Block ? "blok" : "polilinia";

        /// <summary>"Block"/"Blok" → Block, "Polyline"/"Polilinia" → Polyline (bez rozróżniania wielkości liter); inaczej null.</summary>
        internal static BarFormatKind? Parse(string s)
        {
            string v = (s ?? "").Trim();
            if (v.Equals("Block", StringComparison.OrdinalIgnoreCase) || v.Equals("Blok", StringComparison.OrdinalIgnoreCase))
                return BarFormatKind.Block;
            if (v.Equals("Polyline", StringComparison.OrdinalIgnoreCase) || v.Equals("Polilinia", StringComparison.OrdinalIgnoreCase))
                return BarFormatKind.Polyline;
            return null;
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _current = BarFormatKind.Polyline;   // domyślnie, gdy brak zmiennej i pliku
            try
            {
                var env = Parse(Environment.GetEnvironmentVariable(EnvVar));
                if (env.HasValue)
                {
                    _current = env.Value;
                    Log.Info($"[RC] Format nowych prętów z {EnvVar}: {_current}");
                    return;
                }
                if (!UserSettingEnabled) return;

                string path = File.Exists(FilePath) ? FilePath
                            : File.Exists(LegacyFilePath) ? LegacyFilePath : null;
                if (path == null) return;
                foreach (var line in File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    if (line.Substring(0, eq).Trim() != "Format") continue;
                    var k = Parse(line.Substring(eq + 1));
                    if (k.HasValue) _current = k.Value;
                }
                Log.Info($"[RC] Format nowych prętów z {path}: {_current}");
            }
            catch (Exception ex) { Log.Error("BarFormat.Load", ex); }
        }

        private static bool Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, "Format=" + _current + Environment.NewLine);
                return true;
            }
            catch (Exception ex) { Log.Error("BarFormat.Save", ex); return false; }
        }
    }
}
