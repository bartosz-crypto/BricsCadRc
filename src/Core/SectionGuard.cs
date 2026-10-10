using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bricscad.ApplicationServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Bezpiecznik przekrojów przed wydaniem dokumentów (docs/PLAN_BEZPIECZNIK_PRZEKROJOW_2026-10-10.md).
    /// Etap 1 — RC_BBS: <see cref="ConfirmBeforeOutput"/> (okno Tak / Nie w KOMENDZIE, chmurki synchronizowane).
    /// Etap 2 — wydruk / PDF / PUBLISH i otwarcie rysunku:
    ///  - <c>DocumentCollection.DocumentLockModeChanged</c> (raz na aplikację): komendy PLOT, -PLOT, PRINT, EXPORTPDF, PUBLISH, -PUBLISH
    ///    (wyniki spiku; nazwy „#…” = odblokowanie i wyrażenia LISP „(…” pomijane); tylko pierwsze zdarzenie komendy najwyższego
    ///    poziomu (flaga per dokument, kasowana w CommandEnded / Cancelled / Failed komendy najwyższego poziomu); `_inGuard`;
    ///    sprawdzenie TYLKO DO ODCZYTU (SectionStore.CheckAll, bez chmurek); e.Document, nie MdiActiveDocument.
    ///    Przekroje nieaktualne: interaktywnie — Veto bez okna w handlerze, pytanie na Idle („Tak, drukuj” / „Nie”), po „Tak”
    ///    ponowienie tej samej komendy przez SendStringToExecute z jednorazową flagą obejścia; tryb bez okien (ScriptMode) —
    ///    tylko komunikat, BEZ Veto (spike: odpowiedzi skryptu stałyby się komendami). Tryb testowy RC_SG_TEST=veto — Veto także
    ///    w trybie bez okien (bez pytania).
    ///  - Otwarcie rysunku: jednorazowe sprawdzenie na Idle po podpięciu dokumentu (DocumentWatch) — rysunek bez RegApp przekrojów:
    ///    szybkie wyjście; komunikat w linii poleceń, bez zapisów (P3) i bez SendStringToExecute.
    /// Czas sprawdzenia w logu; bez cache (pomiar Holbrook: CheckAll 45 ms).
    /// </summary>
    public static class SectionGuard
    {
        public const string Prefix = "[RC SECTION]";

        /// <summary>Komunikaty ostatniego wywołania ConfirmBeforeOutput (testy BRX).</summary>
        public static List<string> LastMessages { get; } = new List<string>();
        /// <summary>Komunikaty ostatniego sprawdzenia przed wydrukiem (testy BRX).</summary>
        public static List<string> LastPrintMessages { get; } = new List<string>();
        /// <summary>Komunikaty ostatniego sprawdzenia przy otwarciu rysunku (testy BRX); OpenCheckCount — liczba sprawdzeń.</summary>
        public static List<string> LastOpenMessages { get; } = new List<string>();
        public static int OpenCheckCount { get; private set; }
        /// <summary>Rysunki czekające na sprawdzenie przy otwarciu (Idle; testy BRX).</summary>
        public static int PendingOpenCount => _pendingOpen.Count;
        /// <summary>Czy rysunek czeka na sprawdzenie przy otwarciu (testy BRX — w /automation Idle nie przychodzi w trakcie skryptu).</summary>
        public static bool IsOpenCheckPending(Document doc) => doc != null && _pendingOpen.Contains(doc);
        /// <summary>Liczba zawetowanych komend wydruku (testy BRX).</summary>
        public static int VetoCount { get; private set; }

        /// <summary>Komendy wydruku z DocumentLockModeChanged (GlobalCommandName — spike etapu 0; -EXPORTPDF nie istnieje w V25).</summary>
        private static readonly HashSet<string> PrintCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PLOT", "-PLOT", "PRINT", "EXPORTPDF", "PUBLISH", "-PUBLISH",
        };

        private static bool _registered, _inGuard, _idleHooked;
        /// <summary>Dokument → komenda wydruku już sprawdzona (trwa); kasowane na końcu komendy najwyższego poziomu.</summary>
        private static readonly Dictionary<Document, string> _checked = new Dictionary<Document, string>();
        /// <summary>Dokument → komenda ponawiana po „Tak, drukuj” (jednorazowe obejście, ważne kilka sekund).</summary>
        private static readonly Dictionary<Document, (string Cmd, DateTime At)> _bypass = new Dictionary<Document, (string Cmd, DateTime At)>();
        private static readonly TimeSpan BypassTtl = TimeSpan.FromSeconds(10);
        /// <summary>
        /// Dokumenty, w których trwa wyrażenie LISP (zdarzenie blokady z nazwą „(…”; kasowane przy „#(…” i na końcu komendy):
        /// Veto zgubiłoby argumenty (command …) — reszta trafiłaby do linii poleceń jako komendy (np. „_N” = QNEW).
        /// </summary>
        private static readonly HashSet<Document> _lisp = new HashSet<Document>();
        private static readonly List<Document> _pendingOpen = new List<Document>();

        private sealed class PendingAsk
        {
            public Document Doc;
            public string Command;
            public string Head;
            public List<string> Lines;
        }
        private static PendingAsk _pendingAsk;

        // ── rejestracja ─────────────────────────────────────────────────

        public static void Register()
        {
            if (_registered) return;
            try
            {
                Application.DocumentManager.DocumentLockModeChanged += OnLockModeChanged;
                DocumentWatch.Subscribe("SectionGuard",
                    d =>
                    {
                        d.CommandEnded += OnCommandEnd; d.CommandCancelled += OnCommandEnd; d.CommandFailed += OnCommandEnd;
                        ScheduleOpenCheck(d);
                    },
                    d =>
                    {
                        d.CommandEnded -= OnCommandEnd; d.CommandCancelled -= OnCommandEnd; d.CommandFailed -= OnCommandEnd;
                        Forget(d);
                    });
                _registered = true;
            }
            catch (System.Exception ex) { Log.Error("SectionGuard.Register", ex); }
        }

        public static void Unregister()
        {
            if (!_registered) return;
            try { Application.DocumentManager.DocumentLockModeChanged -= OnLockModeChanged; }
            catch (System.Exception ex) { Log.Error("SectionGuard.Unregister", ex); }
            DocumentWatch.Unsubscribe("SectionGuard");
            UnhookIdle();
            _checked.Clear(); _bypass.Clear(); _lisp.Clear(); _pendingOpen.Clear(); _pendingAsk = null;
            _registered = false;
        }

        private static void Forget(Document d)
        {
            _checked.Remove(d);
            _bypass.Remove(d);
            _lisp.Remove(d);
            _pendingOpen.Remove(d);
            if (_pendingAsk != null && _pendingAsk.Doc == d) _pendingAsk = null;
        }

        private static string Norm(string cmd) => (cmd ?? "").Trim().TrimStart('_', '.').ToUpperInvariant();

        /// <summary>Przekroje „do zgłoszenia” — jak przed BBS: NIEAKTUALNY, osierocony, uszkodzony, kopia.</summary>
        private static List<SectionInfo> Bad(List<SectionInfo> infos)
            => infos.Where(s => s.Status == SectionStatus.Nieaktualny || s.Status == SectionStatus.Osierocony
                                || s.Status == SectionStatus.Uszkodzony || s.Status == SectionStatus.Kopia).ToList();

        private static List<string> Lines(List<SectionInfo> bad)
            => bad.Select(s => $"{s.Label}: {SectionStore.StatusText(s.Status)}" +
                               (s.Reasons.Count > 0 ? " — " + string.Join("; ", s.Reasons.Take(3)) + (s.Reasons.Count > 3 ? "; …" : "") : ""))
                  .ToList();

        // ── RC_BBS (etap 1) ─────────────────────────────────────────────

        /// <summary>
        /// true = kontynuować (brak nieaktualnych przekrojów, użytkownik wybrał „Tak” albo tryb bez okien). what — co wydajemy
        /// (np. „BBS”). Wołać z komendy (zablokowany dokument — synchronizacja chmurek zapisuje).
        /// </summary>
        public static bool ConfirmBeforeOutput(Document doc, string what)
        {
            LastMessages.Clear();
            if (doc == null) return true;
            var db = doc.Database;
            try
            {
                var sw = Stopwatch.StartNew();
                if (!SectionStore.HasRegApps(db)) return true;
                var infos = SectionStore.CheckAll(db, includeLegacy: false);
                SectionStore.SyncClouds(db, infos);
                sw.Stop();
                Log.Info($"SectionGuard.ConfirmBeforeOutput({what}): przekrojów {infos.Count}, {sw.ElapsedMilliseconds} ms.");

                var bad = Bad(infos);
                if (bad.Count == 0) return true;

                var lines = Lines(bad);
                string head = $"Przekroje nieaktualne / do sprawdzenia ({bad.Count}) — sprawdź RC_SECTION_CHECK:";
                LastMessages.Add(head);
                LastMessages.AddRange(lines);

                if (ScriptMode.IsNonInteractive())
                {
                    doc.Editor.WriteMessage($"\n{Prefix} UWAGA: {head}");
                    foreach (var l in lines) doc.Editor.WriteMessage($"\n{Prefix}   {l}");
                    doc.Editor.WriteMessage($"\n{Prefix} {what}: tryb bez okien — dalej bez pytania.\n");
                    LastMessages.Add(what + ": tryb bez okien — dalej bez pytania.");
                    return true;
                }
                // okno modalne z właścicielem = główne okno BricsCAD (ShowModalWindow), jak inne potwierdzenia wtyczki
                var dlg = new BricsCadRc.Dialogs.TextConfirmDialog("Przekroje nieaktualne",
                    head + "\n\n" + string.Join("\n", lines) + $"\n\nKontynuować ({what})?", "Tak, kontynuuj", "Nie") { Width = 620, Height = 360 };
                bool go = Application.ShowModalWindow(dlg) == true;
                if (!go) doc.Editor.WriteMessage($"\n{Prefix} {what}: przerwano — przekroje nieaktualne.\n");
                return go;
            }
            catch (System.Exception ex)
            {
                // bezpiecznik nie może zablokować wydania dokumentów — błąd do logu, dalej
                Log.Error("SectionGuard.ConfirmBeforeOutput", ex);
                return true;
            }
        }

        // ── wydruk / PDF / PUBLISH (etap 2) ─────────────────────────────

        private static void OnLockModeChanged(object sender, DocumentLockModeChangedEventArgs e)
        {
            string raw = e?.GlobalCommandName ?? "";
            // szybkie wyjście: zdarzenie przychodzi przy KAŻDEJ zmianie blokady (każda komenda, LockDocument)
            if (raw.Length == 0) return;
            if (raw[0] == '(' || raw.StartsWith("#(", StringComparison.Ordinal))
            {
                // wyrażenie LISP: początek → zapamiętaj, koniec („#(…”) → zapomnij
                var d = e.Document;
                if (d != null) { if (raw[0] == '(') _lisp.Add(d); else _lisp.Remove(d); }
                return;
            }
            if (raw[0] == '#') return;
            string cmd = Norm(raw);
            if (!PrintCommands.Contains(cmd) || _inGuard) return;
            var doc = e.Document;
            if (doc == null || _checked.ContainsKey(doc)) return;

            _inGuard = true;
            try
            {
                if (_bypass.TryGetValue(doc, out var bp))
                {
                    _bypass.Remove(doc);
                    if (bp.Cmd == cmd && DateTime.UtcNow - bp.At <= BypassTtl)
                    {
                        _checked[doc] = cmd;
                        Log.Info($"SectionGuard: {cmd} ponowione po „Tak, drukuj” — bez sprawdzania.");
                        return;
                    }
                }
                var sw = Stopwatch.StartNew();
                var db = doc.Database;
                if (!SectionStore.HasRegApps(db)) { _checked[doc] = cmd; return; }
                var infos = SectionStore.CheckAll(db, includeLegacy: false);   // tylko odczyt — chmurki w komendach (CHECK / BBS)
                var bad = Bad(infos);
                sw.Stop();
                Log.Info($"SectionGuard.Print({cmd}): przekrojów {infos.Count}, do zgłoszenia {bad.Count}, {sw.ElapsedMilliseconds} ms.");
                if (bad.Count == 0) { _checked[doc] = cmd; return; }

                var lines = Lines(bad);
                string head = $"Przekroje nieaktualne / do sprawdzenia ({bad.Count}):";
                LastPrintMessages.Clear();
                LastPrintMessages.Add(cmd + ": " + head);
                LastPrintMessages.AddRange(lines);
                string test = Environment.GetEnvironmentVariable("RC_SG_TEST") ?? "";
                bool testVeto = string.Equals(test, "veto", StringComparison.OrdinalIgnoreCase);
                // RC_SG_TEST=interactive — testy BRX: zachowanie interaktywne w /automation (bez okna — niżej)
                bool script = ScriptMode.IsNonInteractive() && !string.Equals(test, "interactive", StringComparison.OrdinalIgnoreCase);
                bool lisp = _lisp.Contains(doc) || LispActive();
                var ed = doc.Editor;
                ed.WriteMessage($"\n{Prefix} UWAGA ({cmd}): {head}");
                foreach (var l in lines) ed.WriteMessage($"\n{Prefix}   {l}");
                if (lisp && !testVeto)
                {
                    // wywołanie z LISP: Veto zgubiłoby argumenty (reszta → komendy, np. „_N” = QNEW) — jak tryb bez okien
                    ed.WriteMessage($"\n{Prefix} {cmd}: wywołanie z LISP — wydruk bez pytania (RC_SECTION_CHECK / RC_SECTION_UPDATE).\n");
                    LastPrintMessages.Add(cmd + ": wywołanie z LISP — wydruk bez pytania.");
                    _checked[doc] = cmd;
                    return;
                }

                if (script && !testVeto)
                {
                    // spike: Veto w skrypcie zamienia dalsze odpowiedzi skryptu w komendy — tylko komunikat
                    ed.WriteMessage($"\n{Prefix} {cmd}: tryb bez okien — wydruk bez pytania (RC_SECTION_CHECK / RC_SECTION_UPDATE).\n");
                    LastPrintMessages.Add(cmd + ": tryb bez okien — wydruk bez pytania.");
                    _checked[doc] = cmd;
                    return;
                }
                e.Veto();
                VetoCount++;
                if (script)
                {
                    ed.WriteMessage($"\n{Prefix} {cmd}: zatrzymano (tryb testowy RC_SG_TEST=veto).\n");
                    LastPrintMessages.Add(cmd + ": zatrzymano (tryb testowy RC_SG_TEST=veto).");
                    return;
                }
                ed.WriteMessage($"\n{Prefix} {cmd}: wstrzymano — pytanie o wydruk mimo nieaktualnych przekrojów.\n");
                LastPrintMessages.Add(cmd + ": wstrzymano — pytanie na Idle.");
                if (test.Length > 0) return;   // tryb testowy: bez okna (w /automation okno zawiesiłoby skrypt)
                // okno NIE w handlerze (spike) — na Idle
                _pendingAsk = new PendingAsk { Doc = doc, Command = cmd, Head = head, Lines = lines };
                HookIdle();
            }
            catch (System.Exception ex)
            {
                // bezpiecznik nie może zablokować wydruku — błąd do logu, komenda idzie dalej (bez Veto)
                Log.Error("SectionGuard.OnLockModeChanged", ex);
                _checked[doc] = cmd;
            }
            finally { _inGuard = false; }
        }

        /// <summary>Koniec komendy najwyższego poziomu → kasowanie flagi „komenda wydruku sprawdzona”.</summary>
        private static void OnCommandEnd(object sender, CommandEventArgs e)
        {
            try
            {
                if (!(sender is Document doc)) return;
                if (SingleBarGripOverrule.OtherCommandInProgress(doc, e?.GlobalCommandName ?? "", out _)) return;
                _checked.Remove(doc);
                _lisp.Remove(doc);
                // obejście „Tak, drukuj” — tylko dla komendy ponawianej zaraz po pytaniu (nie dla późniejszej)
                if (_bypass.TryGetValue(doc, out var bp) && DateTime.UtcNow - bp.At > BypassTtl) _bypass.Remove(doc);
            }
            catch (System.Exception ex)
            {
                Log.Error("SectionGuard.OnCommandEnd", ex);
                if (sender is Document d) { _checked.Remove(d); _lisp.Remove(d); }
            }
        }

        /// <summary>Bit 32 CMDACTIVE — trwa wyrażenie LISP (makr menu bez LISP nie da się wykryć).</summary>
        private static bool LispActive()
        {
            try { return (Convert.ToInt32(Application.GetSystemVariable("CMDACTIVE"), System.Globalization.CultureInfo.InvariantCulture) & 32) != 0; }
            catch (System.Exception ex) { Log.Error("SectionGuard.LispActive", ex); return false; }
        }

        /// <summary>Pytanie po zawetowaniu (Idle, poza handlerem); „Tak” → ponowienie komendy z jednorazowym obejściem.</summary>
        private static void Ask(PendingAsk a)
        {
            var dlg = new BricsCadRc.Dialogs.TextConfirmDialog("Przekroje nieaktualne — wydruk",
                a.Head + "\n\n" + string.Join("\n", a.Lines) +
                $"\n\nPrzekroje można zaktualizować komendą RC_SECTION_UPDATE (szczegóły: RC_SECTION_CHECK).\n\nDrukować mimo to ({a.Command})?",
                "Tak, drukuj", "Nie") { Width = 640, Height = 380 };
            bool go = Application.ShowModalWindow(dlg) == true;
            if (!go)
            {
                a.Doc.Editor.WriteMessage($"\n{Prefix} {a.Command}: anulowano — przekroje nieaktualne (RC_SECTION_UPDATE / RC_SECTION_CHECK).\n");
                return;
            }
            _bypass[a.Doc] = (a.Command, DateTime.UtcNow);
            a.Doc.SendStringToExecute("_." + a.Command + " ", true, false, true);
        }

        // ── otwarcie rysunku (etap 2) ───────────────────────────────────

        private static void ScheduleOpenCheck(Document d)
        {
            if (d == null || _pendingOpen.Contains(d)) return;
            _pendingOpen.Add(d);
            HookIdle();
        }

        /// <summary>
        /// Sprawdzenie przy otwarciu (tylko odczyt, bez zapisów i bez SendStringToExecute): komunikat w linii poleceń, gdy są
        /// przekroje nieaktualne / do sprawdzenia. Rysunek bez RegApp przekrojów — szybkie wyjście.
        /// </summary>
        public static void CheckOnOpen(Document doc)
        {
            if (doc == null) return;
            LastOpenMessages.Clear();
            OpenCheckCount++;
            try
            {
                var sw = Stopwatch.StartNew();
                var db = doc.Database;
                if (!SectionStore.HasRegApps(db)) return;
                var infos = SectionStore.CheckAll(db, includeLegacy: false);
                var bad = Bad(infos);
                sw.Stop();
                Log.Info($"SectionGuard.CheckOnOpen({doc.Name}): przekrojów {infos.Count}, do zgłoszenia {bad.Count}, {sw.ElapsedMilliseconds} ms.");
                if (bad.Count == 0) return;
                var lines = Lines(bad);
                string head = $"Rysunek ma przekroje nieaktualne / do sprawdzenia ({bad.Count}) — RC_SECTION_CHECK (szczegóły), RC_SECTION_UPDATE (aktualizacja):";
                LastOpenMessages.Add(head);
                LastOpenMessages.AddRange(lines);
                var ed = doc.Editor;
                ed.WriteMessage($"\n{Prefix} {head}");
                foreach (var l in lines) ed.WriteMessage($"\n{Prefix}   {l}");
                ed.WriteMessage("\n");
            }
            catch (System.Exception ex) { Log.Error("SectionGuard.CheckOnOpen", ex); }
        }

        // ── Idle ────────────────────────────────────────────────────────

        private static void HookIdle()
        {
            if (_idleHooked) return;
            Application.Idle += OnIdle;
            _idleHooked = true;
        }

        private static void UnhookIdle()
        {
            if (!_idleHooked) return;
            try { Application.Idle -= OnIdle; }
            catch (System.Exception ex) { Log.Error("SectionGuard.UnhookIdle", ex); }
            _idleHooked = false;
        }

        private static void OnIdle(object sender, EventArgs e) => ProcessPending();

        /// <summary>
        /// Obsługa Idle: pytanie o zawetowany wydruk i sprawdzenie przy otwarciu aktywnego rysunku. Publiczne dla testów BRX
        /// (w /automation Idle przychodzi dopiero po skrypcie) — ta sama procedura, którą woła handler Idle.
        /// </summary>
        public static void ProcessPending()
        {
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (_pendingAsk != null)
                {
                    var a = _pendingAsk;
                    _pendingAsk = null;
                    if (a.Doc == doc) Ask(a);
                    else
                    {
                        Log.Info($"SectionGuard: pytanie o {a.Command} pominięte — rysunek nie jest aktywny.");
                        try { a.Doc.Editor.WriteMessage($"\n{Prefix} {a.Command}: nie wydrukowano — przekroje nieaktualne, a rysunek przestał być aktywny; uruchom {a.Command} ponownie.\n"); }
                        catch (System.Exception ex) { Log.Error("SectionGuard.ProcessPending.Msg", ex); }
                    }
                }
                // otwarcie: tylko rysunek aktywny (komunikat w jego linii poleceń); inne czekają, aż staną się aktywne
                if (doc != null && _pendingOpen.Remove(doc)) CheckOnOpen(doc);
            }
            catch (System.Exception ex) { Log.Error("SectionGuard.OnIdle", ex); }
            finally
            {
                if (_pendingAsk == null && _pendingOpen.Count == 0) UnhookIdle();
            }
        }
    }
}
