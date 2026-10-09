// PORT — testy dymne przeniesienia wybranych zmian kolegi (docs/PLAN_PRZENIESIENIA_2026-10-09.md) w BricsCAD V25.
// Uruchamiane skryptem planc-port.scr (run-planc-port.ps1). Komendy RCSMOKE_PORT_*. Klasa częściowa PlanCFixW1Tests —
// te same helpery (pręty, rozkłady, refleksja, W2: Link / ScanMs / EdCopy); RCSMOKE_PORT_SETUP przełącza plik wyników
// na planc-port-results.txt.
//
// Pasy Y = 20000·k (Y(k) z PlanCFixW2Tests).
// P16A (K3, e7b600d) rozkład B pręta A bez własnego opisu, [11] → opis rozkładu A (jak kopia rozkładu bez opisu);
//      usunięcie B → opis A ZOSTAJE (back-link [16] opisu wskazuje A).
// P16B (K3) kaskada usuwania pręta Z: jego rozkład D ma [11] → opis cudzego rozkładu C → D usunięty, opis C zostaje;
//      kontrola: zwykły pręt (polilinia i blok) z rozkładem i opisem → wszystko usunięte (bez regresji).
// P16C (K3, recenzja 2) w jednej komendzie: COPY rozkładu + opisu, usunięcie rozkładu-kopii przed końcem komendy →
//      opis-kopia usunięty (bez osieroconego opisu), oryginał bez zmian.
// P16D (K3, recenzja diffu 1) stary rysunek (np. -ARRAY sprzed 3b): opis kopii ma [16] → rozkład ORYGINAŁU, a [11] oryginału
//      wskazuje inny opis → usunięcie kopii usuwa jej opis, opis oryginału zostaje.
// IDENT (K4) BarCommands.FindIdenticalBars: identyczny pręt-blok z innym numerem i kopia o tym samym numerze — tak;
//      pręt bez numeru (H12-T), inna długość, inna średnica — nie. ConfirmIdenticalBars w /automation → true bez okna.
// SCRIPT (K4) ScriptMode.IsNonInteractive() = true w /automation.
// COPYMSG1/2 (K4) _.COPY pręta-polilinii / pręta-bloku → komunikat o kopii z tym samym numerem; MIRROR (_Y) → brak.
// 12A/12AU/12AR (K6, #12) _.STRETCH końca pręta-polilinii 00 poniżej max(2d, 50) → pręt sprzed STRETCH (A, obrys,
//      rozkład); _.UNDO 1 i _.REDO → bez zmian. 12B pręt-blok (tylko test). 12C pręt-polilinia obrócony 30°.
// 3C/3CV (K5, P4/P4b) B1 na płycie, COPY płyty z rozkładami i COPY samych rozkładów, ponowne B1 na oryginale →
//      rozkłady kopii zostają; znacznik RC_AUTOREBAR_SLAB kopii → obrys kopii, samych rozkładów → "".

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using Application = Bricscad.ApplicationServices.Application;

namespace BricsCadRc.BrxSmokeTests
{
    public partial class PlanCFixW1Tests
    {
        private static bool _portMode;

        private static readonly List<string> ExpectedStepsPort = new List<string>
        {
            "SETUP", "P16A", "P16B", "P16C", "P16D", "IDENT", "SCRIPT",
            "COPYMSG1", "COPYMSG2", "MIRROR",
            "12A", "12AU", "12AR", "12B", "12CPRE", "12C",
            "3C", "3CV",
        };

        private const string BarCommandsType = "BricsCadRc.Commands.BarCommands";
        private const string CopyWatcherType = "BricsCadRc.Core.BarCopyWatcher";
        private const string SlabApp         = "RC_AUTOREBAR_SLAB";

        private static void SetModePort()
        {
            _w2Mode = false;
            _w4Mode = false;
            _f3Mode = false;
            _portMode = true;
            ResultsFile = "planc-port-results.txt";
            Prefix = "[RC PORT]";
            Title = "PORT — testy dymne przeniesienia zmian kolegi (K3–K6)";
        }

