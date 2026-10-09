// S-REG (docs/PLAN_C_etap2.md, 6.1) — ten sam scenariusz z DLL wzorca (aba0ed1, assembly „BricsCadRc”)
// i z naszym DLL (assembly „NaSmartRc”) → znormalizowany zrzut rysunku → diff musi być pusty.
//
// Dostęp do wtyczki WYŁĄCZNIE przez refleksję (typy BricsCadRc.Core.* w zestawie wczytanym przez BricsCAD),
// tylko API obecne w obu wersjach. Komendy uruchamia tools/RegressionDump/reg.scr:
//   RC_REG_SETUP      — obrysy płyty (rzut dolny i górny, z otworem)
//   RC_REG_AUTOREBAR  — AutoRebar B1, B2, T1, T2, UB B1, UB B2 (jak RC_GENERUJ_SIATKA, bez wskazań)
//   RC_REG_BARS       — PlaceBar 00 i 21 + etykiety + rozkłady + opisy + UpdateBarLabelCount; trzeci pręt
//                       (kopia pozycji pręta 1) do testu Reconcilera
//   RC_REG_RECONCILE  — zmiana wymiaru trzeciego pręta jak RC_EDIT_BAR (BarEditApply.Apply; we wzorcu te same
//                       publiczne kroki w tej samej kolejności) → PositionReconciler + rozkłady + etykiety
//   RC_REG_DUMP <plik> — BBS (BbsDrawingReader.ReadBars), zestawienie (BarScheduleEngine) i zrzut rysunku
//   RC_REG_FIX_SETUP / RC_REG_FIX_CHECK / RC_REG_DUMP_FIX <plik> — przypadki poprawek błędów wzorca
//                       (docs/PLAN_POPRAWKI_W1_W2.md 5.2–5.3, RegFixCommands.cs)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace RegressionDump
{
    public partial class RegCommands
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const string Prefix    = "[RC REG]";
        private const string SlabLayer = "SD-PILED-RAFT";

        // Geometria scenariusza (deterministyczna)
        private const double SlabW = 12000, SlabH = 8000;
        private const double TopDy = -20000;                    // rzut górny: obrys przystający, przesunięty w dół
        private const double HoleX0 = 5000, HoleY0 = 3000, HoleX1 = 6000, HoleY1 = 4000;

        /// <summary>Wyniki kroków (wartości zwrócone, wyjątki) — trafiają do zrzutu.</summary>
        private static readonly List<string> _log = new List<string>();

        /// <summary>Notatki przebiegu (np. format prętów) — w zrzucie jako linie „#” (poza diffem).</summary>
        private static readonly List<string> _notes = new List<string>();

        private const string EnvBarFormat = "RC_REG_BARFORMAT";   // Block | Polyline — format nowych prętów (Plan C, etap 3)
        private const string EnvBarEquiv  = "RC_REG_BAR_EQUIV";   // 1 → dodatkowy zrzut bar-equiv (<plik>-bareq.txt)
        private static Assembly _plugin;
        private static ObjectId _bottomId, _topId, _bar1, _bar2, _bar3;
        private static int _pos1, _pos2;

        // ================================================================
        // Komendy scenariusza
        // ================================================================

        [CommandMethod("RC_REG_SETUP", CommandFlags.Modal)]
        public void Setup()
        {
            _log.Clear();
            _notes.Clear();
            _plugin = null;
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            Step("SETUP.plugin", () => Plugin.GetName().Name == "NaSmartRc" || Plugin.GetName().Name == "BricsCadRc" ? "OK" : "?");
            SetupBarFormat();
            Step("SETUP.outlines", () =>
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, SlabLayer, 7);
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    _bottomId = AddRect(ms, tr, 0, 0, SlabW, SlabH, SlabLayer);
                    AddHole(ms, tr, 0);
                    _topId = AddRect(ms, tr, 0, TopDy, SlabW, SlabH + TopDy, SlabLayer);
                    AddHole(ms, tr, TopDy);
                    tr.Commit();
                }
                return "OK";
            });
        }

        /// <summary>
        /// BarFormat wtyczki z RC_REG_BARFORMAT (refleksja, bez zapisu do pliku). We wzorcu (brak typu BarFormat)
        /// krok pomijany. Wynik tylko w notatkach „#” — linie kroków muszą być identyczne we wzorcu i u nas.
        /// </summary>
        private static void SetupBarFormat()
        {
            string want = (Environment.GetEnvironmentVariable(EnvBarFormat) ?? "").Trim();
            try
            {
                var ft = Plugin.GetType("BricsCadRc.Core.BarFormat", false);
                var kt = Plugin.GetType("BricsCadRc.Core.BarFormatKind", false);
                if (ft == null || kt == null) { _notes.Add("# barformat=brak (wtyczka bez BarFormat — wzorzec)"); return; }
                if (want.Length > 0)
                {
                    var set = ft.GetMethod("Set", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    set.Invoke(null, new[] { Enum.Parse(kt, want, true), (object)false });
                }
                var cur = ft.GetProperty("Current", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
                _notes.Add("# barformat=" + Convert.ToString(cur, Inv) + (want.Length > 0 ? " (z " + EnvBarFormat + ")" : " (domyślny)"));
            }
            catch (System.Exception ex)
            {
                var e = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                _notes.Add(OneLine($"# barformat=BŁĄD {e.GetType().Name}: {e.Message}"));
            }
        }

        [CommandMethod("RC_REG_AUTOREBAR", CommandFlags.Modal)]
        public void AutoRebar()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            const string E = "AutoRebarEngine";
            // Kolejność i parametry jak RC_GENERUJ_SIATKA (grubość 225, widoczny pręt reprezentatywny)
            Step("AR.B1", () => Call(E, "GenerateLayer", doc, _bottomId, "rebar_bottom", "X", "B1", 10, 200.0, 40.0, true));
            Step("AR.B2", () => Call(E, "GenerateLayer", doc, _bottomId, "rebar_bottom", "Y", "B2", 10, 200.0, 40.0, true));
            Step("AR.T1", () => Call(E, "GenerateLayer", doc, _topId, "rebar_top", "X", "T1", 12, 200.0, 40.0, true));
            Step("AR.T2", () => Call(E, "GenerateLayer", doc, _topId, "rebar_top", "Y", "T2", 12, 200.0, 40.0, true));
            Step("AR.UB_B1", () => Call(E, "GenerateUBLayer", doc, _bottomId, "rebar_bottom", "B1", 225, "X", 200.0, 40.0, true));
            Step("AR.UB_B2", () => Call(E, "GenerateUBLayer", doc, _bottomId, "rebar_bottom", "B2", 225, "Y", 200.0, 40.0, true));
            Step("AR.nib", () => Call("NibDetector", "Detect", db, _bottomId) == null ? "brak" : "jest");
            Step("AR.collisions_bottom", () => Call(E, "ResolveRepresentativeCollisions", doc, _bottomId));
            Step("AR.collisions_top", () => Call(E, "ResolveRepresentativeCollisions", doc, _topId));
        }

        [CommandMethod("RC_REG_BARS", CommandFlags.Modal)]
        public void Bars()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            Step("BARS.bar1", () =>
            {
                var used = Call("PositionCounter", "GetUsedPositionNumbers", db);
                _pos1 = (int)Call("PositionCounter", "NextAuto", db, used);
                _bar1 = PlaceBarWithLabel(db, 12, "00", 3000, 0, 0, _pos1, new Point3d(0, 30000, 0));
                return _pos1;
            });
            Step("BARS.bar2", () =>
            {
                var used = Call("PositionCounter", "GetUsedPositionNumbers", db);
                _pos2 = (int)Call("PositionCounter", "NextAuto", db, used);
                _bar2 = PlaceBarWithLabel(db, 10, "21", 500, 300, 500, _pos2, new Point3d(5000, 30000, 0));
                return _pos2;
            });
            Step("BARS.dist1", () => Distribute(db, _bar1, 40, 32040, 3040, 34960, true, 200, "B1"));
            Step("BARS.dist2", () => Distribute(db, _bar1, 40, 36040, 3040, 37000, true, 150, "T1"));
            Step("BARS.dist3", () => Distribute(db, _bar2, 5040, 32040, 7960, 32540, false, 200, "B2"));
            // Trzeci pręt: ta sama pozycja i wymiary co pręt 1 (jak kopia) — do testu Reconcilera
            Step("BARS.bar3", () =>
            {
                _bar3 = PlaceBarWithLabel(db, 12, "00", 3000, 0, 0, _pos1, new Point3d(0, 40000, 0));
                return "OK";
            });
            Step("BARS.dist4", () => Distribute(db, _bar3, 40, 42040, 3040, 43000, true, 200, "B1"));
            Step("BARS.labelCount1", () =>
            {
                Call("AnnotationEngine", "UpdateBarLabelCount", db, X8(_bar1), null);
                return LabelText(db, _bar1);
            });
        }

        [CommandMethod("RC_REG_RECONCILE", CommandFlags.Modal)]
        public void Reconcile()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            // Pręt 1 bez konfliktu (pręt 3 ma te same wymiary) → numer zostaje
            Step("REC.bar1_noconflict", () => Call("PositionReconciler", "ReconcileAfterGeometryChange", db, _bar1) ?? "null");
            // Pręt 3: A 3000 → 3500 jak RC_EDIT_BAR po dialogu (Plan C, etap 3d): nasza wtyczka — BarEditApply.Apply
            // (refleksja; polilinia i pręt-blok), wzorzec — te same publiczne kroki w tej samej kolejności (EditBarSteps).
            // UWAGA (wynik dochodzenia 3d): we wzorcu BarGeometryWatcher w CommandEnded tej komendy traktuje zmianę obrysu
            // polilinii z RebuildCompanions jak grip na końcu pręta (TryEndStretch) i dolicza przyrost drugi raz
            // (3500 → 4000) — tak samo w prawdziwym RC_EDIT_BAR. Błąd #1 poprawiony u nas (W1b, BarGeometryWatcher.Suppress
            // w BarEditApply.Apply): polilinia i pręt-blok 3500 — różnica w expected-diffs\{polyline,block}\01-*.txt.
            Step("REC.bar3_edit", () =>
            {
                object old, upd;
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var ent = (Entity)tr.GetObject(_bar3, OpenMode.ForRead);
                    old = Call("SingleBarEngine", "ReadBarXData", ent);
                    upd = Call("SingleBarEngine", "ReadBarXData", ent);
                }
                // Wynik dialogu: A = 3500, TotalLength jak BarElevationDialog, numer z dialogu „D2”
                Set(upd, "LengthA", 3500.0);
                Set(upd, "TotalLength", TotalLength(upd));
                int posNr = (int)Call("SingleBarEngine", "ExtractPosNr", (string)Get(old, "Mark"));
                string posNrStr = posNr.ToString("D2", Inv);

                if (Plugin.GetType("BricsCadRc.Core.BarEditApply", false) != null)
                {
                    var ok = Call("BarEditApply", "Apply", db, null, _bar3, old, upd, posNrStr);
                    _notes.Add("# REC.bar3_edit: BarEditApply.Apply (nasza wtyczka)");
                    if (!(ok is bool b) || !b) throw new InvalidOperationException("BarEditApply.Apply = " + Fmt(ok));
                }
                else
                {
                    EditBarSteps(db, _bar3, old, upd, posNrStr);
                    _notes.Add("# REC.bar3_edit: publiczne kroki RC_EDIT_BAR (wzorzec)");
                }
                return (string)Get(upd, "Mark");
            });
            Step("REC.labelCount3", () => LabelText(db, _bar3));
            Step("REC.labelCount1", () =>
            {
                Call("AnnotationEngine", "UpdateBarLabelCount", db, X8(_bar1), null);
                return LabelText(db, _bar1);
            });
        }

        [CommandMethod("RC_REG_DUMP", CommandFlags.Modal)]
        public void Dump()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;
            var pr = ed.GetString(new PromptStringOptions("\nPlik zrzutu: ") { AllowSpaces = true });
            if (pr.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(pr.StringResult))
            {
                ed.WriteMessage($"\n{Prefix} Brak ścieżki pliku — przerwano.\n");
                return;
            }
            string path = pr.StringResult.Trim().Trim('"');

            WriteDump(ed, path, BuildDump(db, false));
            if ((Environment.GetEnvironmentVariable(EnvBarEquiv) ?? "").Trim() == "1")
            {
                string eqPath = Path.Combine(Path.GetDirectoryName(path),
                                             Path.GetFileNameWithoutExtension(path) + "-bareq" + Path.GetExtension(path));
                WriteDump(ed, eqPath, BuildDump(db, true));
            }
        }

        private static void WriteDump(Editor ed, string path, List<string> lines)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
                ed.WriteMessage($"\n{Prefix} Zrzut: {path} ({lines.Count} linii).\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n{Prefix} Błąd zapisu zrzutu {path}: {ex.Message}\n");
            }
        }

        /// <summary>Zrzut: kroki, BBS, zestawienie, numeracja, rysunek (barEquiv → DrawingDump w trybie bar-equiv).</summary>
        private static List<string> BuildDump(Database db, bool barEquiv)
        {
            var lines = new List<string>();
            try
            {
                lines.Add("# S-REG zrzut (linie z # nie są porównywane)" + (barEquiv ? " — tryb bar-equiv" : ""));
                lines.Add("# plugin=" + SafeLoc());
                lines.Add("# czas=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
                lines.AddRange(_notes);

                lines.Add("== KROKI");
                lines.AddRange(_log);

                lines.Add("== BBS (BbsDrawingReader.ReadBars)");
                Step2(lines, () =>
                {
                    var res = Call("BbsDrawingReader", "ReadBars", db);
                    var outL = new List<string>();
                    outL.Add("Distributions=" + Fmt(Get(res, "Distributions")));
                    int i = 0;
                    foreach (var row in (IEnumerable)Get(res, "Rows"))
                        outL.Add($"row {i++}: " + Props(row));
                    foreach (var w in (IEnumerable)Get(res, "Warnings"))
                        outL.Add("warn: " + w);
                    return outL;
                });

                lines.Add("== ZESTAWIENIE (BarScheduleEngine.BuildSchedule)");
                Step2(lines, () =>
                {
                    var list = (IEnumerable)Call("BarScheduleEngine", "BuildSchedule", db);
                    var outL = new List<string>();
                    int i = 0;
                    foreach (var e in list) outL.Add($"entry {i++}: " + Props(e));
                    return outL;
                });

                lines.Add("== NUMERACJA");
                Step2(lines, () =>
                {
                    var used = ((IEnumerable)Call("PositionCounter", "GetUsedPositionNumbers", db)).Cast<int>().OrderBy(n => n);
                    return new List<string>
                    {
                        "used=" + string.Join(",", used.Select(n => n.ToString(Inv))),
                        "peek=" + Fmt(Call("PositionCounter", "Peek", db)),
                    };
                });

                lines.Add("== RYSUNEK");
                Step2(lines, () => DrawingDump.Run(db, barEquiv));
                lines.Add("END");
            }
            catch (System.Exception ex)
            {
                lines.Add("FATAL " + ex.GetType().Name + ": " + ex.Message);
                lines.Add("END");
            }
            return lines;
        }

        // ================================================================
        // Scenariusz — pomocnicze (odtwarzają nieinteraktywną część RC_BAR / RC_DISTRIBUTION)
        // ================================================================

        /// <summary>RC_BAR bez dialogu i jiga: PlaceBar + etykieta w domyślnym punkcie + LabelHandle w XData.</summary>
        private static ObjectId PlaceBarWithLabel(Database db, int d, string shape, double a, double b, double c,
                                                  int posNr, Point3d insertPt, bool withLabel = true)
        {
            var bar = NewObj("BarData");
            Set(bar, "Diameter", d);
            Set(bar, "ShapeCode", shape);
            Set(bar, "LengthA", a);
            Set(bar, "LengthB", b);
            Set(bar, "LengthC", c);
            Set(bar, "LengthOverridden", false);
            Set(bar, "LayerCode", "B1");
            Set(bar, "Position", "TOP");
            Set(bar, "Direction", "X");
            Set(bar, "TotalLength", TotalLength(bar));
            Set(bar, "Mark", Call("BarData", "FormatMark", d, posNr, 0.0, 1));

            var barId = (ObjectId)Call("SingleBarEngine", "PlaceBar", db, bar, insertPt);
            if (!withLabel) return barId;   // przypadki poprawek: pręt bez etykiety (RC_BAR z „bez etykiety”)

            Point3d textPt, arrowTip;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var center = BarMidPoint(tr, barId, insertPt);
                textPt = new Point3d(center.X - 50, center.Y + 150, 0);
                arrowTip = (Point3d)Call("SingleBarEngine", "GetBarArrowTip", barId, bar, textPt, tr);
                tr.Commit();
            }

            var labelId = (ObjectId)Call("SingleBarEngine", "PlaceBarLabel", db, arrowTip, textPt, (string)Get(bar, "Mark"), barId);
            if (!labelId.IsNull)
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ent = (Entity)tr.GetObject(barId, OpenMode.ForWrite);
                    Set(bar, "LabelHandle", labelId.Handle.ToString());
                    Call("SingleBarEngine", "WriteXData", ent, bar);
                    tr.Commit();
                }
            }
            return barId;
        }

        /// <summary>
        /// Środek pręta jak RC_BAR: polilinia — GetPointAtParameter(EndParam / 2) (wzorzec, bez zmian);
        /// pręt-blok (Plan C, etap 3, tylko nasza wtyczka) — BarEntity.MidPoint przez refleksję; inaczej fallback.
        /// </summary>
        private static Point3d BarMidPoint(Transaction tr, ObjectId barId, Point3d fallback)
        {
            var ent = tr.GetObject(barId, OpenMode.ForRead) as Entity;
            if (ent is Polyline pl) return pl.GetPointAtParameter(pl.EndParam / 2.0);
            if (!(ent is BlockReference)) return fallback;
            var kinds = Plugin.GetType("BricsCadRc.Core.BarKinds", false);
            if (kinds == null) return fallback;                       // wzorzec: brak prętów-bloków
            var be = Call("BarEntity", "From", ent, Enum.Parse(kinds, "Block"));
            if (be == null) return fallback;
            var mi = be.GetType().GetMethod("MidPoint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi == null) return fallback;
            object mp;
            try { mp = mi.Invoke(be, new object[] { tr }); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
            return mp is Point3d p ? p : fallback;
        }

        /// <summary>RC_DISTRIBUTION bez dialogów i jigów: GenerateFromBounds + Mark + CreateLeader (punkt domyślny)
        /// + LinkAnnotation + UpdateBarLabelCount — w tej samej kolejności co BarCommands.</summary>
        private static string Distribute(Database db, ObjectId barId, double x0, double y0, double x1, double y1,
                                         bool horizontal, double spacing, string suffix)
        {
            object src;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                src = Call("SingleBarEngine", "ReadBarXData", (Entity)tr.GetObject(barId, OpenMode.ForRead));
                tr.Commit();
            }
            if (src == null) throw new InvalidOperationException("pręt bez RC_SINGLE_BAR");
            string srcHandle = X8(barId);
            Set(src, "SourceBarHandle", srcHandle);
            Set(src, "Angle", 0.0);
            double rawSpan = horizontal ? (y1 - y0) : (x1 - x0);
            int autoCount = Math.Max(1, (int)(rawSpan / spacing) + 1);
            int posNr = (int)Call("SingleBarEngine", "ExtractPosNr", (string)Get(src, "Mark"));

            Set(src, "Direction", horizontal ? "X" : "Y");
            Set(src, "Cover", 40.0);
            Set(src, "Pt1X", x0);
            Set(src, "Pt1Y", y0);
            Set(src, "BarsSpan", rawSpan);
            Set(src, "Spacing", spacing);
            Set(src, "Flipped", false);

            var res = Call("BarBlockEngine", "GenerateFromBounds", db, x0, y0, x1, y1, src, horizontal, posNr);
            var blockRefId = (ObjectId)Get(res, "BlockRefId");
            if (blockRefId.IsNull) throw new InvalidOperationException("GenerateFromBounds: brak rozkładu");

            // RunAnnotationFlow: Mark z liczbą/rozstawem + sufiks, CountDisplay = null (liczba bez zmian)
            string mark = (string)Call("BarData", "FormatMark", (int)Get(src, "Diameter"), posNr, spacing, autoCount)
                          + " " + suffix;
            Set(src, "Mark", mark);
            Set(src, "CountDisplay", null);
            WriteBlockMark(db, blockRefId, mark, true);

            var ann = Call("AnnotationEngine", "CreateLeader", db, res, src, false, posNr, null, horizontal, true, true);
            var annId = (ObjectId)Get(ann, "BlockRefId");
            if (annId.IsNull) throw new InvalidOperationException("CreateLeader: brak opisu");
            Call("BarBlockEngine", "LinkAnnotation", db, blockRefId, annId);
            Call("AnnotationEngine", "UpdateBarLabelCount", db, srcHandle, mark);

            // Po RunAnnotationFlow: Mark do XData rozkładu + ponowne przeliczenie etykiety
            WriteBlockMark(db, blockRefId, mark, false);
            Call("AnnotationEngine", "UpdateBarLabelCount", db, srcHandle, mark);
            return $"{mark} count={Get(src, "Count")}";
        }

        private static void WriteBlockMark(Database db, ObjectId blockRefId, string mark, bool withCountDisplay)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = tr.GetObject(blockRefId, OpenMode.ForWrite) as BlockReference;
                var xd = br != null ? Call("BarBlockEngine", "ReadXData", br) : null;
                if (xd != null)
                {
                    Set(xd, "Mark", mark);
                    if (withCountDisplay) Set(xd, "CountDisplay", null);
                    Call("BarBlockEngine", "WriteXData", br, xd);
                }
                tr.Commit();
            }
        }

        /// <summary>
        /// RC_EDIT_BAR po dialogu we wzorcu (brak BarEditApply) — publiczne kroki w kolejności BarCommands.EditBar (aba0ed1):
        /// Mark + pola niezmienne → WriteXData → (numer bez zmian i inne wymiary) ReconcileAfterGeometryChange →
        /// RebuildCompanions → grot etykiety na najbliższy punkt polilinii → UpdateBarLabelCount → PropagateToDistributions.
        /// </summary>
        private static void EditBarSteps(Database db, ObjectId editId, object bar, object updated, string posNrStr)
        {
            int posNr = int.Parse(posNrStr, Inv);
            Set(updated, "Mark", Call("BarData", "FormatMark", (int)Get(updated, "Diameter"), posNr, 0.0, 1));
            Set(updated, "LayerCode", Get(bar, "LayerCode"));
            Set(updated, "Position", Get(bar, "Position"));
            Set(updated, "Direction", Get(bar, "Direction"));
            Set(updated, "LabelHandle", Get(bar, "LabelHandle"));

            using (var tr2 = db.TransactionManager.StartTransaction())
            {
                var plineRw = tr2.GetObject(editId, OpenMode.ForWrite) as Polyline;
                if (plineRw == null) { tr2.Abort(); throw new InvalidOperationException("pręt nie jest polilinią"); }
                Call("SingleBarEngine", "WriteXData", plineRw, updated);
                tr2.Commit();
            }

            if (posNr == (int)Call("SingleBarEngine", "ExtractPosNr", (string)Get(bar, "Mark"))
                && !(bool)Call("PositionReconciler", "SameShape", bar, updated))
            {
                var renumbered = (string)Call("PositionReconciler", "ReconcileAfterGeometryChange", db, editId);
                if (renumbered != null) Set(updated, "Mark", renumbered);
            }

            Call("SingleBarEngine", "RebuildCompanions", db, editId, updated, null);

            string lh = (string)Get(updated, "LabelHandle");
            using (var trFix = db.TransactionManager.StartTransaction())
            {
                var plineFix = trFix.GetObject(editId, OpenMode.ForRead) as Polyline;
                if (plineFix != null && !string.IsNullOrEmpty(lh)
                    && long.TryParse(lh, NumberStyles.HexNumber, null, out long hv)
                    && db.TryGetObjectId(new Handle(hv), out ObjectId lid) && !lid.IsNull && !lid.IsErased
                    && trFix.GetObject(lid, OpenMode.ForWrite) is MLeader ml)
                {
                    var leaderIdxs = ml.GetLeaderIndexes();
                    if (leaderIdxs != null && leaderIdxs.Count > 0)
                    {
                        var lineIdxs = ml.GetLeaderLineIndexes((int)leaderIdxs[0]);
                        if (lineIdxs != null && lineIdxs.Count > 0)
                        {
                            int lni = (int)lineIdxs[0];
                            ml.SetFirstVertex(lni, plineFix.GetClosestPointTo(ml.GetFirstVertex(lni), false));
                        }
                    }
                }
                trFix.Commit();
            }

            Call("AnnotationEngine", "UpdateBarLabelCount", db, X8(editId), (string)Get(updated, "Mark"));
            Call("PositionReconciler", "PropagateToDistributions", db, editId, updated, 0);
        }

        private static double TotalLength(object bar)
        {
            var shape = Call("ShapeCodeLibrary", "Get", (string)Get(bar, "ShapeCode"));
            // Jak BarElevationDialog: tyle wartości, ile parametrów ma kształt (A, B, C, …)
            int n = ((string[])Get(shape, "Parameters")).Length;
            var pv = ((double[])Get(bar, "ParamValues")).Take(n).ToArray();
            var m = shape.GetType().GetMethod("CalculateTotalLength");
            return (double)m.Invoke(shape, new object[] { pv, (double)(int)Get(bar, "Diameter") });
        }

        private static string LabelText(Database db, ObjectId barId)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var bar = Call("SingleBarEngine", "ReadBarXData", (Entity)tr.GetObject(barId, OpenMode.ForRead));
                string lh = bar != null ? (string)Get(bar, "LabelHandle") : null;
                if (string.IsNullOrEmpty(lh) || !long.TryParse(lh, NumberStyles.HexNumber, Inv, out long hv)) return "brak etykiety";
                if (!db.TryGetObjectId(new Handle(hv), out var lid) || lid.IsErased) return "etykieta usunięta";
                var ml = tr.GetObject(lid, OpenMode.ForRead) as MLeader;
                return ml?.MText?.Contents ?? "?";
            }
        }

        private static string X8(ObjectId id) => id.Handle.Value.ToString("X8");

        private static void EnsureLayer(Database db, Transaction tr, string name, short color)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord { Name = name };
            rec.Color = Teigha.Colors.Color.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, color);
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        private static ObjectId AddRect(BlockTableRecord ms, Transaction tr, double x0, double y0, double x1, double y1, string layer)
        {
            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = layer;
            var id = ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            return id;
        }

        /// <summary>Otwór: zamknięta polilinia wewnątrz obrysu + krzyżyk (dwie linie po przekątnych) — jak FindHoles.</summary>
        private static void AddHole(BlockTableRecord ms, Transaction tr, double dy)
        {
            AddRect(ms, tr, HoleX0, HoleY0 + dy, HoleX1, HoleY1 + dy, SlabLayer);
            foreach (var (a, b) in new[]
            {
                (new Point3d(HoleX0, HoleY0 + dy, 0), new Point3d(HoleX1, HoleY1 + dy, 0)),
                (new Point3d(HoleX0, HoleY1 + dy, 0), new Point3d(HoleX1, HoleY0 + dy, 0)),
            })
            {
                var ln = new Line(a, b) { Layer = SlabLayer };
                ms.AppendEntity(ln);
                tr.AddNewlyCreatedDBObject(ln, true);
            }
        }

        // ================================================================
        // Kroki i refleksja
        // ================================================================

        /// <summary>Krok scenariusza: wynik albo wyjątek → log (domyślnie _log zrzutu głównego).</summary>
        private static void Step(string name, Func<object> f, List<string> log = null)
        {
            var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
            string line;
            try { line = $"{name} = {Fmt(f())}"; }
            catch (System.Exception ex)
            {
                var e = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                line = $"{name} EXC {e.GetType().Name}: {e.Message}";
            }
            line = OneLine(line);
            (log ?? _log).Add(line);
            ed?.WriteMessage($"\n{Prefix} {line}\n");
        }

        /// <summary>Komunikat wieloliniowy (np. ArgumentException) → jedna linia zrzutu.</summary>
        private static string OneLine(string s)
            => s.Replace(((char)13).ToString(), "").Replace(((char)10).ToString(), " | ");

        private static void Step2(List<string> lines, Func<List<string>> f)
        {
            try { lines.AddRange(f()); }
            catch (System.Exception ex)
            {
                var e = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                lines.Add(OneLine($"EXC {e.GetType().Name}: {e.Message}"));
            }
        }

        private static Assembly Plugin
        {
            get
            {
                if (_plugin != null) return _plugin;
                var all = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => { var n = a.GetName().Name; return n == "NaSmartRc" || n == "BricsCadRc"; })
                    .ToList();
                if (all.Count != 1)
                    throw new InvalidOperationException(
                        $"oczekiwano dokładnie jednej wtyczki (NaSmartRc albo BricsCadRc), wczytane: {all.Count} " +
                        $"({string.Join("; ", all.Select(a => a.GetName().Name))})");
                return _plugin = all[0];
            }
        }

        private static string SafeLoc()
        {
            try { return Plugin.GetName().Name + " | " + Plugin.Location; }
            catch (System.Exception ex) { return "BŁĄD: " + ex.Message; }
        }

        private static Type PT(string shortName) => Plugin.GetType("BricsCadRc.Core." + shortName, true);

        private static object NewObj(string shortName) => Activator.CreateInstance(PT(shortName));

        private static object Call(string typeName, string method, params object[] args)
        {
            var t = PT(typeName);
            var cands = t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == method)
                .Where(m =>
                {
                    var ps = m.GetParameters();
                    return ps.Length >= args.Length && ps.Skip(args.Length).All(p => p.IsOptional);
                })
                .ToList();
            if (cands.Count != 1)
                throw new MissingMethodException($"{typeName}.{method}: pasujących przeciążeń {cands.Count}");
            var pars = cands[0].GetParameters();
            var full = new object[pars.Length];
            for (int i = 0; i < pars.Length; i++)
                full[i] = i < args.Length ? args[i] : pars[i].DefaultValue;
            try { return cands[0].Invoke(null, full); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private static object Get(object o, string name)
        {
            var t = o.GetType();
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) return p.GetValue(o);
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f.GetValue(o);
            throw new MissingMemberException(t.Name, name);
        }

        private static void Set(object o, string name, object v)
        {
            var p = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new MissingMemberException(o.GetType().Name, name);
            var target = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            object val = v == null || target.IsInstanceOfType(v) ? v : Convert.ChangeType(v, target, Inv);
            p.SetValue(o, val);
        }

        /// <summary>Publiczne właściwości obiektu (posortowane po nazwie), wartości znormalizowane.</summary>
        private static string Props(object o)
        {
            var sb = new StringBuilder();
            foreach (var p in o.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                               .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                               .OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                object v;
                try { v = p.GetValue(o); }
                catch (System.Exception ex) { v = "EXC " + ex.GetType().Name; }
                sb.Append(p.Name).Append('=').Append(Fmt(v)).Append("; ");
            }
            foreach (var f in o.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public)
                               .OrderBy(f => f.Name, StringComparer.Ordinal))
                sb.Append(f.Name).Append('=').Append(Fmt(f.GetValue(o))).Append("; ");
            return sb.ToString();
        }

        internal static string Fmt(object v)
        {
            switch (v)
            {
                case null: return "null";
                case double d: return DrawingDump.F(d);
                case float fl: return DrawingDump.F(fl);
                case string s: return s;
                case ObjectId id: return id.IsNull ? "ObjectId.Null" : "ObjectId";
                case Point3d p: return DrawingDump.P(p);
                case IEnumerable e:
                    return "[" + string.Join(",", e.Cast<object>().Select(Fmt)) + "]";
                case IFormattable f: return f.ToString(null, Inv);
                default: return v.ToString();
            }
        }
    }
}
