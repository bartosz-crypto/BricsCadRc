namespace BricsCadRc.App
{
    /// <summary>
    /// Nazwa produktu „NA Smart RC” — odróżnia tę wtyczkę od starej BricsCadRc (wersja kolegi).
    /// Obie mogą być zainstalowane; wczytana jest jedna naraz (autoload). Komendy i metki XData
    /// zostają wspólne, więc rysunki są zgodne w obie strony.
    /// </summary>
    public static class ProductInfo
    {
        public const string Name = "NA Smart RC";

        /// <summary>Folder w %LOCALAPPDATA% / %APPDATA% (log, ustawienia).</summary>
        public const string FolderName = "NaSmartRc";

        /// <summary>Folder starej wtyczki — tylko do jednorazowego przejęcia ustawień.</summary>
        public const string LegacyFolderName = "BricsCadRc";
    }
}