        // ---- stan zestawów ----
        private static W2Set _p16a, _p16cSet, _p16bC, _p16bPoly, _p16bBlock, _p16d;
        private static ObjectId _p16aDistB, _p16bZ, _p16bZLabel, _p16bD, _p16dDistK, _p16dAnnotK;
        private static ObjectId _iA, _iB, _iC, _iD, _iE, _iF;
        private static ObjectId _m1, _m2, _m3;
        private static ObjectId _p12aBar, _p12aDist, _p12bBar, _p12cBar;
        private static List<Point3d> _p12aPre, _p12bPre, _p12cPre;
        private static ObjectId _p3Rect;
        private static int _p3Count;

        private const double P3X0 = 100000, P3Y0 = 300000, P3W = 12000, P3H = 8000;

        // ================================================================
        // SETUP / REPORT
        // ================================================================

        [CommandMethod("RCSMOKE_PORT_SETUP", CommandFlags.Modal)]
        public void PortSetup()
        {
            SetModePort();
            _results.Clear();
            _executed.Clear();
            const string step = "SETUP";
            Run(step, db =>
            {
                Check(step, "wtyczka NaSmartRc wczytana (jedna kopia)", () =>
                {
                    var all = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "NaSmartRc").ToList();
                    if (all.Count != 1) throw new AssertFail("wczytane kopie NaSmartRc: " + all.Count);
                    return "Location=" + Loc(Plugin);
                });
                Check(step, "czyszczenie + RegApp + format Polyline", () =>
                {
                    int n = CleanModelSpace(db);
                    Call(SingleBarType, "EnsureAppIdRegistered", db);
                    Call(BarBlockType, "EnsureAppIdRegistered", db);
                    Call(AnnotType, "EnsureAppIdRegistered", db);
                    SetFormat("Polyline");
                    return "usunięto " + n.ToString(Inv);
                });
                Check(step, "P16A: pręt A + rozkład A + opis A; rozkład B bez opisu, [11] → opis A", () =>
                {
                    _p16a = MakeW2Set(db, "Polyline", 0, Y(1));
                    var (distB, annotB) = Distribute(db, _p16a.Bar, 40, Y(1) + 3040, 3040, Y(1) + 4000);
                    EraseInCode(db, annotB);
                    SetDistAnnot(db, distB, _p16a.Annot);
                    _p16aDistB = distB;
                    return Eq("[11] B", Link(db, distB, DistApp2, 11), Hx(_p16a.Annot)) + "; " + DistAnnotPair(db, _p16a.Dist, _p16a.Annot);
                });
                Check(step, "P16B: zestaw C (pręt-blok); pręt Z (polilinia) + etykieta + rozkład D z [11] → opis C; zestawy kontrolne", () =>
                {
                    _p16bC = MakeW2Set(db, "Block", 8000, Y(2));
                    (_p16bZ, _p16bZLabel) = PlaceBarWithLabel(db, "Polyline", new Def { A = 3000, At = new Point3d(0, Y(2), 0) });
                    var (distD, annotD) = Distribute(db, _p16bZ, 40, Y(2) + 1040, 3040, Y(2) + 2000);
                    EraseInCode(db, annotD);
                    SetDistAnnot(db, distD, _p16bC.Annot);
                    _p16bD = distD;
                    _p16bPoly = MakeW2Set(db, "Polyline", 0, Y(3));
                    _p16bBlock = MakeW2Set(db, "Block", 8000, Y(3));
                    return Eq("[11] D", Link(db, distD, DistApp2, 11), Hx(_p16bC.Annot));
                });
                Check(step, "P16D: zestaw O (polilinia, Y5); rozkład K + opis K, [16] opisu K → rozkład O (stary błąd 3b)", () =>
                {
                    _p16d = MakeW2Set(db, "Polyline", 0, Y(5));
                    (_p16dDistK, _p16dAnnotK) = Distribute(db, _p16d.Bar, 40, Y(5) + 3040, 3040, Y(5) + 4000);
                    SetAnnotSource(db, _p16dAnnotK, _p16d.Dist);
                    return Eq("[16] K", Link(db, _p16dAnnotK, AnnotApp2, 16), Hx(_p16d.Dist))
                         + "; " + Eq("[11] K", Link(db, _p16dDistK, DistApp2, 11), Hx(_p16dAnnotK))
                         + "; " + DistAnnotPair(db, _p16d.Dist, _p16d.Annot);
                });
                Check(step, "P16C: zestaw (polilinia) do COPY rozkładu + opisu", () =>
                {
                    _p16cSet = MakeW2Set(db, "Polyline", 0, Y(4));
                    return _p16cSet.Mark;
                });
                Check(step, "IDENT: pręty 00 H12 A=2750 (A, blok B, kopia C o numerze A, D bez numeru), E A=2800, F H10", () =>
                {
                    _iA = PlaceBar(db, "Polyline", new Def { A = 2750, At = new Point3d(0, Y(6), 0) });
                    int nrA = PosOfBar(db, _iA);
                    _iB = PlaceBar(db, "Block", new Def { A = 2750, At = new Point3d(4000, Y(6), 0) });
                    _iC = PlaceBar(db, "Polyline", new Def { A = 2750, Nr = nrA, At = new Point3d(8000, Y(6), 0) });
                    _iD = PlaceBar(db, "Polyline", new Def { A = 2750, At = new Point3d(12000, Y(6), 0) });
                    WriteBar(db, _iD, b => SetProp(b, "Mark", "H12-T"));
                    _iE = PlaceBar(db, "Polyline", new Def { A = 2800, At = new Point3d(16000, Y(6), 0) });
                    _iF = PlaceBar(db, "Polyline", new Def { D = 10, A = 2750, At = new Point3d(20000, Y(6), 0) });
                    return "A=" + BarMark(db, _iA) + " B=" + BarMark(db, _iB) + " C=" + BarMark(db, _iC) + " D=" + BarMark(db, _iD)
                         + "; " + Int("ExtractPosNr(H12-T)", PosOfBar(db, _iD), 0);
                });
                Check(step, "COPYMSG: pręt-polilinia A=2600 (0, Y7), pręt-blok A=2650 (8000, Y7), MIRROR: polilinia A=2700 (0, Y8)", () =>
                {
                    _m1 = PlaceBar(db, "Polyline", new Def { A = 2600, At = new Point3d(0, Y(7), 0) });
                    _m2 = PlaceBar(db, "Block", new Def { A = 2650, At = new Point3d(8000, Y(7), 0) });
                    _m3 = PlaceBar(db, "Polyline", new Def { A = 2700, At = new Point3d(0, Y(8), 0) });
                    return BarMark(db, _m1) + ", " + BarMark(db, _m2) + ", " + BarMark(db, _m3);
                });
                Check(step, "P12: polilinia 00 A=3000 + etykieta + rozkład (0, Y9); pręt-blok + etykieta (0, Y10); polilinia (0, Y11)", () =>
                {
                    (_p12aBar, _) = PlaceBarWithLabel(db, "Polyline", new Def { A = 3000, At = new Point3d(0, Y(9), 0) });
                    _p12aDist = Distribute(db, _p12aBar, 40, Y(9) + 1040, 3040, Y(9) + 2000).dist;
                    _p12aPre = PolyVerts(db, _p12aBar);
                    (_p12bBar, _) = PlaceBarWithLabel(db, "Block", new Def { A = 3000, At = new Point3d(0, Y(10), 0) });
                    _p12bPre = Outline(db, _p12bBar);
                    _p12cBar = PlaceBar(db, "Polyline", new Def { A = 3000, At = new Point3d(0, Y(11), 0) });
                    return "wierzchołki 12A=" + _p12aPre.Count.ToString(Inv) + " 12B=" + _p12bPre.Count.ToString(Inv);
                });
            });
        }

