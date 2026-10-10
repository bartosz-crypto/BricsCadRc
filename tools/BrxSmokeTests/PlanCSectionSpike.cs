// ETAP 0 — spike bezpiecznika przekrojów (docs/PLAN_BEZPIECZNIK_PRZEKROJOW_2026-10-10.md, krok 0). BEZ zmian we wtyczce.
// Uruchamiany skryptem planc-sgspike.scr (run-planc-sgspike.ps1). Komendy RCSPIKE_SG_*.
//
// Sprawdza w prawdziwym BricsCAD V25 (/automation):
//  - nazwy komend (GlobalCommandName) w DocumentCollection.DocumentLockModeChanged dla -PLOT, EXPORTPDF, PLOT, PRINT, PUBLISH,
//    -PUBLISH (komendy z oknem — osobne przebiegi z Veto, bo okno w /automation zawiesza skrypt);
//  - czy e.Veto() przerywa komendę (plik PDF nie powstaje) — z poziomu skryptu i z LISP (command …);
//  - czy odczyt bazy (OpenCloseTransaction, przestrzeń modelu) w handlerze działa;
//  - czy warstwa IsPlottable = false nie trafia do PDF (-PLOT i EXPORTPDF: rozmiar pliku z 3000 linii na warstwie
//    niedrukowanej ≈ bez nich, na drukowanej — wyraźnie większy);
//  - PlotReactorManager (BeginPlot / EndPlot / PlotCancelled), Publisher (AboutToBeginPublishing, …Background…), BACKGROUNDPLOT.
// Log: planc-sgspike-<tag>.txt obok DLL testów (zapis po każdym wpisie — także, gdy skrypt zawiśnie).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using Application = Bricscad.ApplicationServices.Application;

namespace BricsCadRc.BrxSmokeTests
{
    public class PlanCSectionSpike
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const string NoPlotLayer = "SPIKE-NOPLOT";
        private const string PlotLayer = "SPIKE-PLOT";

