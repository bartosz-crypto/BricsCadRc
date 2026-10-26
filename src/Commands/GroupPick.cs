using System.Collections.Generic;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using BricsCadRc.Core;

namespace BricsCadRc.Commands
{
    /// <summary>
    /// Grupa płyt z Prepare GA (uwaga Bartka 2026-10-26, PLOT 357-362): po wskazaniu jednej płyty grupy / szeregu (RC_GA [3]) pytanie,
    /// czy zbroić także pozostałe płyty grupy — wspólne dla RC_GENERUJ_SIATKA, RC_GENERATE_BEAM, RC_GENERATE_CANTILEVER.
    /// </summary>
    internal static class GroupPick
    {
        /// <summary>
        /// Pozostałe płyty grupy wskazanego dołu (pary dół–góra; góra Null — brak rzutu górnego). Pusta lista — płyta spoza grupy
        /// albo odpowiedź „Nie”; null — anulowano.
        /// </summary>
        public static List<(ObjectId bottom, ObjectId top, string member)> Others(Editor ed, Database db, ObjectId bottomId, string tag)
        {
            var res = new List<(ObjectId bottom, ObjectId top, string member)>();
            if (bottomId.IsNull) return res;
            var (group, pairs) = AutoRebarEngine.GroupMeshPairs(db, bottomId);
            if (pairs.Count == 0) return res;
            var o = new PromptKeywordOptions(
                $"\n{tag} Płyta należy do grupy {group} — także pozostałe płyty grupy ({pairs.Count})? [Tak/Nie] <Tak>: ") { AllowNone = true };
            o.Keywords.Add("Tak");
            o.Keywords.Add("Nie");
            o.Keywords.Default = "Tak";
            var r = ed.GetKeywords(o);
            if (r.Status == PromptStatus.Cancel) return null;
            if (r.Status == PromptStatus.OK && r.StringResult == "Nie") return res;
            res.AddRange(pairs);
            return res;
        }
    }
}
