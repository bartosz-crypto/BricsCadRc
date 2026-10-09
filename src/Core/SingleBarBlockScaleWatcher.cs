using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Plan C, etap 3e2 (0-POPRAWKI p.2): automatyczna naprawa SCALE pręta-bloku v2 (jak wzorzec — watcher prętów-polilinii
    /// odbudowuje zły obrys z parametrów).
    ///
    ///   1. Database.ObjectModified — BlockReference z KindOf == Block trafia do kolejki (bez zapisów, bez transakcji;
    ///      pomijane: IsUndoing, usunięte, własne zmiany — licznik reentrancji).
    ///   2. Document.CommandEnded — po zakończeniu komendy (nie U/UNDO/REDO, nie komenda widoku / przezroczysta /
    ///      zagnieżdżona) każdy pręt-blok z kolejki ze skalą ≠ ±1 dostaje ScaleFactors (±1, ±1, 1) ze znakiem,
    ///      punkt wstawienia (axis[0]) bez zmian, + SingleBarBlock.Rebuild z XData. Komunikat [RC AUTO].
    ///      Zmiana w CommandEnded należy do tej samej grupy UNDO co komenda (jeden Ctrl+Z; Ctrl+Y odtwarza stan naprawiony).
    ///
    /// XData (długość) bez zmian — pręt wraca do wymiarów z danych, więc rozkłady i numer pozycji się nie zmieniają.
    /// Pręty-polilinie — bez zmian (BarGeometryWatcher).
    /// </summary>
    internal static class SingleBarBlockScaleWatcher
    {
        private const string WatchKey = "SingleBarBlockScaleWatcher";

        private static bool _active;
        private static readonly HashSet<ObjectId> _pending = new HashSet<ObjectId>();

        // Licznik reentrancji: > 0, gdy watcher sam zapisuje (naprawa skali) — własne zmiany nie wracają do kolejki
        private static int _depth;

        public static void Register()
        {
            if (_active) return;
            DocumentWatch.Subscribe(WatchKey,
                d =>
                {
                    d.Database.ObjectModified += OnObjectModified;
                    d.CommandEnded            += OnCommandEnded;
                    d.CommandCancelled        += OnCommandCancelled;
                    d.CommandFailed           += OnCommandCancelled;
                },
                d =>
                {
                    try { d.Database.ObjectModified -= OnObjectModified; }
                    catch (System.Exception ex) { Log.Error("SingleBarBlockScaleWatcher.Detach", ex); }
                    d.CommandEnded     -= OnCommandEnded;
                    d.CommandCancelled -= OnCommandCancelled;
                    d.CommandFailed    -= OnCommandCancelled;
                });
            _active = true;
        }

        public static void Unregister()
        {
            if (!_active) return;
            DocumentWatch.Unsubscribe(WatchKey);
            _pending.Clear();
            _active = false;
        }

        // ----------------------------------------------------------------
        // ObjectModified — tylko kolejka (zero zapisów do bazy)
        // ----------------------------------------------------------------
        private static void OnObjectModified(object sender, ObjectEventArgs e)
        {
            if (_depth > 0) return;
            try
            {
                if (!(e.DBObject is BlockReference br) || br.IsUndoing || br.IsErased) return;
                if (BarEntity.KindOf(br) != BarKind.Block) return;
                _pending.Add(br.ObjectId);
            }
            catch (System.Exception ex) { Log.Error("SingleBarBlockScaleWatcher.OnObjectModified", ex); }
        }

        private static void Discard(Database db) => _pending.RemoveWhere(id => id.Database == db);

        private static void OnCommandCancelled(object sender, CommandEventArgs e)
        {
            if (_depth > 0) return;
            var doc = sender as Document;
            var db  = doc?.Database;
            if (db == null) return;
            try
            {
                string cmd = e?.GlobalCommandName ?? "";
                // Komenda zagnieżdżona (np. 'ZOOM w trakcie innej) — kolejka zostaje do końca właściwej komendy
                if (SingleBarGripOverrule.IsViewOrTransparentCommand(cmd)
                    || SingleBarGripOverrule.OtherCommandInProgress(doc, cmd, out _)) return;
                Discard(db);
            }
            catch (System.Exception ex) { Log.Error("SingleBarBlockScaleWatcher.OnCommandCancelled", ex); }
        }

        // ----------------------------------------------------------------
        // CommandEnded — naprawa skali po zakończeniu komendy
        // ----------------------------------------------------------------
        private static void OnCommandEnded(object sender, CommandEventArgs e)
        {
            if (_depth > 0) return;
            var doc = sender as Document;
            var db  = doc?.Database;
            if (db == null) return;
            string cmd = e?.GlobalCommandName ?? "";

            try
            {
                if (!_pending.Any(id => id.Database == db)) return;

                // Po U/UNDO/REDO niczego nie poprawiamy (zepsułoby REDO) — UNDO sam przywraca stan
                if (DocumentWatch.IsUndoCommand(cmd)) { Discard(db); return; }

                // Komenda widoku / przezroczysta / zagnieżdżona — naprawa dopiero po właściwej komendzie
                if (SingleBarGripOverrule.IsViewOrTransparentCommand(cmd)
                    || SingleBarGripOverrule.OtherCommandInProgress(doc, cmd, out _)) return;

                var ids = _pending.Where(id => id.Database == db).ToList();
                Discard(db);

                using (doc.LockDocument())
                {
                    _depth++;
                    try
                    {
                        foreach (var id in ids)
                        {
                            try { RepairOne(doc, db, id, cmd); }
                            catch (System.Exception ex) { Log.Error($"SingleBarBlockScaleWatcher.Repair {id}", ex); }
                        }
                    }
                    finally { _depth--; }
                }
            }
            catch (System.Exception ex) { Log.Error("SingleBarBlockScaleWatcher.OnCommandEnded", ex); }
        }

        /// <summary>Pręt-blok ze skalą ≠ ±1 → skala (±1, ±1, 1) + Rebuild z XData. Skala ±1 — bez zapisu.</summary>
        private static void RepairOne(Document doc, Database db, ObjectId id, string cmd)
        {
            if (id.IsNull || id.IsErased) return;
            string msg;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var be  = BarEntity.Open(tr, id, OpenMode.ForRead, BarKinds.Block);
                var br  = be?.Entity as BlockReference;
                var bar = be?.Read();
                if (br == null || bar == null || SingleBarBlock.IsUnitScale(br.ScaleFactors))
                {
                    tr.Commit();
                    return;
                }
                string oldScale = SingleBarBlock.ScaleText(br.ScaleFactors);
                SingleBarBlock.RepairScale(tr, br, bar);
                tr.Commit();

                string dims = string.Join("/", new[] { bar.LengthA, bar.LengthB, bar.LengthC, bar.LengthD, bar.LengthE }
                                                .Where(v => v > 0).Select(v => SingleBarBlock.F(v)));
                msg = $"[RC AUTO] Pręt {bar.Mark}: skala {oldScale} cofnięta — pręt odbudowany z danych ({dims} mm).";
            }
            doc.Editor?.WriteMessage("\n" + msg + "\n");
            Log.Info(msg + $" (uchwyt {id.Handle}, komenda {cmd})");
        }
    }
}
