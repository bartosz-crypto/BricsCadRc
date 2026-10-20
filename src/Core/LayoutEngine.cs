using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// RC_LAYOUT — gotowe arkusze A1 z rzutniami (decyzja użytkownika 2026-10-19, wzór: arkusze PLOT 30 / 20 / 206 / 9-10):
    ///   • zawartość z przestrzeni modelu: rzut dolny i górny 1:50 (obrys RC_GA + opisy wokół + tytuł „REINFORCEMENT DETAILS …”),
    ///     przekroje 1:20 (RC_SECTION, bez znaczników na rzutach), UŻYTE detale PH 1:25 (tytuł bez „N/A”), detale belek
    ///     (RC_BEAM_DETAIL) i otworów (RC_DETAIL) 1:25,
    ///   • rozmieszczenie i podział na arkusze — <see cref="LayoutRules"/>; arkusz bazowy = layout z A1-BL z RC_PRZYGOTUJ_GA,
    ///     kolejne arkusze — jego kopie (numer rysunku +1, nazwa layoutu = numer + rewizja),
    ///   • A1-BL: DRAWING_NUMBER, TITLE_3, SCALE wg zawartości; pod przekrojem z nibem notatka o prętach podłużnych; przy kilku
    ///     arkuszach odsyłacz „… SEE DRG. …” nad CONSTRUCTION ISSUE; na końcu PlotSetupEngine (PCN-VIEWPORT, blokada rzutni).
    /// Obiekty wstawione przez RC_LAYOUT (rzutnie, notatki, odsyłacz, A1-BL arkuszy dodatkowych) mają XData RC_LAYOUT —
    /// ponowne wywołanie usuwa je / arkusze dodatkowe i układa od nowa. Notatki ogólne / płyty / pali / zbrojenia — bez zmian.
    /// </summary>
    public static class LayoutEngine
    {
        public const string XApp = "RC_LAYOUT";
        public const string RoleViewport = "VP", RoleNote = "NOTE", RoleXref = "XREF", RoleSheet = "SHEET";
        public const string NoteLayer = "SD-Text";
        public const string NoteStyle = "ROMANS NARROW";
        /// <summary>Zasięg opisów wokół obrysu rzutu (mm modelu) — wymiary, opisy prętów, pale.</summary>
        public const double PlanMargin = 3500.0;
        /// <summary>Zasięg opisów wokół ramki detalu belki / otworu (mm modelu; strefa szablonów detalu otworu zaczyna się 3000 dalej).</summary>
        public const double DetailMargin = 2500.0;
        /// <summary>Odsyłacz nad CONSTRUCTION ISSUE (arkusz wzorcowy PLOT 9-10, mm papieru).</summary>
        private const double XrefX0 = 721.1, XrefX1 = 810.3, XrefY0 = 252.8, XrefTextX = 723.28, XrefTextTop = 2.6;

        private static readonly Regex PhRx = new Regex(@"PILE\s+CONDITION\s+PH\s*([1-9])(?!\d)", RegexOptions.IgnoreCase);
        private static readonly Regex PhUnusedRx = new Regex(@"APPLICABLE\s+FOR\s+PILE(?:/S|S)?\s+N/A", RegexOptions.IgnoreCase);
        private static readonly Regex DetailRx = new Regex(@"DETAIL\s*'(\d+)'", RegexOptions.IgnoreCase);

        // ── dane ─────────────────────────────────────────────────────────

        internal struct Box
        {
            public double X0, Y0, X1, Y1;
            public static Box Empty => new Box { X0 = double.MaxValue, Y0 = double.MaxValue, X1 = double.MinValue, Y1 = double.MinValue };
            public bool IsEmpty => X0 > X1;
            public double W => X1 - X0;
            public double H => Y1 - Y0;
            public double Cx => (X0 + X1) / 2;
            public double Cy => (Y0 + Y1) / 2;
            public void Add(Box b) { if (b.IsEmpty) return; X0 = Math.Min(X0, b.X0); Y0 = Math.Min(Y0, b.Y0); X1 = Math.Max(X1, b.X1); Y1 = Math.Max(Y1, b.Y1); }
            public Box Grow(double d) => new Box { X0 = X0 - d, Y0 = Y0 - d, X1 = X1 + d, Y1 = Y1 + d };
            public bool Inside(Box b) => X0 >= b.X0 && Y0 >= b.Y0 && X1 <= b.X1 && Y1 <= b.Y1;
            public bool HasPoint(double x, double y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;
            /// <summary>Odległość punktu od prostokąta (0 wewnątrz).</summary>
            public double Dist(double x, double y)
            {
                double dx = Math.Max(0, Math.Max(X0 - x, x - X1)), dy = Math.Max(0, Math.Max(Y0 - y, y - Y1));
                return Math.Sqrt(dx * dx + dy * dy);
            }
        }

        private sealed class Ent
        {
            public ObjectId Id;
            public Box Box;
            public string Layer;
            public bool Claimed;
        }

        internal sealed class Src
        {
            public LayoutRules.Item Item;
            public Box Ext;
            public int Scale;
            public double VpW, VpH;
            public string Letter;
            public int NibDir;
            /// <summary>Przekrój: środki znaczników cięcia (MARK) — na rzucie albo na detalu.</summary>
            public List<(double X, double Y)> Marks = new List<(double, double)>();
            public int Order;
        }

        public sealed class SheetInfo
        {
            public string Layout, Number, Title3, Scale;
            public List<string> Contents = new List<string>();
        }

        /// <summary>Przygotowanie (tylko odczyt): zawartość, rozmieszczenie, arkusz bazowy. Komenda pokazuje i pyta, potem <see cref="Apply"/>.</summary>
        public sealed class Ctx
        {
            internal List<Src> Items = new List<Src>();
            internal LayoutRules.Plan Plan;
            public ObjectId BaseLayoutId = ObjectId.Null;
            public string BaseLayoutName = "";
            internal int BaseTab;
            internal string BaseNumber = "", Rev = "";
            /// <summary>Layouty z poprzedniego RC_LAYOUT (A1-BL z XData RC_LAYOUT) — do usunięcia.</summary>
            public List<string> OldSheets = new List<string>();
            /// <summary>Rzutnie arkusza bazowego bez znacznika RC_LAYOUT (ręczne) — usuwane tylko za zgodą.</summary>
            public int UserViewports;
            /// <summary>Layouty o nazwach kolejnych arkuszy, nie z RC_LAYOUT (zrobione ręcznie) — zastępowane tylko za zgodą.</summary>
            public List<string> Overwrite = new List<string>();
            /// <summary>Warstwy obiektów map zbrojenia (RC_MAP) — zamrożone w rzutniach RC_LAYOUT.</summary>
            public HashSet<string> MapLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public List<SheetInfo> Sheets = new List<SheetInfo>();
            public List<string> Warnings = new List<string>();
            public List<string> Errors = new List<string>();
        }

        // ── przygotowanie ────────────────────────────────────────────────

        public static Ctx Prepare(Database db, string currentLayout)
        {
            var ctx = new Ctx();
            FindBase(db, currentLayout, ctx);
            if (ctx.BaseLayoutId.IsNull) { ctx.Errors.Add("brak arkusza z blokiem A1-BL — najpierw Prepare GA (RC_PRZYGOTUJ_GA)"); return ctx; }

            Collect(db, ctx);

            // 5. wymiary na papierze i rozmieszczenie
            foreach (var s in ctx.Items)
            {
                s.VpW = s.Ext.W / s.Scale + 2 * LayoutRules.Pad;
                s.VpH = s.Ext.H / s.Scale + 2 * LayoutRules.Pad;
                s.Item.W = s.VpW;
                s.Item.H = s.VpH;
                if (s.Item.Kind == LayoutRules.Kind.Section && s.NibDir > 0)
                {
                    s.Item.W = Math.Max(s.VpW, LayoutRules.NibNoteMinWidth);
                    s.Item.H = s.VpH + LayoutRules.NibNoteBox;
                }
            }
            ctx.Items = ctx.Items.OrderBy(s => s.Order).ToList();
            ctx.Plan = LayoutRules.Arrange(ctx.Items.Select(s => s.Item).ToList());
            ctx.Warnings.AddRange(ctx.Plan.Warnings);

            for (int k = 0; k < ctx.Plan.Sheets.Count; k++)
            {
                var sh = ctx.Plan.Sheets[k];
                string num = LayoutRules.IncrementDrawingNumber(ctx.BaseNumber, k);
                ctx.Sheets.Add(new SheetInfo
                {
                    Layout = k == 0 ? ctx.BaseLayoutName : LayoutRules.DrawingSuffix(num) + ctx.Rev,
                    Number = num,
                    Title3 = LayoutRules.Title3(sh),
                    Scale = LayoutRules.ScaleText(sh.Placed.Select(p => LayoutRules.ScaleOf(p.Item.Kind))),
                    Contents = sh.Placed.Select(p => p.Item.Key).ToList()
                });
            }
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ld = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (var sh in ctx.Sheets.Skip(1))
                    if (ld.Contains(sh.Layout) && !ctx.OldSheets.Contains(sh.Layout, StringComparer.OrdinalIgnoreCase)) ctx.Overwrite.Add(sh.Layout);
                tr.Commit();
            }
            return ctx;
        }

        /// <summary>
        /// Zawartość arkuszy z przestrzeni modelu (przekroje, PH, detale, rzuty) → ctx.Items (zakresy w modelu, skale). Płyta przy kilku —
        /// wg TITLE_1 arkusza ctx.BaseLayoutId. Wspólne dla RC_LAYOUT i RC_LAYOUT_TEXTS.
        /// </summary>
        private static void Collect(Database db, Ctx ctx)
        {
            var ents = new List<Ent>();
            var layerUse = new Dictionary<string, (int all, int map)>(StringComparer.OrdinalIgnoreCase);
            var byId = new Dictionary<ObjectId, Ent>();
            var gaTags = new List<(Ent e, string plot, string view)>();
            var planTitles = new List<(Ent e, string view)>();
            var phTitles = new List<(Ent e, int nr, bool used)>();
            var beamParts = new List<(Ent e, string zone, int nr, string role)>();
            var holeFrames = new List<(Ent e, int nr)>();
            var detailTitles = new List<(Ent e, int nr)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    if (e is Xline || e is Ray) continue;
                    Box b = ExtentsOf(e);
                    if (b.IsEmpty) continue;   // obiekt bez zakresu (np. pusty tekst)
                    if (b.W > 1e6 || b.H > 1e6 || double.IsNaN(b.X0)) continue;
                    var en = new Ent { Id = id, Box = b, Layer = e.Layer ?? "" };
                    ents.Add(en);
                    byId[id] = en;
                    try
                    {
                        using (var rb = e.GetXDataForApplication(GaImportEngine.XApp))
                        {
                            var v = rb?.AsArray();
                            if (v != null && v.Length >= 3)
                                gaTags.Add((en, Convert.ToString(v[1].Value, CultureInfo.InvariantCulture), Convert.ToString(v[2].Value, CultureInfo.InvariantCulture)));
                        }
                        // mapy zbrojenia (RC_MAP) — warstwy zamrażane w rzutniach arkuszy (kreskowania map: błędy rysowania BricsCAD)
                        using (var rbm = e.GetXDataForApplication(MapImportEngine.XApp))
                        {
                            layerUse.TryGetValue(en.Layer, out var u);
                            layerUse[en.Layer] = (u.all + 1, u.map + (rbm != null ? 1 : 0));
                        }
                        var bd = BeamStore.ReadDetail(e);
                        if (bd != null && !string.IsNullOrEmpty(bd.Value.zone) && bd.Value.role != BeamStore.RolePlanRef)
                        { beamParts.Add((en, XLink.Norm(bd.Value.zone), bd.Value.nr, bd.Value.role)); continue; }
                        if (bd == null && AutoRebarEngine.TryReadDetailFrame(e, out int hnr, out _, out _)) { holeFrames.Add((en, hnr)); continue; }
                        if (e is MText mt)
                        {
                            string t = mt.Text ?? "";
                            string tu = t.ToUpperInvariant();
                            if (tu.Contains("REINFORCEMENT DETAILS") && (tu.Contains("BOTTOM LAYER") || tu.Contains("TOP LAYER")))
                                planTitles.Add((en, tu.Contains("BOTTOM LAYER") ? "B" : "T"));
                            else if (mt.Layer.Equals(PunchingAutoEngine.TemplateLayer, StringComparison.OrdinalIgnoreCase) && PhRx.IsMatch(t)
                                     && tu.Contains("APPLICABLE"))
                                phTitles.Add((en, int.Parse(PhRx.Match(t).Groups[1].Value, CultureInfo.InvariantCulture), !PhUnusedRx.IsMatch(t)));
                            else if (bd == null && DetailRx.IsMatch(t) && tu.Contains("SCALE"))
                                detailTitles.Add((en, int.Parse(DetailRx.Match(t).Groups[1].Value, CultureInfo.InvariantCulture)));
                        }
                    }
                    catch (System.Exception ex) { Log.Error("LayoutEngine.Prepare.Read", ex); }
                }
                tr.Commit();
            }

            // warstwy zamrażane w rzutniach: tylko takie, na których są WYŁĄCZNIE obiekty map (mapa na „0” itp. — warstwa zostaje)
            foreach (var kv in layerUse)
                if (kv.Value.map > 0 && kv.Value.map == kv.Value.all) ctx.MapLayers.Add(kv.Key);

            int order = 0;
            // 1. przekroje
            foreach (var s in SectionStore.ReadAll(db, false).OrderBy(s => s.Letter, StringComparer.OrdinalIgnoreCase))
            {
                if (s.TitleId.IsNull || s.Status == SectionStatus.Kopia || s.Status == SectionStatus.Uszkodzony
                    || s.Status == SectionStatus.CzesciBezTytulu || !byId.TryGetValue(s.TitleId, out var te))
                {
                    if (!s.TitleId.IsNull) ctx.Warnings.Add($"przekrój {s.Label}: {SectionStore.StatusText(s.Status)} — pominięty");
                    continue;
                }
                var box = te.Box; te.Claimed = true;
                // nib: składniki rekordu (segment z nibem / UB 03) albo opis otuliny nibu (NIBNOTE — przekroje od 2026-10-16)
                bool nib = LayoutRules.IsNibSection(s.Record?.Components);
                var marks = new List<(double, double)>();
                foreach (var (pid, role, _) in s.Parts)
                {
                    if (role == "MARK" && byId.TryGetValue(pid, out var me)) marks.Add((me.Box.Cx, me.Box.Cy));
                    if (role == "MARK" || role == "MARKTXT" || role == AutoRebarEngine.RoleCloud) continue;
                    if (role == "NIBNOTE") nib = true;
                    if (byId.TryGetValue(pid, out var pe)) { box.Add(pe.Box); pe.Claimed = true; }
                }
                // przekrój przez belkę — bez notatki o prętach w nibie (decyzja Bartka 2026-10-20)
                if (LayoutRules.IsBeamSection(s.Record?.Components)) nib = false;
                // decyzja Bartka 2026-10-19: notatka „A-A” — przekrój w kierunku 1, „B-B” — w kierunku 2
                int dir = nib ? LayoutRules.SectionDir(s.Record?.Components, s.Record?.CutAlongX ?? true) : 0;
                if (s.Status == SectionStatus.Nieaktualny) ctx.Warnings.Add($"przekrój {s.Label} jest NIEAKTUALNY — zaktualizuj (RC_SECTION_UPDATE) przed wydrukiem");
                ctx.Items.Add(new Src
                {
                    Item = new LayoutRules.Item { Key = "SECTION " + s.Label, Kind = LayoutRules.Kind.Section, Nib = nib && dir > 0 },
                    Ext = box, Scale = 20, Letter = s.Letter, NibDir = dir, Order = order++, Marks = marks
                });
            }

            // 2. detale PH (wszystkie tytuły jako kotwice — obiekty nieużytych detali nie trafiają do sąsiednich)
            {
                var anchors = phTitles.Select(p =>
                {
                    var b = p.e.Box;
                    var zone = new Box { X0 = b.X0 - 700, Y0 = b.Y0 - 300, X1 = Math.Max(b.X1, b.X0 + 3800) + 1500, Y1 = b.Y1 + 4200 };
                    p.e.Claimed = true;
                    return (p, zone, ax: b.Cx, ay: b.Y1 + 1500, box: b);
                }).ToList();
                var boxes = anchors.Select(a => a.box).ToList();
                foreach (var en in ents)
                {
                    if (en.Claimed || anchors.Count == 0 || en.Box.W > 6000 || en.Box.H > 6000) continue;
                    if (en.Layer.Equals(PunchingAutoEngine.NotUsedLayer, StringComparison.OrdinalIgnoreCase)) { continue; }
                    int best = -1; double bd = double.MaxValue;
                    for (int i = 0; i < anchors.Count; i++)
                    {
                        if (!anchors[i].zone.HasPoint(en.Box.Cx, en.Box.Cy)) continue;
                        double d = Math.Abs(en.Box.Cx - anchors[i].ax) + Math.Abs(en.Box.Cy - anchors[i].ay);
                        if (d < bd) { bd = d; best = i; }
                    }
                    if (best < 0) continue;
                    var bb = boxes[best]; bb.Add(en.Box); boxes[best] = bb;
                    en.Claimed = true;
                }
                for (int i = 0; i < anchors.Count; i++)
                {
                    var (p, _, _, _, _) = anchors[i];
                    if (!p.used) continue;
                    ctx.Items.Add(new Src
                    {
                        Item = new LayoutRules.Item { Key = "PH" + p.nr, Kind = LayoutRules.Kind.Ph },
                        Ext = boxes[i], Scale = 25, Order = 1000 + p.nr * 10 + i
                    });
                }
                if (phTitles.Count > 0 && !phTitles.Any(p => p.used))
                    ctx.Warnings.Add("żaden detal PH nie jest użyty (wszystkie „N/A”) — uruchom Punching przed arkuszami, jeśli płyta ma pale z przebiciem");
            }

            // 3. detale belek i otworów
            {
                var dets = new List<(string key, int nr, Box core, Box box)>();
                foreach (var g in beamParts.GroupBy(p => (p.zone, p.nr)))
                {
                    var core = Box.Empty;
                    foreach (var p in g) { core.Add(p.e.Box); p.e.Claimed = true; }
                    dets.Add(("DETAIL '" + g.Key.nr + "'", g.Key.nr, core, core));
                }
                foreach (var (fe, nr) in holeFrames)
                {
                    var core = fe.Box; fe.Claimed = true;
                    // tytuł „DETAIL 'n'” / „SCALE 1:25” pod ramką (nie odnośnik na rzucie — ten bez „SCALE”)
                    var title = detailTitles.Where(t => t.nr == nr && !t.e.Claimed && t.e.Box.Cx >= core.X0 && t.e.Box.Cx <= core.X1 && t.e.Box.Y1 <= core.Y0 + 1)
                                            .OrderBy(t => core.Y0 - t.e.Box.Y1).Select(t => t.e).FirstOrDefault();
                    var box = core;
                    if (title != null) { box.Add(title.Box); title.Claimed = true; }
                    dets.Add(("DETAIL '" + nr + "'", nr, core, box));
                }
                foreach (var en in ents)
                {
                    if (en.Claimed || dets.Count == 0) continue;
                    int best = -1; double bd = double.MaxValue;
                    for (int i = 0; i < dets.Count; i++)
                    {
                        if (!en.Box.Inside(dets[i].core.Grow(DetailMargin))) continue;
                        double d = dets[i].core.Dist(en.Box.Cx, en.Box.Cy);
                        if (d < bd) { bd = d; best = i; }
                    }
                    if (best < 0) continue;
                    var dd = dets[best]; dd.box.Add(en.Box); dets[best] = dd;
                    en.Claimed = true;
                }
                foreach (var d in dets.OrderBy(d => d.nr))
                    ctx.Items.Add(new Src
                    {
                        Item = new LayoutRules.Item { Key = d.key, Kind = LayoutRules.Kind.Detail },
                        Ext = d.box, Scale = 25, Order = 500 + d.nr
                    });
                // decyzja Bartka 2026-10-19: przekrój z linią cięcia na detalu — na arkuszu tego detalu
                foreach (var sec in ctx.Items.Where(i => i.Item.Kind == LayoutRules.Kind.Section))
                {
                    var host = dets.FirstOrDefault(d => sec.Marks.Any(m => d.box.HasPoint(m.X, m.Y)));
                    if (host.key != null) sec.Item.Group = host.key;
                }
                var dup = dets.GroupBy(d => d.nr).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                if (dup.Count > 0) ctx.Warnings.Add("numer detalu użyty kilka razy: " + string.Join(", ", dup.Select(n => "DETAIL '" + n + "'")));
            }

            // 4. rzuty
            {
                var plots = gaTags.Select(g => g.plot).Distinct().ToList();
                string plot = plots.FirstOrDefault();
                if (plots.Count > 1)
                {
                    string t1 = BaseAttr(db, ctx.BaseLayoutId, "TITLE_1").ToUpperInvariant();
                    plot = plots.FirstOrDefault(p => t1.Contains((p ?? "").ToUpperInvariant())) ?? plot;
                    ctx.Warnings.Add($"kilka płyt w rysunku ({string.Join(", ", plots)}) — arkusze dla „{plot}”");
                }
                var cores = new Dictionary<string, Box>();
                foreach (var view in new[] { "B", "T" })
                {
                    var core = Box.Empty;
                    foreach (var g in gaTags.Where(g => g.plot == plot && g.view == view)) core.Add(g.e.Box);
                    if (!core.IsEmpty) cores[view] = core;
                }
                if (cores.Count == 0) ctx.Errors.Add("brak rzutów płyty (obrys z Prepare GA, znacznik RC_GA) — arkusze bez rzutów");
                foreach (var kv in cores)
                {
                    var core = kv.Value;
                    var reg = core.Grow(PlanMargin);
                    foreach (var o in cores.Where(o => o.Key != kv.Key).Select(o => o.Value))
                    {
                        if (o.X0 >= core.X1) reg.X1 = Math.Min(reg.X1, (core.X1 + o.X0) / 2);
                        if (o.X1 <= core.X0) reg.X0 = Math.Max(reg.X0, (core.X0 + o.X1) / 2);
                        if (o.Y0 >= core.Y1) reg.Y1 = Math.Min(reg.Y1, (core.Y1 + o.Y0) / 2);
                        if (o.Y1 <= core.Y0) reg.Y0 = Math.Max(reg.Y0, (core.Y0 + o.Y1) / 2);
                    }
                    var box = core;
                    // tytuł rzutu — najbliższy pod rzutem
                    var title = planTitles.Where(t => t.view == kv.Key && !t.e.Claimed)
                                          .OrderBy(t => core.Dist(t.e.Box.Cx, t.e.Box.Y1)).Select(t => t.e).FirstOrDefault();
                    if (title != null && core.Dist(title.Box.Cx, title.Box.Y1) < 3 * PlanMargin) { box.Add(title.Box); title.Claimed = true; }
                    else ctx.Warnings.Add($"rzut {(kv.Key == "B" ? "dolny" : "górny")}: brak tytułu „REINFORCEMENT DETAILS …” pod rzutem");
                    foreach (var en in ents)
                    {
                        if (en.Claimed || !en.Box.Inside(reg)) continue;
                        box.Add(en.Box);
                    }
                    ctx.Items.Add(new Src
                    {
                        Item = new LayoutRules.Item { Key = kv.Key == "B" ? "BOTTOM LAYER" : "TOP LAYER", Kind = kv.Key == "B" ? LayoutRules.Kind.PlanBottom : LayoutRules.Kind.PlanTop },
                        Ext = box, Scale = 50, Order = kv.Key == "B" ? -2 : -1
                    });
                }
            }

        }

        /// <summary>
        /// Zakres obiektu. MText / MLeader — z fragmentów po rozbiciu (GeometricExtents MText w BricsCAD bywa za mały: tytuł detalu PH
        /// bez „SCALE …”, opisy prętów ucięte w rzutni — 2026-10-19); błąd rozbicia → GeometricExtents. Brak zakresu — Box.Empty.
        /// </summary>
        private static Box ExtentsOf(Entity e)
        {
            var b = Box.Empty;
            try
            {
                var x = e.GeometricExtents;
                b = new Box { X0 = x.MinPoint.X, Y0 = x.MinPoint.Y, X1 = x.MaxPoint.X, Y1 = x.MaxPoint.Y };
            }
            catch (System.Exception) { /* obiekt bez zakresu geometrycznego (np. pusty tekst) — zakres z rozbicia albo pominięty */ }
            if (!(e is MText) && !(e is MLeader)) return b;
            // MText: GeometricExtents bywa i za mały (ucięty „SCALE …”), i za duży (zadana wysokość ramki — rzutnia PH1 „schodziła”
            // w dół, 2026-10-19) — zakres TYLKO z fragmentów tekstu; GeometricExtents tylko, gdy rozbicie nic nie dało
            var gx = b;
            b = Box.Empty;
            try
            {
                using var parts = new DBObjectCollection();
                e.Explode(parts);
                foreach (DBObject o in parts)
                {
                    try
                    {
                        if (o is Entity pe)
                        {
                            var px = pe.GeometricExtents;
                            b.Add(new Box { X0 = px.MinPoint.X, Y0 = px.MinPoint.Y, X1 = px.MaxPoint.X, Y1 = px.MaxPoint.Y });
                        }
                    }
                    catch (System.Exception) { /* fragment bez zakresu (pusty akapit) */ }
                    finally { o.Dispose(); }
                }
            }
            catch (System.Exception ex) { Log.Error("LayoutEngine.ExtentsOf", ex); }
            return b.IsEmpty ? gx : b;
        }

        /// <summary>Skala opisowa 1:n z rysunku (ACDB_ANNOTATIONSCALES); brak — dodana. null — błąd (komunikat w log).</summary>
        private static AnnotationScale AnnoScale(Database db, int n)
        {
            try
            {
                var occ = db.ObjectContextManager.GetContextCollection("ACDB_ANNOTATIONSCALES");
                foreach (ObjectContext oc in occ)
                    if (oc is AnnotationScale a && a.DrawingUnits > 0 && Math.Abs(a.PaperUnits / a.DrawingUnits - 1.0 / n) < 1e-9)
                        return a;
                var ns = new AnnotationScale { Name = "1:" + n.ToString(CultureInfo.InvariantCulture), PaperUnits = 1, DrawingUnits = n };
                occ.AddContext(ns);
                return ns;
            }
            catch (System.Exception ex) { Log.Error("LayoutEngine.AnnoScale", ex); return null; }
        }

        private static string BaseAttr(Database db, ObjectId layId, string tag)
        {
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var lay = (Layout)tr.GetObject(layId, OpenMode.ForRead);
            foreach (var br in GaTitleEngine.TitleBlocks(tr, lay.BlockTableRecordId))
                foreach (ObjectId aid in br.AttributeCollection)
                    if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar && ar.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase))
                        return ar.TextString ?? "";
            return "";
        }

        /// <summary>
        /// Arkusz bazowy: layout z A1-BL bez znacznika RC_LAYOUT — bieżący, jeśli taki; inaczej pierwszy wg kolejności zakładek.
        /// Layouty z A1-BL ze znacznikiem — arkusze dodatkowe poprzedniego RC_LAYOUT (do usunięcia).
        /// </summary>
        private static void FindBase(Database db, string currentLayout, Ctx ctx)
        {
            var bases = new List<(ObjectId id, string name, int tab, ObjectId btr)>();
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var ld = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry e in ld)
            {
                var lay = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
                if (lay.ModelType) continue;
                var tbs = GaTitleEngine.TitleBlocks(tr, lay.BlockTableRecordId).ToList();
                if (tbs.Count == 0) continue;
                if (tbs.Any(b => Role(b) == RoleSheet)) ctx.OldSheets.Add(lay.LayoutName);
                else bases.Add((e.Value, lay.LayoutName, lay.TabOrder, lay.BlockTableRecordId));
            }
            if (bases.Count == 0) { tr.Commit(); return; }
            var b0 = bases.FirstOrDefault(b => string.Equals(b.name, currentLayout, StringComparison.OrdinalIgnoreCase));
            if (b0.id.IsNull) b0 = bases.OrderBy(b => b.tab).First();
            if (bases.Count > 1)
                ctx.Warnings.Add($"kilka arkuszy z A1-BL ({string.Join(", ", bases.OrderBy(b => b.tab).Select(b => b.name))}) — bazowy: {b0.name} " +
                                 "(żeby wybrać inny, przejdź na jego zakładkę i uruchom komendę tam)");
            ctx.BaseLayoutId = b0.id;
            ctx.BaseLayoutName = b0.name;
            ctx.BaseTab = b0.tab;
            var btr = (BlockTableRecord)tr.GetObject(b0.btr, OpenMode.ForRead);
            ObjectId overall = OverallViewport(tr, btr);
            foreach (ObjectId id in btr)
                if (!id.IsErased && id != overall && tr.GetObject(id, OpenMode.ForRead) is Viewport vp && Role(vp) == null) ctx.UserViewports++;
            foreach (var br in GaTitleEngine.TitleBlocks(tr, b0.btr))
                foreach (ObjectId aid in br.AttributeCollection)
                    if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar)
                    {
                        if (ar.Tag.Equals("DRAWING_NUMBER", StringComparison.OrdinalIgnoreCase)) ctx.BaseNumber = (ar.TextString ?? "").Trim();
                        else if (ar.Tag.Equals("REV", StringComparison.OrdinalIgnoreCase)) ctx.Rev = (ar.TextString ?? "").Trim();
                    }
            tr.Commit();
            if (ctx.BaseNumber.Length == 0) ctx.Warnings.Add("A1-BL bez DRAWING_NUMBER — kolejne arkusze bez numeru (uzupełnij przez GA Texts)");
        }

        /// <summary>Rzutnia arkusza (papieru) = pierwsza (najmniejszy uchwyt) — jak PlotSetupEngine.</summary>
        private static ObjectId OverallViewport(Transaction tr, BlockTableRecord btr)
        {
            ObjectId overall = ObjectId.Null; long minH = long.MaxValue;
            foreach (ObjectId id in btr)
                if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is Viewport && id.Handle.Value < minH) { minH = id.Handle.Value; overall = id; }
            return overall;
        }

        private static string Role(Entity e)
        {
            try
            {
                using var rb = e.GetXDataForApplication(XApp);
                var v = rb?.AsArray();
                return v != null && v.Length > 1 ? Convert.ToString(v[1].Value, CultureInfo.InvariantCulture) : null;
            }
            catch (System.Exception ex) { Log.Error("LayoutEngine.Role", ex); return null; }
        }

        private static void Tag(Transaction tr, Database db, Entity e, string role, string key)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(XApp))
            {
                rat.UpgradeOpen();
                var rec = new RegAppTableRecord { Name = XApp };
                rat.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }
            if (!e.IsWriteEnabled) e.UpgradeOpen();
            e.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, XApp),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, role),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, key ?? ""));
        }

        // ── teksty arkusza (wspólne dla RC_LAYOUT i RC_LAYOUT_TEXTS) ──────

        private static void AddNibNote(Transaction tr, Database db, BlockTableRecord btr, string layer, ObjectId style,
                                       string letter, int dir, double x, double yTop, string key)
        {
            var note = new MText();
            note.SetDatabaseDefaults(db);
            note.Contents = LayoutRules.NibNote(letter, dir);
            note.Layer = layer;
            note.TextStyleId = style;
            note.TextHeight = LayoutRules.NibNoteHeight;
            note.Width = LayoutRules.NibNoteWidth;
            note.Attachment = AttachmentPoint.TopLeft;
            note.Location = new Point3d(x, yTop, 0);
            btr.AppendEntity(note);
            tr.AddNewlyCreatedDBObject(note, true);
            Tag(tr, db, note, RoleNote, key);
        }

        /// <summary>
        /// Odsyłacze nad CONSTRUCTION ISSUE (decyzja Bartka 2026-10-19): każdy inny arkusz w osobnej ramce („… SEE / DRG. …”),
        /// ramki jedna nad drugą od y 252,8 — pierwszy arkusz na górze.
        /// </summary>
        private static void AddCrossRefs(Transaction tr, Database db, BlockTableRecord btr, string layer, ObjectId style,
                                         List<(string Title3, string Number)> others)
        {
            double y0 = XrefY0;
            for (int i = others.Count - 1; i >= 0; i--)
            {
                var lines = LayoutRules.CrossRefBlock(others[i].Title3, others[i].Number);
                double top = y0 + LayoutRules.CrossRefBoxHeight(lines.Count);
                var fr = new Polyline();
                fr.SetDatabaseDefaults(db);
                fr.Layer = "0";
                fr.AddVertexAt(0, new Point2d(XrefX0, y0), 0, 0, 0);
                fr.AddVertexAt(1, new Point2d(XrefX1, y0), 0, 0, 0);
                fr.AddVertexAt(2, new Point2d(XrefX1, top), 0, 0, 0);
                fr.AddVertexAt(3, new Point2d(XrefX0, top), 0, 0, 0);
                fr.Closed = true;
                btr.AppendEntity(fr);
                tr.AddNewlyCreatedDBObject(fr, true);
                Tag(tr, db, fr, RoleXref, "FRAME " + others[i].Number);
                var xt = new MText();
                xt.SetDatabaseDefaults(db);
                xt.Contents = "\\C3;" + string.Join("\\P", lines);
                xt.Layer = layer;
                xt.TextStyleId = style;
                xt.TextHeight = LayoutRules.NibNoteHeight;
                xt.Width = XrefX1 - XrefTextX - 0.5;   // łamanie tylko jawne (\P) — bez zawijania przez MText
                xt.Attachment = AttachmentPoint.TopLeft;
                xt.Location = new Point3d(XrefTextX, top - XrefTextTop, 0);
                btr.AppendEntity(xt);
                tr.AddNewlyCreatedDBObject(xt, true);
                Tag(tr, db, xt, RoleXref, "TEXT " + others[i].Number);
                y0 = top;
            }
        }

        private static void SetTitleAttrs(Transaction tr, ObjectId layoutBtrId, string number, string title3, string scale)
        {
            foreach (var br in GaTitleEngine.TitleBlocks(tr, layoutBtrId).ToList())
                foreach (ObjectId aid in br.AttributeCollection)
                {
                    if (!(tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar)) continue;
                    string nv = null;
                    if (ar.Tag.Equals("DRAWING_NUMBER", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(number)) nv = number;
                    else if (ar.Tag.Equals("TITLE_3", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(title3)) nv = title3;
                    else if (ar.Tag.Equals("SCALE", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(scale)) nv = scale;
                    if (nv == null || nv == ar.TextString) continue;
                    ar.UpgradeOpen();
                    ar.TextString = nv;
                }
        }

        // ── wykonanie ────────────────────────────────────────────────────

        public sealed class Result
        {
            public int Viewports, Notes, Sheets, Removed, RemovedSheets, Thawed;
            public List<string> Messages = new List<string>();
        }

        /// <summary>
        /// Arkusze wg <paramref name="ctx"/>: usuwa poprzednie arkusze dodatkowe i obiekty RC_LAYOUT z arkusza bazowego (ręczne rzutnie —
        /// gdy <paramref name="removeUserViewports"/>), tworzy arkusze dodatkowe, rzutnie, notatki, odsyłacze, atrybuty A1-BL,
        /// na końcu PlotSetupEngine. Wywołanie w kontekście komendy z zablokowanym dokumentem.
        /// </summary>
        public static Result Apply(Database db, Ctx ctx, bool removeUserViewports)
        {
            var res = new Result();
            var lm = LayoutManager.Current;
            try { lm.CurrentLayout = "Model"; } catch (System.Exception ex) { Log.Error("LayoutEngine.Apply.Model", ex); }

            // 1. poprzednie arkusze dodatkowe
            foreach (var n in ctx.OldSheets)
            {
                try { lm.DeleteLayout(n); res.RemovedSheets++; }
                catch (System.Exception ex) { Log.Error("LayoutEngine.Apply.DeleteOld", ex); res.Messages.Add($"layout „{n}” — nie usunięto ({ex.Message})"); }
            }

            // 2. arkusz bazowy: obiekty RC_LAYOUT (+ ręczne rzutnie za zgodą); treść do kopii
            ObjectId baseBtr;
            var content = new ObjectIdCollection();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lay = (Layout)tr.GetObject(ctx.BaseLayoutId, OpenMode.ForRead);
                baseBtr = lay.BlockTableRecordId;
                var btr = (BlockTableRecord)tr.GetObject(baseBtr, OpenMode.ForRead);
                ObjectId overall = OverallViewport(tr, btr);
                foreach (ObjectId id in btr)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    string role = Role(e);
                    if (e is Viewport)
                    {
                        if (id == overall) continue;
                        if (role != null || removeUserViewports) { e.UpgradeOpen(); e.Erase(); res.Removed++; }
                        continue;
                    }
                    if (role != null) { e.UpgradeOpen(); e.Erase(); res.Removed++; continue; }
                    content.Add(id);
                }
                tr.Commit();
            }

            // 3. arkusze
            var layIds = new List<ObjectId> { ctx.BaseLayoutId };
            for (int k = 1; k < ctx.Sheets.Count; k++)
            {
                var info = ctx.Sheets[k];
                var clones = new Dictionary<ObjectId, ObjectId>();
                var rep = new RcTemplateEngine.Report();
                var lid = RcTemplateEngine.CreateSheetFrom(db, ctx.BaseLayoutId, info.Layout, ctx.BaseTab + k, content, clones, rep);
                res.Messages.AddRange(rep.Messages);
                layIds.Add(lid);
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var lay = (Layout)tr.GetObject(lid, OpenMode.ForRead);
                    info.Layout = lay.LayoutName;
                    foreach (var br in GaTitleEngine.TitleBlocks(tr, lay.BlockTableRecordId).ToList()) Tag(tr, db, br, RoleSheet, info.Number);
                    tr.Commit();
                }
                res.Sheets++;
            }

            // 4. zawartość arkuszy
            var srcByItem = ctx.Items.ToDictionary(s => s.Item);
            for (int k = 0; k < ctx.Sheets.Count; k++)
            {
                var info = ctx.Sheets[k];
                var sheet = ctx.Plan.Sheets[k];
                try { lm.CurrentLayout = info.Layout; } catch (System.Exception ex) { Log.Error("LayoutEngine.Apply.Activate", ex); }
                // obiekty opisowe bez skali rzutni też widoczne (ANNOALLVISIBLE zapisywane per layout)
                try { Bricscad.ApplicationServices.Application.SetSystemVariable("ANNOALLVISIBLE", 1); }
                catch (System.Exception ex) { Log.Error("LayoutEngine.Apply.AnnoAllVisible", ex); }
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var lay = (Layout)tr.GetObject(layIds[k], OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForWrite);
                    var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    var st = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                    ObjectId noteStyle = st.Has(NoteStyle) ? st[NoteStyle] : db.Textstyle;
                    string noteLayer = lt.Has(NoteLayer) ? NoteLayer : "0";
                    ObjectId wire = PlotSetupEngine.WireframeStyle(tr, db);

                    foreach (var pl in sheet.Placed)
                    {
                        var s = srcByItem[pl.Item];
                        double x0 = pl.Rect.X0, y1 = pl.Rect.Y1;
                        var vp = new Viewport();
                        vp.SetDatabaseDefaults(db);
                        btr.AppendEntity(vp);
                        tr.AddNewlyCreatedDBObject(vp, true);
                        // widok z góry, styl 2D Wireframe (rzutnia bez stylu wizualnego — kreskowania nie rysowały się, 2026-10-19)
                        try
                        {
                            vp.ViewDirection = Vector3d.ZAxis;
                            vp.ViewTarget = Point3d.Origin;
                            vp.TwistAngle = 0;
                            if (!wire.IsNull) vp.VisualStyleId = wire;
                        }
                        catch (System.Exception ex) { Log.Error("LayoutEngine.Apply.VpView", ex); }
                        vp.CenterPoint = new Point3d(x0 + s.VpW / 2, y1 - s.VpH / 2, 0);
                        vp.Width = s.VpW;
                        vp.Height = s.VpH;
                        vp.ViewCenter = new Point2d(s.Ext.Cx, s.Ext.Cy);
                        vp.ViewHeight = s.VpH * s.Scale;
                        if (lt.Has(PlotSetupEngine.ViewportLayer)) vp.Layer = PlotSetupEngine.ViewportLayer;
                        // skala opisowa rzutni = skala rzutni (sama wysokość widoku zostawia 1:1 — obiekty opisowe, np. pale i opisy
                        // prętów na rzucie górnym, nie były widoczne w rzutni, 2026-10-19)
                        try
                        {
                            vp.CustomScale = 1.0 / s.Scale;
                            var asc = AnnoScale(db, s.Scale);
                            if (asc != null) vp.AnnotationScale = asc;
                        }
                        catch (System.Exception ex) { Log.Error("LayoutEngine.Apply.AnnoScale", ex); }
                        vp.ViewCenter = new Point2d(s.Ext.Cx, s.Ext.Cy);
                        try { vp.On = true; } catch (System.Exception ex) { Log.Error("LayoutEngine.Apply.VpOn", ex); }
                        // warstwy z „VP Freeze w nowych rzutniach” (szablon / ASD) — w nowej rzutni zamrożone: brak pali, tagów PH,
                        // opisów, okręgu w detalu PH (2026-10-19). W rzutniach RC_LAYOUT wszystkie warstwy odmrożone, jak na arkuszach wzorcowych.
                        try
                        {
                            var thaw = new ObjectIdCollection();
                            foreach (ObjectId lid in lt)
                                if (vp.IsLayerFrozenInViewport(lid)) thaw.Add(lid);
                            if (thaw.Count > 0) { vp.ThawLayersInViewport(thaw.GetEnumerator()); res.Thawed += thaw.Count; }
                            var freeze = new ObjectIdCollection();
                            foreach (var ln in ctx.MapLayers) if (lt.Has(ln)) freeze.Add(lt[ln]);
                            if (freeze.Count > 0) vp.FreezeLayersInViewport(freeze.GetEnumerator());
                        }
                        catch (System.Exception ex) { Log.Error("LayoutEngine.Apply.Thaw", ex); }
                        Tag(tr, db, vp, RoleViewport, pl.Item.Key);
                        res.Viewports++;

                        if (s.Item.Kind == LayoutRules.Kind.Section && s.NibDir > 0)
                        {
                            AddNibNote(tr, db, btr, noteLayer, noteStyle, s.Letter, s.NibDir, x0, y1 - s.VpH - LayoutRules.NibNoteGap, pl.Item.Key);
                            res.Notes++;
                        }
                    }

                    // odsyłacze do pozostałych arkuszy — każdy w osobnej ramce
                    if (ctx.Sheets.Count > 1)
                        AddCrossRefs(tr, db, btr, noteLayer, noteStyle, ctx.Sheets.Where((_, j) => j != k).Select(o => (o.Title3, o.Number)).ToList());
                    SetTitleAttrs(tr, lay.BlockTableRecordId, info.Number, info.Title3, info.Scale);
                    tr.Commit();
                }
            }

            // 5. ustawienia wydruku (PCN-VIEWPORT, blokada rzutni, tagi PH odmrożone)
            try
            {
                var pr = PlotSetupEngine.Apply(db);
                res.Messages.AddRange(pr.Messages);
            }
            catch (System.Exception ex) { Log.Error("LayoutEngine.Apply.PlotSetup", ex); res.Messages.Add("ustawienia wydruku — błąd: " + ex.Message); }
            try { lm.CurrentLayout = ctx.Sheets.Count > 0 ? ctx.Sheets[0].Layout : "Model"; }
            catch (System.Exception ex) { Log.Error("LayoutEngine.Apply.Back", ex); }
            try { Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor.Regen(); }
            catch (System.Exception ex) { Log.Error("LayoutEngine.Apply.Regen", ex); }
            return res;
        }

        // ── RC_LAYOUT_TEXTS: opisy istniejących arkuszy (decyzja Bartka 2026-10-19) ─────────────────

        public sealed class TextsSheet
        {
            public ObjectId LayoutId;
            public string Layout, NewLayout, Number, Title3, Scale;
            public List<string> Contents = new List<string>();
            internal List<(string Letter, int Dir, double X, double YTop, string Key)> Notes = new List<(string, int, double, double, string)>();
        }

        public sealed class TextsCtx
        {
            public List<TextsSheet> Sheets = new List<TextsSheet>();
            public List<string> Warnings = new List<string>();
            public List<string> Errors = new List<string>();
            public bool AnyRename => Sheets.Any(s => !string.Equals(s.Layout, s.NewLayout, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Opisy arkuszy z rzutni (tylko odczyt): arkusze = layouty z A1-BL wg kolejności zakładek; w każdej rzutni (poza rzutnią arkusza)
        /// rozpoznane elementy modelu (rzut dolny / górny, przekrój, PH, detal — środek zakresu w oknie rzutni) i skala rzutni →
        /// TITLE_3, SCALE; numery rysunków po kolei od numeru pierwszego arkusza; nazwa layoutu = numer + REV; notatki pod przekrojami
        /// z nibem (gdy w layoucie nie ma już takiej notatki); odsyłacze w ramkach. Zapis — <see cref="ApplyTexts"/>.
        /// </summary>
        public static TextsCtx PrepareTexts(Database db)
        {
            var t = new TextsCtx();
            var lays = new List<(ObjectId id, string name, int tab, ObjectId btr)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ld = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry e in ld)
                {
                    var lay = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
                    if (lay.ModelType || !GaTitleEngine.TitleBlocks(tr, lay.BlockTableRecordId).Any()) continue;
                    lays.Add((e.Value, lay.LayoutName, lay.TabOrder, lay.BlockTableRecordId));
                }
                tr.Commit();
            }
            lays = lays.OrderBy(l => l.tab).ToList();
            if (lays.Count == 0) { t.Errors.Add("brak arkuszy z blokiem A1-BL"); return t; }

            var ctx = new Ctx { BaseLayoutId = lays[0].id };
            Collect(db, ctx);
            t.Warnings.AddRange(ctx.Warnings);
            string baseNumber = BaseAttr(db, lays[0].id, "DRAWING_NUMBER").Trim();
            string rev = BaseAttr(db, lays[0].id, "REV").Trim();
            if (baseNumber.Length == 0) t.Warnings.Add($"{lays[0].name}: A1-BL bez DRAWING_NUMBER — numery rysunków bez zmian");

            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                for (int k = 0; k < lays.Count; k++)
                {
                    var (lid, name, _, btrId) = lays[k];
                    var sh = new TextsSheet { LayoutId = lid, Layout = name };
                    sh.Number = baseNumber.Length > 0 ? LayoutRules.IncrementDrawingNumber(baseNumber, k) : BaseAttr(db, lid, "DRAWING_NUMBER");
                    sh.NewLayout = baseNumber.Length > 0 ? LayoutRules.DrawingSuffix(sh.Number) + rev : name;
                    var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    ObjectId overall = OverallViewport(tr, btr);
                    var kinds = new HashSet<LayoutRules.Kind>();
                    var scales = new List<int>();
                    string allText = "";
                    foreach (ObjectId id in btr)
                        if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is MText mt && Role(mt) == null) allText += "\n" + (mt.Text ?? "");
                    foreach (ObjectId id in btr)
                    {
                        if (id.IsErased || id == overall || !(tr.GetObject(id, OpenMode.ForRead) is Viewport vp) || vp.Height < 1e-6) continue;
                        double sc = vp.ViewHeight / vp.Height;
                        int n = (int)Math.Round(sc);
                        if (n <= 0) continue;
                        if (Math.Abs(vp.TwistAngle) > 1e-6) t.Warnings.Add($"{name}: rzutnia {vp.Handle} obrócona — zawartość rozpoznana w przybliżeniu");
                        double cx = vp.ViewTarget.X + vp.ViewCenter.X, cy = vp.ViewTarget.Y + vp.ViewCenter.Y;
                        double hw = vp.ViewHeight * vp.Width / vp.Height / 2, hh = vp.ViewHeight / 2;
                        var win = new Box { X0 = cx - hw, Y0 = cy - hh, X1 = cx + hw, Y1 = cy + hh };
                        bool any = false;
                        foreach (var it in ctx.Items)
                        {
                            if (!win.HasPoint(it.Ext.Cx, it.Ext.Cy)) continue;
                            any = true;
                            kinds.Add(it.Item.Kind);
                            if (!sh.Contents.Contains(it.Item.Key)) sh.Contents.Add(it.Item.Key);
                            if (it.Item.Kind == LayoutRules.Kind.Section && it.NibDir > 0
                                && allText.IndexOf("Section " + it.Letter + "-" + it.Letter + " has longitudinal", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                // pod przekrojem: lewa krawędź przekroju w rzutni, pod dolną krawędzią rzutni
                                double px = vp.CenterPoint.X + (Math.Max(it.Ext.X0, win.X0) - cx) / n;
                                double py = vp.CenterPoint.Y - vp.Height / 2 - LayoutRules.NibNoteGap;
                                sh.Notes.Add((it.Letter, it.NibDir, px, py, it.Item.Key));
                            }
                        }
                        if (any) scales.Add(n);
                    }
                    if (sh.Contents.Count == 0) t.Warnings.Add($"{name}: w rzutniach nie rozpoznano rzutów / przekrojów / detali — TITLE_3 i SCALE bez zmian");
                    sh.Title3 = sh.Contents.Count == 0 ? "" : LayoutRules.Title3(kinds.Contains(LayoutRules.Kind.PlanBottom), kinds.Contains(LayoutRules.Kind.PlanTop),
                        kinds.Contains(LayoutRules.Kind.Section), kinds.Contains(LayoutRules.Kind.Detail), kinds.Contains(LayoutRules.Kind.Ph));
                    sh.Scale = LayoutRules.ScaleText(scales);
                    t.Sheets.Add(sh);
                }
                tr.Commit();
            }
            return t;
        }

        public sealed class TextsResult
        {
            public int Notes, CrossRefs, Renamed, Removed;
            public List<string> Messages = new List<string>();
        }

        /// <summary>
        /// Zapis opisów arkuszy: usuwa poprzednie odsyłacze / notatki RC_LAYOUT, dopisuje notatki pod przekrojami z nibem, odsyłacze
        /// (przy kilku arkuszach), atrybuty A1-BL; <paramref name="rename"/> — nazwy layoutów wg numerów (w dwóch krokach, bez kolizji).
        /// </summary>
        public static TextsResult ApplyTexts(Database db, TextsCtx t, bool rename)
        {
            var res = new TextsResult();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                var st = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                ObjectId noteStyle = st.Has(NoteStyle) ? st[NoteStyle] : db.Textstyle;
                string noteLayer = lt.Has(NoteLayer) ? NoteLayer : "0";
                for (int k = 0; k < t.Sheets.Count; k++)
                {
                    var sh = t.Sheets[k];
                    var lay = (Layout)tr.GetObject(sh.LayoutId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForWrite);
                    foreach (ObjectId id in btr)
                    {
                        if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e) || e is Viewport) continue;
                        string role = Role(e);
                        if (role != RoleXref && role != RoleNote) continue;
                        e.UpgradeOpen(); e.Erase(); res.Removed++;
                    }
                    foreach (var n in sh.Notes) { AddNibNote(tr, db, btr, noteLayer, noteStyle, n.Letter, n.Dir, n.X, n.YTop, n.Key); res.Notes++; }
                    if (t.Sheets.Count > 1)
                    {
                        var others = t.Sheets.Where((_, j) => j != k).Select(o => (o.Title3, o.Number)).ToList();
                        AddCrossRefs(tr, db, btr, noteLayer, noteStyle, others);
                        res.CrossRefs += others.Count;
                    }
                    SetTitleAttrs(tr, lay.BlockTableRecordId, sh.Number, sh.Title3, sh.Scale);
                }
                tr.Commit();
            }
            if (rename && t.AnyRename)
            {
                var lm = LayoutManager.Current;
                var todo = t.Sheets.Where(s => !string.Equals(s.Layout, s.NewLayout, StringComparison.OrdinalIgnoreCase) && s.NewLayout.Length > 0).ToList();
                foreach (var s in todo)
                {
                    try { lm.RenameLayout(s.Layout, s.Layout + "~rc"); s.Layout += "~rc"; }
                    catch (System.Exception ex) { Log.Error("LayoutEngine.ApplyTexts.Rename1", ex); res.Messages.Add($"layout „{s.Layout}” — nie zmieniono nazwy ({ex.Message})"); }
                }
                foreach (var s in todo.Where(s => s.Layout.EndsWith("~rc", StringComparison.Ordinal)))
                {
                    try { lm.RenameLayout(s.Layout, s.NewLayout); res.Renamed++; s.Layout = s.NewLayout; }
                    catch (System.Exception ex)
                    {
                        Log.Error("LayoutEngine.ApplyTexts.Rename2", ex);
                        res.Messages.Add($"nazwa „{s.NewLayout}” zajęta — layout zostaje jako „{s.Layout}” ({ex.Message})");
                    }
                }
            }
            return res;
        }
    }
}
