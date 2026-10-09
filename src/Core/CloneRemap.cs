using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Przepięcie powiązań klonów z mapy klonowania (W2, docs/PLAN_POPRAWKI_W1_W2.md 4.3; poprawki #3, #6, 3b).
    /// Wołane z BarCopyWatcher.HandleCommandFinish (CommandEnded komendy najwyższego poziomu) z paczkami CloneMap.Take.
    ///
    /// resolve(h) dla pola klonu C z paczki b: „Cloned” (h = klucz paczki, ta sama baza) → klon klucza;
    /// „AlreadyClone” (h = klon tej komendy — BricsCAD przetłumaczył przy wstawianiu) → h; inaczej „NotCloned”.
    ///
    /// | Pole klonu            | Cloned / AlreadyClone                                   | NotCloned, ten sam rysunek | NotCloned, inny rysunek |
    /// | pręt [13]             | klon etykiety TYLKO gdy jej [1] → TEN pręt (#6), inaczej "" | ""                     | ""                      |
    /// | etykieta [1]          | klon pręta, gdy jego [13] → ta etykieta; inaczej bez zmian | bez zmian                | ""                      |
    /// | rozkład [18]          | klon, gdy jest prętem (polilinia / v2) (#3)             | bez zmian (oryginał)       | ""                      |
    /// | RC_BAR_LINK [1]       | klon pręta                                              | bez zmian                  | ""                      |
    /// | rozkład [11]          | klon opisu (3b)                                         | ""                         | ""                      |
    /// | opis [16]             | klon rozkładu (3b)                                      | bez zmian                  | ""                      |
    /// „Ten sam rysunek”: COPY/MIRROR/ARRAY albo wklejenie ze schowka skopiowanego w tym rysunku — wtedy wartość „bez zmian”
    /// to wartość obiektu źródłowego (BricsCAD zeruje w schowku odwołania do obiektów spoza schowka; Ctrl+V = COPY, pyt. 4).
    /// Etykieta przypisana więcej niż jednemu prętowi → wszystkim "" (Log.Info).
    ///
    /// Zapis: pręt — BarEntity.Write (BarXData.Write, polilinia i pręt-blok), rozkład — BarBlockEngine.WriteXData,
    /// opis / etykieta / RC_BAR_LINK — punktowa podmiana pola przez XLink.Write (WriteAnnotXData przy pustym [16]
    /// odtwarzałby starą wartość — 0-POPRAWKI p.4). Pary rozkład ↔ opis przepięte z mapy przechodzą tę samą
    /// normalizację odbicia i przebudowę opisu co w BarCopyWatcher (0-POPRAWKI p.1) — tylko po komendach kopiowania.
    /// </summary>
    internal static class CloneRemap
    {
        private enum Kind { None, Bar, Label, Dist, Annot, Link }

        private const int BarLabelIdx   = 13;   // RC_SINGLE_BAR [13] LabelHandle
        private const int LabelBarIdx   = 1;    // RC_BAR_LABEL [1]
        private const int DistAnnotIdx  = 11;   // RC_BAR_BLOCK [11] AnnotHandle
        private const int DistBarIdx    = 18;   // RC_BAR_BLOCK [18] SourceBarHandle
        private const int AnnotDistIdx  = 16;   // RC_BAR_ANNOT [16] SourceBlockHandle
        private const int LinkBarIdx    = 1;    // RC_BAR_LINK [1]

        private sealed class Clone
        {
            public ObjectId Id;
            public long H;
            public CloneBatch Batch;
            public Kind Kind;
            /// <summary>Łańcuch paczek od korzenia do paczki klonu (CloneMap.ChainOf).</summary>
            public List<CloneBatch> Chain;
            /// <summary>Korzeń łańcucha to wklejenie tej zawartości schowka (CloneMap.IsClipPaste).</summary>
            public bool ClipPaste;
            /// <summary>Obiekt źródłowy w tym samym rysunku (COPY: klucz korzenia; schowek: z mapy schowka); Null = brak.</summary>
            public ObjectId Origin = ObjectId.Null;
            public bool SameDrawing;
        }

        /// <summary>Pole powiązania: handle „X8” i czy zapisane starym tekstem 1000 (BricsCAD go nie tłumaczy).</summary>
        private struct Field
        {
            public string Hex;
            public bool Text;
        }

        private delegate long ResolveFn(Clone c, Field f, out long keep);

        /// <summary>
        /// Przepina powiązania wszystkich klonów z paczek (w przestrzeni modelu / papieru). Zwraca zbiór klonów obsłużonych
        /// z mapy — BarCopyWatcher stosuje dla nich heurystyki tylko jako zapas dla obiektów spoza mapy.
        /// rebuildPairs — normalizacja odbicia i przebudowa opisu par przepiętych (komendy kopiowania, jak RemapCopiedPairs).
        /// </summary>
        public static HashSet<ObjectId> Apply(Database db, List<CloneBatch> batches, bool rebuildPairs)
        {
            var mapped = new HashSet<ObjectId>();
            batches = (batches ?? new List<CloneBatch>()).Where(b => b.Dest == db).ToList();
            if (db == null || batches.Count == 0) return mapped;

            var relinkedPairs = new List<(ObjectId annotId, ObjectId blockId)>();
            using (CloneMap.Suppress())
            using (BarGeometryWatcher.Suppress())
            {
                try
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var clones = Collect(tr, db, batches);
                        foreach (var c in clones.Values) mapped.Add(c.Id);
                        if (clones.Count > 0) Relink(tr, db, clones, relinkedPairs);
                        tr.Commit();
                    }
                }
                catch (System.Exception ex) { Log.Error("CloneRemap.Apply", ex); }

                if (rebuildPairs)
                    foreach (var (annotId, blockId) in relinkedPairs)
                        BarCopyWatcher.NormalizeAndRebuildPair(db, annotId, blockId);
            }
            if (mapped.Count > 0)
                Log.Info($"CloneRemap: przepięto z mapy {mapped.Count} klonów ({batches.Count} paczek), par rozkład–opis {relinkedPairs.Count}.");
            return mapped;
        }

        // ----------------------------------------------------------------
        // Zbiór klonów: wszystkie ŻYWE klony w przestrzeni modelu / papieru (także te, które w tej samej komendzie
        // skopiowano dalej — COPY A→B, B→C). Obiekty pomocnicze (blok pomocniczy PASTECLIP, definicje bloków) odpadają
        // przez InLayout.
        // ----------------------------------------------------------------

        private static Dictionary<long, Clone> Collect(Transaction tr, Database db, List<CloneBatch> batches)
        {
            var res = new Dictionary<long, Clone>();
            for (int bi = 0; bi < batches.Count; bi++)
            {
                var b = batches[bi];
                foreach (var kv in b.Map)
                {
                    var id = kv.Value;
                    if (id.IsNull || !id.IsValid || id.IsErased || id.Database != db) continue;
                    long h = id.Handle.Value;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    if (!InLayout(tr, e)) continue;
                    var kind = KindOf(e);
                    if (kind == Kind.None) continue;

                    var c = new Clone { Id = id, H = h, Batch = b, Kind = kind };
                    c.Chain = CloneMap.ChainOf(batches, bi, kv.Key, out long rootKey);
                    c.ClipPaste = CloneMap.IsClipPaste(c.Chain[0]);
                    ResolveOrigin(tr, db, c, rootKey, e);
                    res[h] = c;
                }
            }
            return res;
        }

        /// <summary>Obiekt źródłowy w tym samym rysunku: klucz korzenia (COPY) albo oryginał z mapy schowka (wklejenie).</summary>
        private static void ResolveOrigin(Transaction tr, Database db, Clone c, long rootKey, Entity clone)
        {
            var root = c.Chain[0];
            long originHandle = 0;
            if (!root.CrossDb) originHandle = rootKey;
            else if (c.ClipPaste && CloneMap.IsClipOrigin(db)) originHandle = CloneMap.ClipOriginalOf(rootKey);
            if (originHandle == 0) return;

            // Obiekt źródłowy może być już usunięty (CUTCLIP, WBLOCK) — jego XData nadal są źródłem wartości „bez zmian”
            if (!db.TryGetObjectId(new Handle(originHandle), out var oid) || oid.IsNull)
            {
                c.SameDrawing = !root.CrossDb;
                return;
            }
            var o = tr.GetObject(oid, OpenMode.ForRead, true) as Entity;
            // Schowek: kontrola, że obiekt źródłowy to ten sam rodzaj obiektu RC z tym samym polem [1]
            if (root.CrossDb && (o == null || KindOf(o) != c.Kind || Field1(o, c.Kind) != Field1(clone, c.Kind)))
            {
                Log.Info($"CloneRemap: obiekt źródłowy schowka {originHandle:X} nie pasuje do klonu {c.H:X} — jak z innego rysunku.");
                return;
            }
            c.Origin = oid;
            c.SameDrawing = true;
        }

        // ----------------------------------------------------------------
        // Przepięcie
        // ----------------------------------------------------------------

        private static void Relink(Transaction tr, Database db, Dictionary<long, Clone> clones,
                                   List<(ObjectId annotId, ObjectId blockId)> relinkedPairs)
        {
            // resolve(pole) przez łańcuch paczek klonu → handle klonu (Cloned / AlreadyClone) albo 0 (NotCloned);
            // keep — wartość „bez zmian” (klon osiągnięty przed przerwaniem łańcucha albo obecna)
            long Resolve(Clone c, Field f, out long keep)
            {
                keep = 0;
                if (!XLink.TryParse(f.Hex, out long hv) || hv == 0) return 0;
                return CloneMap.ResolveInChain(c.Chain, hv, f.Text, c.ClipPaste, out keep);
            }
            string Hex(long h) => h == 0 ? "" : h.ToString("X8");

            // Wartość „bez zmian” dla NotCloned w tym samym rysunku: obecna wartość klonu (COPY; po łańcuchu — klon
            // pośredni) albo wartość obiektu źródłowego, gdy BricsCAD ją wyzerował (schowek); inny rysunek — ""
            string Keep(Clone c, string app, int idx, Field cur, long keep)
            {
                if (!c.SameDrawing) return "";
                if (string.IsNullOrEmpty(cur.Hex))
                    return c.Origin.IsNull ? "" : ReadField(tr.GetObject(c.Origin, OpenMode.ForRead, true) as Entity, app, idx).Hex;
                return keep != 0 ? Hex(keep) : cur.Hex;
            }

            // ---- pręt ↔ etykieta (#6: tylko para dwustronna; etykieta dla > 1 pręta → nikomu) ----
            var labelOf = new Dictionary<long, long>();   // pręt → etykieta (kandydat)
            foreach (var c in clones.Values.Where(x => x.Kind == Kind.Bar))
            {
                var e = (Entity)tr.GetObject(c.Id, OpenMode.ForRead);
                long l = Resolve(c, ReadField(e, SingleBarEngine.XAppName, BarLabelIdx), out _);
                if (l == 0 || !clones.TryGetValue(l, out var lc) || lc.Kind != Kind.Label) continue;
                var le = (Entity)tr.GetObject(lc.Id, OpenMode.ForRead);
                if (Resolve(lc, ReadField(le, SingleBarEngine.XLabelAppName, LabelBarIdx), out _) == c.H)
                    labelOf[c.H] = l;
            }
            foreach (var dup in labelOf.GroupBy(kv => kv.Value).Where(g => g.Count() > 1).ToList())
            {
                Log.Info($"CloneRemap: etykieta {dup.Key:X} przypisana kilku prętom — bez przypisania.");
                foreach (var kv in dup) labelOf.Remove(kv.Key);
            }
            var barOfLabel = labelOf.ToDictionary(kv => kv.Value, kv => kv.Key);

            foreach (var c in clones.Values)
            {
                var e = (Entity)tr.GetObject(c.Id, OpenMode.ForRead);
                switch (c.Kind)
                {
                    case Kind.Bar:
                    {
                        var cur = ReadField(e, SingleBarEngine.XAppName, BarLabelIdx);
                        string nw = labelOf.TryGetValue(c.H, out long l) ? Hex(l) : "";
                        if (!SameLink(cur.Hex, nw))
                        {
                            var be = BarEntity.From(e, BarKinds.Polyline | BarKinds.Block);
                            var bar = be?.Read();
                            if (bar != null)
                            {
                                if (!e.IsWriteEnabled) e.UpgradeOpen();
                                bar.LabelHandle = nw;
                                be.Write(bar);
                            }
                        }
                        break;
                    }
                    case Kind.Label:
                    {
                        var cur = ReadField(e, SingleBarEngine.XLabelAppName, LabelBarIdx);
                        long t = Resolve(c, cur, out long keep);
                        string nw;
                        if (barOfLabel.TryGetValue(c.H, out long b)) nw = Hex(b);
                        else if (t != 0) nw = cur.Hex;                                   // klon pręta bez pary — bez zmian
                        else nw = Keep(c, SingleBarEngine.XLabelAppName, LabelBarIdx, cur, keep);
                        SetLinkIfChanged(e, SingleBarEngine.XLabelAppName, LabelBarIdx, cur, nw);
                        break;
                    }
                    case Kind.Link:
                    {
                        var cur = ReadField(e, SingleBarEngine.XLinkAppName, LinkBarIdx);
                        long t = Resolve(c, cur, out long keep);
                        string nw = t != 0 ? Hex(t) : Keep(c, SingleBarEngine.XLinkAppName, LinkBarIdx, cur, keep);
                        SetLinkIfChanged(e, SingleBarEngine.XLinkAppName, LinkBarIdx, cur, nw);
                        break;
                    }
                    case Kind.Annot:
                    {
                        var cur = ReadField(e, AnnotationEngine.AnnotAppName, AnnotDistIdx);
                        long t = Resolve(c, cur, out long keep);                        string nw = t != 0 ? Hex(t) : Keep(c, AnnotationEngine.AnnotAppName, AnnotDistIdx, cur, keep);
                        SetLinkIfChanged(e, AnnotationEngine.AnnotAppName, AnnotDistIdx, cur, nw);
                        break;
                    }
                    case Kind.Dist:
                        RelinkDist(tr, db, c, e, clones, Resolve, Keep, relinkedPairs);
                        break;
                }
            }
        }

        private static void RelinkDist(Transaction tr, Database db, Clone c, Entity e, Dictionary<long, Clone> clones,
                                       ResolveFn resolve, Func<Clone, string, int, Field, long, string> keepFn,
                                       List<(ObjectId annotId, ObjectId blockId)> relinkedPairs)
        {
            var data = BarBlockEngine.ReadXData(e);
            if (data == null) return;
            int len = RecordLength(e, BarBlockEngine.XAppName);

            string curAnnot = data.AnnotHandle ?? "";
            string newAnnot = curAnnot;
            if (len > DistAnnotIdx)
            {
                long a = resolve(c, ReadField(e, BarBlockEngine.XAppName, DistAnnotIdx), out _);
                newAnnot = a != 0 && clones.TryGetValue(a, out var ac) && ac.Kind == Kind.Annot ? a.ToString("X8") : "";
            }

            string curBar = data.SourceBarHandle ?? "";
            string newBar = curBar;
            if (len > DistBarIdx)   // krótkie (stare) rekordy bez [18] — pomijane
            {
                var curBarF = ReadField(e, BarBlockEngine.XAppName, DistBarIdx);
                long t = resolve(c, curBarF, out long keep);
                bool isBar = false;
                if (t != 0 && db.TryGetObjectId(new Handle(t), out var bid) && !bid.IsNull && !bid.IsErased)
                    isBar = BarEntity.KindOf(tr.GetObject(bid, OpenMode.ForRead) as Entity) is BarKind k
                            && (k == BarKind.Polyline || k == BarKind.Block);
                newBar = isBar ? t.ToString("X8") : keepFn(c, BarBlockEngine.XAppName, DistBarIdx, curBarF, keep);
            }

            // Liczba na etykietach: stary (tylko w tym samym rysunku — inaczej handle z innej bazy) i nowy pręt źródłowy
            if (!string.IsNullOrEmpty(curBar) && c.SameDrawing) PendingLabelUpdates.Add(db, curBar);
            if (!string.IsNullOrEmpty(newBar)) PendingLabelUpdates.Add(db, newBar);

            bool annotChanged = !SameLink(curAnnot, newAnnot);
            if (!annotChanged && SameLink(curBar, newBar)) return;
            if (!e.IsWriteEnabled) e.UpgradeOpen();
            data.AnnotHandle = newAnnot;
            data.SourceBarHandle = newBar;
            BarBlockEngine.WriteXData(e, data);

            // Para przepięta (jak RemapCopiedPairs): rozkład ↔ jego klon opisu → normalizacja i przebudowa opisu
            if (annotChanged && XLink.TryParse(newAnnot, out long ah) && ah != 0 && clones.TryGetValue(ah, out var annot))
                relinkedPairs.Add((annot.Id, c.Id));
        }

        // ----------------------------------------------------------------
        // Pomocnicze
        // ----------------------------------------------------------------

        private static Kind KindOf(Entity e)
        {
            if (e == null) return Kind.None;
            var bk = BarEntity.KindOf(e);
            if (bk == BarKind.Polyline || bk == BarKind.Block) return Kind.Bar;
            if (e is MLeader) return Has(e, SingleBarEngine.XLabelAppName) ? Kind.Label : Kind.None;
            if (e is BlockReference)
            {
                if (Has(e, BarBlockEngine.XAppName)) return Kind.Dist;
                if (Has(e, AnnotationEngine.AnnotAppName)) return Kind.Annot;
                return Kind.None;
            }
            if (bk == null && Has(e, SingleBarEngine.XLinkAppName)) return Kind.Link;
            return Kind.None;
        }

        private static bool Has(Entity e, string app)
        {
            using (var rb = e.GetXDataForApplication(app)) return rb != null;
        }

        /// <summary>Klon leży w przestrzeni modelu albo papieru (nie w definicji bloku).</summary>
        private static bool InLayout(Transaction tr, Entity e)
        {
            try
            {
                // Właściciel usunięty (np. definicja bloku z 1. fazy -INSERT *) — nie przestrzeń modelu / papieru
                if (e.OwnerId.IsNull || e.OwnerId.IsErased) return false;
                return tr.GetObject(e.OwnerId, OpenMode.ForRead) is BlockTableRecord btr && btr.IsLayout;
            }
            catch (System.Exception ex)
            {
                Log.Error("CloneRemap.InLayout", ex);
                return false;
            }
        }

        private static string AppOf(Kind k)
        {
            switch (k)
            {
                case Kind.Bar:   return SingleBarEngine.XAppName;
                case Kind.Label: return SingleBarEngine.XLabelAppName;
                case Kind.Dist:  return BarBlockEngine.XAppName;
                case Kind.Annot: return AnnotationEngine.AnnotAppName;
                case Kind.Link:  return SingleBarEngine.XLinkAppName;
                default:         return "";
            }
        }

        /// <summary>Pole [1] rekordu (Mark / powiązanie) jako tekst — kontrola obiektu źródłowego schowka.</summary>
        private static string Field1(Entity e, Kind k)
        {
            if (k == Kind.Label || k == Kind.Link) return "";   // [1] to powiązanie — wyzerowane w schowku
            using (var rb = e?.GetXDataForApplication(AppOf(k)))
            {
                var v = rb?.AsArray();
                return v != null && v.Length > 1 ? Convert.ToString(v[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
            }
        }

        private static int RecordLength(Entity e, string app)
        {
            using (var rb = e.GetXDataForApplication(app)) return rb?.AsArray().Length ?? 0;
        }

        /// <summary>Powiązanie z pola idx rekordu app (XLink.Read: 1005 albo stary 1000) → „X8” albo "" + czy tekst 1000.</summary>
        private static Field ReadField(Entity e, string app, int idx)
        {
            if (e == null) return new Field { Hex = "" };
            using (var rb = e.GetXDataForApplication(app))
            {
                var v = rb?.AsArray();
                if (v == null || v.Length <= idx) return new Field { Hex = "" };
                string hex = XLink.Read(v[idx]);
                return new Field { Hex = hex, Text = hex.Length > 0 && v[idx].TypeCode == (int)DxfCode.ExtendedDataAsciiString };
            }
        }

        private static bool SameLink(string a, string b)
            => XLink.Norm(a) == XLink.Norm(b);

        /// <summary>
        /// Punktowa podmiana pola powiązania (XLink.Write → 1005) — reszta rekordu bez zmian; krótki rekord pomijany.
        /// Stary tekst 1000 przetłumaczony na inny handle zapisywany jest jako 1005; wartość niezmieniona zostaje.
        /// </summary>
        private static void SetLinkIfChanged(Entity e, string app, int idx, Field cur, string nw)
        {
            if (SameLink(cur.Hex, nw)) return;
            TypedValue[] v;
            using (var rb = e.GetXDataForApplication(app)) v = rb?.AsArray();
            if (v == null || v.Length <= idx) return;
            v[idx] = XLink.Write(nw);
            if (!e.IsWriteEnabled) e.UpgradeOpen();
            e.XData = new ResultBuffer(v);
        }
    }
}
