using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Szablon arkusza RC przy RC_PRZYGOTUJ_GA (decyzja użytkownika 2026-10-14): w rysunku bez elementów „pliku RC default” program
    /// dokłada je z wbudowanego szablonu (Resources/rc-template.dxf.gz, budowany z przykładowego rysunku RC narzędziem
    /// tools/RcTemplate/build_template.py):
    ///   • tytuły rzutów „REINFORCEMENT DETAILS OF SPEEDECK PILED RAFT FOUNDATION – BOTTOM / TOP LAYER” (model),
    ///   • typowy layout arkusza bez rzutni — blok A1-BL, ramki, notatki ogólne, tabela rewizji, ustawienia wydruku (pc3, A1, ctb),
    ///   • detale PH1–PH9 (model, obok rzutu górnego) z opisami „EXTRA BARS (N/A)” / „APPLICABLE FOR PILES N/A” — RC_PUNCHING_AUTO
    ///     wpisuje do nich liczbę i listę pali.
    /// Style tekstu / wymiarów, warstwy i bloki przychodzą razem z obiektami (WblockCloneObjects, istniejące — bez zmian).
    /// Element już obecny w rysunku — pomijany (ponowne wywołanie niczego nie dubluje).
    /// </summary>
    public static class RcTemplateEngine
    {
        private const string ResourceName = "BricsCadRc.rc-template.dxf.gz";
        public const string DefaultLayoutName = "RC 01";
        private static readonly Regex PhTitleRx = new Regex(@"PILE\s+CONDITION\s+PH\s*[1-9]", RegexOptions.IgnoreCase);

        public sealed class Report
        {
            public int Titles;
            public string Layout;
            public int PhEntities;
            public List<string> Messages = new List<string>();
        }

        /// <summary>Szablon z zasobów wtyczki (Database tylko do odczytu); null — brak zasobu / błąd (komunikat w log).</summary>
        public static Database OpenTemplate()
        {
            try
            {
                using var s = typeof(RcTemplateEngine).Assembly.GetManifestResourceStream(ResourceName);
                if (s == null) return null;
                string tmp = Path.Combine(Path.GetTempPath(), "NaSmartRc_rc-template.dxf");
                using (var gz = new GZipStream(s, CompressionMode.Decompress))
                using (var f = File.Create(tmp))
                    gz.CopyTo(f);
                var db = new Database(false, true);
                db.DxfIn(tmp, null);
                return db;
            }
            catch (System.Exception ex)
            {
                Log.Error("RcTemplateEngine.OpenTemplate", ex);
                return null;
            }
        }

        private static bool IsViewTitle(MText mt, string layer)
        {
            string t = (mt.Text ?? "").ToUpperInvariant();
            return t.Contains("REINFORCEMENT DETAILS") && t.Contains(layer);
        }

        /// <summary>Tytuły rzutów (BOTTOM / TOP LAYER) — przed wstawieniem płyty (GaImportEngine ustawia rzuty nad tytułami).</summary>
        public static void EnsureTitles(Database db, Database tpl, Report rep)
        {
            bool hasB = false, hasT = false;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                    if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is MText mt)
                    {
                        hasB |= IsViewTitle(mt, "BOTTOM LAYER");
                        hasT |= IsViewTitle(mt, "TOP LAYER");
                    }
                tr.Commit();
            }
            if (hasB && hasT) return;
            var ids = new ObjectIdCollection();
            using (var tr = tpl.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(tpl), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                    if (tr.GetObject(id, OpenMode.ForRead) is MText mt
                        && ((!hasB && IsViewTitle(mt, "BOTTOM LAYER")) || (!hasT && IsViewTitle(mt, "TOP LAYER"))))
                        ids.Add(id);
                tr.Commit();
            }
            if (ids.Count == 0) return;
            using var map = new IdMapping();
            tpl.WblockCloneObjects(ids, SymbolUtilityServices.GetBlockModelSpaceId(db), map, DuplicateRecordCloning.Ignore, false);
            rep.Titles = ids.Count;
            rep.Messages.Add($"tytuły rzutów z szablonu: {ids.Count}");
        }

        /// <summary>Czy rysunek ma layout z blokiem tytułowym A1-BL.</summary>
        private static bool HasSheet(Database db)
        {
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var ld = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry e in ld)
            {
                var lay = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
                if (lay.ModelType) continue;
                var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                    if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is BlockReference br
                        && tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) is BlockTableRecord bb
                        && string.Equals(bb.Name, GaTitleEngine.TitleBlockName, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            return false;
        }

        /// <summary>Rewizja z bloku A1-BL szablonu (atrybut REV, np. „C1”); brak — „C1”.</summary>
        public static string TemplateRev(Database tpl)
        {
            try
            {
                using var tr = tpl.TransactionManager.StartOpenCloseTransaction();
                var ld = (DBDictionary)tr.GetObject(tpl.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry e in ld)
                {
                    var lay = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
                    if (lay.ModelType) continue;
                    var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForRead);
                    foreach (ObjectId id in btr)
                        if (tr.GetObject(id, OpenMode.ForRead) is BlockReference br)
                            foreach (ObjectId aid in br.AttributeCollection)
                                if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar
                                    && ar.Tag.Equals("REV", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(ar.TextString))
                                    return ar.TextString.Trim();
                }
            }
            catch (System.Exception ex) { Log.Error("RcTemplateEngine.TemplateRev", ex); }
            return "C1";
        }

        /// <summary>
        /// Czysty layout arkusza RC z szablonu (decyzja użytkownika 2026-10-14): nazwa wg GA (GA0300… → RC0300 + rewizja szablonu,
        /// np. „RC0300C1”), pierwsza zakładka; layout o tej nazwie (poprzednie RC_PRZYGOTUJ_GA) i puste domyślne „Layout1/2” —
        /// usuwane. Bez rzutni; blok A1-BL wypełnia dalej GaTitleEngine (teksty z GA). Wywołanie w kontekście komendy (LayoutManager).
        /// </summary>
        public static void CreateSheet(Database db, Database tpl, string name, Report rep)
        {
            ObjectId tplLayId = ObjectId.Null;
            var ids = new ObjectIdCollection();
            using (var tr = tpl.TransactionManager.StartOpenCloseTransaction())
            {
                var ld = (DBDictionary)tr.GetObject(tpl.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry e in ld)
                {
                    var lay = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
                    if (lay.ModelType) continue;
                    tplLayId = e.Value;
                    var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForRead);
                    foreach (ObjectId id in btr)
                        if (!id.IsErased && !(tr.GetObject(id, OpenMode.ForRead) is Viewport)) ids.Add(id);
                    break;
                }
                tr.Commit();
            }
            if (tplLayId.IsNull || ids.Count == 0) { rep.Messages.Add("szablon bez layoutu arkusza — pominięty"); return; }
            if (string.IsNullOrWhiteSpace(name)) name = DefaultLayoutName;

            var lm = LayoutManager.Current;
            try { lm.CurrentLayout = "Model"; } catch (System.Exception ex) { Log.Error("RcTemplateEngine.CreateSheet.Model", ex); }
            // poprzedni arkusz o tej nazwie i puste domyślne zakładki
            var toDelete = new List<string>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ld = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry e in ld)
                {
                    var lay = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
                    if (lay.ModelType) continue;
                    if (string.Equals(lay.LayoutName, name, StringComparison.OrdinalIgnoreCase)) { toDelete.Add(lay.LayoutName); continue; }
                    if (Regex.IsMatch(lay.LayoutName, @"^Layout\d+$", RegexOptions.IgnoreCase))
                    {
                        var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForRead);
                        bool empty = true;
                        foreach (ObjectId id in btr)
                            if (!id.IsErased && !(tr.GetObject(id, OpenMode.ForRead) is Viewport)) { empty = false; break; }
                        if (empty) toDelete.Add(lay.LayoutName);
                    }
                }
                tr.Commit();
            }
            ObjectId layId = lm.CreateLayout(name + "~new");
            foreach (var n in toDelete)
            {
                try { lm.DeleteLayout(n); rep.Messages.Add($"usunięty layout „{n}”"); }
                catch (System.Exception ex) { Log.Error("RcTemplateEngine.CreateSheet.Delete", ex); rep.Messages.Add($"layout „{n}” — nie usunięto ({ex.Message})"); }
            }
            try { lm.RenameLayout(name + "~new", name); }
            catch (System.Exception ex) { Log.Error("RcTemplateEngine.CreateSheet.Rename", ex); rep.Messages.Add($"nazwa „{name}” zajęta — layout „{name}~new”"); name += "~new"; }

            // Ustawienia wydruku dopiero na AKTYWNYM layoucie (jak SDTools WarmUpLayoutPageSetup): ustawione na nieaktywnym
            // BricsCAD nie stosuje — arkusz zostaje domyślny (szare tło, papier w calach → grubości linii ×25.4, teksty jako plamy).
            try { lm.CurrentLayout = name; } catch (System.Exception ex) { Log.Error("RcTemplateEngine.CreateSheet.Activate", ex); }
            ObjectId psId;
            using (var tr = db.TransactionManager.StartTransaction())
            using (var ttr = tpl.TransactionManager.StartOpenCloseTransaction())
            {
                var lay = (Layout)tr.GetObject(layId, OpenMode.ForWrite);
                var src = (Layout)ttr.GetObject(tplLayId, OpenMode.ForRead);
                CopyPlotSettings(src, lay, rep);
                try { lay.TabOrder = 1; } catch (System.Exception ex) { Log.Error("RcTemplateEngine.CreateSheet.Tab", ex); }
                psId = lay.BlockTableRecordId;
                ttr.Commit();
                tr.Commit();
            }
            using (var map = new IdMapping())
                tpl.WblockCloneObjects(ids, psId, map, DuplicateRecordCloning.Ignore, false);
            // drugi raz po wstawieniu treści („Page Setup → Apply”) + regeneracja
            var silent = new Report();
            using (var tr = db.TransactionManager.StartTransaction())
            using (var ttr = tpl.TransactionManager.StartOpenCloseTransaction())
            {
                CopyPlotSettings((Layout)ttr.GetObject(tplLayId, OpenMode.ForRead), (Layout)tr.GetObject(layId, OpenMode.ForWrite), silent);
                ttr.Commit();
                tr.Commit();
            }
            try { Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor.Regen(); }
            catch (System.Exception ex) { Log.Error("RcTemplateEngine.CreateSheet.Regen", ex); }
            try { lm.CurrentLayout = "Model"; } catch (System.Exception ex) { Log.Error("RcTemplateEngine.CreateSheet.Back", ex); }
            rep.Layout = name;
            rep.Messages.Add($"layout arkusza „{name}” z szablonu (czysty: A1-BL, notatki ogólne, tabela rewizji; bez rzutni)");
        }

        /// <summary>Ustawienia wydruku jak w szablonie (pc3, arkusz, tabela pisaków, obrót, skala, obszar, początek) — każde osobno, błąd = pominięte.</summary>
        private static void CopyPlotSettings(Layout src, Layout dst, Report rep)
        {
            var psv = PlotSettingsValidator.Current;
            void Try(string what, Action a)
            {
                try { a(); }
                catch (System.Exception ex) { Log.Error("RcTemplateEngine.Plot." + what, ex); rep.Messages.Add($"wydruk: {what} — nie ustawiono ({ex.Message})"); }
            }
            Try("drukarka", () => psv.SetPlotConfigurationName(dst, src.PlotConfigurationName, null));
            Try("lista", () => psv.RefreshLists(dst));
            Try("arkusz", () => psv.SetCanonicalMediaName(dst, src.CanonicalMediaName));
            Try("pisaki", () => psv.SetCurrentStyleSheet(dst, src.CurrentStyleSheet));
            Try("jednostki", () => psv.SetPlotPaperUnits(dst, src.PlotPaperUnits));
            Try("obrót", () => psv.SetPlotRotation(dst, src.PlotRotation));
            Try("skala", () =>
            {
                if (src.UseStandardScale)
                {
                    psv.SetUseStandardScale(dst, true);
                    psv.SetStdScaleType(dst, src.StdScaleType);
                }
                else
                {
                    psv.SetUseStandardScale(dst, false);
                    psv.SetCustomPrintScale(dst, src.CustomPrintScale);
                }
            });
            // jak w szablonie: obszar „Layout”, bez centrowania, początek (0,0) — „Extents + centruj” przesuwał arkusz względem ramki
            Try("obszar", () => psv.SetPlotType(dst, src.PlotType));
            Try("środek", () => psv.SetPlotCentered(dst, src.PlotCentered));
            Try("początek", () => psv.SetPlotOrigin(dst, src.PlotOrigin));
        }

        /// <summary>Czy w modelu są już detale PH (MText z „PILE CONDITION PHn”).</summary>
        private static bool HasPh(Database db)
        {
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
                if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is MText mt && PhTitleRx.IsMatch(mt.Text ?? ""))
                    return true;
            return false;
        }

        /// <summary>
        /// Detale PH1–PH9 z szablonu: lewy górny róg obwiedni w (left, top). Gdy w rysunku już są — bez zmian.
        /// </summary>
        public static void EnsurePh(Database db, Database tpl, double left, double top, Report rep)
        {
            if (HasPh(db)) return;
            var ids = new ObjectIdCollection();
            double x0 = double.MaxValue, y1 = double.MinValue;
            using (var tr = tpl.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(tpl), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    if (e is MText mt && (mt.Text ?? "").ToUpperInvariant().Contains("REINFORCEMENT DETAILS")) continue;   // tytuły rzutów
                    ids.Add(id);
                    try
                    {
                        var x = e.GeometricExtents;
                        x0 = Math.Min(x0, x.MinPoint.X); y1 = Math.Max(y1, x.MaxPoint.Y);
                    }
                    catch (System.Exception ex) { Log.Error("RcTemplateEngine.EnsurePh.Extents", ex); }
                }
                tr.Commit();
            }
            if (ids.Count == 0 || x0 == double.MaxValue) { rep.Messages.Add("szablon bez detali PH — pominięte"); return; }

            using var map = new IdMapping();
            tpl.WblockCloneObjects(ids, SymbolUtilityServices.GetBlockModelSpaceId(db), map, DuplicateRecordCloning.Ignore, false);
            var move = Matrix3d.Displacement(new Vector3d(left - x0, top - y1, 0));
            var srcSet = new HashSet<ObjectId>(ids.Cast<ObjectId>());
            int n = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (IdPair p in map)
                {
                    if (!p.IsCloned || !p.IsPrimary || !srcSet.Contains(p.Key)) continue;
                    if (tr.GetObject(p.Value, OpenMode.ForWrite) is Entity e) { e.TransformBy(move); n++; }
                }
                tr.Commit();
            }
            rep.PhEntities = n;
            rep.Messages.Add($"detale PH1–PH9 z szablonu ({n} obiektów) — opisy uzupełnia RC_PUNCHING_AUTO");
        }

        /// <summary>
        /// Miejsce detali PH: na prawo od rzutu górnego i jego ramki szablonów (rebar_top), PlanGap dalej; góra = góra rzutu górnego.
        /// null — brak obrysu rzutu górnego.
        /// </summary>
        public static (double left, double top)? PhAnchor(Database db, ObjectId outlineTop)
        {
            if (outlineTop.IsNull || outlineTop.IsErased) return null;
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            if (!(tr.GetObject(outlineTop, OpenMode.ForRead) is Entity o)) return null;
            var ox = o.GeometricExtents;
            double right = ox.MaxPoint.X;
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
                if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is Polyline pl
                    && pl.Layer.Equals("rebar_top", StringComparison.OrdinalIgnoreCase))
                {
                    var fx = pl.GeometricExtents;
                    if (fx.MinPoint.X >= ox.MaxPoint.X - 1.0) right = Math.Max(right, fx.MaxPoint.X);
                }
            tr.Commit();
            return (right + GaImportEngine.PlanGap, ox.MaxPoint.Y);
        }
    }
}
