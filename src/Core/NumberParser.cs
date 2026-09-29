using System.Globalization;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Parsowanie liczb wpisywanych przez użytkownika w dialogach.
    /// Akceptuje zarówno "12.5", jak i "12,5" — niezależnie od ustawień regionalnych Windows.
    /// </summary>
    public static class NumberParser
    {
        public static bool TryParseDouble(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim().Replace(" ", "").Replace(' '.ToString(), "").Replace(',', '.');
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static bool TryParseInt(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
