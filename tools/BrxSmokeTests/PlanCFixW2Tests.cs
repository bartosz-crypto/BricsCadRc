// W2 — testy dymne mapy klonowania (docs/PLAN_POPRAWKI_W1_W2.md 4, 6.2; poprawki #3, #6, 3b) w BricsCAD V25.
// Uruchamiane skryptem planc-w2.scr (run-planc-w2.ps1). Komendy RCSMOKE_W2_*. Klasa częściowa PlanCFixW1Tests —
// te same helpery (pręty, rozkłady, refleksja); RCSMOKE_W2_SETUP przełącza plik wyników na planc-w2-results.txt.
//
// Pasy Y = 20000·k; zestaw = pręt 00 H12 A=3000 + etykieta + rozkład B1 + opis: polilinia w x=0, pręt-blok w x=8000.
//  1  COPY zestawów (polilinia + blok): [13]/[1], [18], [11]/[16] kopii → kopie; etykiety n / n; paczki CloneMap = 0
//  2  COPY rozkładu + opisu: [18] kopii → oryginał (bez zmian), opis ↔ rozkład kopii, etykieta oryginału 2n
//  3  COPY samego pręta: [13] kopii = "", etykieta oryginału nietknięta
//  4  MIRROR N zestawów: pary z mapy + normalizacja odbicia (skala +1) i przebudowa opisu (0-POPRAWKI p.1)
//  5  MIRROR Y (BricsCAD V25 odbija w miejscu): nic nie usunięte, powiązania bez zmian
//  6  _-ARRAY 1×3: każdy rozkład → swój pręt, każdy opis ↔ swój rozkład, etykiety n (3b)
//  7  #6: A bez etykiety, B z etykietą (grot < 50 mm od A); COPY A, B, etykiety → etykieta przy kopii B (polilinia i blok)
//  8  schowek i pliki: 8a COPYCLIP+PASTECLIP rozkładu bez pręta → [18] = oryginał (Ctrl+V = COPY);
//     8b COPYCLIP → -WBLOCK innych obiektów → PASTECLIP: [18] zachowane (0-POPRAWKI p.3);
//     8c -INSERT * pliku z rozkładem bez pręta (inny rysunek) → [18] = "", opis ↔ rozkład; 8d -WBLOCK/-INSERT * zestawu
//  9  rekordy 1000 (stary tekst) → po COPY 1005 wskazujące kopie
// 10  44 COPY: RC_BAR_LINK kopii → kopia (1 + 1); Apply oryginału nie rusza pierścienia kopii
// 11  _.U / _.REDO po COPY
// 13  DeepCloneObjects (kontekst Objects, jak EnsureUniqueBtr) nie tworzy paczki
// 15  COPY z przezroczystym 'ZOOM (0-POPRAWKI p.5)
// 16  COPY wielokrotne + „_U” drugiej kopii: opisy oryginałów NIE usunięte (D8), pierwsza kopia przepięta
// 17 zagnieżdżone COPY i MIRROR (Editor.Command): BTR kopii unikalne, odbicie znormalizowane (runda 1, uwaga 2)
// 19 dwa COPY w jednej komendzie (A→B, B→C): C przepięte przez łańcuch paczek (uwaga 3)
// 20 COPYCLIP + PASTECLIP + ERASE wklejonego pręta w jednej komendzie: rozkład i opis usunięte (uwaga 4)
// 21 COPYCLIP, potem -INSERT * pliku z tymi samymi obiektami (bez pręta): [18] pusty — INSERT bez mapy schowka (uwaga 5)
// 22 -INSERT * kompletu z powiązaniami tekstowymi 1000: powiązania poprawne, zapis 1005 (uwaga 1)
// 23 COPYCLIP → SAVEAS (Database.SaveAs) → PASTECLIP: [18] → istniejący pręt (runda 2)
// 24 COPYCLIP kompletu 1000 → zamknięcie źródła (OnDocumentClosing) → PASTECLIP: powiązania poprawne (runda 2)
// (12 „dwa rysunki” — schowek do innego rysunku tylko ręcznie; automatycznie: -INSERT z pliku, 8c/8d. 14 — E3 S13/S16/S17/S13P.)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using Application = Bricscad.ApplicationServices.Application;

namespace BricsCadRc.BrxSmokeTests
{
    public partial class PlanCFixW1Tests
    {
        private static bool _w2Mode;

        private static readonly List<string> ExpectedStepsW2 = new List<string>
        {
            "SETUP",
            "1V", "2V", "3V", "4V", "5V", "6V", "7V",
            "8AV", "8BV", "8CV", "8DV",
            "9V", "10V", "10EDIT", "10E", "11V", "11U", "11R", "13", "15V", "16V",
            "17", "17V", "19", "19V", "20", "20V", "21FILE", "21V", "22FILE", "22V",
            "23SAVE", "23V", "24CLOSE", "24V",
        };

        private const string DistApp2 = "RC_BAR_BLOCK", AnnotApp2 = "RC_BAR_ANNOT", LabelApp2 = "RC_BAR_LABEL";

        private static void SetMode(bool w2)
        {
            _w4Mode = false;   // PlanCW4Tests.cs — tryb W4 włącza SetModeW4
            _f3Mode = false;   // PlanCStage3fTests.cs — tryb 3f włącza SetModeF3
            _w2Mode = w2;
            ResultsFile = w2 ? "planc-w2-results.txt" : "planc-w1-results.txt";
            Prefix = w2 ? "[RC W2]" : "[RC W1]";
            Title = w2 ? "W2 — testy dymne mapy klonowania" : "W1 — testy dymne poprawek wzorca";
        }

        private sealed class W2Set
        {
            public ObjectId Bar, Label, Dist, Annot;
            public string Mark = "";
            public int N;          // liczba na etykiecie po utworzeniu
            public Extents3d AnnotExt;
        }

        private static readonly Dictionary<string, W2Set> _w2 = new Dictionary<string, W2Set>();
        private static ObjectId _w2RingA, _w2RingCopy;

        private static double Y(int k) => 20000.0 * k;

        // ================================================================
        // SETUP — wszystkie pasy naraz (komendy skryptu działają na współrzędnych pasów)
        // ================================================================

