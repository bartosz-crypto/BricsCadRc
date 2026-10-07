using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace BricsCadRc.Core
{
    /// <summary>Operacje tekstowe na notatkach MText (GA → RC) — bez API BricsCAD (testowalne).</summary>
    public static class GaNotesText
    {
        private static readonly Regex RxFormat = new Regex(@"\\[ACcFfHhQqTtWwpS][^;\\]*;|\\[LlOoKkPN~]|[{}]");
        public static readonly Regex RxConcreteBlock = new Regex(@"CONCRETE(?:\s|\\[A-Za-z][^;\\]*;)+TO(?:\s|\\[A-Za-z][^;\\]*;)+BE(?:\s|\\[A-Za-z][^;\\]*;)+DESIGN(?:ATED|ED)\b.*?CERTIFICATE\s*\.",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        public static readonly Regex RxHystools = new Regex(@"(HYSTOOLS\s+DK(?:\\[A-Za-z][^;]*;)*)(?:90|165)((?:\\[A-Za-z][^;]*;)*)",
            RegexOptions.IgnoreCase);

        public static string Clean(string contents)
        {
            string s = Regex.Replace(contents ?? "", @"\\S2\^[^;]*;", "²");
            s = Regex.Replace(s, @"\\S3\^[^;]*;", "³");
            return Regex.Replace(RxFormat.Replace(s, " "), @"\s+", " ").Replace(" ²", "²").Replace(" ³", "³").Trim();
        }

        /// <summary>Akapit (między \P) z markerem — wszystko po pierwszym „=”.</summary>
        public static string ParagraphTail(string contents, string marker)
        {
            if (string.IsNullOrEmpty(contents)) return null;
            foreach (var p in contents.Split(new[] { @"\P" }, StringSplitOptions.None))
            {
                int mi = IndexOfIgnoringCodes(p, marker);
                if (mi < 0) continue;
                int eq = p.IndexOf('=', mi);
                if (eq < 0) continue;
                return p.Substring(eq + 1);
            }
            return null;
        }

        /// <summary>Marker w surowym tekście; dopuszcza wielokrotne spacje („SLAB THICKNESS  =”).</summary>
        public static int IndexOfIgnoringCodes(string raw, string marker)
        {
            var rx = new Regex(Regex.Escape(marker).Replace(@"\ ", @"\s+"), RegexOptions.IgnoreCase);
            var m = rx.Match(raw);
            return m.Success ? m.Index : -1;
        }

        /// <summary>„HOUSE. REINFORCEMENT DETAILS” + „PLOT 9-10.” → „PLOT 9-10. REINFORCEMENT DETAILS”.</summary>
        public static string ReplaceTitlePrefix(string rcTitle1, string prefix)
        {
            if (string.IsNullOrWhiteSpace(rcTitle1)) return rcTitle1;
            var m = Regex.Match(rcTitle1, @"REINFORCEMENT\s+DETAILS", RegexOptions.IgnoreCase);
            if (!m.Success) return rcTitle1;
            string rest = rcTitle1.Substring(m.Index);
            return string.IsNullOrEmpty(prefix) ? rest : prefix + " " + rest;
        }

        /// <summary>Kolor w kodach czcionki „\fROMANS|b0|i0|c238|p0;” → c7 (inaczej nadpisuje \C7;).</summary>
        public static string ForceWhiteFont(string s) =>
            string.IsNullOrEmpty(s) ? s : Regex.Replace(s, @"(\\f[^|;]+\|b\d+\|i\d+\|c)\d+(\|p\d+;)", "${1}7${2}");

        /// <summary>Ogon akapitu RC (po „=”) zastąpiony ogonem z GA; kolor wartości biały.</summary>
        public static string ApplyTail(string contents, string marker, string gaTail)
        {
            if (string.IsNullOrEmpty(contents) || gaTail == null) return contents;
            string tail = Regex.Replace(gaTail, @"\\[Cc]\d+;", "");
            if (tail.Count(ch => ch == '{') != tail.Count(ch => ch == '}'))
                tail = tail.Replace("{", "").Replace("}", "");    // niedomknięte grupy z GA rozbiłyby MText RC
            tail = ForceWhiteFont(@"\C7;" + tail);

            var paras = contents.Split(new[] { @"\P" }, StringSplitOptions.None);
            bool changed = false;
            for (int i = 0; i < paras.Length; i++)
            {
                int mi = IndexOfIgnoringCodes(paras[i], marker);
                if (mi < 0) continue;
                int eq = paras[i].IndexOf('=', mi);
                if (eq < 0) continue;
                string head = paras[i].Substring(0, eq + 1);
                // Akapit RC może zamykać grupę „}” po wartości — zachowaj nadmiarowe zamknięcia
                string oldTail = paras[i].Substring(eq + 1);
                int bal = oldTail.Count(ch => ch == '{') - oldTail.Count(ch => ch == '}');
                paras[i] = @"\C7;" + ForceWhiteFont(head) + tail + (bal > 0 ? new string('{', bal) : bal < 0 ? new string('}', -bal) : "");
                changed = true;
            }
            return changed ? string.Join(@"\P", paras) : contents;
        }

        /// <summary>Blok „CONCRETE TO BE …” z GA: łamania akapitów i wcięcia GA → spacje (RC ma własne wcięcia).</summary>
        public static string FlattenBlock(string block)
        {
            string s = Regex.Replace(block, @"\\P(?:\\p[^;]*;)*", " ");
            s = Regex.Replace(s, @"\\[Cc]\d+;", "");
            s = Regex.Replace(s, @"\\A\d;", "");
            if (s.Count(ch => ch == '{') != s.Count(ch => ch == '}')) s = s.Replace("{", "").Replace("}", "");
            return ForceWhiteFont(Regex.Replace(s, @" {2,}", " "));
        }
    }
}
