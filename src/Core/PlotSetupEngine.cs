using System;
using System.Collections.Generic;
using System.IO;
using Teigha.Colors;
using Teigha.DatabaseServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Ustawienia wydruku jak w rysunku wzorcowym RC (322-324, decyzja 2026-10-15) — RC_USTAW_WYDRUK i koniec RC_PRZYGOTUJ_GA:
    ///   • PSLTSCALE = 0 na każdym layoucie (wzorzec: 0; u nas było 1 → typy linii w rzutniach skalowane do papieru, kreskowane),
    ///   • rzutnie (poza rzutnią arkusza) na warstwie PCN-VIEWPORT (czerwona, 0,00 mm, NIE drukowana) i z zablokowanym widokiem
    ///     (u nas bywały na SD-PILED-RAFT — ramki rzutni szły na wydruk),
    ///   • znaczniki przekroju SD-SECTION: warstwa 0,00 mm, obiekty kolor 2 (żółty) i 0,00 mm — jak we wzorcu,
    ///   • warstwy prętów RC-SLAB-BOT/TOP-01/02 w kolorze 5 (niebieski jak w ASD; speedeck-mono.ctb drukuje czarno, grubość wg KOLORU:
    ///     5 → 0,80 mm) — przywraca też kolor po próbie z magentą (6) z 2026-10-15,
    ///   • speedeck-mono.ctb (zasób wtyczki, z ASD) kopiowany do PlotStyles BricsCAD, gdy go tam nie ma (istniejący — bez zmian).
    /// Ustawienia strony (DWG To PDF.pc3, ISO full bleed A1, speedeck-mono.ctb) daje szablon arkusza (RcTemplateEngine).
    /// </summary>
    public static class PlotSetupEngine
    {
        public const string ViewportLayer = "PCN-VIEWPORT";
        public const string PlotStyleTable = "speedeck-mono.ctb";

        public sealed class Result
        {
            public int Layouts, Viewports, SectionMarks, BarLayers, LabelTexts, ThawedTags, VisualStyles;
            public List<string> Messages = new List<string>();
        }

        public static Result Apply(Database db)
        {
            var res = new Result();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureViewportLayer(tr, db);
                FixSectionMarks(tr, db, res);
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (var name in LayerManager.BarLayers)
                {
                    if (!lt.Has(name)) continue;
                    var rec = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForRead);
                    if (rec.Color.IsByAci && rec.Color.ColorIndex == LayerManager.BarLayerColor) continue;
                    rec.UpgradeOpen();
                    rec.Color = Color.FromColorIndex(ColorMethod.ByAci, LayerManager.BarLayerColor);
                    res.BarLayers++;
                }

                ObjectId wire = WireframeStyle(tr, db);
                var ld = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry e in ld)
                {
                    var lay = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
                    if (lay.ModelType) continue;
                    res.Layouts++;
                    if (!string.IsNullOrEmpty(lay.CurrentStyleSheet)
                        && !lay.CurrentStyleSheet.Equals(PlotStyleTable, StringComparison.OrdinalIgnoreCase))
                        res.Messages.Add($"layout {lay.LayoutName}: tabela pisaków {lay.CurrentStyleSheet} (wzorzec: {PlotStyleTable}) — sprawdź Page Setup");
                    var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForRead);
                    // rzutnia arkusza = pierwsza rzutnia w layoucie (najmniejszy uchwyt) — bez zmian
                    ObjectId overall = ObjectId.Null; long minH = long.MaxValue;
                    var vps = new List<ObjectId>();
                    foreach (ObjectId id in btr)
                    {
                        if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Viewport)) continue;
                        vps.Add(id);
                        if (id.Handle.Value < minH) { minH = id.Handle.Value; overall = id; }
                    }
                    foreach (var id in vps)
                    {
                        if (id == overall) continue;
                        var vp = (Viewport)tr.GetObject(id, OpenMode.ForWrite);
                        bool changed = false;
                        if (!vp.Layer.Equals(ViewportLayer, StringComparison.OrdinalIgnoreCase)) { vp.Layer = ViewportLayer; changed = true; }
                        if (!vp.Locked) { vp.Locked = true; changed = true; }
                        // tagi PH i kreskowanie pali (RC_PUNCHING_AUTO) muszą być widoczne w rzutniach — w PLOT 9-10 warstwa
                        // „AP rebar top” była zamrożona w każdej rzutni (VP Freeze) i tagi PH znikały z arkusza (2026-10-15)
                        var thaw = new ObjectIdCollection();
                        foreach (var ln in new[] { PunchingAutoEngine.TagLayer, PunchingAutoEngine.HatchLayer })
                            if (lt.Has(ln) && vp.IsLayerFrozenInViewport(lt[ln])) thaw.Add(lt[ln]);
                        if (thaw.Count > 0) { vp.ThawLayersInViewport(thaw.GetEnumerator()); changed = true; res.ThawedTags += thaw.Count; }
                        // styl wizualny rzutni = 2D Wireframe (2026-10-20): rzutnia utworzona przez BricsCAD przy nowym layoucie kopiuje widok
                        // modelu — po RC_MODEL_3D / SHADEMODE „Building modeling” (render 6); rysowanie modelu w takiej rzutni dawało
                        // „error while rendering entity” przy kreskowaniach i fatal error BricsCAD po przejściu na layout
                        if (!wire.IsNull && vp.VisualStyleId != wire)
                        {
                            try { vp.VisualStyleId = wire; res.VisualStyles++; changed = true; }
                            catch (System.Exception ex) { Log.Error("PlotSetupEngine.VisualStyle", ex); }
                        }
                        if (changed) res.Viewports++;
                    }
                }
                tr.Commit();
            }
            SetPsltscaleOnLayouts(db, res);
            EnsureCtbInstalled(res);
            try { res.LabelTexts = AnnotationEngine.MakeAnnotTextsAsd(db); }
            catch (System.Exception ex) { Log.Error("PlotSetupEngine.LabelTexts", ex); }
            if (res.VisualStyles > 0) res.Messages.Add($"rzutnie przełączone na styl 2D Wireframe (były cieniowane jak widok modelu — błędy rysowania / zawieszenie BricsCAD): {res.VisualStyles}");
            if (res.ThawedTags > 0) res.Messages.Add($"tagi PH (warstwy {PunchingAutoEngine.TagLayer} / {PunchingAutoEngine.HatchLayer}) odmrożone w rzutniach: {res.ThawedTags}");
            if (res.LabelTexts > 0) res.Messages.Add($"opisy rozkładów jak ASD (PH-LABEL, romans, szer. 0,80): {res.LabelTexts} tekstów");
            return res;
        }

        /// <summary>
        /// speedeck-mono.ctb w folderze tabel pisaków BricsCAD (ustawienie „Plot styles path”; zapasowo
        /// %APPDATA%\Bricsys\BricsCAD\V*\*\PlotStyles). Plik już jest — bez zmian.
        /// </summary>
        /// <summary>Styl wizualny „2D Wireframe” (nazwy AutoCAD / BricsCAD); brak — Null.</summary>
        internal static ObjectId WireframeStyle(Transaction tr, Database db)
        {
            try
            {
                var d = (DBDictionary)tr.GetObject(db.VisualStyleDictionaryId, OpenMode.ForRead);
                foreach (var n in new[] { "2dWireframe", "2D Wireframe", "2DWireframe", "Wireframe" })
                    if (d.Contains(n)) return d.GetAt(n);
            }
            catch (System.Exception ex) { Log.Error("PlotSetupEngine.WireframeStyle", ex); }
            return ObjectId.Null;
        }

        public static void EnsureCtbInstalled(Result res)
        {
            try
            {
                var dirs = new List<string>();
                try
                {
                    // przez refleksję — BricsCAD .NET nie ma Application.Preferences (jest COM AcadApplication.Preferences)
                    var appT = typeof(Bricscad.ApplicationServices.Application);
                    var gp = System.Reflection.BindingFlags.GetProperty;
                    object prefs = appT.GetProperty("Preferences")?.GetValue(null);
                    if (prefs == null)
                    {
                        object acad = appT.GetProperty("AcadApplication")?.GetValue(null);
                        prefs = acad?.GetType().InvokeMember("Preferences", gp, null, acad, null);
                    }
                    object files = prefs?.GetType().InvokeMember("Files", System.Reflection.BindingFlags.GetProperty, null, prefs, null);
                    string p = files?.GetType().InvokeMember("PrinterStyleSheetPath", System.Reflection.BindingFlags.GetProperty, null, files, null) as string;
                    if (!string.IsNullOrWhiteSpace(p))
                        foreach (var d in p.Split(';')) if (!string.IsNullOrWhiteSpace(d)) dirs.Add(Environment.ExpandEnvironmentVariables(d.Trim()));
                }
                catch (System.Exception ex) { Log.Error("PlotSetupEngine.CtbPath", ex); }
                if (dirs.Count == 0)
                {
                    string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bricsys", "BricsCAD");
                    if (Directory.Exists(root))
                        foreach (var d in Directory.GetDirectories(root, "PlotStyles", SearchOption.AllDirectories)) dirs.Add(d);
                }
                foreach (var d in dirs)
                    if (File.Exists(Path.Combine(d, PlotStyleTable))) return;
                string dir = dirs.Find(Directory.Exists);
                if (dir == null) { res.Messages.Add($"nie znaleziono folderu tabel pisaków BricsCAD — skopiuj {PlotStyleTable} ręcznie"); return; }
                using var s = typeof(PlotSetupEngine).Assembly.GetManifestResourceStream("BricsCadRc." + PlotStyleTable);
                if (s == null) { res.Messages.Add($"brak {PlotStyleTable} w zasobach wtyczki"); return; }
                using (var f = File.Create(Path.Combine(dir, PlotStyleTable))) s.CopyTo(f);
                res.Messages.Add($"{PlotStyleTable} skopiowany do {dir}");
            }
            catch (System.Exception ex)
            {
                Log.Error("PlotSetupEngine.EnsureCtbInstalled", ex);
                res.Messages.Add($"{PlotStyleTable} — nie skopiowano ({ex.Message})");
            }
        }

        private static void EnsureViewportLayer(Transaction tr, Database db)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            LayerTableRecord rec;
            if (lt.Has(ViewportLayer)) rec = (LayerTableRecord)tr.GetObject(lt[ViewportLayer], OpenMode.ForWrite);
            else
            {
                lt.UpgradeOpen();
                rec = new LayerTableRecord { Name = ViewportLayer, Color = Color.FromColorIndex(ColorMethod.ByAci, 1), LineWeight = LineWeight.LineWeight000 };
                lt.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }
            rec.IsPlottable = false;
        }

        /// <summary>Znaczniki przekroju (SD-SECTION) jak we wzorcu: warstwa 0,00 mm, obiekty kolor 2 i 0,00 mm.</summary>
        private static void FixSectionMarks(Transaction tr, Database db, Result res)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(AutoRebarEngine.SectionMarkLayer)) return;
            var rec = (LayerTableRecord)tr.GetObject(lt[AutoRebarEngine.SectionMarkLayer], OpenMode.ForWrite);
            rec.LineWeight = LineWeight.LineWeight000;
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity en)) continue;
                if (!en.Layer.Equals(AutoRebarEngine.SectionMarkLayer, StringComparison.OrdinalIgnoreCase)) continue;
                if (en.ColorIndex == 2 && en.LineWeight == LineWeight.LineWeight000) continue;
                en.UpgradeOpen();
                en.ColorIndex = 2;
                en.LineWeight = LineWeight.LineWeight000;
                res.SectionMarks++;
            }
        }

        /// <summary>PSLTSCALE jest ustawieniem layoutu — przełączenie na każdy layout, PSLTSCALE = 0, powrót do bieżącego.</summary>
        private static void SetPsltscaleOnLayouts(Database db, Result res)
        {
            var lm = LayoutManager.Current;
            string back = null;
            try { back = lm.CurrentLayout; } catch (System.Exception ex) { Log.Error("PlotSetupEngine.CurrentLayout", ex); }
            var names = new List<string>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ld = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry e in ld)
                    if (!((Layout)tr.GetObject(e.Value, OpenMode.ForRead)).ModelType) names.Add(e.Key);
                tr.Commit();
            }
            int n = 0;
            foreach (var name in names)
            {
                try
                {
                    lm.CurrentLayout = name;
                    if (Convert.ToInt32(Bricscad.ApplicationServices.Application.GetSystemVariable("PSLTSCALE")) != 0)
                    {
                        Bricscad.ApplicationServices.Application.SetSystemVariable("PSLTSCALE", 0);
                        n++;
                    }
                }
                catch (System.Exception ex) { Log.Error("PlotSetupEngine.Psltscale", ex); res.Messages.Add($"layout {name}: PSLTSCALE — nie ustawiono ({ex.Message})"); }
            }
            try { if (!string.IsNullOrEmpty(back)) lm.CurrentLayout = back; }
            catch (System.Exception ex) { Log.Error("PlotSetupEngine.Back", ex); }
            if (n > 0) res.Messages.Add($"PSLTSCALE = 0 na {n} layout(ach)");
        }
    }
}
