using System;
using System.Collections.Generic;
using System.Globalization;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Runtime;
using Application = Bricscad.ApplicationServices.Application;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Jedna paczka klonowania (jedno BeginDeepCloneTranslation / InsertMappingAvailable): klucz (handle w bazie źródłowej)
    /// → klon (ObjectId w bazie docelowej). Tylko pary IsCloned z niepustą wartością. Bez otwierania obiektów.
    /// </summary>
    internal sealed class CloneBatch
    {
        public Database Dest;
        /// <summary>Baza źródłowa ≠ docelowa (wstawianie z pliku / schowka).</summary>
        public bool CrossDb;
        public DeepCloneType Ctx;
        /// <summary>CMDNAMES w chwili zdarzenia (log, rozpoznanie wklejania ze schowka).</summary>
        public string Cmd = "";
        /// <summary>Paczka powstała w komendzie wklejania (PASTECLIP / PASTEBLOCK / PASTEORIG / PASTESPEC).</summary>
        public bool IsPaste;
        /// <summary>Handle klucza (przestrzeń bazy źródłowej) → klon.</summary>
        public readonly Dictionary<long, ObjectId> Map = new Dictionary<long, ObjectId>();
        /// <summary>Handle klonu → handle klucza.</summary>
        public readonly Dictionary<long, long> KeyOf = new Dictionary<long, long>();
        /// <summary>Klucze par głównych (IsPrimary) będące encjami — kontrola zgodności ze schowkiem (plik schowka po
        /// odczycie ma dodatkowe definicje bloków, więc zawartość bloków nie jest porównywana).</summary>
        public readonly HashSet<long> EntityKeys = new HashSet<long>();

        public bool SameContent(CloneBatch o)
        {
            if (o == null || o.Dest != Dest || o.CrossDb != CrossDb || o.Ctx != Ctx || o.Map.Count != Map.Count) return false;
            foreach (var kv in Map)
                if (!o.Map.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
            return true;
        }
    }

    /// <summary>
    /// Mapa klonowania oryginał → klon (W2, docs/PLAN_POPRAWKI_W1_W2.md 4.1–4.2; PLAN_C_etap3 0-POPRAWKI p.5 = 3e3).
    ///
    /// BricsCAD V25 przy COPY / MIRROR N / ARRAY NIE przemapowuje handle 1005 w XData klonów (wskazują oryginały); przy
    /// wstawianiu (PASTECLIP, -INSERT) tłumaczy odwołania 1005 do obiektów wstawianych razem, a pozostałe zeruje; starych
    /// odwołań tekstowych (1000) nie tłumaczy nigdy. Ta klasa tylko ZAPISUJE paczki z BeginDeepCloneTranslation /
    /// InsertMappingAvailable (handler bez zapisów do bazy); przepięcie robi CloneRemap w CommandEnded.
    ///
    /// Konteksty: Copy, Insert, InsertCopy → paczka per baza docelowa (w kolejności). Wblock z komendy schowka
    /// (COPYCLIP / CUTCLIP / COPYBASE) → mapa schowka (handle w pliku schowka ↔ handle oryginału) + rysunek źródłowy;
    /// zwykły WBLOCK jej nie nadpisuje. Mapa schowka działa wyłącznie dla paczki komendy wklejania, której wszystkie
    /// klucze-encje są w mapie schowka (ta sama zawartość) — INSERT nigdy jej nie używa. Objects i inne — pomijane.
    ///
    /// Łańcuch paczek: klon paczki późniejszej, którego klucz jest klonem paczki wcześniejszej tej samej komendy
    /// (PASTECLIP: plik schowka → blok pomocniczy → model; COPY A→B, potem B→C w jednej komendzie nadrzędnej).
    /// </summary>
    public static class CloneMap
    {
        private static readonly Dictionary<Database, List<CloneBatch>> _batches = new Dictionary<Database, List<CloneBatch>>();
        private static int _suppress;
        private static bool _registered;

        // Schowek: rysunek źródłowy (Guid dokumentu) i mapy handle schowka ↔ oryginał (do następnego COPYCLIP / CUTCLIP).
        // Runda 2: tożsamość źródła = Guid czasu życia dokumentu (nadawany przy podpięciu, klucz: wskaźnik bazy natywnej,
        // usuwany przy zamknięciu) — niezależny od nazwy pliku (SAVEAS) i odporny na ponowne użycie adresu.
        private static readonly Dictionary<IntPtr, Guid> _docIds = new Dictionary<IntPtr, Guid>();
        private static Guid _clipOriginId = Guid.Empty;
        private static Dictionary<long, long> _clipTempToOrig = new Dictionary<long, long>();
        private static Dictionary<long, long> _clipOrigToTemp = new Dictionary<long, long>();

        public static void Register()
        {
            if (_registered) return;
            DocumentWatch.Subscribe("CloneMap",
                d =>
                {
                    if (d.Database != null) _docIds[d.Database.UnmanagedObject] = Guid.NewGuid();
                    d.Database.BeginDeepCloneTranslation += OnMapping;
                    d.Database.InsertMappingAvailable    += OnMapping;
                    d.Database.DeepCloneAborted          += OnAborted;
                    d.CommandFailed                      += OnCommandFailed;
                },
                d =>
                {
                    try
                    {
                        d.Database.BeginDeepCloneTranslation -= OnMapping;
                        d.Database.InsertMappingAvailable    -= OnMapping;
                        d.Database.DeepCloneAborted          -= OnAborted;
                    }
                    catch (System.Exception ex) { Log.Error("CloneMap.Detach", ex); }
                    d.CommandFailed -= OnCommandFailed;
                    OnDocumentClosing(d.Database);
                });
            _registered = true;
        }

        public static void Unregister()
        {
            DocumentWatch.Unsubscribe("CloneMap");
            _batches.Clear();
            _docIds.Clear();
            ClearClipboard();
            _registered = false;
        }

        /// <summary>
        /// Zamknięcie rysunku (DocumentToBeDestroyed): paczki odrzucone, rysunek przestaje być „żywym źródłem” schowka,
        /// ale mapy schowka ZOSTAJĄ (zawartość schowka ta sama do następnego COPYCLIP / CUTCLIP — wklejenie do innego
        /// rysunku nadal tłumaczy stare odwołania 1000).
        /// </summary>
        internal static void OnDocumentClosing(Database db)
        {
            if (db == null) return;
            Forget(db);
            if (_docIds.TryGetValue(db.UnmanagedObject, out var id))
            {
                if (id == _clipOriginId) _clipOriginId = Guid.Empty;
                _docIds.Remove(db.UnmanagedObject);
            }
        }

        private static void ClearClipboard()
        {
            _clipOriginId = Guid.Empty;
            _clipTempToOrig = new Dictionary<long, long>();
            _clipOrigToTemp = new Dictionary<long, long>();
        }

        /// <summary>Wycisza zapis paczek (własne DeepCloneObjects wtyczki, przepinanie w CommandEnded). Zawsze w using.</summary>
        public static IDisposable Suppress() => new SuppressScope();

        private sealed class SuppressScope : IDisposable
        {
            private bool _disposed;
            public SuppressScope() { _suppress++; }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _suppress--;
            }
        }

        /// <summary>Paczki zapisane dla bazy od ostatniego Take/Forget (w kolejności) — usuwane z mapy.</summary>
        internal static List<CloneBatch> Take(Database db)
        {
            if (db == null || !_batches.TryGetValue(db, out var list)) return new List<CloneBatch>();
            _batches.Remove(db);
            return list;
        }

        /// <summary>Odrzuca paczki bazy (UNDO/REDO, CommandFailed, zamknięcie rysunku).</summary>
        public static void Forget(Database db)
        {
            if (db != null) _batches.Remove(db);
        }

        /// <summary>Liczba paczek oczekujących dla bazy (diagnostyka / testy).</summary>
        internal static int PendingCount(Database db)
            => db != null && _batches.TryGetValue(db, out var l) ? l.Count : 0;

        // ----------------------------------------------------------------
        // Schowek
        // ----------------------------------------------------------------

        /// <summary>Czy baza jest otwartym rysunkiem, z którego pochodzi zawartość schowka (Guid dokumentu, niezależny od SAVEAS).</summary>
        internal static bool IsClipOrigin(Database db)
            => db != null && _clipOriginId != Guid.Empty && _docIds.TryGetValue(db.UnmanagedObject, out var id) && id == _clipOriginId;

        /// <summary>
        /// Paczka (korzeń łańcucha) to wklejenie TEJ zawartości schowka: komenda wklejania, wstawianie z innej bazy,
        /// wszystkie klucze par głównych (encje) paczki są w mapie schowka. INSERT / -INSERT nigdy (0-POPRAWKI p.3, uwaga 5).
        /// </summary>
        internal static bool IsClipPaste(CloneBatch root)
        {
            if (root == null || !root.CrossDb || !root.IsPaste || _clipTempToOrig.Count == 0 || root.EntityKeys.Count == 0) return false;
            foreach (long k in root.EntityKeys)
                if (!_clipTempToOrig.ContainsKey(k)) return false;
            return true;
        }

        /// <summary>Handle oryginału dla handle obiektu w pliku schowka (0 = brak).</summary>
        internal static long ClipOriginalOf(long tempHandle)
            => _clipTempToOrig.TryGetValue(tempHandle, out long h) ? h : 0;

        /// <summary>Handle w pliku schowka dla handle oryginału (0 = brak) — stare odwołania tekstowe 1000.</summary>
        internal static long ClipTempOf(long origHandle)
            => _clipOrigToTemp.TryGetValue(origHandle, out long h) ? h : 0;

        // ----------------------------------------------------------------
        // Łańcuch paczek
        // ----------------------------------------------------------------

        /// <summary>
        /// Łańcuch paczek klonu: od korzenia do paczki bi (klucz paczki późniejszej = klon paczki wcześniejszej, tylko w tej
        /// samej bazie). rootKey — klucz w paczce-korzeniu.
        /// </summary>
        internal static List<CloneBatch> ChainOf(List<CloneBatch> batches, int bi, long key, out long rootKey)
        {
            var chain = new List<CloneBatch> { batches[bi] };
            rootKey = key;
            var cur = batches[bi];
            for (int j = bi - 1; j >= 0 && !cur.CrossDb; j--)
            {
                if (batches[j].Dest != cur.Dest || !batches[j].KeyOf.TryGetValue(rootKey, out long k2)) continue;
                chain.Insert(0, batches[j]);
                cur = batches[j];
                rootKey = k2;
            }
            return chain;
        }

        /// <summary>Klucz paczki chain[i] przez kolejne paczki łańcucha; 0 gdy przerwane. partial — ostatni osiągnięty klon.</summary>
        private static long Forward(List<CloneBatch> chain, int i, long key, out long partial)
        {
            partial = 0;
            if (!chain[i].Map.TryGetValue(key, out var v) || v.IsNull) return 0;
            long h = v.Handle.Value;
            partial = h;
            for (int j = i + 1; j < chain.Count; j++)
            {
                if (!chain[j].Map.TryGetValue(h, out v) || v.IsNull) return 0;
                h = v.Handle.Value;
                partial = h;
            }
            return h;
        }

        /// <summary>
        /// Powiązanie hv pola klonu z łańcucha chain → handle klonu końcowego (0 = obiekt wskazywany nie był klonowany).
        /// isText — stare odwołanie 1000 (BricsCAD go nie tłumaczy: przestrzeń bazy źródłowej korzenia; przy wklejeniu ze
        /// schowka — przestrzeń rysunku źródłowego, tłumaczona mapą schowka). keep — wartość „bez zmian” w tym samym rysunku:
        /// klon osiągnięty przed przerwaniem łańcucha albo hv.
        /// </summary>
        internal static long ResolveInChain(List<CloneBatch> chain, long hv, bool isText, bool clipPaste, out long keep)
        {
            keep = hv;
            if (hv == 0 || chain == null || chain.Count == 0) return 0;
            long r, partial;
            if (isText)
            {
                if (chain[0].CrossDb)
                {
                    long k = clipPaste ? ClipTempOf(hv) : hv;
                    if (k == 0) return 0;
                    r = Forward(chain, 0, k, out partial);
                    return r;   // inny rysunek / plik — brak częściowego wyniku w przestrzeni celu
                }
            }
            else
            {
                // Już klon (przetłumaczony przez BricsCAD albo pośredni) — dalej przez późniejsze paczki
                for (int i = chain.Count - 1; i >= 0; i--)
                {
                    if (!chain[i].KeyOf.ContainsKey(hv)) continue;
                    if (i == chain.Count - 1) return hv;
                    r = Forward(chain, i + 1, hv, out partial);
                    if (partial != 0) keep = partial;
                    if (r == 0 && partial == 0) keep = hv;
                    return r;
                }
            }
            // Klucz paczki w tej samej bazie (COPY: wartości klonów = oryginały)
            for (int i = 0; i < chain.Count; i++)
            {
                if (chain[i].CrossDb || !chain[i].Map.ContainsKey(hv)) continue;
                r = Forward(chain, i, hv, out partial);
                if (r == 0 && partial != 0) keep = partial;
                return r;
            }
            // Wstawianie z innej bazy: 1005, którego BricsCAD nie przetłumaczył (nie jest klonem), pochodzi z tekstu 1000
            // przepisanego w trakcie komendy przez kod wtyczki (np. WriteAnnotXData zapisuje stary handle jako 1005) —
            // przestrzeń jak dla tekstu: mapa schowka albo klucz pliku.
            if (!isText && chain[0].CrossDb)
            {
                long k = clipPaste ? ClipTempOf(hv) : hv;
                if (k != 0 && chain[0].Map.ContainsKey(k)) return Forward(chain, 0, k, out _);
            }
            return 0;
        }

        // ----------------------------------------------------------------
        // Zapytania w trakcie komendy (przed Take) — strażnicy kaskad usuwania
        // ----------------------------------------------------------------

        private static bool FindPending(Database db, ObjectId cloneId, out List<CloneBatch> chain)
        {
            chain = null;
            if (db == null || cloneId.IsNull || !_batches.TryGetValue(db, out var list)) return false;
            long ch = cloneId.Handle.Value;
            for (int bi = list.Count - 1; bi >= 0; bi--)
            {
                if (!list[bi].KeyOf.TryGetValue(ch, out long key)) continue;
                chain = ChainOf(list, bi, key, out _);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Strażnik kaskady usuwania pręta (plan 4.5, uwaga 4): rozkład distId sklonowany w trwającej komendzie, którego [18]
        /// (= usuwany pręt sourceBarHandle) po przepięciu wskaże INNY, żywy pręt (klon) — nie należy do usuwanego pręta.
        /// Gdy [18] już wskazuje właściwy pręt (np. przetłumaczone przy wklejaniu) — kaskada działa normalnie.
        /// </summary>
        public static bool IsPendingCloneRetarget(Database db, ObjectId distId, string sourceBarHandle)
        {
            if (!XLink.TryParse(sourceBarHandle, out long hv) || hv == 0) return false;
            if (!FindPending(db, distId, out var chain)) return false;
            long r = ResolveInChain(chain, hv, false, false, out _);
            if (r == 0 || r == hv) return false;
            return db.TryGetObjectId(new Handle(r), out var target) && !target.IsNull && !target.IsErased;
        }

        /// <summary>
        /// Powiązanie linkHex obiektu cloneId sklonowanego w trwającej komendzie (przed przepięciem w CommandEnded):
        /// false — obiekt nie jest oczekującym klonem (stosuj powiązanie jak dotąd); true — resolvedHex = klon wskazywany
        /// przez łańcuch paczek albo "" (wskazywany obiekt nie był klonowany — nie należy do klonu, np. opis oryginału).
        /// </summary>
        internal static bool TryResolvePending(Database db, ObjectId cloneId, string linkHex, out string resolvedHex)
        {
            resolvedHex = "";
            if (!FindPending(db, cloneId, out var chain)) return false;
            if (!XLink.TryParse(linkHex, out long hv) || hv == 0) return true;
            long r = ResolveInChain(chain, hv, false, false, out _);
            if (r == 0) r = ResolveInChain(chain, hv, true, false, out _);   // stary zapis tekstowy 1000
            if (r != 0) resolvedHex = r.ToString("X8");
            return true;
        }

        // ----------------------------------------------------------------
        // Handlery zdarzeń — WYŁĄCZNIE zapis do pamięci (bez otwierania obiektów i zapisów do bazy)
        // ----------------------------------------------------------------

        private static void OnMapping(object sender, IdMappingEventArgs e)
        {
            if (_suppress > 0) return;
            try
            {
                var m = e?.IdMapping;
                if (m == null) return;
                var ctx = m.DeepCloneContext;
                if (ctx == DeepCloneType.Wblock)
                {
                    RecordClipboard(m);
                    return;
                }
                if (ctx != DeepCloneType.Copy && ctx != DeepCloneType.Insert && ctx != DeepCloneType.InsertCopy) return;

                var dest = m.DestinationDatabase;
                if (dest == null) return;
                string cmd = CmdNames();
                var b = new CloneBatch
                {
                    Dest    = dest,
                    CrossDb = m.OriginalDatabase != dest,   // operator == porównuje bazę natywną (wrappery bywają różne)
                    Ctx     = ctx,
                    Cmd     = cmd,
                    IsPaste = cmd.ToUpperInvariant().Contains("PASTE"),
                };
                var entityClass = RXObject.GetClass(typeof(Entity));
                foreach (IdPair p in m)
                {
                    if (!p.IsCloned || p.Key.IsNull || p.Value.IsNull) continue;
                    long k = p.Key.Handle.Value;
                    b.Map[k] = p.Value;
                    b.KeyOf[p.Value.Handle.Value] = k;
                    if (p.IsPrimary && IsEntity(p.Key, entityClass)) b.EntityKeys.Add(k);   // obiekty wybrane (nie zawartość bloków)
                }
                if (b.Map.Count == 0) return;

                if (!_batches.TryGetValue(dest, out var list)) _batches[dest] = list = new List<CloneBatch>();
                if (list.Count > 0 && list[list.Count - 1].SameContent(b)) return;   // InsertMappingAvailable + BDCT
                list.Add(b);
                Log.Info($"CloneMap: paczka {b.Ctx} ({b.Map.Count} par, {(b.CrossDb ? "z innej bazy" : "ta sama baza")}), komenda '{b.Cmd}'.");
            }
            catch (System.Exception ex) { Log.Error("CloneMap.OnMapping", ex); }
        }

        private static bool IsEntity(ObjectId id, RXClass entityClass)
        {
            try { return id.ObjectClass != null && id.ObjectClass.IsDerivedFrom(entityClass); }
            catch (System.Exception ex) { Log.Error("CloneMap.IsEntity", ex); return false; }
        }

        /// <summary>Wblock z komendy schowka: mapy handle w pliku schowka ↔ handle oryginału, rysunek źródłowy.</summary>
        private static void RecordClipboard(IdMapping m)
        {
            string cmd = CmdNames().ToUpperInvariant();
            if (!(cmd.Contains("COPYCLIP") || cmd.Contains("CUTCLIP") || cmd.Contains("COPYBASE"))) return;   // WBLOCK — nie schowek
            var toOrig = new Dictionary<long, long>();
            var toTemp = new Dictionary<long, long>();
            foreach (IdPair p in m)
            {
                if (!p.IsCloned || p.Key.IsNull || p.Value.IsNull) continue;
                toOrig[p.Value.Handle.Value] = p.Key.Handle.Value;
                toTemp[p.Key.Handle.Value] = p.Value.Handle.Value;
            }
            var src = m.OriginalDatabase;
            _clipTempToOrig = toOrig;
            _clipOrigToTemp = toTemp;
            _clipOriginId = src != null && _docIds.TryGetValue(src.UnmanagedObject, out var sid) ? sid : Guid.Empty;
            Log.Info($"CloneMap: schowek z '{cmd}' — {toOrig.Count} par.");
        }

        private static void OnAborted(object sender, EventArgs e)
        {
            try
            {
                if (!(sender is Database db) || !_batches.TryGetValue(db, out var list) || list.Count == 0) return;
                list.RemoveAt(list.Count - 1);
                Log.Info("CloneMap: DeepCloneAborted — ostatnia paczka odrzucona.");
            }
            catch (System.Exception ex) { Log.Error("CloneMap.OnAborted", ex); }
        }

        private static void OnCommandFailed(object sender, CommandEventArgs e)
        {
            var doc = sender as Document;
            if (doc?.Database == null) return;
            try
            {
                if (SingleBarGripOverrule.OtherCommandInProgress(doc, e?.GlobalCommandName ?? "", out _)) return;
                Forget(doc.Database);
            }
            catch (System.Exception ex) { Log.Error("CloneMap.OnCommandFailed", ex); }
        }

        private static string CmdNames()
        {
            try { return Convert.ToString(Application.GetSystemVariable("CMDNAMES"), CultureInfo.InvariantCulture) ?? ""; }
            catch (System.Exception ex) { Log.Error("CloneMap.CmdNames", ex); return ""; }
        }
    }
}
