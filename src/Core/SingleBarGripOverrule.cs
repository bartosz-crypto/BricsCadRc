// Plan C — gripy pręta-bloku (etap 1: próba; krok 3f: długość prętów prostych z propagacją numeru i rozkładów)

using System;
using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Plan C — gripy pręta-bloku (BlockReference z RC_SINGLE_BAR [14] = 2: RC_BAR / AutoRebar w formacie Blok,
    /// pręt testowy RC_TEST_BARBLOCK).
    ///
    /// Historia: GripOverrule na natywnej Polyline nie działa w BricsCAD (IsApplicable nie jest wołane),
    /// na BlockReference działa (AnnotGripOverrule). Dlatego pręt-próba jest blokiem.
    ///
    /// Gripy: [0] początek osi → natywne przesunięcie całego pręta (base), [1] koniec osi → zmiana długości
    /// (tylko wzdłuż osi; reguły SingleBarBlock.ApplyEndLength / BarLengthRules: zaokrąglenie 5 mm, odcinek
    /// krótszy niż max(2d, 50) → zmiana cofnięta). STRETCH oknem: oba punkty → przesunięcie,
    /// tylko koniec → dłuższy/krótszy, tylko początek → początek się przesuwa, koniec stoi.
    ///
    /// Krok 3f (0-POPRAWKI p.1 zniesione): zmiana długości dla każdego pręta-bloku 00/99; pręt z numerem pozycji
    /// po zapisie przechodzi łańcuch BarChangePropagator.Run (numer pozycji, rozkłady, grot i liczba na etykiecie —
    /// jak wzorcowy BarGeometryWatcher.ApplyEndStretch dla polilinii). Pręt gięty: tylko grip [0] (przesunięcie)
    /// i STRETCH całości; częściowy STRETCH odrzucony z komunikatem [RC AUTO] (długość przez RC_EDIT_BAR).
    ///
    /// Wzorzec AnnotGripOverrule: w trakcie dragu (klon LUB prawdziwy obiekt) tylko transient podgląd
    /// i wpis do _pending — ZERO zapisów do bazy. Zapis raz, w CommandEnded, gdy w dokumencie nie trwa już
    /// żadna inna komenda (komenda przezroczysta 'ZOOM w trakcie dragu nie zapisuje i nie czyści pending).
    /// ESC (CommandCancelled) = reset, nic do cofania.
    /// </summary>
    public class SingleBarGripOverrule : GripOverrule
    {
        private const string WatchKey = "SingleBarGripOverrule";
        private static SingleBarGripOverrule _instance;

        // ── Stan dragu ──────────────────────────────────────────────────────
        private struct PendingEdit
        {
            public bool     AtStart;      // przesuwa się początek osi (koniec stoi); inaczej koniec
            public double   Delta;        // przesunięcie końca wzdłuż osi, na zewnątrz = + (przed zaokrągleniem)
            public double   OldLength;    // LengthA z chwili dragu (weryfikacja przy zapisie)
            public Matrix3d Transform;    // BlockTransform z chwili dragu (weryfikacja przy zapisie)
        }

        // Klucz = ObjectId prawdziwego pręta (ObjectId zawiera bazę → pending przypisany do dokumentu)
        private static readonly Dictionary<ObjectId, PendingEdit> _pending = new Dictionary<ObjectId, PendingEdit>();

        // Pręty, dla których pokazano gripy / punkty STRETCH: przekształcenie i długość z tej chwili.
        // Służą WYŁĄCZNIE do jednoznacznego przypisania klonu podglądu do oryginału; przy zapisie
        // stan pręta jest sprawdzany na żywo w bazie (ApplyLength: expectedLength / expectedTransform).
        private static readonly Dictionary<ObjectId, (Matrix3d xf, double len)> _gripOwners
            = new Dictionary<ObjectId, (Matrix3d xf, double len)>();

        // Dokumenty, w których klonu nie dało się jednoznacznie przypisać → cała edycja odrzucona
        private static readonly HashSet<Database> _ambiguous = new HashSet<Database>();

        // Dokumenty, w których odrzucono częściowy STRETCH pręta giętego (bez zmiany długości) → komunikat
        private static readonly Dictionary<Database, string> _refused = new Dictionary<Database, string>();

        // Transienty podglądu per pręt (kilka prętów naraz — T14)
        private static readonly Dictionary<ObjectId, List<Entity>> _previews = new Dictionary<ObjectId, List<Entity>>();

        // Diagnostyka: co już zalogowano w bieżącym dragu
        private static readonly HashSet<string> _loggedDrag = new HashSet<string>();

        private static bool _applying;

        // Poprawka 3f (recenzja, uwaga 1): pręty-bloki dopisane do bazy w trakcie dragu (grip w trybie kopiowania — Ctrl /
        // opcja „Copy”). Zapis tylko do pamięci w ObjectAppended (zero zapisów do bazy): klon → (Mark, LengthA) z chwili dopisania.
        private static readonly Dictionary<ObjectId, (string mark, double len)> _appended
            = new Dictionary<ObjectId, (string mark, double len)>();

        /// <summary>Komunikat odrzucenia edycji długości przy kopiowaniu gripem.</summary>
        internal const string GripCopyRefusedMessage =
            "[RC AUTO] Kopiowanie gripem ze zmianą długości — użyj COPY, potem grip kopii. Pręty bez zmian.";

        // ----------------------------------------------------------------
        // Rejestracja
        // ----------------------------------------------------------------

        public static void Register()
        {
            if (_instance != null) return;
            _instance = new SingleBarGripOverrule();
            Overrule.AddOverrule(RXObject.GetClass(typeof(BlockReference)), _instance, false);
            Overrule.Overruling = true;

            DocumentWatch.Subscribe(WatchKey,
                d =>
                {
                    d.CommandEnded += OnCommandEnded; d.CommandCancelled += OnCommandCancelled; d.CommandFailed += OnCommandCancelled;
                    d.Database.ObjectAppended += OnObjectAppended;
                },
                d =>
                {
                    d.CommandEnded -= OnCommandEnded; d.CommandCancelled -= OnCommandCancelled; d.CommandFailed -= OnCommandCancelled;
                    try { d.Database.ObjectAppended -= OnObjectAppended; }
                    catch (System.Exception ex) { Log.Error("SingleBarGripOverrule.Detach", ex); }
                });
        }

        public static void Unregister()
        {
            if (_instance == null) return;
            DocumentWatch.Unsubscribe(WatchKey);
            ResetDragState(null);
            _gripOwners.Clear();
            Overrule.RemoveOverrule(RXObject.GetClass(typeof(BlockReference)), _instance);
            _instance.Dispose();
            _instance = null;
        }

        // ----------------------------------------------------------------
        // Stan dragu
        // ----------------------------------------------------------------

        /// <summary>Czyści podgląd i stan dragu; db == null → wszystkie dokumenty.</summary>
        internal static void ResetDragState(Database db)
        {
            if (db == null)
            {
                ClearAllPreviews();
                _pending.Clear();
                _ambiguous.Clear();
                _refused.Clear();
                _appended.Clear();
            }
            else
            {
                // Tylko podglądy tej bazy (drag w innym rysunku zostaje)
                foreach (var key in _previews.Keys.Where(k => k.Database == db).ToList())
                    ClearPreview(key);
                foreach (var id in _pending.Keys.Where(k => k.Database == db).ToList())
                    _pending.Remove(id);
                foreach (var id in _appended.Keys.Where(k => k.Database == db).ToList())
                    _appended.Remove(id);
                _ambiguous.Remove(db);
                _ambiguous.Remove(null);   // klon bez aktywnego dokumentu
                _refused.Remove(db);
            }
            _loggedDrag.Clear();
        }

        private static Database ActiveDb()
        {
            try { return Application.DocumentManager.MdiActiveDocument?.Database; }
            catch (System.Exception ex) { Log.Error("SingleBarGripOverrule.ActiveDb", ex); return null; }
        }

        private static string ActiveCommand()
        {
            try { return Application.DocumentManager.MdiActiveDocument?.CommandInProgress ?? ""; }
            catch (System.Exception ex) { Log.Error("SingleBarGripOverrule.ActiveCommand", ex); return "?"; }
        }

        private static void RememberOwner(BlockReference br, BarData bar)
        {
            if (br.ObjectId.IsNull) return;
            if (_gripOwners.Count > 256)
                foreach (var id in _gripOwners.Keys.Where(k => k.IsNull || k.IsErased || !k.IsValid).ToList())
                    _gripOwners.Remove(id);
            _gripOwners[br.ObjectId] = (br.BlockTransform, bar.LengthA);
        }

        /// <summary>
        /// Prawdziwy pręt dla obiektu z dragu. Klon podglądu (ObjectId.IsNull) — jednoznaczne dopasowanie
        /// po AKTUALNYM (odczyt z bazy) BlockTransform i LengthA prętów, dla których pokazano gripy.
        /// Brak albo kilka dopasowań → ObjectId.Null i znacznik odrzucenia edycji w dokumencie.
        /// </summary>
        private static ObjectId ResolveRealId(BlockReference br, BarData bar)
        {
            if (!br.ObjectId.IsNull) return br.ObjectId;

            var db = ActiveDb();
            var xf = br.BlockTransform;
            var matches = new List<ObjectId>();
            if (db != null)
            {
                foreach (var id in _gripOwners.Keys.ToList())
                {
                    if (id.IsNull || id.IsErased || !id.IsValid) { _gripOwners.Remove(id); continue; }
                    if (id.Database != db) continue;
                    if (!TryReadLive(id, out Matrix3d liveXf, out double liveLen))
                    {
                        // Nie do odczytu (np. otwarty do zapisu przez BricsCAD w trakcie dragu) —
                        // ostatni znany stan z GetGripPoints/GetStretchPoints; zapis i tak weryfikuje ApplyLength.
                        if (!_gripOwners.TryGetValue(id, out var snap)) continue;
                        liveXf = snap.xf; liveLen = snap.len;
                    }
                    else if (liveLen < 0)
                    {
                        _gripOwners.Remove(id);   // usunięty albo już nie pręt testowy
                        continue;
                    }
                    else
                        _gripOwners[id] = (liveXf, liveLen);   // odśwież nieaktualny wpis

                    if (Math.Abs(liveLen - bar.LengthA) < 1e-6 && SingleBarBlock.SameTransform(liveXf, xf))
                        matches.Add(id);
                }
            }
            if (matches.Count == 1) return matches[0];

            _ambiguous.Add(db);
            if (_loggedDrag.Add("AMBIG"))
                Log.Info($"[RC AUTO] Klon podglądu nie przypisany jednoznacznie (dopasowań: {matches.Count}) — edycja zostanie odrzucona.");
            return ObjectId.Null;
        }

        /// <summary>
        /// Odczyt stanu pręta z bazy — TYLKO ODCZYT (OpenCloseTransaction bez Commit, bez UpgradeOpen).
        /// true i len &lt; 0 → obiekt usunięty / nie jest prętem testowym. false → odczyt nieudany.
        /// </summary>
        private static bool TryReadLive(ObjectId id, out Matrix3d xf, out double len)
        {
            xf = Matrix3d.Identity;
            len = -1;
            try
            {
                // Bez tr.Commit() — transakcja tylko do odczytu, Dispose = przerwanie bez skutków
                using (var tr = id.Database.TransactionManager.StartOpenCloseTransaction())
                {
                    var live = tr.GetObject(id, OpenMode.ForRead, false) as BlockReference;
                    if (live == null || live.IsErased || !TestBarBlockEngine.IsV2(live)) return true;
                    var liveBar = SingleBarEngine.ReadBarXData(live);
                    if (liveBar == null) return true;
                    xf  = live.BlockTransform;
                    len = liveBar.LengthA;
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                if (_loggedDrag.Add("READ|" + id.ToString()))
                    Log.Error($"SingleBarGripOverrule.TryReadLive {id}", ex);
                return false;
            }
        }

        private static void LogFirst(string kind, BlockReference br)
        {
            bool clone = br.ObjectId.IsNull;
            if (!_loggedDrag.Add(kind + (clone ? "|klon" : "|oryginal"))) return;
            Log.Info($"[RC AUTO] {kind}: pierwsze wywołanie w dragu — " +
                     (clone ? "klon (ObjectId.IsNull)" : "oryginał " + br.ObjectId.Handle.ToString()) +
                     $", komenda: '{ActiveCommand()}'");
        }

        // ----------------------------------------------------------------
        // Podgląd (transienty) — bez zapisów do bazy
        // ----------------------------------------------------------------

        private static void ClearPreview(ObjectId key)
        {
            if (!_previews.TryGetValue(key, out var list)) return;
            _previews.Remove(key);
            EraseTransients(list);
        }

        private static void ClearAllPreviews()
        {
            if (_previews.Count == 0) return;
            var all = _previews.Values.ToList();
            _previews.Clear();
            foreach (var list in all) EraseTransients(list);
        }

        private static void EraseTransients(List<Entity> list)
        {
            var tm = TransientManager.CurrentTransientManager;
            var vpIds = new IntegerCollection();
            foreach (var e in list)
            {
                try { tm.EraseTransient(e, vpIds); }
                catch (System.Exception ex) { Log.Error("SingleBarGripOverrule.EraseTransient", ex); }
                try { e.Dispose(); }
                catch (System.Exception ex) { Log.Error("SingleBarGripOverrule.DisposeTransient", ex); }
            }
        }

        /// <summary>
        /// Obrys pręta po zmianie końca (kształt z bar, reguły BarLengthRules), przekształcony BlockTransform, kolor 4.
        /// Długość odrzucona (za krótki odcinek) → bez podglądu.
        /// </summary>
        private static void DrawPreview(ObjectId key, BlockReference br, BarData bar, bool atStart, double delta)
        {
            ClearPreview(key);
            double? newLength = BarLengthRules.StretchedLength(bar.LengthA, delta, bar.Diameter);
            if (!newLength.HasValue) return;
            double shift = atStart ? bar.LengthA - newLength.Value : 0.0;
            var tmpBar = new BarData
            {
                Diameter  = bar.Diameter,
                ShapeCode = bar.ShapeCode,
                LengthA   = newLength.Value,
                LengthB   = bar.LengthB,
                LengthC   = bar.LengthC,
                LengthD   = bar.LengthD,
                LengthE   = bar.LengthE,
            };
            var list = new List<Entity>();
            var tm = TransientManager.CurrentTransientManager;
            var xf = br.BlockTransform * Matrix3d.Displacement(new Vector3d(shift, 0, 0));
            foreach (var ent in SingleBarBlock.BuildEntities(tmpBar))
            {
                try
                {
                    ent.TransformBy(xf);
                    ent.ColorIndex = 4;
                    tm.AddTransient(ent, TransientDrawingMode.DirectTopmost, 128, new IntegerCollection());
                    list.Add(ent);
                }
                catch (System.Exception ex)
                {
                    Log.Error("SingleBarGripOverrule.DrawPreview", ex);
                    ent.Dispose();
                }
            }
            if (list.Count > 0) _previews[key] = list;
        }

        /// <summary>Zapamiętuje edycję długości (podgląd + _pending). Zero zapisów do bazy.</summary>
        private static void RecordEdit(BlockReference br, BarData bar, bool atStart, double delta)
        {
            var id = ResolveRealId(br, bar);
            if (id.IsNull) return;
            DrawPreview(id, br, bar, atStart, delta);
            _pending[id] = new PendingEdit
            {
                AtStart   = atStart,
                Delta     = delta,
                OldLength = bar.LengthA,
                Transform = br.BlockTransform,
            };
        }

        /// <summary>Grip [1]: długość = A + rzut offsetu (kumulatywnego od startu dragu) na oś pręta.</summary>
        private static void DragEnd(BlockReference br, BarData bar, Vector3d offset)
        {
            LogFirst("GRIP koniec", br);
            double dx = SingleBarBlock.ToLocal(br, offset).X;
            RecordEdit(br, bar, false, dx);
        }

        /// <summary>
        /// Zmiana długości gripem / STRETCH dozwolona: kształt 00/99 (krok 3f: także pręt z numerem pozycji —
        /// propagacja w CommandEnded). Gięte: tylko przesunięcie.
        /// </summary>
        private static bool AllowsLengthEdit(BarData bar)
            => SingleBarBlock.IsLengthGripShape(bar.ShapeCode);

        /// <summary>Zapamiętuje odrzucenie częściowego STRETCH pręta giętego (komunikat w CommandEnded). Zero zapisów do bazy.</summary>
        private static void RecordRefusal(BlockReference br, BarData bar)
        {
            var db = br.ObjectId.IsNull ? ActiveDb() : br.Database;
            if (db == null) return;
            _refused[db] = $"Pręt {bar.Mark}: kształt {bar.ShapeCode} — długość tylko przez RC_EDIT_BAR. Pręt bez zmian.";
            if (_loggedDrag.Add("REFUSED"))
                Log.Info($"[RC AUTO] STRETCH częściowy odrzucony: {_refused[db]}");
        }

        private static bool IsNear(Point3d a, Point3d b) => (a - b).LengthSqrd < 4.0;

        private static IntegerCollection IndexZero()
        {
            var ic = new IntegerCollection();
            ic.Add(0);
            return ic;
        }

        // ----------------------------------------------------------------
        // GripOverrule
        // ----------------------------------------------------------------

        public override bool IsApplicable(RXObject overruledSubject)
        {
            try
            {
                return overruledSubject is BlockReference br && TestBarBlockEngine.IsV2(br);
            }
            catch (System.Exception ex)
            {
                Log.Error("SingleBarGripOverrule.IsApplicable", ex);
                return false;
            }
        }

        public override void GetGripPoints(
            Entity entity,
            Point3dCollection gripPoints,
            IntegerCollection snapModes,
            IntegerCollection geometryIds)
        {
            var br  = entity as BlockReference;
            var bar = br != null ? SingleBarEngine.ReadBarXData(br) : null;
            if (bar == null) { base.GetGripPoints(entity, gripPoints, snapModes, geometryIds); return; }

            RememberOwner(br, bar);
            if (!br.ObjectId.IsNull) ClearPreview(br.ObjectId);

            gripPoints.Add(SingleBarBlock.AxisStartWcs(br));             // [0] początek osi (przesunięcie)
            if (AllowsLengthEdit(bar))
                gripPoints.Add(SingleBarBlock.AxisEndWcs(br, bar));      // [1] koniec osi (długość)
        }

        public override void MoveGripPointsAt(
            Entity entity,
            GripDataCollection grips,
            Vector3d offset,
            MoveGripPointsFlags bitFlags)
        {
            var br  = entity as BlockReference;
            var bar = br != null ? SingleBarEngine.ReadBarXData(br) : null;
            if (bar == null || !AllowsLengthEdit(bar)) { base.MoveGripPointsAt(entity, grips, offset, bitFlags); return; }

            var start = SingleBarBlock.AxisStartWcs(br);
            var end   = SingleBarBlock.AxisEndWcs(br, bar);
            bool hasStart = false, hasEnd = false;
            foreach (GripData gd in grips)
            {
                if (IsNear(gd.GripPoint, end)) hasEnd = true;
                else if (IsNear(gd.GripPoint, start)) hasStart = true;
            }

            if (hasEnd && !hasStart) { DragEnd(br, bar, offset); return; }

            LogFirst("GRIP początek (przesunięcie)", br);
            base.MoveGripPointsAt(entity, grips, offset, bitFlags);
        }

        public override void MoveGripPointsAt(
            Entity entity,
            IntegerCollection indices,
            Vector3d offset)
        {
            var br  = entity as BlockReference;
            var bar = br != null ? SingleBarEngine.ReadBarXData(br) : null;
            if (bar == null) { base.MoveGripPointsAt(entity, indices, offset); return; }

            bool has0 = false, has1 = false;
            foreach (int idx in indices)
            {
                if (idx == 0) has0 = true;
                else if (idx == 1) has1 = true;
            }

            if (has1 && !has0 && AllowsLengthEdit(bar)) { DragEnd(br, bar, offset); return; }

            // Grip [0] (albo oba naraz): natywne przesunięcie całego pręta — punkt wstawienia = grip 0 bazowy
            LogFirst("GRIP początek (przesunięcie)", br);
            base.MoveGripPointsAt(entity, IndexZero(), offset);
        }

        // ----------------------------------------------------------------
        // STRETCH oknem
        // ----------------------------------------------------------------

        public override void GetStretchPoints(Entity entity, Point3dCollection stretchPoints)
        {
            var br  = entity as BlockReference;
            var bar = br != null ? SingleBarEngine.ReadBarXData(br) : null;
            if (bar == null) { base.GetStretchPoints(entity, stretchPoints); return; }

            RememberOwner(br, bar);
            stretchPoints.Add(SingleBarBlock.AxisStartWcs(br));           // [0]
            stretchPoints.Add(SingleBarBlock.AxisEndWcs(br, bar));        // [1]
        }

        public override void MoveStretchPointsAt(Entity entity, IntegerCollection indices, Vector3d offset)
        {
            var br  = entity as BlockReference;
            var bar = br != null ? SingleBarEngine.ReadBarXData(br) : null;
            if (bar == null) { base.MoveStretchPointsAt(entity, indices, offset); return; }

            bool has0 = false, has1 = false;
            foreach (int idx in indices)
            {
                if (idx == 0) has0 = true;
                else if (idx == 1) has1 = true;
            }

            if (has0 != has1 && !AllowsLengthEdit(bar))
            {
                // Pręt gięty: częściowy STRETCH odrzucony (bez przesunięcia i bez zmiany długości)
                LogFirst("STRETCH częściowy (odrzucony)", br);
                RecordRefusal(br, bar);
                return;
            }

            double dx = SingleBarBlock.ToLocal(br, offset).X;
            if (has1 && !has0)
            {
                // Tylko koniec w oknie: początek stoi
                LogFirst("STRETCH koniec", br);
                RecordEdit(br, bar, false, dx);
                return;
            }
            if (has0 && !has1)
            {
                // Tylko początek w oknie: długość zaokrąglana, potem przesunięcie początku —
                // koniec spoza okna nigdy się nie rusza (SingleBarBlock.ApplyEndLength).
                LogFirst("STRETCH początek", br);
                RecordEdit(br, bar, true, -dx);
                return;
            }

            // Oba punkty w oknie: natywne przesunięcie całego pręta
            LogFirst("STRETCH całość (przesunięcie)", br);
            base.MoveStretchPointsAt(entity, IndexZero(), offset);
        }

        // ----------------------------------------------------------------
        // Zdarzenia dokumentu
        // ----------------------------------------------------------------

        private static string NormalizeCmd(string s)
            => (s ?? "").Trim().TrimStart('_', '.', '-', '+', '\'').ToUpperInvariant();

        // Komendy widoku / przezroczyste: ich zakończenie NIGDY nie zapisuje ani nie czyści stanu dragu.
        // (NormalizeCmd zdejmuje '-' i apostrof, więc -VIEW → VIEW.)
        private static readonly HashSet<string> _viewCommands = new HashSet<string>(StringComparer.Ordinal)
        {
            "ZOOM", "PAN", "RTZOOM", "RTPAN", "REDRAW", "REDRAWALL", "REGEN", "REGENALL", "VIEW",
            "3DORBIT", "RTROT", "VIEWRES", "DVIEW", "ORBIT",
        };

        /// <summary>Komenda widoku z listy albo wywołana jako przezroczysta (nazwa z apostrofem).
        /// Używane też przez SingleBarBlockScaleWatcher (Plan C, etap 3e2).</summary>
        internal static bool IsViewOrTransparentCommand(string rawCmd)
        {
            string raw = (rawCmd ?? "").Trim();
            if (raw.StartsWith("'", StringComparison.Ordinal)) return true;
            return _viewCommands.Contains(NormalizeCmd(raw));
        }

        /// <summary>
        /// Czy po zakończeniu komendy endedCmd w dokumencie trwa jeszcze inna komenda
        /// (np. drag gripa, w trakcie którego zakończył się przezroczysty 'ZOOM).
        /// CommandInProgress może jeszcze zawierać kończącą się komendę — jedno jej wystąpienie pomijamy.
        ///
        /// Odstępstwo od planu 6a.1: warunek „CommandInProgress puste” zastąpiono usunięciem nazwy kończącej
        /// się komendy + listą komend widoku (IsViewOrTransparentCommand). Powód: nie wiadomo, czy BricsCAD
        /// w CommandEnded nadal zwraca w CommandInProgress kończącą się komendę — wtedy samo „puste” nigdy
        /// by nie zaszło i grip nigdy by się nie zapisał. Lista komend widoku jest pierwszym zabezpieczeniem
        /// (niezależnym od formatu CommandInProgress), ta metoda — drugim.
        /// </summary>
        internal static bool OtherCommandInProgress(Document doc, string endedCmd, out string cip)
        {
            cip = "";
            try { cip = doc.CommandInProgress ?? ""; }
            catch (System.Exception ex) { Log.Error("SingleBarGripOverrule.CommandInProgress", ex); }

            var tokens = cip.Split(new[] { '\'', ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(NormalizeCmd)
                            .Where(t => t.Length > 0)
                            .ToList();
            int i = tokens.LastIndexOf(NormalizeCmd(endedCmd));
            if (i >= 0) tokens.RemoveAt(i);
            return tokens.Count > 0;
        }

        private static bool HasStateFor(Database db)
            => _ambiguous.Contains(db) || _refused.ContainsKey(db) || _pending.Keys.Any(k => k.Database == db);

        private static void OnCommandEnded(object sender, CommandEventArgs e)
        {
            if (_applying) return;
            var doc = sender as Document;
            var db  = doc?.Database;
            if (db == null) return;
            string cmd = e?.GlobalCommandName ?? "";
            bool keepState = false;

            try
            {
                bool hasState = HasStateFor(db);
                if (IsViewOrTransparentCommand(cmd))
                {
                    keepState = true;
                    if (hasState)
                        Log.Info($"[RC AUTO] CommandEnded '{cmd}' — komenda widoku, podgląd i pending zostają.");
                    return;
                }
                if (OtherCommandInProgress(doc, cmd, out string cip))
                {
                    // Komenda zagnieżdżona (np. 'ZOOM w trakcie dragu): bez zapisu i bez czyszczenia
                    keepState = true;
                    if (hasState)
                        Log.Info($"[RC AUTO] CommandEnded '{cmd}' w trakcie innej komendy ('{cip}') — podgląd i pending zostają.");
                    return;
                }
                if (!hasState) return;

                if (DocumentWatch.IsUndoCommand(cmd))
                {
                    Log.Info($"[RC AUTO] CommandEnded '{cmd}' (undo) — pending odrzucony bez zapisu.");
                    return;
                }

                if (_ambiguous.Contains(db))
                {
                    doc.Editor.WriteMessage("\n[RC AUTO] Edycja odrzucona: podglądu nie dało się jednoznacznie przypisać do pręta " +
                                            "(kilka prętów o tym samym położeniu i długości albo brak gripów). Pręty bez zmian.\n");
                    Log.Info($"[RC AUTO] CommandEnded '{cmd}': edycja odrzucona (niejednoznaczny klon).");
                    return;
                }

                if (_refused.TryGetValue(db, out string refusedMsg))
                    doc.Editor.WriteMessage("\n[RC AUTO] " + refusedMsg + "\n");

                var items = _pending.Where(kv => kv.Key.Database == db).ToList();
                Log.Info($"[RC AUTO] CommandEnded '{cmd}' (CommandInProgress='{cip}'): zapis {items.Count} pręt(ów).");
                using (doc.LockDocument())
                {
                    _applying = true;
                    try
                    {
                        foreach (var kv in items)
                        {
                            if (kv.Key.IsNull || kv.Key.IsErased) continue;
                            try
                            {
                                var p = kv.Value;
                                if (CopiedDuringDrag(db, kv.Key, p.OldLength))
                                {
                                    doc.Editor.WriteMessage("\n" + GripCopyRefusedMessage + "\n");
                                    Log.Info(GripCopyRefusedMessage + $" (uchwyt {kv.Key.Handle}, komenda {cmd})");
                                    continue;
                                }
                                ApplyLengthEdit(doc, kv.Key, p.AtStart, p.Delta, p.OldLength, p.Transform, cmd);
                            }
                            catch (System.Exception ex) { Log.Error($"SingleBarGripOverrule.ApplyLength {kv.Key}", ex); }
                        }
                    }
                    finally { _applying = false; }
                }
            }
            catch (System.Exception ex) { Log.Error("SingleBarGripOverrule.OnCommandEnded", ex); }
            finally
            {
                // Reset po zapisie / odrzuceniu; po komendzie zagnieżdżonej stan zostaje
                if (!keepState) ResetDragState(db);
            }
        }

        /// <summary>
        /// Zapis edycji długości jednego pręta-bloku (z CommandEnded; testy dymne wołają wprost):
        /// SingleBarBlock.ApplyEndLength (reguły BarLengthRules, copy-on-write), ostrzeżenie o nadpisanej długości
        /// całkowitej (jak watcher, poprawka #7), potem — dla pręta z numerem pozycji — BarChangePropagator.Run
        /// (numer, rozkłady, grot i liczba na etykiecie). Pręt bez numeru (H12-T) — bez propagacji.
        /// Wywołujący: dokument zablokowany, poza dragiem i UNDO. Zwraca komunikaty [RC AUTO] (też w linii poleceń i logu).
        /// </summary>
        internal static List<string> ApplyLengthEdit(Document doc, ObjectId id, bool atStart, double delta,
                                                     double? expectedLength, Matrix3d? expectedTransform, string cmd)
        {
            var msgs = new List<string>();
            void Say(string m)
            {
                msgs.Add(m);
                doc?.Editor?.WriteMessage("\n" + m + "\n");
                Log.Info(m + $" (uchwyt {id.Handle}, komenda {cmd})");
            }

            var r = SingleBarBlock.ApplyEndLength(id.Database, id, atStart, delta, expectedLength, expectedTransform);
            if (r.Rejected)
                Say($"[RC AUTO] Pręt {r.Handle}: edycja odrzucona — stan pręta inny niż w trakcie przeciągania.");
            else if (r.TooShort)
                Say($"[RC AUTO] Pręt {r.Mark}: za krótki odcinek ({SingleBarBlock.F(r.AttemptedLength)} mm) — zmiana cofnięta.");
            else if (r.NotLengthShape)
                Say($"[RC AUTO] Pręt {r.Mark}: kształt gięty — długość tylko przez RC_EDIT_BAR. Pręt bez zmian.");
            else if (r.Applied)
            {
                if (r.LengthOverridden)
                    Say(BarLengthRules.OverriddenTotalLengthWarning(r.Mark, r.TotalLength));
                if (SingleBarEngine.ExtractPosNr(r.Mark) > 0)
                {
                    string source = NormalizeCmd(cmd) == "STRETCH" ? "STRETCH" : "grip";
                    var p = BarChangePropagator.Run(doc, id, r.Mark, source);   // komunikat [RC AUTO] wypisuje Run
                    if (p.Done) msgs.Add(p.Message);
                }
                else
                    Say($"[RC AUTO] Pręt {r.Mark}: L {SingleBarBlock.F(r.OldLength)} → {SingleBarBlock.F(r.NewLength)}" +
                        (r.NewBtr ? $", nowy BTR {r.BtrName}" : "") + ".");
            }
            return msgs;
        }

        /// <summary>
        /// ObjectAppended: pręt-blok dopisany do bazy, gdy trwa drag z edycją długości w tej bazie (grip w trybie kopiowania)
        /// → zapamiętanie (Mark, LengthA) klonu. Bez zapisów do bazy; pomijane w trakcie własnego zapisu i UNDO.
        /// </summary>
        private static void OnObjectAppended(object sender, ObjectEventArgs e)
        {
            if (_applying) return;
            try
            {
                if (!(e.DBObject is BlockReference br) || br.IsUndoing) return;
                var db = br.Database;
                if (db == null || !_pending.Keys.Any(k => k.Database == db)) return;
                if (BarEntity.KindOf(br) != BarKind.Block) return;
                var bar = SingleBarEngine.ReadBarXData(br);
                if (bar == null) return;
                _appended[br.ObjectId] = (bar.Mark ?? "", bar.LengthA);
            }
            catch (System.Exception ex) { Log.Error("SingleBarGripOverrule.OnObjectAppended", ex); }
        }

        /// <summary>
        /// Czy w tej komendzie dopisano kopię pręta id (grip w trybie kopiowania): klon o tym samym Mark i długości co pręt
        /// w chwili dragu. Wtedy pending dotyczy ORYGINAŁU (BricsCAD przekazuje drag klonu) — edycja długości odrzucana.
        /// </summary>
        internal static bool CopiedDuringDrag(Database db, ObjectId id, double oldLength)
        {
            if (_appended.Count == 0) return false;
            string mark;
            try
            {
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                    mark = BarEntity.Open(tr, id, OpenMode.ForRead, BarKinds.Block)?.Read()?.Mark;
            }
            catch (System.Exception ex) { Log.Error("SingleBarGripOverrule.CopiedDuringDrag", ex); return false; }
            if (mark == null) return false;
            foreach (var kv in _appended)
            {
                if (kv.Key == id || kv.Key.Database != db || kv.Key.IsErased) continue;
                if (kv.Value.mark == mark && Math.Abs(kv.Value.len - oldLength) < 1e-6) return true;
            }
            return false;
        }

        private static void OnCommandCancelled(object sender, CommandEventArgs e)
        {
            if (_applying) return;
            var doc = sender as Document;
            var db  = doc?.Database;
            if (db == null) return;
            string cmd = e?.GlobalCommandName ?? "";
            try
            {
                if (IsViewOrTransparentCommand(cmd))
                {
                    if (HasStateFor(db))
                        Log.Info($"[RC AUTO] CommandCancelled/Failed '{cmd}' — komenda widoku, podgląd i pending zostają.");
                    return;
                }
                if (OtherCommandInProgress(doc, cmd, out string cip))
                {
                    if (HasStateFor(db))
                        Log.Info($"[RC AUTO] CommandCancelled '{cmd}' w trakcie innej komendy ('{cip}') — pending zostaje.");
                    return;
                }
                if (HasStateFor(db))
                    Log.Info($"[RC AUTO] CommandCancelled '{cmd}' — edycja porzucona (ESC), pręty bez zmian.");
                ResetDragState(db);
            }
            catch (System.Exception ex) { Log.Error("SingleBarGripOverrule.OnCommandCancelled", ex); }
        }
    }
}