        [CommandMethod("RCSMOKE_PORT_REPORT", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void PortReport()
        {
            Report();
            _portMode = false;
            SetMode(false);
        }

        // ================================================================
        // K3 — back-link [16] opisu przy usuwaniu
        // ================================================================

        [CommandMethod("RCSMOKE_PORT_P16A", CommandFlags.Modal)]
        public void PortP16A()
        {
            const string step = "P16A";
            Run(step, db =>
            {
                Check(step, "usunięcie rozkładu B (bez opisu, [11] → opis A) → opis A i rozkład A zostają", () =>
                {
                    EraseInCode(db, _p16aDistB);
                    True(_p16aDistB.IsErased, "rozkład B nieusunięty");
                    True(!_p16a.Annot.IsErased, "opis A usunięty razem z cudzym rozkładem B");
                    True(!_p16a.Dist.IsErased, "rozkład A usunięty");
                    return DistAnnotPair(db, _p16a.Dist, _p16a.Annot);
                });
            });
        }

        [CommandMethod("RCSMOKE_PORT_P16B", CommandFlags.Modal)]
        public void PortP16B()
        {
            const string step = "P16B";
            Run(step, db =>
            {
                Check(step, "usunięcie pręta Z → jego rozkład D i etykieta usunięte, opis C (cudzy, [16] → C) zostaje", () =>
                {
                    EraseInCode(db, _p16bZ);
                    True(_p16bD.IsErased, "rozkład D pręta Z nieusunięty");
                    True(_p16bZLabel.IsErased, "etykieta pręta Z nieusunięta");
                    True(!_p16bC.Annot.IsErased, "opis C usunięty kaskadą cudzego pręta");
                    True(!_p16bC.Dist.IsErased && !_p16bC.Bar.IsErased, "zestaw C naruszony");
                    return DistAnnotPair(db, _p16bC.Dist, _p16bC.Annot);
                });
                foreach (var (tag, s) in new[] { ("polilinia", _p16bPoly), ("blok", _p16bBlock) })
                {
                    Check(step, $"kontrola ({tag}): usunięcie pręta → etykieta, rozkład i jego opis usunięte", () =>
                    {
                        EraseInCode(db, s.Bar);
                        True(s.Label.IsErased, "etykieta nieusunięta");
                        True(s.Dist.IsErased, "rozkład nieusunięty");
                        True(s.Annot.IsErased, "opis nieusunięty");
                        return "OK";
                    });
                }
            });
        }

        [CommandMethod("RCSMOKE_PORT_P16C", CommandFlags.Modal)]
        public void PortP16C()
        {
            const string step = "P16C";
            Run(step, db =>
            {
                var ed = Application.DocumentManager.MdiActiveDocument.Editor;
                Check(step, "COPY rozkładu + opisu, usunięcie rozkładu-kopii w tej samej komendzie → opis-kopia usunięty, oryginał bez zmian", () =>
                {
                    Func<Entity, bool> distOrAnnot = e => e is BlockReference && (Has(e, DistApp2) || Has(e, AnnotApp2));
                    var before = new HashSet<ObjectId>(ScanMs(db, distOrAnnot));
                    EdZoom(ed, 4);
                    EdCopy(ed, Pw(-100, Y(4) + 1000), Pw(3500, Y(4) + 2100), new Vector3d(0, 4000, 0));
                    var added = ScanMs(db, distOrAnnot).Where(id => !before.Contains(id)).ToList();
                    var cDist = added.Where(id => Has(Open(db, id), DistApp2)).ToList();
                    var cAnnot = added.Where(id => Has(Open(db, id), AnnotApp2)).ToList();
                    if (cDist.Count != 1 || cAnnot.Count != 1)
                        throw new AssertFail($"po COPY: rozkłady-kopie={cDist.Count}, opisy-kopie={cAnnot.Count} (oczekiwano 1 / 1)");
                    // Ścieżka viaClone: opis-klon ma jeszcze [16] → rozkład ORYGINAŁU (przepięcie dopiero w CommandEnded)
                    Eq("[16] opisu-klonu", Link(db, cAnnot[0], AnnotApp2, 16), Hx(_p16cSet.Dist));
                    EraseInCode(db, cDist[0]);
                    True(cAnnot[0].IsErased, "opis-kopia osierocony (nieusunięty z rozkładem-kopią)");
                    True(!_p16cSet.Annot.IsErased && !_p16cSet.Dist.IsErased, "oryginał naruszony");
                    return DistAnnotPair(db, _p16cSet.Dist, _p16cSet.Annot);
                });
            });
        }

        [CommandMethod("RCSMOKE_PORT_P16D", CommandFlags.Modal)]
        public void PortP16D()
        {
            const string step = "P16D";
            Run(step, db =>
            {
                Check(step, "usunięcie rozkładu K (opis K: [16] → O, a [11] O → opis O) → opis K usunięty, zestaw O zostaje", () =>
                {
                    EraseInCode(db, _p16dDistK);
                    True(_p16dDistK.IsErased, "rozkład K nieusunięty");
                    True(_p16dAnnotK.IsErased, "opis K osierocony (nieusunięty z rozkładem K)");
                    True(!_p16d.Annot.IsErased && !_p16d.Dist.IsErased, "zestaw O naruszony");
                    return DistAnnotPair(db, _p16d.Dist, _p16d.Annot);
                });
            });
        }

        // ================================================================
        // K4 — okno „identyczny pręt”, tryb skryptowy, komunikat po COPY
        // ================================================================

        [CommandMethod("RCSMOKE_PORT_IDENT", CommandFlags.Modal)]
        public void PortIdent()
        {
            const string step = "IDENT";
            Run(step, db =>
            {
                string Sorted(IEnumerable<string> m) => string.Join(",", m.OrderBy(x => x, StringComparer.Ordinal));
                Check(step, "FindIdenticalBars(A) = {B (blok, inny numer), C (kopia, ten sam numer)}", () =>
                {
                    var found = ((IEnumerable)Call(BarCommandsType, "FindIdenticalBars", db, _iA, ReadBar(db, _iA))).Cast<string>().ToList();
                    return Eq("numery", Sorted(found), Sorted(new[] { BarMark(db, _iB), BarMark(db, _iC) }));
                });
                Check(step, "FindIdenticalBars(B) = {A, C}", () =>
                {
                    var found = ((IEnumerable)Call(BarCommandsType, "FindIdenticalBars", db, _iB, ReadBar(db, _iB))).Cast<string>().ToList();
                    return Eq("numery", Sorted(found), Sorted(new[] { BarMark(db, _iA), BarMark(db, _iC) }));
                });
                Check(step, "FindIdenticalBars(E) = {} (inna długość)", () =>
                {
                    var found = ((IEnumerable)Call(BarCommandsType, "FindIdenticalBars", db, _iE, ReadBar(db, _iE))).Cast<string>().ToList();
                    return Int("liczba", found.Count, 0);
                });
                Check(step, "ConfirmIdenticalBars(A) w /automation → true bez okna", () =>
                {
                    var ed = Application.DocumentManager.MdiActiveDocument.Editor;
                    var ok = Call(BarCommandsType, "ConfirmIdenticalBars", ed, db, _iA, ReadBar(db, _iA));
                    return Eq("wynik", ok, true);
                });
            });
        }

        [CommandMethod("RCSMOKE_PORT_SCRIPT", CommandFlags.Modal)]
        public void PortScript()
        {
            const string step = "SCRIPT";
            Run(step, db =>
            {
                Check(step, "ScriptMode.IsNonInteractive() = true (/automation, skrypt .scr)", () =>
                {
                    var r = Call("BricsCadRc.Core.ScriptMode", "IsNonInteractive");
                    return Eq("IsNonInteractive", r, true) + "; args=" + string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
                });
            });
        }

        private static List<string> CopyMessages()
            => ((IEnumerable)Call(CopyWatcherType, "get_LastSameNumberCopyMessages")).Cast<string>().ToList();

        private static string OneCopyMessage(string mark)
        {
            var m = CopyMessages();
            if (m.Count != 1) throw new AssertFail("komunikaty: " + m.Count + " [" + string.Join(" | ", m) + "]");
            True(m[0].Contains("Kopia pręta " + mark + " (1 szt.)"), "komunikat: " + m[0]);
            return m[0];
        }

        [CommandMethod("RCSMOKE_PORT_COPYMSG1", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void PortCopyMsg1()
        {
            const string step = "COPYMSG1";
            Run(step, db => Check(step, "_.COPY pręta-polilinii → komunikat o kopii z tym samym numerem", () => OneCopyMessage(BarMark(db, _m1))));
        }

        [CommandMethod("RCSMOKE_PORT_COPYMSG2", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void PortCopyMsg2()
        {
            const string step = "COPYMSG2";
            Run(step, db => Check(step, "_.COPY pręta-bloku → komunikat o kopii z tym samym numerem", () => OneCopyMessage(BarMark(db, _m2))));
        }

        [CommandMethod("RCSMOKE_PORT_MIRROR", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void PortMirror()
        {
            const string step = "MIRROR";
            Run(step, db => Check(step, "_.MIRROR z usunięciem źródła (_Y) → brak komunikatu", () =>
            {
                var m = CopyMessages();
                return Int("komunikaty", m.Count, 0) + "; pręt " + (_m3.IsErased ? "usunięty" : "zostaje");
            }));
        }

        // ================================================================
        // K6 — #12: odrzucony grip / STRETCH przywraca pręt
        // ================================================================

        private static string P12aState(Database db)
            => Num("A", BarNum(db, _p12aBar, "LengthA"), 3000) + "; " + Pts("obrys", PolyVerts(db, _p12aBar), _p12aPre)
             + "; " + Num("rozkład A", DistLength(db, _p12aDist), 3000);

        [CommandMethod("RCSMOKE_PORT_12A", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void Port12A()
        {
            const string step = "12A";
            Run(step, db => Check(step, "_.STRETCH końca polilinii 00 do 20 mm (< 50) → pręt sprzed STRETCH (A, obrys, rozkład)", () => P12aState(db)));
        }

        [CommandMethod("RCSMOKE_PORT_12AU", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void Port12AU()
        {
            const string step = "12AU";
            Run(step, db => Check(step, "_.UNDO 1 po odrzuconym STRETCH → pręt sprzed STRETCH", () => P12aState(db)));
        }

        [CommandMethod("RCSMOKE_PORT_12AR", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void Port12AR()
        {
            const string step = "12AR";
            Run(step, db => Check(step, "_.REDO → nadal pręt sprzed STRETCH", () => P12aState(db)));
        }

        [CommandMethod("RCSMOKE_PORT_12B", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void Port12B()
        {
            const string step = "12B";
            Run(step, db => Check(step, "_.STRETCH końca pręta-bloku 00 do 20 mm → bez zmian", () =>
                Num("A", BarNum(db, _p12bBar, "LengthA"), 3000) + "; " + Pts("obrys", Outline(db, _p12bBar), _p12bPre)));
        }

        [CommandMethod("RCSMOKE_PORT_12CPRE", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void Port12CPre()
        {
            const string step = "12CPRE";
            Run(step, db => Check(step, "pręt-polilinia po _.ROTATE 30° — obrys zapamiętany", () =>
            {
                _p12cPre = PolyVerts(db, _p12cBar);
                var (ang, _) = AxisFrame(_p12cPre);
                return Num("kąt osi", ang, 30, 1e-6) + "; " + Num("A", BarNum(db, _p12cBar, "LengthA"), 3000);
            }));
        }

        [CommandMethod("RCSMOKE_PORT_12C", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void Port12C()
        {
            const string step = "12C";
            Run(step, db => Check(step, "_.STRETCH końca obróconej polilinii do 20 mm → pręt sprzed STRETCH", () =>
                Num("A", BarNum(db, _p12cBar, "LengthA"), 3000) + "; " + Pts("obrys", PolyVerts(db, _p12cBar), _p12cPre, 1e-6)));
        }

        // ================================================================
        // K5 — przepięcie RC_AUTOREBAR_SLAB przy COPY (P4, P4b)
        // ================================================================

        private static int GenB1(Database db, ObjectId slab)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            return Convert.ToInt32(Call(AutoRebarType, "GenerateLayer", doc, slab, "rebar_bottom", "X", "B1", 10, 200.0, 40.0, false), Inv);
        }

        /// <summary>Rozkłady (RC_BAR_BLOCK), których środek zakresu leży w prostokącie.</summary>
        private static List<ObjectId> DistsIn(Database db, double x0, double y0, double x1, double y1)
            => ScanMs(db, e => e is BlockReference && Has(e, DistApp2)).Where(id =>
            {
                var ext = ExtOf(db, id);
                double cx = (ext.MinPoint.X + ext.MaxPoint.X) / 2, cy = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2;
                return cx > x0 && cx < x1 && cy > y0 && cy < y1;
            }).ToList();

        private static List<ObjectId> P3Dists(Database db, double dy) => DistsIn(db, P3X0, P3Y0 + dy, P3X0 + P3W, P3Y0 + P3H + dy);

        [CommandMethod("RCSMOKE_PORT_3C", CommandFlags.Modal)]
        public void Port3C()
        {
            const string step = "3C";
            Run(step, db =>
            {
                Check(step, "płyta 12000×8000 (100000, 300000) + AutoRebar B1 → rozkłady ze znacznikiem płyty", () =>
                {
                    _p3Rect = ArRect(db, P3X0, P3Y0, P3X0 + P3W, P3Y0 + P3H);
                    int r = GenB1(db, _p3Rect);
                    if (r < 0) throw new AssertFail("GenerateLayer B1 zwrócił " + r);
                    var d = P3Dists(db, 0);
                    _p3Count = d.Count;
                    True(_p3Count > 0, "brak rozkładów B1 na płycie");
                    foreach (var id in d) Eq("znacznik " + id.Handle, Link(db, id, SlabApp, 1), Hx(_p3Rect));
                    return "rozkłady=" + _p3Count.ToString(Inv) + " GenerateLayer=" + r.ToString(Inv);
                });
            });
        }

        [CommandMethod("RCSMOKE_PORT_3CV", CommandFlags.Modal)]
        public void Port3CV()
        {
            const string step = "3CV";
            Run(step, db =>
            {
                List<ObjectId> copies = null, only = null;
                ObjectId copyRect = ObjectId.Null;
                Check(step, "COPY płyty z rozkładami (+30000): obrys kopii i rozkłady kopii", () =>
                {
                    copyRect = ScanMs(db, e => e is Polyline pl && pl.Closed && pl.Layer == "SD-PILED-RAFT")
                        .FirstOrDefault(id => ExtOf(db, id).MinPoint.DistanceTo(new Point3d(P3X0, P3Y0 + 30000, 0)) < 1);
                    if (copyRect.IsNull) throw new AssertFail("brak obrysu kopii płyty");
                    copies = P3Dists(db, 30000);
                    return Int("rozkłady kopii", copies.Count, _p3Count);
                });
                Check(step, "P4: znacznik RC_AUTOREBAR_SLAB rozkładów kopii → obrys kopii", () =>
                {
                    foreach (var id in copies) Eq("znacznik " + id.Handle, Link(db, id, SlabApp, 1), Hx(copyRect));
                    return "OK (" + copies.Count.ToString(Inv) + ")";
                });
                Check(step, "P4b: COPY samych rozkładów (+60000) → znacznik pusty", () =>
                {
                    only = P3Dists(db, 60000);
                    True(only.Count > 0, "brak kopii samych rozkładów");
                    foreach (var id in only) Eq("znacznik " + id.Handle, Link(db, id, SlabApp, 1), "");
                    return "OK (" + only.Count.ToString(Inv) + ")";
                });
                Check(step, "ponowne B1 na ORYGINALE → rozkłady kopii płyty i kopie samych rozkładów zostają", () =>
                {
                    int r = GenB1(db, _p3Rect);
                    if (r < 0) throw new AssertFail("GenerateLayer B1 zwrócił " + r);
                    int lostCopies = (copies ?? new List<ObjectId>()).Count(id => id.IsErased);
                    int lostOnly = (only ?? new List<ObjectId>()).Count(id => id.IsErased);
                    if (lostCopies > 0 || lostOnly > 0)
                        throw new AssertFail($"usunięte: rozkłady kopii płyty {lostCopies}/{copies?.Count}, kopie samych rozkładów {lostOnly}/{only?.Count}");
                    return Int("rozkłady oryginału po ponownym B1", P3Dists(db, 0).Count, _p3Count);
                });
            });
        }

        // ================================================================
        // Pomocnicze PORT
        // ================================================================

        private static Entity Open(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                return (Entity)tr.GetObject(id, OpenMode.ForRead);
        }

        /// <summary>Opis [16] → wskazany rozkład (punktowo, 1005) — symulacja starego rekordu (błąd 3b).</summary>
        private static void SetAnnotSource(Database db, ObjectId annot, ObjectId dist)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var e = (Entity)tr.GetObject(annot, OpenMode.ForWrite);
                TypedValue[] v;
                using (var rb = e.GetXDataForApplication(AnnotApp2)) v = rb.AsArray();
                if (v.Length <= 16) throw new AssertFail("RC_BAR_ANNOT bez [16]");
                v[16] = new TypedValue((int)DxfCode.ExtendedDataHandle, dist.Handle.ToString());
                e.XData = new ResultBuffer(v);
                tr.Commit();
            }
        }

        /// <summary>Rozkład [11] → wskazany opis (zapis BarBlockEngine.WriteXData, 1005).</summary>
        private static void SetDistAnnot(Database db, ObjectId dist, ObjectId annot)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = (BlockReference)tr.GetObject(dist, OpenMode.ForWrite);
                var xd = CallD(BarBlockType, "ReadXData", br);
                SetProp(xd, "AnnotHandle", Hx(annot));
                CallD(BarBlockType, "WriteXData", br, xd);
                tr.Commit();
            }
        }
    }
}
