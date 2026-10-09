using System;
using System.IO;

// Kompilowany także w tests (net8.0, Nullable enable) — kontekst nullable jak we wtyczce (wyłączony).
#nullable disable

namespace BricsCadRc.Core
{
    /// <summary>
    /// Prosty log do pliku: %LOCALAPPDATA%\NaSmartRc\log.txt
    /// Zamiast pustych catch {} — błąd nie przerywa pracy, ale zostaje ślad do diagnozy.
    /// Plik jest rotowany (log.old.txt) po przekroczeniu ~1 MB.
    /// </summary>
    public static class Log
    {
        private static readonly object _lock = new object();
        private const long MaxBytes = 1_000_000;

        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            App.ProductInfo.FolderName, "log.txt");

        public static void Info(string message) => Write("INFO ", message, null);

        /// <summary>
        /// Błąd z miejscem wystąpienia. Ten sam błąd (miejsce + typ wyjątku) zapisywany najwyżej raz na 60 s —
        /// wyjątki w podglądach / jigach / zdarzeniach nie zapychają pliku.
        /// </summary>
        public static void Error(string where, System.Exception ex)
        {
            string key = where + "|" + ex?.GetType().FullName;
            var now = DateTime.Now;
            lock (_lock)
            {
                if (_lastByKey.TryGetValue(key, out var last) && (now - last).TotalSeconds < 60) return;
                // Słownik nie rośnie bez końca (np. dynamiczne miejsca błędu) — po > 1000 kluczy czyszczony
                if (_lastByKey.Count > 1000) _lastByKey.Clear();
                _lastByKey[key] = now;
            }
            Write("ERROR", where, ex);
        }

        private static readonly System.Collections.Generic.Dictionary<string, DateTime> _lastByKey =
            new System.Collections.Generic.Dictionary<string, DateTime>();

        private static void Write(string level, string message, System.Exception ex)
        {
            try
            {
                lock (_lock)
                {
                    var dir = Path.GetDirectoryName(FilePath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        var old = Path.Combine(dir, "log.old.txt");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }

                    string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}";
                    if (ex != null) line += Environment.NewLine + "    " + ex;
                    File.AppendAllText(FilePath, line + Environment.NewLine);
                }
            }
            catch
            {
                // logowanie nigdy nie może wywalić pluginu
            }
        }
    }
}