        [CommandMethod("RCSMOKE_W2_SETUP", CommandFlags.Modal)]
        public void W2Setup()
        {
            SetMode(true);
            _results.Clear();
            _executed.Clear();
            _w2.Clear();
            const string step = "SETUP";
            Run(step, db =>
            {
                Check(step, "wtyczka NaSmartRc wczytana (jedna kopia), CloneMap obecny", () =>
                {
                    var all = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "NaSmartRc").ToList();
                    if (all.Count != 1) throw new AssertFail("wczytane kopie NaSmartRc: " + all.Count);
                    Plugin.GetType("BricsCadRc.Core.CloneMap", true);
                    return "Location=" + Loc(Plugin);
                });
                Check(step, "czyszczenie + RegApp + format Polyline", () =>
                {
                    int n = CleanModelSpace(db);
                    Call(SingleBarType, "EnsureAppIdRegistered", db);
                    Call(BarBlockType, "EnsureAppIdRegistered", db);
                    Call(AnnotType, "EnsureAppIdRegistered", db);
                    SetFormat("Polyline");
                    foreach (var t in new[] { "w2t_wb1.dwg", "w2t_wb2.dwg", "w2t_wb3.dwg", "w2t_wb4.dwg" })
                    {
                        string p = Path.Combine(Path.GetTempPath(), t);
                        if (File.Exists(p)) File.Delete(p);
                    }
                    return "usunięto " + n.ToString(Inv);
                });
                foreach (int k in new[] { 1, 2, 3, 4, 5, 8, 9, 10, 11, 13, 14, 15, 17, 18, 19, 20, 21, 22, 23, 24 })
                {
                    int kk = k;
                    Check(step, $"pas {kk}: zestaw polilinii (x=0) i pręta-bloku (x=8000)", () =>
                    {
                        _w2["P" + kk] = MakeW2Set(db, "Polyline", 0, Y(kk));
                        _w2["B" + kk] = MakeW2Set(db, "Block", 8000, Y(kk));
                        return "P=" + _w2["P" + kk].Mark + " B=" + _w2["B" + kk].Mark + " n=" + _w2["P" + kk].N.ToString(Inv);
                    });
                }
                Check(step, "pas 6: zestaw polilinii (ARRAY)", () => { _w2["P6"] = MakeW2Set(db, "Polyline", 0, Y(6)); return _w2["P6"].Mark; });
                Check(step, "pas 7 (#6): A bez etykiety, B z etykietą 40 mm nad A (polilinia x=0, blok x=8000)", () =>
                {
                    foreach (var (fmt, x0, tag) in new[] { ("Polyline", 0.0, "P"), ("Block", 8000.0, "B") })
                    {
                        var a = PlaceBar(db, fmt, new Def { A = 3000, At = new Point3d(x0, Y(7), 0) });
                        var (b, lb) = PlaceBarWithLabel(db, fmt, new Def { A = 3000, At = new Point3d(x0, Y(7) + 40, 0) });
                        _w2["7A" + tag] = new W2Set { Bar = a, Mark = BarMark(db, a) };
                        _w2["7B" + tag] = new W2Set { Bar = b, Label = lb, Mark = BarMark(db, b) };
                    }
                    return "OK";
                });
                Check(step, "pas 9: rekordy w starym formacie 1000 (zestaw polilinii x=0 — [13], [1], [11], [16], [18] jako tekst)", () =>
                {
                    // pas 9 ma już zestawy (P9/B9 — używane w 8b); stary format na osobnym zestawie w pasie 12
                    _w2["P12"] = MakeW2Set(db, "Polyline", 0, Y(12));
                    var s = _w2["P12"];
                    ToText(db, s.Bar, XApp, 13); ToText(db, s.Label, LabelApp2, 1);
                    ToText(db, s.Dist, DistApp2, 11); ToText(db, s.Dist, DistApp2, 18); ToText(db, s.Annot, AnnotApp2, 16);
                    return "P12=" + s.Mark + " " + LinkCode(db, s.Dist, DistApp2, 18);
                });
                Check(step, "pas 24: zestaw polilinii z powiązaniami w starym formacie 1000 (runda 2)", () =>
                {
                    var s24 = _w2["P24"];
                    ToText(db, s24.Bar, XApp, 13); ToText(db, s24.Label, LabelApp2, 1);
                    ToText(db, s24.Dist, DistApp2, 11); ToText(db, s24.Dist, DistApp2, 18); ToText(db, s24.Annot, AnnotApp2, 16);
                    return LinkCode(db, s24.Dist, DistApp2, 18).ToString(Inv);
                });
                Check(step, "pas 16: pierścień 44 A=300 (polilinia, x=0)", () =>
                {
                    _w2RingA = PlaceBar(db, "Polyline", new Def { Code = W4Compat.RingCode, A = 300, At = new Point3d(0, Y(16), 0) });
                    return Int("RC_BAR_LINK", LinksOf(db, _w2RingA).Count, 1);
                });
            });
        }

        private static W2Set MakeW2Set(Database db, string fmt, double x0, double y0)
        {
            var s = new W2Set();
            (s.Bar, s.Label) = PlaceBarWithLabel(db, fmt, new Def { A = 3000, At = new Point3d(x0, y0, 0) });
            (s.Dist, s.Annot) = Distribute(db, s.Bar, x0 + 40, y0 + 1040, x0 + 3040, y0 + 2000);
            s.Mark = BarMark(db, s.Bar);
            s.N = LeadInt(LabelText(db, s.Label));
            s.AnnotExt = ExtOf(db, s.Annot);
            return s;
        }

        // ================================================================
        // 1 — COPY zestawów
        // ================================================================