        private static readonly List<string> _log = new List<string>();
        private static string _tag = "main";
        private static int _seq;
        private static bool _subscribed;
        private static bool _veto;
        private static int _vetoCount;
        private static readonly HashSet<string> VetoNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PLOT", "-PLOT", "PRINT", "EXPORTPDF", "-EXPORTPDF", "PUBLISH", "-PUBLISH",
        };

        private static string OutDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        private static string LogPath => Path.Combine(OutDir, "planc-sgspike-" + _tag + ".txt");

        private static void L(string line)
        {
            _seq++;
            _log.Add(_seq.ToString("D4", Inv) + " " + line);
            try { File.WriteAllLines(LogPath, _log, new UTF8Encoding(false)); }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("PlanCSectionSpike.L: " + ex.Message); }
        }

        private static string Var(string name)
        {
            try { return Convert.ToString(Application.GetSystemVariable(name), Inv); }
            catch (System.Exception ex) { return "?" + ex.Message; }
        }

        private static string Norm(string cmd) => (cmd ?? "").TrimStart('_', '.').ToUpperInvariant();

        // ================================================================
        // SETUP — subskrypcje, scena
        // ================================================================

        [CommandMethod("RCSPIKE_SG_SETUP", CommandFlags.Modal)]
        public void Setup()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var pr = doc.Editor.GetString(new PromptStringOptions("\nTag logu: ") { AllowSpaces = false });
            if (pr.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(pr.StringResult)) _tag = pr.StringResult.Trim();
            _log.Clear();
            _seq = 0;
            L("SPIKE sgspike tag=" + _tag + " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
            L("BACKGROUNDPLOT=" + Var("BACKGROUNDPLOT") + " FILEDIA=" + Var("FILEDIA") + " CMDDIA=" + Var("CMDDIA")
              + " PDFLAYOUTSTOEXPORT=" + Var("PDFLAYOUTSTOEXPORT") + " ACADVER=" + Var("ACADVER"));
            Subscribe(doc);
            Scene(doc.Database);
            L("SETUP OK");
        }

        private static void Subscribe(Document doc)
        {
            if (_subscribed) return;
            _subscribed = true;
            var dm = Application.DocumentManager;
            dm.DocumentLockModeChanged += OnLockModeChanged;
            dm.DocumentLockModeChangeVetoed += (s, e) => L($"EV LockModeChangeVetoed cmd={e.GlobalCommandName}");
            doc.CommandWillStart += (s, e) => L($"CMD WillStart {e.GlobalCommandName} CMDNAMES={Var("CMDNAMES")}");
            doc.CommandEnded += (s, e) => L($"CMD Ended {e.GlobalCommandName}");
            doc.CommandCancelled += (s, e) => L($"CMD Cancelled {e.GlobalCommandName}");
            doc.CommandFailed += (s, e) => L($"CMD Failed {e.GlobalCommandName}");
            try
            {
                var prm = new Bricscad.PlottingServices.PlotReactorManager();
                prm.BeginPlot += (s, e) => L($"PLOT BeginPlot type={e.BRX_PlotType} CMDNAMES={Var("CMDNAMES")}");
                prm.EndPlot += (s, e) => L("PLOT EndPlot");
                prm.PlotCancelled += (s, e) => L("PLOT PlotCancelled");
                _keep.Add(prm);
                L("PlotReactorManager: OK");
            }
            catch (System.Exception ex) { L("PlotReactorManager: " + ex.GetType().Name + " " + ex.Message); }
            try
            {
                var pub = Application.Publisher;
                pub.AboutToBeginPublishing += (s, e) => L($"PUB AboutToBeginPublishing background={e.JobWillPublishInBackground}");
                pub.AboutToBeginBackgroundPublishing += (s, e) => L("PUB AboutToBeginBackgroundPublishing");
                pub.EndPublish += (s, e) => L("PUB EndPublish");
                pub.CancelledOrFailedPublishing += (s, e) => L("PUB CancelledOrFailedPublishing");
                L("Publisher: OK");
            }
            catch (System.Exception ex) { L("Publisher: " + ex.GetType().Name + " " + ex.Message); }
        }

        private static readonly List<object> _keep = new List<object>();

        private static void OnLockModeChanged(object sender, DocumentLockModeChangedEventArgs e)
        {
            string cmd = e.GlobalCommandName;
            string dbRead;
            try
            {
                // odczyt bazy w handlerze (tylko odczyt, bez zapisów)
                var db = e.Document?.Database;
                int n = 0;
                if (db != null)
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                        foreach (ObjectId id in ms) if (!id.IsErased) n++;
                        tr.Commit();
                    }
                dbRead = "OK n=" + n.ToString(Inv);
            }
            catch (System.Exception ex) { dbRead = "BŁĄD " + ex.GetType().Name + " " + ex.Message; }

            bool match = VetoNames.Contains(Norm(cmd));
            string vetoed = "";
            if (_veto && match)
            {
                try { e.Veto(); _vetoCount++; vetoed = " VETO→IsVetoed=" + e.IsVetoed; }
                catch (System.Exception ex) { vetoed = " VETO-BŁĄD " + ex.GetType().Name + " " + ex.Message; }
            }
            L($"EV LockModeChanged cmd='{cmd}' my={e.MyPreviousMode}->{e.MyCurrentMode} cur={e.CurrentMode} " +
              $"match={match} db={dbRead}{vetoed}");
        }

        /// <summary>Scena: kwadrat 1000 × 1000 i krzyż na warstwie drukowanej (zakres rysunku stały dla ZOOM E).</summary>
        private static void Scene(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayer(tr, db, PlotLayer, true);
                EnsureLayer(tr, db, NoPlotLayer, false);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var pl = new Polyline { Layer = PlotLayer };
                pl.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(1000, 0), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(1000, 1000), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(0, 1000), 0, 0, 0);
                pl.Closed = true;
                ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                var l1 = new Line(new Point3d(0, 0, 0), new Point3d(1000, 1000, 0)) { Layer = PlotLayer };
                ms.AppendEntity(l1); tr.AddNewlyCreatedDBObject(l1, true);
                tr.Commit();
            }
        }

        private static void EnsureLayer(Transaction tr, Database db, string name, bool plottable)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name))
            {
                var rec = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
                rec.IsPlottable = plottable;
                return;
            }
            lt.UpgradeOpen();
            var r = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, 1), IsPlottable = plottable };
            lt.Add(r);
            tr.AddNewlyCreatedDBObject(r, true);
        }

        // ================================================================
        // Warstwa niedrukowana
        // ================================================================

        /// <summary>3000 linii (deterministycznie) na warstwie SPIKE-NOPLOT (IsPlottable = false), w zakresie kwadratu.</summary>
        [CommandMethod("RCSPIKE_SG_NOPLOT", CommandFlags.Modal)]
        public void NoPlot()
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var rnd = new Random(12345);
                for (int i = 0; i < 3000; i++)
                {
                    var ln = new Line(new Point3d(rnd.NextDouble() * 1000, rnd.NextDouble() * 1000, 0),
                                      new Point3d(rnd.NextDouble() * 1000, rnd.NextDouble() * 1000, 0)) { Layer = NoPlotLayer };
                    ms.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
                }
                tr.Commit();
            }
            L("NOPLOT: 3000 linii na " + NoPlotLayer + " (IsPlottable=false)");
        }

        [CommandMethod("RCSPIKE_SG_PLOTON", CommandFlags.Modal)]
        public void PlotOn() => SetPlottable(true);

        [CommandMethod("RCSPIKE_SG_PLOTOFF", CommandFlags.Modal)]
        public void PlotOff() => SetPlottable(false);

        private static void SetPlottable(bool on)
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayer(tr, db, NoPlotLayer, on);
                tr.Commit();
            }
            L(NoPlotLayer + ".IsPlottable=" + on);
        }

        // ================================================================
        // Veto, pliki, znaczniki
        // ================================================================

        [CommandMethod("RCSPIKE_SG_VETO", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void Veto()
        {
            var ed = Application.DocumentManager.MdiActiveDocument.Editor;
            var pr = ed.GetInteger(new PromptIntegerOptions("\nVeto (1 = włącz, 0 = wyłącz): "));
            _veto = pr.Status == PromptStatus.OK && pr.Value != 0;
            L("VETO=" + _veto);
        }

        [CommandMethod("RCSPIKE_SG_MARK", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void Mark()
        {
            var ed = Application.DocumentManager.MdiActiveDocument.Editor;
            var pr = ed.GetString(new PromptStringOptions("\nZnacznik: ") { AllowSpaces = false });
            L("MARK " + (pr.Status == PromptStatus.OK ? pr.StringResult : "?") + " (skrypt doszedł tutaj) veto=" + _vetoCount);
        }

        /// <summary>Rozmiar pliku PDF z katalogu wyników (nazwa z wiersza skryptu): „FILE x.pdf size=… / BRAK”.</summary>
        [CommandMethod("RCSPIKE_SG_FILE", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void FileInfo()
        {
            var ed = Application.DocumentManager.MdiActiveDocument.Editor;
            var pr = ed.GetString(new PromptStringOptions("\nPlik: ") { AllowSpaces = false });
            if (pr.Status != PromptStatus.OK) return;
            string p = Path.Combine(Path.GetTempPath(), "rc-sgspike", pr.StringResult);
            L("FILE " + pr.StringResult + (File.Exists(p) ? " size=" + new System.IO.FileInfo(p).Length.ToString(Inv) : " BRAK"));
        }

        [CommandMethod("RCSPIKE_SG_END", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void End()
        {
            L("VETO wykonane: " + _vetoCount.ToString(Inv));
            L("END");
        }
    }
}
