using System;
using System.IO;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Ustawienia BBS zapamiętywane między sesjami: %APPDATA%\NaSmartRc\bbs.txt
    /// (linie „klucz=wartość”). Pusty szablon = wbudowany default-bbs.xls.
    /// </summary>
    public static class BbsSettings
    {
        private static bool _loaded;
        private static string _templatePath = "";

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), App.ProductInfo.FolderName, "bbs.txt");

        // Ustawienia starej wtyczki BricsCadRc — czytane, gdy NA Smart RC nie ma jeszcze własnych.
        private static string LegacyFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), App.ProductInfo.LegacyFolderName, "bbs.txt");

        public static string TemplatePath
        {
            get { Load(); return _templatePath; }
            set
            {
                Load();
                value = (value ?? "").Trim();
                if (value == _templatePath) return;
                _templatePath = value;
                Save();
            }
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string path = File.Exists(FilePath) ? FilePath
                            : File.Exists(LegacyFilePath) ? LegacyFilePath : null;
                if (path == null) return;
                foreach (var line in File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    if (line.Substring(0, eq).Trim() == "TemplatePath") _templatePath = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception ex) { Log.Error("BbsSettings.Load", ex); }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, "TemplatePath=" + _templatePath + Environment.NewLine);
            }
            catch (Exception ex) { Log.Error("BbsSettings.Save", ex); }
        }
    }
}