        [CommandMethod("RCSMOKE_W2_1V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T1V()
        {
            const string step = "1V";
            Run(step, db =>
            {
                foreach (var tag in new[] { "P1", "B1" }) CheckFullCopy(db, step, tag, Y(1), 1);
                Check(step, "#13: po COPY brak oczekujących paczek CloneMap (Take + EnsureUniqueBtr bez paczki)", () =>
                    Int("PendingCount", (int)Call("BricsCadRc.Core.CloneMap", "PendingCount", db), 0));
            });
        }

        /// <summary>Kopie zestawu (copies szt.): każda kopia — pręt ↔ etykieta, rozkład → pręt kopii, opis ↔ rozkład; etykiety n.</summary>
        private static void CheckFullCopy(Database db, string step, string tag, double y0, int copies)
        {
            var o = _w2[tag];
            Check(step, $"{tag}: oryginał bez zmian (pręt ↔ etykieta, rozkład → oryginał, opis ↔ rozkład, etykieta n)", () =>
                BarLabelPair(db, o.Bar, o.Label) + "; " + Eq("[18]", Link(db, o.Dist, DistApp2, 18), Hx(o.Bar))
                + "; " + DistAnnotPair(db, o.Dist, o.Annot) + "; " + Int("liczba na etykiecie", LeadInt(LabelText(db, o.Label)), o.N));
            Check(step, $"{tag}: {copies} kopi(e) — pręt ↔ własna etykieta, rozkład → pręt kopii, opis ↔ rozkład kopii, etykieta n", () =>
            {
                var bars = BarsByMark(db, o.Mark).Where(b => b != o.Bar).ToList();
                Int("kopie pręta " + o.Mark, bars.Count, copies);
                var r = new List<string>();
                foreach (var cb in bars)
                {
                    string l = Link(db, cb, XApp, 13);
                    var lid = IdOf(db, l);
                    if (lid.IsNull || lid == o.Label) throw new AssertFail($"kopia {cb.Handle}: [13]={l} (etykieta oryginału albo brak)");
                    r.Add(BarLabelPair(db, cb, lid));
                    var dists = DistsPointingTo(db, cb);
                    Int($"rozkłady kopii {cb.Handle}", dists.Count, 1);
                    var aid = IdOf(db, Link(db, dists[0], DistApp2, 11));
                    if (aid.IsNull || aid == o.Annot) throw new AssertFail($"rozkład kopii {dists[0].Handle}: [11] → {Link(db, dists[0], DistApp2, 11)}");
                    r.Add(DistAnnotPair(db, dists[0], aid));
                    r.Add(Int("liczba na etykiecie kopii", LeadInt(LabelText(db, lid)), o.N));
                }
                return string.Join("; ", r);
            });
        }

        // ================================================================
        // 2 — COPY rozkładu + opisu
        // ================================================================

        [CommandMethod("RCSMOKE_W2_2V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T2V()
        {
            const string step = "2V";
            Run(step, db =>
            {
                var o = _w2["P2"];
                Check(step, "kopia rozkładu: [18] → oryginał (bez zmian), [11] ↔ kopia opisu; etykieta oryginału 2n", () =>
                {
                    var d = DistsPointingTo(db, o.Bar);
                    Int("rozkłady oryginału", d.Count, 2);
                    var cd = d.First(x => x != o.Dist);
                    var aid = IdOf(db, Link(db, cd, DistApp2, 11));
                    if (aid.IsNull || aid == o.Annot) throw new AssertFail("[11] kopii → " + Link(db, cd, DistApp2, 11));
                    return DistAnnotPair(db, cd, aid) + "; " + DistAnnotPair(db, o.Dist, o.Annot)
                           + "; " + Int("liczba na etykiecie", LeadInt(LabelText(db, o.Label)), 2 * o.N);
                });
            });
        }

        // ================================================================
        // 3 — COPY samego pręta
        // ================================================================

        [CommandMethod("RCSMOKE_W2_3V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T3V()
        {
            const string step = "3V";
            Run(step, db =>
            {
                foreach (var tag in new[] { "P3", "B3" })
                {
                    var o = _w2[tag];
                    Check(step, $"{tag}: kopia pręta bez etykiety ([13] = \"\"), oryginał ↔ etykieta", () =>
                    {
                        var c = BarsByMark(db, o.Mark).Where(b => b != o.Bar).ToList();
                        Int("kopie", c.Count, 1);
                        return Eq("[13] kopii", Link(db, c[0], XApp, 13), "") + "; " + BarLabelPair(db, o.Bar, o.Label);
                    });
                }
            });
        }

        // ================================================================
        // 4 — MIRROR N, 5 — MIRROR Y
        // ================================================================

        [CommandMethod("RCSMOKE_W2_4V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T4V()
        {
            const string step = "4V";
            Run(step, db =>
            {
                foreach (var tag in new[] { "P4", "B4" })
                {
                    CheckFullCopy(db, step, tag, Y(4), 1);
                    var o = _w2[tag];
                    Check(step, $"{tag}: odbicie — pręt kopii odbity; rozkład i opis kopii znormalizowane (skala +1, obrót 0), " +
                                "opis w odbiciu lustrzanym miejsca oryginału (0-POPRAWKI p.1)", () =>
                    {
                        var cb = BarsByMark(db, o.Mark).First(b => b != o.Bar);
                        var fr = AxisFrame(Outline(db, cb));
                        var cd = DistsPointingTo(db, cb)[0];
                        var ca = IdOf(db, Link(db, cd, DistApp2, 11));
                        var r = True(fr.mirrored, "pręt kopii odbity: " + fr.mirrored) + "; " + Positive(db, cd, "rozkład") + "; " + Positive(db, ca, "opis");
                        // Lustro x = 16000: obwiednia opisu kopii nachodzi na lustrzane odbicie obwiedni opisu oryginału
                        var e = ExtOf(db, ca);
                        double mx0 = 32000 - o.AnnotExt.MaxPoint.X, mx1 = 32000 - o.AnnotExt.MinPoint.X;
                        bool overlap = e.MinPoint.X < mx1 && e.MaxPoint.X > mx0 && e.MinPoint.Y < o.AnnotExt.MaxPoint.Y && e.MaxPoint.Y > o.AnnotExt.MinPoint.Y;
                        return r + "; " + True(overlap, $"opis kopii {P(e.MinPoint)}-{P(e.MaxPoint)}, odbicie oryginału X {F(mx0)}..{F(mx1)} Y {F(o.AnnotExt.MinPoint.Y)}..{F(o.AnnotExt.MaxPoint.Y)}");
                    });
                }
            });
        }

        [CommandMethod("RCSMOKE_W2_5V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T5V()
        {
            const string step = "5V";
            Run(step, db =>
            {
                foreach (var tag in new[] { "P5", "B5" })
                {
                    var o = _w2[tag];
                    Check(step, $"{tag}: MIRROR Y — te same obiekty (nic nie usunięte), powiązania bez zmian, pręt odbity", () =>
                    {
                        foreach (var id in new[] { o.Bar, o.Label, o.Dist, o.Annot })
                            if (id.IsErased) throw new AssertFail("usunięty " + id.Handle);
                        Int("pręty " + o.Mark, BarsByMark(db, o.Mark).Count, 1);
                        return BarLabelPair(db, o.Bar, o.Label) + "; " + Eq("[18]", Link(db, o.Dist, DistApp2, 18), Hx(o.Bar))
                               + "; " + DistAnnotPair(db, o.Dist, o.Annot) + "; " + True(AxisFrame(Outline(db, o.Bar)).mirrored, "odbity");
                    });
                }
            });
        }

        // ================================================================
        // 6 — ARRAY 1×3
        // ================================================================

        [CommandMethod("RCSMOKE_W2_6V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T6V()
        {
            const string step = "6V";
            Run(step, db =>
            {
                CheckFullCopy(db, step, "P6", Y(6), 2);
                Check(step, "3b: każdy rozkład obok swojego pręta (X rozkładu − X pręta = 40)", () =>
                {
                    var r = new List<string>();
                    foreach (var b in BarsByMark(db, _w2["P6"].Mark))
                    {
                        var d = DistsPointingTo(db, b)[0];
                        double dx = ((Point3d)PosOf(db, d)).X - Outline(db, b).Min(p => p.X);
                        r.Add(Num("dx " + b.Handle, dx, 40, 1.0));
                    }
                    return string.Join("; ", r);
                });
            });
        }

        // ================================================================
        // 7 — #6 (etykieta B przy pręcie A)
        // ================================================================

        [CommandMethod("RCSMOKE_W2_7V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T7V()
        {
            const string step = "7V";
            Run(step, db =>
            {
                foreach (var t in new[] { "P", "B" })
                {
                    var a = _w2["7A" + t]; var b = _w2["7B" + t];
                    Check(step, $"#6 ({t}): kopia B ma kopię etykiety B (back-link), kopia A — [13] = \"\"; oryginały bez zmian", () =>
                    {
                        var ca = BarsByMark(db, a.Mark).Where(x => x != a.Bar).ToList();
                        var cb = BarsByMark(db, b.Mark).Where(x => x != b.Bar).ToList();
                        Int("kopie A", ca.Count, 1); Int("kopie B", cb.Count, 1);
                        var lb = IdOf(db, Link(db, cb[0], XApp, 13));
                        if (lb.IsNull || lb == b.Label) throw new AssertFail("[13] kopii B → " + Link(db, cb[0], XApp, 13));
                        return BarLabelPair(db, cb[0], lb) + "; " + Eq("[13] kopii A", Link(db, ca[0], XApp, 13), "")
                               + "; " + BarLabelPair(db, b.Bar, b.Label) + "; " + Eq("[13] A", Link(db, a.Bar, XApp, 13), "");
                    });
                }
            });
        }

        // ================================================================
        // 8 — schowek i pliki
        // ================================================================

        [CommandMethod("RCSMOKE_W2_8AV", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T8AV()
        {
            const string step = "8AV";
            Run(step, db => CheckPastedDist(db, step, "P8", "PASTECLIP rozkładu bez pręta (ten sam rysunek = COPY): [18] → oryginał"));
        }

        [CommandMethod("RCSMOKE_W2_8BV", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T8BV()
        {
            const string step = "8BV";
            Run(step, db => CheckPastedDist(db, step, "B8", "COPYCLIP → -WBLOCK innych obiektów → PASTECLIP: [18] zachowane (0-POPRAWKI p.3)"));
        }

        private static void CheckPastedDist(Database db, string step, string tag, string name)
        {
            var o = _w2[tag];
            Check(step, tag + ": " + name + "; [11] ↔ wklejony opis; etykieta oryginału 2n", () =>
            {
                var d = DistsPointingTo(db, o.Bar);
                if (d.Count != 2)
                {
                    var all = DistsByMark(db, DistMarkOf(o)).Select(x => x.Handle + "[18]=" + Link(db, x, DistApp2, 18) + "[11]=" + Link(db, x, DistApp2, 11));
                    throw new AssertFail($"rozkłady → oryginał {o.Bar.Handle}: {d.Count}, oczekiwano 2; rozkłady {DistMarkOf(o)}: {string.Join(", ", all)}");
                }
                var pd = d.First(x => x != o.Dist);
                var pa = IdOf(db, Link(db, pd, DistApp2, 11));
                if (pa.IsNull || pa == o.Annot) throw new AssertFail("[11] wklejonego → " + Link(db, pd, DistApp2, 11));
                return DistAnnotPair(db, pd, pa) + "; " + Int("liczba na etykiecie", LeadInt(LabelText(db, o.Label)), 2 * o.N);
            });
        }

        [CommandMethod("RCSMOKE_W2_8CV", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T8CV()
        {
            const string step = "8CV";
            Run(step, db =>
            {
                var o = _w2["P9"];
                Check(step, "-WBLOCK rozkładu + opisu (bez pręta) + -INSERT * (inny rysunek): [18] pusty, [11] ↔ wstawiony opis", () =>
                {
                    var ds = DistsByMark(db, DistMarkOf(o)).Where(x => x != o.Dist && !x.IsErased).ToList();
                    Int("wstawione rozkłady", ds.Count, 1);
                    var a = IdOf(db, Link(db, ds[0], DistApp2, 11));
                    return Eq("[18]", Link(db, ds[0], DistApp2, 18), "") + "; " + DistAnnotPair(db, ds[0], a)
                           + "; " + True(o.Dist.IsErased, "oryginał usunięty przez WBLOCK");
                });
            });
        }

        [CommandMethod("RCSMOKE_W2_8DV", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T8DV()
        {
            const string step = "8DV";
            Run(step, db =>
            {
                var o = _w2["P10"];
                Check(step, "-WBLOCK zestawu + -INSERT *: wstawiony pręt ↔ etykieta, rozkład → wstawiony pręt, opis ↔ rozkład", () =>
                {
                    var bars = BarsByMark(db, o.Mark).Where(b => b != o.Bar).ToList();
                    Int("wstawione pręty", bars.Count, 1);
                    var l = IdOf(db, Link(db, bars[0], XApp, 13));
                    var d = DistsPointingTo(db, bars[0]);
                    Int("rozkłady wstawionego", d.Count, 1);
                    return BarLabelPair(db, bars[0], l) + "; " + DistAnnotPair(db, d[0], IdOf(db, Link(db, d[0], DistApp2, 11)));
                });
            });
        }

        // ================================================================
        // 9 — rekordy 1000 → 1005
        // ================================================================

        [CommandMethod("RCSMOKE_W2_9V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T9V()
        {
            const string step = "9V";
            Run(step, db =>
            {
                var o = _w2["P12"];
                Check(step, "stary format (1000): kopie wskazują kopie, zapis 1005", () =>
                {
                    var cb = BarsByMark(db, o.Mark).First(b => b != o.Bar);
                    var cl = IdOf(db, Link(db, cb, XApp, 13));
                    var cd = DistsPointingTo(db, cb);
                    Int("rozkłady kopii", cd.Count, 1);
                    var ca = IdOf(db, Link(db, cd[0], DistApp2, 11));
                    return BarLabelPair(db, cb, cl) + "; " + DistAnnotPair(db, cd[0], ca)
                           + "; " + Eq("[13] kopii", LinkCode(db, cb, XApp, 13), 1005) + "; " + Eq("[1] etykiety kopii", LinkCode(db, cl, LabelApp2, 1), 1005)
                           + "; " + Eq("[18] kopii", LinkCode(db, cd[0], DistApp2, 18), 1005) + "; " + Eq("[11] kopii", LinkCode(db, cd[0], DistApp2, 11), 1005)
                           + "; " + Eq("[16] kopii", LinkCode(db, ca, AnnotApp2, 16), 1005)
                           + "; " + Eq("[18] oryginału (bez zmian)", LinkCode(db, o.Dist, DistApp2, 18), 1000);
                });
            });
        }

        // ================================================================
        // 10 — 44 COPY
        // ================================================================

        [CommandMethod("RCSMOKE_W2_10V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T10V()
        {
            const string step = "10V";
            Run(step, db =>
            {
                Check(step, "44 COPY: RC_BAR_LINK oryginału 1, kopii 1 (przepięty z mapy, bez dodatkowego)", () =>
                {
                    string mark = BarMark(db, _w2RingA);
                    var c = BarsByMark(db, mark).Where(b => b != _w2RingA).ToList();
                    Int("kopie 44", c.Count, 1);
                    _w2RingCopy = c[0];
                    return Int("linki oryginału", LinksOf(db, _w2RingA).Count, 1) + "; " + Int("linki kopii", LinksOf(db, _w2RingCopy).Count, 1)
                           + "; " + RingCheck(db, _w2RingCopy, new Point3d(150, Y(16) + 5000 + 150, 0), 300, 12);
                });
            });
        }

        [CommandMethod("RCSMOKE_W2_10EDIT", CommandFlags.Modal)]
        public void W2T10Edit()
        {
            const string step = "10EDIT";
            Run(step, db => Check(step, "BarEditApply oryginału 44: A 300 → 400", () => { EditApply(db, _w2RingA, b => SetProp(b, "LengthA", 400.0)); return "OK"; }));
        }

        [CommandMethod("RCSMOKE_W2_10E", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T10E()
        {
            const string step = "10E";
            Run(step, db => Check(step, "po Apply oryginału: pierścień kopii nietknięty (1 link, promienie A=300), oryginał A=400", () =>
                Int("linki kopii", LinksOf(db, _w2RingCopy).Count, 1)
                + "; " + RingCheck(db, _w2RingCopy, new Point3d(150, Y(16) + 5000 + 150, 0), 300, 12)
                + "; " + RingCheck(db, _w2RingA, new Point3d(150, Y(16) + 150, 0), 400, 12)));
        }

        // ================================================================
        // 11 — _.U / _.REDO po COPY
        // ================================================================

        [CommandMethod("RCSMOKE_W2_11V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T11V()
        {
            const string step = "11V";
            Run(step, db => CheckFullCopy(db, step, "P11", Y(11), 1));
        }

        [CommandMethod("RCSMOKE_W2_11U", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T11U()
        {
            const string step = "11U";
            Run(step, db =>
            {
                var o = _w2["P11"];
                Check(step, "_.U: kopia cofnięta, oryginał ↔ etykieta, rozkład → oryginał, etykieta n", () =>
                    Int("pręty " + o.Mark, BarsByMark(db, o.Mark).Count, 1) + "; " + BarLabelPair(db, o.Bar, o.Label)
                    + "; " + Int("rozkłady oryginału", DistsPointingTo(db, o.Bar).Count, 1)
                    + "; " + DistAnnotPair(db, o.Dist, o.Annot) + "; " + Int("liczba", LeadInt(LabelText(db, o.Label)), o.N));
            });
        }

        [CommandMethod("RCSMOKE_W2_11R", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T11R()
        {
            const string step = "11R";
            Run(step, db =>
            {
                var o = _w2["P11"];
                Check(step, "_.REDO: oryginał ↔ etykieta, rozkład oryginału → oryginał, opis ↔ rozkład, kopia odtworzona", () =>
                    BarLabelPair(db, o.Bar, o.Label) + "; " + Eq("[18]", Link(db, o.Dist, DistApp2, 18), Hx(o.Bar))
                    + "; " + DistAnnotPair(db, o.Dist, o.Annot) + "; " + Int("pręty " + o.Mark, BarsByMark(db, o.Mark).Count, 2));
                Check(step, "obserwacja: stan kopii po _.REDO ([13], [18], [11], BTR rozkładu)", () =>
                {
                    var cb = BarsByMark(db, o.Mark).First(b => b != o.Bar);
                    var cd = DistsByMark(db, DistMarkOf(o)).Where(x => x != o.Dist).ToList();
                    string btr(ObjectId id) { using (var tr = db.TransactionManager.StartOpenCloseTransaction()) return ((BlockReference)tr.GetObject(id, OpenMode.ForRead)).BlockTableRecord.Handle.ToString(); }
                    return $"kopia {cb.Handle} [13]={Link(db, cb, XApp, 13)} (etykieta oryginału {o.Label.Handle}); rozkłady kopii: "
                           + string.Join(", ", cd.Select(x => x.Handle + " [18]=" + Link(db, x, DistApp2, 18) + " [11]=" + Link(db, x, DistApp2, 11) + " btr=" + btr(x)))
                           + "; btr oryginału=" + btr(o.Dist);
                });
            });
        }

        // ================================================================
        // 13 — DeepCloneObjects (Objects) bez paczki
        // ================================================================

        [CommandMethod("RCSMOKE_W2_13", CommandFlags.Modal)]
        public void W2T13()
        {
            const string step = "13";
            Run(step, db => Check(step, "db.DeepCloneObjects (kontekst Objects) w komendzie: CloneMap.PendingCount = 0", () =>
            {
                var o = _w2["P13"];
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ids = new ObjectIdCollection { o.Bar };
                    var map = new IdMapping();
                    db.DeepCloneObjects(ids, SymbolUtilityServices.GetBlockModelSpaceId(db), map, false);
                    tr.Commit();
                }
                return Int("PendingCount", (int)Call("BricsCadRc.Core.CloneMap", "PendingCount", db), 0);
            }));
        }

        // ================================================================
        // 15 — COPY z 'ZOOM; 16 — COPY wielokrotne + _U
        // ================================================================

        [CommandMethod("RCSMOKE_W2_15V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T15V()
        {
            const string step = "15V";
            Run(step, db => { CheckFullCopy(db, step, "P14", Y(14), 1); CheckFullCopy(db, step, "B14", Y(14), 1); });
        }

        [CommandMethod("RCSMOKE_W2_16V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T16V()
        {
            const string step = "16V";
            Run(step, db =>
            {
                foreach (var tag in new[] { "P15", "B15" })
                {
                    var o = _w2[tag];
                    Check(step, $"{tag}: opis oryginału NIE usunięty przez cofnięcie 2. kopii (D8)", () =>
                        True(!o.Annot.IsErased, "opis " + o.Annot.Handle + " erased=" + o.Annot.IsErased));
                    CheckFullCopy(db, step, tag, Y(15), 1);
                }
            });
        }

        // ================================================================
        // Runda poprawek 1 (uwagi 1–5): zagnieżdżone COPY / MIRROR, dwa COPY w jednej komendzie, wklejony komplet
        // usunięty w tej samej komendzie, COPYCLIP + -INSERT, stare rekordy 1000 przez -INSERT *
        // ================================================================

        private static string W2File(string name) => Path.Combine(Path.GetTempPath(), name);

        private static void EdZoom(Bricscad.EditorInput.Editor ed, int k)
            => ed.Command("_.ZOOM", "_W", new Point3d(-4000, Y(k) - 4000, 0), new Point3d(70000, Y(k) + 12000, 0));

        private static void EdCopy(Bricscad.EditorInput.Editor ed, Point3d w0, Point3d w1, Vector3d d)
            => ed.Command("_.COPY", "_C", w0, w1, "", Point3d.Origin, Point3d.Origin + d);

        private static Point3d Pw(double x, double y) => new Point3d(x, y, 0);

        /// <summary>17 (uwaga 2): COPY (pas 17) i MIRROR N (pas 18) zagnieżdżone w tej komendzie (Editor.Command).</summary>
        [CommandMethod("RCSMOKE_W2_17", CommandFlags.Modal)]
        public void W2T17()
        {
            const string step = "17";
            Run(step, db =>
            {
                var ed = Application.DocumentManager.MdiActiveDocument.Editor;
                Check(step, "zagnieżdżone _.COPY zestawów pasa 17 i _.MIRROR N pasa 18", () =>
                {
                    EdZoom(ed, 17);
                    EdCopy(ed, Pw(-3000, Y(17) - 3000), Pw(14000, Y(17) + 6000), new Vector3d(0, 8000, 0));
                    int pending = (int)Call("BricsCadRc.Core.CloneMap", "PendingCount", db);
                    EdZoom(ed, 18);
                    ed.Command("_.MIRROR", "_C", Pw(-3000, Y(18) - 3000), Pw(14000, Y(18) + 6000), "",
                               Pw(16000, Y(18) - 5000), Pw(16000, Y(18) + 5000), "_N");
                    return "paczki oczekujące po zagnieżdżonym COPY (przed końcem komendy nadrzędnej)=" + pending.ToString(Inv);
                });
            });
        }

        [CommandMethod("RCSMOKE_W2_17V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T17V()
        {
            const string step = "17V";
            Run(step, db =>
            {
                foreach (var tag in new[] { "P17", "B17", "P18", "B18" })
                {
                    CheckFullCopy(db, step, tag, 0, 1);
                    var o = _w2[tag];
                    Check(step, $"{tag}: kopia rozkładu i opisu z własnym BTR (EnsureUniqueBtr po komendzie nadrzędnej)", () =>
                    {
                        var cb = BarsByMark(db, o.Mark).First(b => b != o.Bar);
                        var cd = DistsPointingTo(db, cb)[0];
                        var ca = IdOf(db, Link(db, cd, DistApp2, 11));
                        return True(BtrOf(db, cd) != BtrOf(db, o.Dist), "BTR rozkładu kopii=" + BtrOf(db, cd) + " oryginału=" + BtrOf(db, o.Dist))
                               + "; " + True(BtrOf(db, ca) != BtrOf(db, o.Annot), "BTR opisu kopii=" + BtrOf(db, ca) + " oryginału=" + BtrOf(db, o.Annot));
                    });
                    if (tag.EndsWith("18", StringComparison.Ordinal))
                        Check(step, $"{tag}: MIRROR zagnieżdżony — rozkład i opis odbicia znormalizowane (skala +1)", () =>
                        {
                            var cb = BarsByMark(db, o.Mark).First(b => b != o.Bar);
                            var cd = DistsPointingTo(db, cb)[0];
                            return Positive(db, cd, "rozkład") + "; " + Positive(db, IdOf(db, Link(db, cd, DistApp2, 11)), "opis");
                        });
                }
            });
        }

        /// <summary>19 (uwaga 3): w jednej komendzie COPY A→B, potem COPY B→C.</summary>
        [CommandMethod("RCSMOKE_W2_19", CommandFlags.Modal)]
        public void W2T19()
        {
            const string step = "19";
            Run(step, db =>
            {
                var ed = Application.DocumentManager.MdiActiveDocument.Editor;
                Check(step, "zagnieżdżone: COPY A→B (+8000 Y), COPY B→C (+30000 X)", () =>
                {
                    EdZoom(ed, 19);
                    EdCopy(ed, Pw(-3000, Y(19) - 3000), Pw(4000, Y(19) + 6000), new Vector3d(0, 8000, 0));
                    EdCopy(ed, Pw(-3000, Y(19) + 5000), Pw(4000, Y(19) + 14000), new Vector3d(30000, 0, 0));
                    return "OK";
                });
            });
        }

        [CommandMethod("RCSMOKE_W2_19V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T19V()
        {
            const string step = "19V";
            Run(step, db => CheckFullCopy(db, step, "P19", 0, 2));
        }

        /// <summary>20 (uwaga 4): w jednej komendzie COPYCLIP + PASTECLIP kompletu, potem ERASE wklejonego pręta.</summary>
        [CommandMethod("RCSMOKE_W2_20", CommandFlags.Modal)]
        public void W2T20()
        {
            const string step = "20";
            Run(step, db =>
            {
                var ed = Application.DocumentManager.MdiActiveDocument.Editor;
                var o = _w2["P20"];
                Check(step, "zagnieżdżone COPYCLIP + PASTECLIP kompletu, ERASE wklejonego pręta w tej samej komendzie", () =>
                {
                    EdZoom(ed, 20);
                    ed.Command("_.COPYCLIP", "_C", Pw(-3000, Y(20) - 3000), Pw(4000, Y(20) + 6000), "");
                    ed.Command("_.PASTECLIP", Pw(20000, Y(20)));
                    var pasted = BarsByMark(db, o.Mark).Where(b => b != o.Bar).ToList();
                    Int("wklejone pręty", pasted.Count, 1);
                    int pending = (int)Call("BricsCadRc.Core.CloneMap", "PendingCount", db);
                    EraseInCode(db, pasted[0]);
                    return "paczki oczekujące przed ERASE=" + pending.ToString(Inv);
                });
            });
        }

        [CommandMethod("RCSMOKE_W2_20V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T20V()
        {
            const string step = "20V";
            Run(step, db =>
            {
                var o = _w2["P20"];
                Check(step, "usunięty wklejony pręt zabrał swój rozkład i opis; oryginał bez zmian", () =>
                    Int("pręty " + o.Mark, BarsByMark(db, o.Mark).Count, 1)
                    + "; " + Int("rozkłady " + DistMarkOf(o), DistsByMark(db, DistMarkOf(o)).Count, 1)
                    + "; " + Int("opisy " + DistMarkOf(o), ScanMs(db, e => e is BlockReference && Has(e, AnnotApp2)
                                                                       && AnnotMarkOf(e) == DistMarkOf(o)).Count, 1)
                    + "; " + BarLabelPair(db, o.Bar, o.Label) + "; " + DistAnnotPair(db, o.Dist, o.Annot));
            });
        }

        /// <summary>21 (uwaga 5): po COPYCLIP (skrypt) — plik z rozkładem i opisem bez pręta (Database.Wblock tych samych obiektów).</summary>
        [CommandMethod("RCSMOKE_W2_21FILE", CommandFlags.Modal)]
        public void W2T21File()
        {
            const string step = "21FILE";
            Run(step, db => Check(step, "Database.Wblock rozkładu + opisu P21 (bez pręta) → w2t_wb3.dwg", () =>
            {
                var o = _w2["P21"];
                string p = W2File("w2t_wb3.dwg");
                if (File.Exists(p)) File.Delete(p);
                using (var outDb = db.Wblock(new ObjectIdCollection { o.Dist, o.Annot }, Pw(0, Y(21))))
                    outDb.SaveAs(p, DwgVersion.Current);
                return p;
            }));
        }

        [CommandMethod("RCSMOKE_W2_21V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T21V()
        {
            const string step = "21V";
            Run(step, db =>
            {
                var o = _w2["P21"];
                Check(step, "COPYCLIP w tym rysunku, potem -INSERT * pliku z rozkładem bez pręta: [18] pusty (mapa schowka nie dla INSERT)", () =>
                {
                    var ds = DistsByMark(db, DistMarkOf(o)).Where(x => x != o.Dist).ToList();
                    Int("wstawione rozkłady", ds.Count, 1);
                    return Eq("[18]", Link(db, ds[0], DistApp2, 18), "") + "; " + DistAnnotPair(db, ds[0], IdOf(db, Link(db, ds[0], DistApp2, 11)))
                           + "; " + Int("rozkłady → oryginał", DistsPointingTo(db, o.Bar).Count, 1);
                });
            });
        }

        /// <summary>22 (uwaga 1): plik ze „starym” kompletem — powiązania w przestrzeni pliku zapisane tekstem 1000.</summary>
        [CommandMethod("RCSMOKE_W2_22FILE", CommandFlags.Modal)]
        public void W2T22File()
        {
            const string step = "22FILE";
            Run(step, db => Check(step, "Database.Wblock kompletu P22, w pliku powiązania → tekst 1000 → w2t_wb4.dwg", () =>
            {
                var o = _w2["P22"];
                string p = W2File("w2t_wb4.dwg");
                if (File.Exists(p)) File.Delete(p);
                int n = 0;
                using (var outDb = db.Wblock(new ObjectIdCollection { o.Bar, o.Label, o.Dist, o.Annot }, Pw(0, Y(22))))
                {
                    var fields = new[] { (XApp, 13), (LabelApp2, 1), (DistApp2, 11), (DistApp2, 18), (AnnotApp2, 16) };
                    using (var tr = outDb.TransactionManager.StartTransaction())
                    {
                        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(outDb), OpenMode.ForRead);
                        foreach (ObjectId id in ms)
                        {
                            var e = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                            foreach (var (app, idx) in fields)
                            {
                                TypedValue[] v;
                                using (var rb = e.GetXDataForApplication(app)) v = rb?.AsArray();
                                if (v == null || v.Length <= idx || !(v[idx].Value is Handle h) || h.Value == 0) continue;
                                v[idx] = new TypedValue((int)DxfCode.ExtendedDataAsciiString, h.Value.ToString("X8", Inv));
                                e.XData = new ResultBuffer(v);
                                n++;
                            }
                        }
                        tr.Commit();
                    }
                    outDb.SaveAs(p, DwgVersion.Current);
                }
                return Int("pola zamienione na 1000", n, 5) + "; " + p;
            }));
        }

        [CommandMethod("RCSMOKE_W2_22V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T22V()
        {
            const string step = "22V";
            Run(step, db =>
            {
                var o = _w2["P22"];
                Check(step, "-INSERT * kompletu z rekordami 1000: powiązania wewnątrz wstawionego poprawne, zapis 1005", () =>
                {
                    var bars = BarsByMark(db, o.Mark).Where(b => b != o.Bar).ToList();
                    Int("wstawione pręty", bars.Count, 1);
                    var l = IdOf(db, Link(db, bars[0], XApp, 13));
                    var d = DistsPointingTo(db, bars[0]);
                    Int("rozkłady wstawionego pręta", d.Count, 1);
                    var a = IdOf(db, Link(db, d[0], DistApp2, 11));
                    return BarLabelPair(db, bars[0], l) + "; " + DistAnnotPair(db, d[0], a)
                           + "; " + Eq("[13]", LinkCode(db, bars[0], XApp, 13), 1005) + "; " + Eq("[1]", LinkCode(db, l, LabelApp2, 1), 1005)
                           + "; " + Eq("[18]", LinkCode(db, d[0], DistApp2, 18), 1005) + "; " + Eq("[11]", LinkCode(db, d[0], DistApp2, 11), 1005)
                           + "; " + Eq("[16]", LinkCode(db, a, AnnotApp2, 16), 1005)
                           + "; " + BarLabelPair(db, o.Bar, o.Label) + "; " + Int("rozkłady oryginału", DistsPointingTo(db, o.Bar).Count, 1);
                });
            });
        }

        private static string BtrOf(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                return ((BlockReference)tr.GetObject(id, OpenMode.ForRead)).BlockTableRecord.Handle.ToString();
        }

        private static string AnnotMarkOf(Entity e)
        {
            using (var rb = e.GetXDataForApplication(AnnotApp2))
            {
                var v = rb?.AsArray();
                return v != null && v.Length > 1 ? Convert.ToString(v[1].Value, Inv) : "";
            }
        }

        // ================================================================
        // Runda poprawek 2: tożsamość rysunku źródłowego schowka (SAVEAS), zamknięcie źródła nie kasuje map schowka
        // ================================================================

        /// <summary>23: po COPYCLIP (skrypt) zapis rysunku pod nową nazwą (Database.SaveAs — zmienia Filename, jak SAVEAS).</summary>
        [CommandMethod("RCSMOKE_W2_23SAVE", CommandFlags.Modal)]
        public void W2T23Save()
        {
            const string step = "23SAVE";
            Run(step, db => Check(step, "Database.SaveAs pod nową nazwą w katalogu tymczasowym (zmiana Filename)", () =>
            {
                string before = db.Filename ?? "";
                string p = W2File("w2t_saveas.dwg");
                if (File.Exists(p)) File.Delete(p);
                db.SaveAs(p, DwgVersion.Current);
                string after = db.Filename ?? "";
                return True(!string.Equals(before, after, StringComparison.OrdinalIgnoreCase), $"Filename: '{before}' → '{after}'");
            }));
        }

        [CommandMethod("RCSMOKE_W2_23V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T23V()
        {
            const string step = "23V";
            Run(step, db => CheckPastedDist(db, step, "P23", "COPYCLIP → SAVEAS → PASTECLIP w tym samym dokumencie: [18] → istniejący pręt"));
        }

        /// <summary>24: po COPYCLIP kompletu z rekordami 1000 — symulacja zamknięcia rysunku źródłowego (handler zamknięcia).</summary>
        [CommandMethod("RCSMOKE_W2_24CLOSE", CommandFlags.Modal)]
        public void W2T24Close()
        {
            const string step = "24CLOSE";
            Run(step, db => Check(step, "CloneMap.OnDocumentClosing (jak DocumentToBeDestroyed) → rysunek przestaje być źródłem schowka", () =>
            {
                Call("BricsCadRc.Core.CloneMap", "OnDocumentClosing", db);
                return Eq("IsClipOrigin", Call("BricsCadRc.Core.CloneMap", "IsClipOrigin", db), false);
            }));
        }

        [CommandMethod("RCSMOKE_W2_24V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2T24V()
        {
            const string step = "24V";
            Run(step, db =>
            {
                var o = _w2["P24"];
                Check(step, "PASTECLIP kompletu z rekordami 1000 po zamknięciu źródła: powiązania wewnątrz wklejonego poprawne (mapy schowka zostały)", () =>
                {
                    var bars = BarsByMark(db, o.Mark).Where(b => b != o.Bar).ToList();
                    Int("wklejone pręty", bars.Count, 1);
                    var l = IdOf(db, Link(db, bars[0], XApp, 13));
                    var d = DistsPointingTo(db, bars[0]);
                    Int("rozkłady wklejonego pręta", d.Count, 1);
                    var a = IdOf(db, Link(db, d[0], DistApp2, 11));
                    return BarLabelPair(db, bars[0], l) + "; " + DistAnnotPair(db, d[0], a)
                           + "; " + Eq("[18]", LinkCode(db, d[0], DistApp2, 18), 1005)
                           + "; " + BarLabelPair(db, o.Bar, o.Label) + "; " + Int("rozkłady oryginału", DistsPointingTo(db, o.Bar).Count, 1);
                });
            });
        }

        [CommandMethod("RCSMOKE_W2_REPORT", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W2Report()
        {
            Report();
            SetMode(false);
        }

        // ================================================================
        // Pomocnicze W2
        // ================================================================

        private static string Hx(ObjectId id) => id.IsNull ? "" : id.Handle.Value.ToString("X8", Inv);

        private static string BarMark(Database db, ObjectId id) => (string)Prop(ReadBar(db, id), "Mark");

        private static TypedValue[] Rec(Database db, ObjectId id, string app)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            using (var rb = ((Entity)tr.GetObject(id, OpenMode.ForRead, true)).GetXDataForApplication(app))
                return rb?.AsArray() ?? new TypedValue[0];
        }

        /// <summary>Powiązanie [idx] rekordu app jako „X8” (1005 albo stary tekst 1000) albo "".</summary>
        private static string Link(Database db, ObjectId id, string app, int idx)
        {
            var v = Rec(db, id, app);
            if (v.Length <= idx || v[idx].Value == null) return "";
            object val = v[idx].Value;
            long h = val is Handle hh ? hh.Value
                   : long.TryParse(val.ToString(), NumberStyles.HexNumber, Inv, out long x) ? x : 0;
            return h == 0 ? "" : h.ToString("X8", Inv);
        }

        private static int LinkCode(Database db, ObjectId id, string app, int idx)
        {
            var v = Rec(db, id, app);
            return v.Length > idx ? v[idx].TypeCode : -1;
        }

        /// <summary>Pole [idx] zapisane jako tekst 1000 (stary format) — punktowo.</summary>
        private static void ToText(Database db, ObjectId id, string app, int idx)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var e = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                TypedValue[] v;
                using (var rb = e.GetXDataForApplication(app)) v = rb.AsArray();
                string hex = v[idx].Value is Handle h ? h.Value.ToString("X8", Inv) : Convert.ToString(v[idx].Value, Inv);
                v[idx] = new TypedValue((int)DxfCode.ExtendedDataAsciiString, hex);
                e.XData = new ResultBuffer(v);
                tr.Commit();
            }
        }

        private static ObjectId IdOf(Database db, string hex)
        {
            if (string.IsNullOrEmpty(hex) || !long.TryParse(hex, NumberStyles.HexNumber, Inv, out long h) || h == 0) return ObjectId.Null;
            return db.TryGetObjectId(new Handle(h), out var id) ? id : ObjectId.Null;
        }

        /// <summary>Pręt [13] → etykieta i etykieta [1] → pręt.</summary>
        private static string BarLabelPair(Database db, ObjectId bar, ObjectId label)
        {
            if (label.IsNull || label.IsErased) throw new AssertFail("pręt " + bar.Handle + ": brak etykiety");
            if (Link(db, bar, XApp, 13) != Hx(label)) throw new AssertFail($"pręt {bar.Handle}: [13]={Link(db, bar, XApp, 13)}, oczekiwano {label.Handle}");
            if (Link(db, label, LabelApp2, 1) != Hx(bar)) throw new AssertFail($"etykieta {label.Handle}: [1]={Link(db, label, LabelApp2, 1)}, oczekiwano {bar.Handle}");
            return $"pręt {bar.Handle} ↔ etykieta {label.Handle}";
        }

        /// <summary>Rozkład [11] → opis i opis [16] → rozkład.</summary>
        private static string DistAnnotPair(Database db, ObjectId dist, ObjectId annot)
        {
            if (annot.IsNull || annot.IsErased) throw new AssertFail("rozkład " + dist.Handle + ": brak opisu");
            if (Link(db, dist, DistApp2, 11) != Hx(annot)) throw new AssertFail($"rozkład {dist.Handle}: [11]={Link(db, dist, DistApp2, 11)}, oczekiwano {annot.Handle}");
            if (Link(db, annot, AnnotApp2, 16) != Hx(dist)) throw new AssertFail($"opis {annot.Handle}: [16]={Link(db, annot, AnnotApp2, 16)}, oczekiwano {dist.Handle}");
            return $"rozkład {dist.Handle} ↔ opis {annot.Handle}";
        }

        private static List<ObjectId> ScanMs(Database db, Func<Entity, bool> pred)
        {
            var r = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                    if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is Entity e && pred(e)) r.Add(id);
            }
            return r;
        }

        private static List<ObjectId> BarsByMark(Database db, string mark)
            => ScanMs(db, e =>
            {
                using (var rb = e.GetXDataForApplication(XApp))
                {
                    var v = rb?.AsArray();
                    return v != null && v.Length > 1 && Convert.ToString(v[1].Value, Inv) == mark;
                }
            });

        private static List<ObjectId> DistsPointingTo(Database db, ObjectId bar)
        {
            string h = Hx(bar);
            return ScanMs(db, e => e is BlockReference && Has(e, DistApp2)).Where(d => Link(db, d, DistApp2, 18) == h).ToList();
        }

        private static string DistMarkOf(W2Set s) => Convert.ToString(Rec(ActiveDb(), s.Dist, DistApp2)[1].Value, Inv);

        private static List<ObjectId> DistsByMark(Database db, string mark)
            => ScanMs(db, e =>
            {
                if (!(e is BlockReference)) return false;
                using (var rb = e.GetXDataForApplication(DistApp2))
                {
                    var v = rb?.AsArray();
                    return v != null && v.Length > 1 && Convert.ToString(v[1].Value, Inv) == mark;
                }
            });

        private static bool Has(Entity e, string app)
        {
            using (var rb = e.GetXDataForApplication(app)) return rb != null;
        }

        private static string LabelText(Database db, ObjectId label)
        {
            if (label.IsNull) return "";
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                return (tr.GetObject(label, OpenMode.ForRead) as MLeader)?.MText?.Contents ?? "";
        }

        private static int LeadInt(string s)
        {
            var m = Regex.Match(s ?? "", @"^\s*(?:\{[^}]*\})?\s*(\d+)\s");
            return m.Success ? int.Parse(m.Groups[1].Value, Inv) : -1;
        }

        private static Extents3d ExtOf(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                return ((Entity)tr.GetObject(id, OpenMode.ForRead)).GeometricExtents;
        }

        private static object PosOf(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                return ((BlockReference)tr.GetObject(id, OpenMode.ForRead)).Position;
        }

        private static string Positive(Database db, ObjectId id, string what)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                var s = br.ScaleFactors;
                if (s.X <= 0 || s.Y <= 0 || Math.Abs(br.Rotation) > 1e-9)
                    throw new AssertFail($"{what} {id.Handle}: skala ({F(s.X)},{F(s.Y)}) obrót {F(br.Rotation)}");
                return what + " skala +1";
            }
        }
    }
}
