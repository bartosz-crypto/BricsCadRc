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
                var dl = (Layout)tr.GetObject(layId, OpenMode.ForWrite);
                CopyPlotSettings((Layout)ttr.GetObject(tplLayId, OpenMode.ForRead), dl, silent);
                rep.Messages.Add($"wydruk: {dl.PlotConfigurationName} / {dl.CanonicalMediaName} / {dl.CurrentStyleSheet}");
                ttr.Commit();
                tr.Commit();
            }
            try { Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor.Regen(); }
            catch (System.Exception ex) { Log.Error("RcTemplateEngine.CreateSheet.Regen", ex); }
            try { lm.CurrentLayout = "Model"; } catch (System.Exception ex) { Log.Error("RcTemplateEngine.CreateSheet.Back", ex); }
            rep.Layout = name;
            rep.Messages.Add($"layout arkusza „{name}” z szablonu (czysty: A1-BL, notatki ogólne, tabela rewizji; bez rzutni)");
        }

        /// <summary>
        /// RC_LAYOUT (decyzja użytkownika 2026-10-19): kolejny arkusz jako kopia arkusza bazowego — ustawienia wydruku z arkusza
        /// bazowego, treść <paramref name="content"/> (obiekty przestrzeni papieru arkusza bazowego bez rzutni: A1-BL, notatki, tabela
        /// rewizji) skopiowana DeepClone. Layout o tej nazwie — usuwany wcześniej. Zwraca layout i mapę oryginał → kopia.
        /// Wywołanie w kontekście komendy (LayoutManager); na końcu aktywny jest nowy layout.
        /// </summary>
        internal static ObjectId CreateSheetFrom(Database db, ObjectId baseLayId, string name, int tabOrder, ObjectIdCollection content,
                                                 Dictionary<ObjectId, ObjectId> clones, Report rep)
        {
            var lm = LayoutManager.Current;
            try { lm.CurrentLayout = "Model"; } catch (System.Exception ex) { Log.Error("RcTemplateEngine.CreateSheetFrom.Model", ex); }
            bool exists;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                exists = ((DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead)).Contains(name);
                tr.Commit();
            }
            if (exists)
            {
                try { lm.DeleteLayout(name); }
                catch (System.Exception ex) { Log.Error("RcTemplateEngine.CreateSheetFrom.Delete", ex); rep.Messages.Add($"layout „{name}” — nie usunięto ({ex.Message})"); name += "~new"; }
            }
            ObjectId layId = lm.CreateLayout(name);
            // ustawienia wydruku na AKTYWNYM layoucie (jak CreateSheet)
            try { lm.CurrentLayout = name; } catch (System.Exception ex) { Log.Error("RcTemplateEngine.CreateSheetFrom.Activate", ex); }
            ObjectId psId;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lay = (Layout)tr.GetObject(layId, OpenMode.ForWrite);
                // „Create viewport in new layouts” — BricsCAD dokłada rzutnię całego modelu (2026-10-19: pusta rzutnia na arkuszu);
                // zostaje tylko rzutnia arkusza (najmniejszy uchwyt)
                var nbtr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForRead);
                var vps = new List<ObjectId>();
                foreach (ObjectId id in nbtr)
                    if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is Viewport) vps.Add(id);
                foreach (var id in vps.OrderBy(v => v.Handle.Value).Skip(1))
                    tr.GetObject(id, OpenMode.ForWrite).Erase();
                CopyPlotSettings((Layout)tr.GetObject(baseLayId, OpenMode.ForRead), lay, rep);
                try { lay.TabOrder = tabOrder; } catch (System.Exception ex) { Log.Error("RcTemplateEngine.CreateSheetFrom.Tab", ex); }
                psId = lay.BlockTableRecordId;
                tr.Commit();
            }
            if (content != null && content.Count > 0)
                using (var map = new IdMapping())
                {
                    db.DeepCloneObjects(content, psId, map, false);
                    foreach (IdPair p in map)
                        if (p.IsCloned && p.IsPrimary && clones != null) clones[p.Key] = p.Value;
                }
            var silent = new Report();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                CopyPlotSettings((Layout)tr.GetObject(baseLayId, OpenMode.ForRead), (Layout)tr.GetObject(layId, OpenMode.ForWrite), silent);
                tr.Commit();
            }
            return layId;
        }

        // ── Drawn / Checked / data (decyzja 2026-10-15) ─────────────────────────────

        /// <summary>Ostatnio wpisane „Drawn” / „Checked” (%APPDATA%\NaSmartRc\sheet.txt) — podpowiedź przy kolejnym RC_PRZYGOTUJ_GA.</summary>
        public static (string drawn, string chk) LoadPeople()
        {
            string d = "", c = "";
            try
            {
                if (File.Exists(PeopleFile))
                    foreach (var line in File.ReadAllLines(PeopleFile))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                        if (k == "Drawn") d = v; else if (k == "Checked") c = v;
                    }
            }
            catch (System.Exception ex) { Log.Error("RcTemplateEngine.LoadPeople", ex); }
            return (CleanInitials(d), CleanInitials(c));
        }

        /// <summary>Inicjały: litery, cyfry, spacja, „.” „-” „/” i ampersand, maks. 20 znaków; inne (np. „_TK'._ZOOM” z kolejki poleceń) — puste.</summary>
        public static string CleanInitials(string v)
        {
            v = (v ?? "").Trim().ToUpperInvariant();
            return Regex.IsMatch(v, @"^[A-Z0-9 .\-/&]{1,20}$") ? v : "";
        }

        public static void SavePeople(string drawn, string chk)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PeopleFile));
                File.WriteAllText(PeopleFile, $"Drawn={CleanInitials(drawn)}{Environment.NewLine}Checked={CleanInitials(chk)}{Environment.NewLine}");
            }
            catch (System.Exception ex) { Log.Error("RcTemplateEngine.SavePeople", ex); }
        }

        private static string PeopleFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), App.ProductInfo.FolderName, "sheet.txt");

        /// <summary>Data w bloku tytułowym: „OCTOBER 2026”.</summary>
        public static string TitleDate(DateTime d) => d.ToString("MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-GB")).ToUpperInvariant();

        /// <summary>Data w tabeli rewizji: „15.10.26”.</summary>
        public static string RevDate(DateTime d) => d.ToString("dd.MM.yy", System.Globalization.CultureInfo.InvariantCulture);

        private static readonly Regex RevDateRx = new Regex(@"^\d{1,2}\.\d{1,2}\.\d{2,4}$");

        /// <summary>
        /// Arkusz RC: A1-BL DRAWN / CHECKED (puste — bez zmian) i DATE (miesiąc + rok), w tabeli rewizji (MText na TEXTNOTES)
        /// kolumny By / Chkd (teksty równe dawnym DRAWN / CHECKED) i Date (dd.mm.rr).
        /// </summary>
        public static void FillSheetInfo(Database db, string layoutName, string drawn, string chk, DateTime date, Report rep)
        {
            drawn = CleanInitials(drawn);
            chk = CleanInitials(chk);
            int n = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ld = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                if (!ld.Contains(layoutName)) { tr.Commit(); return; }
                var lay = (Layout)tr.GetObject(ld.GetAt(layoutName), OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForRead);
                string oldD = null, oldC = null;
                foreach (ObjectId id in btr)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    if (!(tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) is BlockTableRecord bd)
                        || !string.Equals(bd.Name, GaTitleEngine.TitleBlockName, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (ObjectId aid in br.AttributeCollection)
                    {
                        if (!(tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar)) continue;
                        string tag = ar.Tag.ToUpperInvariant();
                        string v = tag == "DRAWN" && drawn.Length > 0 ? drawn
                                 : tag == "CHECKED" && chk.Length > 0 ? chk
                                 : tag == "DATE" ? TitleDate(date) : null;
                        if (tag == "DRAWN") oldD = ar.TextString?.Trim();
                        if (tag == "CHECKED") oldC = ar.TextString?.Trim();
                        if (v == null || v == ar.TextString) continue;
                        ar.UpgradeOpen();
                        ar.TextString = v;
                        n++;
                    }
                }
                foreach (ObjectId id in btr)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is MText mt)) continue;
                    if (!mt.Layer.Equals("TEXTNOTES", StringComparison.OrdinalIgnoreCase)) continue;
                    string t = (mt.Text ?? "").Trim();
                    string v = RevDateRx.IsMatch(t) ? RevDate(date)
                             : drawn.Length > 0 && !string.IsNullOrEmpty(oldD) && t.Equals(oldD, StringComparison.OrdinalIgnoreCase) ? drawn
                             : chk.Length > 0 && !string.IsNullOrEmpty(oldC) && t.Equals(oldC, StringComparison.OrdinalIgnoreCase) ? chk
                             : null;
                    if (v == null || v == t) continue;
                    mt.UpgradeOpen();
                    mt.Contents = v;
                    n++;
                }
                tr.Commit();
            }
            rep.Messages.Add($"arkusz {layoutName}: Drawn {(drawn.Length > 0 ? drawn : "bez zmian")}, Checked {(chk.Length > 0 ? chk : "bez zmian")}, data {TitleDate(date)} / {RevDate(date)} ({n} pól)");
        }

        /// <summary>
        /// Tabela rewizji arkusza RC: kolumna App = APPROVED z bloku A1-BL (decyzja 2026-10-16; wołane po skopiowaniu tekstów z GA).
        /// Wiersz rewizji = MText na TEXTNOTES na wysokości tekstu z datą (dd.mm.rr); App = skrajny prawy z nich.
        /// </summary>
        public static void FillRevApproved(Database db, string layoutName, Report rep)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var ld = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            if (!ld.Contains(layoutName)) { tr.Commit(); return; }
            var lay = (Layout)tr.GetObject(ld.GetAt(layoutName), OpenMode.ForRead);
            var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForRead);
            string appr = null;
            var notes = new List<MText>();
            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                var o = tr.GetObject(id, OpenMode.ForRead);
                if (o is MText mt && mt.Layer.Equals("TEXTNOTES", StringComparison.OrdinalIgnoreCase)) notes.Add(mt);
                if (o is BlockReference br && tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) is BlockTableRecord bd
                    && string.Equals(bd.Name, GaTitleEngine.TitleBlockName, StringComparison.OrdinalIgnoreCase))
                    foreach (ObjectId aid in br.AttributeCollection)
                        if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar && ar.Tag.Equals("APPROVED", StringComparison.OrdinalIgnoreCase))
                            appr = CleanInitials(ar.TextString);
            }
            var date = notes.FirstOrDefault(m => RevDateRx.IsMatch((m.Text ?? "").Trim()));
            if (string.IsNullOrEmpty(appr) || date == null) { tr.Commit(); return; }
            var app = notes.Where(m => Math.Abs(m.Location.Y - date.Location.Y) < 1.0).OrderByDescending(m => m.Location.X).FirstOrDefault();
            if (app == null || app == date || (app.Text ?? "").Trim() == appr) { tr.Commit(); return; }
            app.UpgradeOpen();
            app.Contents = appr;
            tr.Commit();
            rep.Messages.Add($"arkusz {layoutName}: App w tabeli rewizji = {appr} (APPROVED z GA)");
        }

        /// <summary>
        /// Ustawienia wydruku jak w szablonie. Najpierw kopia 1:1 przez obiekt PlotSettings (jak import Page Setup — bez walidacji
        /// pc3, dokładnie to, co ma layout szablonu: DWG To PDF.pc3, ISO full bleed A1, speedeck-mono.ctb, obszar Layout, 1:1).
        /// Dopiero gdy arkusz się nie zgadza — PlotSettingsValidator krok po kroku (z zapasowymi nazwami arkusza A1).
        /// </summary>
        private static void CopyPlotSettings(Layout src, Layout dst, Report rep)
        {
            void Try(string what, Action a)
            {
                try { a(); }
                catch (System.Exception ex) { Log.Error("RcTemplateEngine.Plot." + what, ex); rep.Messages.Add($"wydruk: {what} — nie ustawiono ({ex.Message})"); }
            }
            Try("kopia ustawień", () =>
            {
                using var ps = new PlotSettings(false);
                ps.CopyFrom(src);
                dst.CopyFrom(ps);
            });
            if (string.Equals(dst.CanonicalMediaName, src.CanonicalMediaName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(dst.PlotConfigurationName, src.PlotConfigurationName, StringComparison.OrdinalIgnoreCase))
                return;

            var psv = PlotSettingsValidator.Current;
            Try("drukarka", () => psv.SetPlotConfigurationName(dst, src.PlotConfigurationName, null));
            Try("lista", () => psv.RefreshLists(dst));
            bool media = false;
            foreach (var m in new[] { src.CanonicalMediaName, "ISO_full_bleed_A1_(841.00_x_594.00_MM)", "ISO_A1_(841.00_x_594.00_MM)",
                                      "ISO_expand_A1_(841.00_x_594.00_MM)", "ISO_full_bleed_A1_(594.00_x_841.00_MM)" })
            {
                if (string.IsNullOrEmpty(m)) continue;
                try { psv.SetCanonicalMediaName(dst, m); media = true; break; }
                catch (System.Exception ex) { Log.Error("RcTemplateEngine.Plot.arkusz " + m, ex); }
            }
            if (!media) rep.Messages.Add($"wydruk: brak arkusza A1 dla „{src.PlotConfigurationName}” — ustaw Page Setup ręcznie");
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
            // jak w szablonie: obszar „Layout”, bez centrowania, początek (0,0)
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
