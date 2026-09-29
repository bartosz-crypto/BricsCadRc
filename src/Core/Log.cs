using System;
using System.IO;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Prosty log do pliku: %LOCALAPPDATA%\BricsCadRc\log.txt
    /// Zamiast pustych catch {} — błąd nie przerywa pracy, ale zostaje ślad do diagnozy.
    /// Plik jest rotowany (log.old.txt) po przekroczeniu ~1 MB.
    /// </summary>
    public static class Log
    {
        private static readonly object _lock = new object();
        private const long MaxBytes = 1_000_000;

        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BricsCadRc", "log.txt");

        public static void Info(string message) => Write("INFO ", message, null);

        public static void Error(string where, System.Exception ex) => Write("ERROR", where, ex);

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
