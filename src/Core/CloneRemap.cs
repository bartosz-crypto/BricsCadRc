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
    /// | rozkład RC_AUTOREBAR_SLAB [1], wymiar ADD RC_ADD [1] | klon obrysu płyty (Polyline) (K5)  | "" (P4b)                   | ""                      |
    /// „Ten sam rysunek”: COPY/MIRROR/ARRAY albo wklejenie ze schowka skopiowanego w tym rysunku — wtedy wartość „bez zmian”
    /// to wartość obiektu źródłowego (BricsCAD zeruje w schowku odwołania do obiektów spoza schowka; Ctrl+V = COPY, pyt. 4).
    /// Etykieta przypisana więcej niż jednemu prętowi → wszystkim "" (Log.Info).
    /// Znacznik płyty (K5, decyzje P4 / P4b): COPY płyty z rozkładami → znacznik kopii wskazuje obrys kopii; kopia samych
    /// rozkładów / wymiarów ADD (płyta nieskopiowana) → "" — przynależność rozstrzyga wtedy test punktu w obrysie
    /// (AutoRebarEngine.BelongsToCurrentSlab / EraseOldAdd). Wcześniej ponowne generowanie na oryginale kasowało rozkłady kopii.
    ///
    /// Zapis: pręt — BarEntity.Write (BarXData.Write, polilinia i pręt-blok), rozkład — BarBlockEngine.WriteXData,
    /// opis / etykieta / RC_BAR_LINK — punktowa podmiana pola przez XLink.Write (WriteAnnotXData przy pustym [16]
    /// odtwarzałby starą wartość — 0-POPRAWKI p.4). Pary rozkład ↔ opis przepięte z mapy przechodzą tę samą
    /// normalizację odbicia i przebudowę opisu co w BarCopyWatcher (0-POPRAWKI p.1) — tylko po komendach kopiowania.
    /// </summary>
    internal static class CloneRemap
    {
        private enum Kind { None, Bar, Label, Dist, Annot, Link, AddDim, SectionHead, SectionPart }

        private const int BarLabelIdx   = 13;   // RC_SINGLE_BAR [13] LabelHandle
        private const int LabelBarIdx   = 1;    // RC_BAR_LABEL [1]
        private const int DistAnnotIdx  = 11;   // RC_BAR_BLOCK [11] AnnotHandle
        private const int DistBarIdx    = 18;   // RC_BAR_BLOCK [18] SourceBarHandle
        private const int AnnotDistIdx  = 16;   // RC_BAR_ANNOT [16] SourceBlockHandle
        private const int LinkBarIdx    = 1;    // RC_BAR_LINK [1]
        private const int SlabTagIdx    = 1;    // RC_AUTOREBAR_SLAB [1] (rozkład), RC_ADD [1] (wymiar ADD) — obrys płyty

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
                        RelinkSlabTag(tr, db, c, e, AutoRebarEngine.SlabAppName, Resolve);
                        break;
                    case Kind.AddDim:
                        RelinkSlabTag(tr, db, c, e, AutoRebarEngine.XAddDimApp, Resolve);
                        break;
                }
            }

            // Przekroje RC_SECTION (bezpiecznik): najpierw tytuły (nowy GUID, lista części), potem części ([1], [4])
            if (clones.Values.Any(x => x.Kind == Kind.SectionHead || x.Kind == Kind.SectionPart))
                RelinkSections(tr, db, clones, Resolve, Keep);
        }

        private const int HeadPickedIdx = 4, HeadOuterIdx = 5, HeadSelfIdx = 6;   // RC_SECTION [4] obrys wskazany, [5] zewnętrzny, [6] tytuł
        private const int PartTitleIdx = 1, PartGuidIdx = 4;                       // RC_SECTION_PART [1] tytuł, [4] GUID

        /// <summary>
        /// Bezpiecznik przekrojów: tytuł sklonowany z ≥ 1 częścią (lista części z rekordu) → samodzielny przekrój: nowy GUID, lista
        /// tylko sklonowanych części (handle klonów), [6] = własny handle, [4] / [5] → klon obrysu (płyta sklonowana razem, cel =
        /// Polyline), inaczej ten sam rysunek — bez zmian, inny — "". Tytuł sklonowany bez części → zwykły tekst (bez RC_SECTION
        /// i Xrecordu). Część: tytuł sklonowany razem (z danymi) → [1] = klon tytułu, [4] = jego nowy GUID; inaczej (P-b) bez RC_SECTION_PART — zwykły obiekt.
        /// </summary>
        private static void RelinkSections(Transaction tr, Database db, Dictionary<long, Clone> clones, ResolveFn resolve,
                                           Func<Clone, string, int, Field, long, string> keepFn)
        {
            string app = AutoRebarEngine.SectionTitleApp;
            var guidOfHead = new Dictionary<long, string>();
            foreach (var c in clones.Values.Where(x => x.Kind == Kind.SectionHead))
            {
                try
                {
                    var e = (Entity)tr.GetObject(c.Id, OpenMode.ForRead);
                    var rec = AutoRebarEngine.ReadRecord(tr, e);
                    var parts = new List<SectionRecord.Part>();
                    if (rec != null)
                        foreach (var p in rec.Parts)
                        {
                            // lista części to tekst w Xrecordzie — BricsCAD jej nie tłumaczy (jak stare 1000)
                            long t = resolve(c, new Field { Hex = XLink.Norm(p.Handle), Text = true }, out _);
                            if (t != 0 && clones.TryGetValue(t, out var pc) && pc.Kind == Kind.SectionPart)
                                parts.Add(p.WithHandle(t.ToString("X8")));   // z pozycją domyślną i rodzajem (etap 3)
                        }
                    if (!e.IsWriteEnabled) e.UpgradeOpen();
                    if (rec == null || parts.Count == 0)
                    {
                        // sam tytuł (albo bez rekordu) — zwykły tekst
                        e.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, app));
                        RemoveRecord(tr, e);
                        Log.Info($"CloneRemap: tytuł przekroju {c.H:X} skopiowany bez części — bez danych przekroju.");
                        continue;
                    }
                    string guid = Guid.NewGuid().ToString("D");
                    var curPicked = ReadField(e, app, HeadPickedIdx);
                    var curOuter = ReadField(e, app, HeadOuterIdx);
                    string picked = SlabOf(tr, db, c, curPicked, resolve, keepFn, HeadPickedIdx);
                    string outer = SlabOf(tr, db, c, curOuter, resolve, keepFn, HeadOuterIdx);
                    rec.Guid = guid;
                    rec.Parts = parts;
                    e.XData = AutoRebarEngine.TitleXData(guid, rec.Letter, picked, outer, c.H.ToString("X8"));
                    AutoRebarEngine.WriteRecord(tr, e, rec);
                    guidOfHead[c.H] = guid;
                }
                catch (System.Exception ex) { Log.Error("CloneRemap.RelinkSections.Head", ex); }
            }

            foreach (var c in clones.Values.Where(x => x.Kind == Kind.SectionPart))
            {
                try
                {
                    var e = (Entity)tr.GetObject(c.Id, OpenMode.ForRead);
                    TypedValue[] v;
                    using (var rb = e.GetXDataForApplication(AutoRebarEngine.SectionPartApp)) v = rb?.AsArray();
                    if (v == null || v.Length <= PartTitleIdx) continue;
                    var cur = ReadField(e, AutoRebarEngine.SectionPartApp, PartTitleIdx);
                    long t = resolve(c, cur, out _);
                    string nw = t != 0 && guidOfHead.ContainsKey(t) ? t.ToString("X8") : "";
                    if (nw.Length == 0)
                    {
                        // P-b (decyzja użytkownika): część skopiowana bez swojego tytułu → zwykły obiekt (bez RC_SECTION_PART)
                        if (!e.IsWriteEnabled) e.UpgradeOpen();
                        e.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, AutoRebarEngine.SectionPartApp));
                        continue;
                    }
                    bool guidChange = nw.Length > 0 && v.Length > PartGuidIdx
                                      && Convert.ToString(v[PartGuidIdx].Value, System.Globalization.CultureInfo.InvariantCulture) != guidOfHead[t];
                    if (SameLink(cur.Hex, nw) && !guidChange) continue;
                    v[PartTitleIdx] = XLink.Write(nw);
                    if (guidChange) v[PartGuidIdx] = new TypedValue((int)DxfCode.ExtendedDataAsciiString, guidOfHead[t]);
                    if (!e.IsWriteEnabled) e.UpgradeOpen();
                    e.XData = new ResultBuffer(v);
                }
                catch (System.Exception ex) { Log.Error("CloneRemap.RelinkSections.Part", ex); }
            }
        }

        /// <summary>Obrys płyty tytułu-klonu: klon (Cloned / AlreadyClone, cel = Polyline) albo wartość „bez zmian” (keep).</summary>
        private static string SlabOf(Transaction tr, Database db, Clone c, Field cur, ResolveFn resolve,
                                     Func<Clone, string, int, Field, long, string> keepFn, int idx)
        {
            if (string.IsNullOrEmpty(cur.Hex)) return "";
            long t = resolve(c, cur, out long keep);
            if (t != 0 && db.TryGetObjectId(new Handle(t), out var sid) && !sid.IsNull && !sid.IsErased
                && tr.GetObject(sid, OpenMode.ForRead) is Polyline)
                return t.ToString("X8");
            return keepFn(c, AutoRebarEngine.SectionTitleApp, idx, cur, keep);
        }

        private static void RemoveRecord(Transaction tr, Entity e)
        {
            if (e.ExtensionDictionary.IsNull) return;
            var dict = (DBDictionary)tr.GetObject(e.ExtensionDictionary, OpenMode.ForWrite);
            if (!dict.Contains(SectionRecord.XrecordName)) return;
            var id = dict.GetAt(SectionRecord.XrecordName);
            dict.Remove(SectionRecord.XrecordName);
            tr.GetObject(id, OpenMode.ForWrite).Erase();
        }

        /// <summary>
        /// K5: znacznik płyty klonu (rozkład RC_AUTOREBAR_SLAB [1], wymiar ADD RC_ADD [1]) → klon obrysu płyty, gdy płytę
        /// sklonowano w tej komendzie (Cloned / AlreadyClone i cel to Polyline); inaczej "" (P4b: płyta nieskopiowana —
        /// przynależność z testu punktu w obrysie). Brak znacznika albo pusty — bez zmian. Zapis punktowy XLink.Write.
        /// </summary>
        private static void RelinkSlabTag(Transaction tr, Database db, Clone c, Entity e, string app, ResolveFn resolve)
        {
            var cur = ReadField(e, app, SlabTagIdx);
            if (string.IsNullOrEmpty(cur.Hex)) return;
            long t = resolve(c, cur, out _);
            bool isSlab = t != 0 && db.TryGetObjectId(new Handle(t), out var sid) && !sid.IsNull && !sid.IsErased
                          && tr.GetObject(sid, OpenMode.ForRead) is Polyline;
            SetLinkIfChanged(e, app, SlabTagIdx, cur, isSlab ? t.ToString("X8") : "");
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
            if (Has(e, AutoRebarEngine.SectionPartApp)) return Kind.SectionPart;   // bezpiecznik przekrojów
            if (Has(e, AutoRebarEngine.SectionTitleApp)) return Kind.SectionHead;
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
            if (e is Dimension && Has(e, AutoRebarEngine.XAddDimApp)) return Kind.AddDim;   // K5: wymiar ADD (znacznik płyty)
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
                case Kind.AddDim: return AutoRebarEngine.XAddDimApp;
                case Kind.SectionHead: return AutoRebarEngine.SectionTitleApp;
                case Kind.SectionPart: return AutoRebarEngine.SectionPartApp;
                default:         return "";
            }
        }

        /// <summary>Pole [1] rekordu (Mark / powiązanie) jako tekst — kontrola obiektu źródłowego schowka.</summary>
        private static string Field1(Entity e, Kind k)
        {
            if (k == Kind.Label || k == Kind.Link || k == Kind.AddDim || k == Kind.SectionPart) return "";   // [1] to powiązanie — wyzerowane w schowku
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
