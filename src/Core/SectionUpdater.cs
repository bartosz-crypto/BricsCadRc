using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// RC_SECTION_UPDATE (bezpiecznik przekrojów, etapy 3–4; docs/PLAN_BEZPIECZNIK_PRZEKROJOW_2026-10-10.md).
    /// Etap 3 — przekrój NIEAKTUALNY: przeliczenie tą samą drogą co RC_SECTION (RecomputeSection, nib domykany jak w RC_SECTION),
    /// przesunięcie całego przekroju (mediana: tytuł + wierzchołek [0] betonu względem TopLeft), ręczne przesunięcia opisów
    /// (<see cref="SectionLabelCarry"/>), tytułu i liter znaczników zachowane; usuwane TYLKO części potwierdzone w obu kierunkach
    /// (lista w rekordzie + [1]); opisy usunięte ręcznie odtwarzane (P5); nowy rekord (ten sam GUID), chmurka usunięta — jedna
    /// transakcja w komendzie (jeden Ctrl+Z / Ctrl+Y). Kopia / uszkodzony → odmowa; osierocony z obrysem w tym samym miejscu →
    /// pytanie (tryb bez okien: pominięty z komunikatem).
    /// Etap 4 (P6) — przekrój „nieznany” (stary, bez danych): adopcja — cięcie / strona / litera ze znaczników SD-SECTION (tylko
    /// jednoznaczne), obiekty starego przekroju wskazuje użytkownik oknem (tryb bez okien: lista handle w zmiennej RC_SG_ADOPT),
    /// okno parametrów z wartościami z rysunku, usunięcie WSKAZANYCH obiektów i znaczników tej litery, nowy przekrój z rekordem.
    /// </summary>
    public static class SectionUpdater
    {
        public const string Prefix = "[RC SECTION]";

        /// <summary>Komunikaty ostatniej komendy RC_SECTION_UPDATE (testy BRX).</summary>
        public static List<string> LastMessages { get; } = new List<string>();

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static void Msg(Editor ed, string s)
        {
            LastMessages.Add(s);
            ed.WriteMessage($"\n{Prefix} {s}");
        }

        private static string F(double v) => Math.Round(v).ToString("0", Inv);

        // ── wybór przekroju ─────────────────────────────────────────────

        /// <summary>Aktualizacja przekroju wskazanego obiektem (tytuł albo dowolna część / chmurka).</summary>
        public static void UpdatePicked(Document doc, ObjectId picked)
        {
            var db = doc.Database;
            var ed = doc.Editor;
            var infos = SectionStore.ReadAll(db, includeLegacy: true);
            var s = infos.FirstOrDefault(i => !i.TitleId.IsNull && i.TitleId == picked)
                    ?? infos.FirstOrDefault(i => i.Parts.Any(p => p.Id == picked) || i.Clouds.Contains(picked));
            if (s == null)
            {
                Msg(ed, "Wskazany obiekt nie należy do przekroju — wskaż tytuł „SECTION X-X” albo obiekt rysunku przekroju.");
                return;
            }
            switch (s.Status)
            {
                case SectionStatus.Nieznany:
                    Adopt(doc, s);
                    return;
                case SectionStatus.Kopia:
                    Msg(ed, $"{s.Label}: kopia przekroju bez przepięcia danych — RC_SECTION_UPDATE odmawia (usuń kopię albo narysuj przekrój ponownie: RC_SECTION).");
                    return;
                case SectionStatus.Uszkodzony:
                    Msg(ed, $"{s.Label}: dane przekroju uszkodzone ({string.Join("; ", s.Reasons)}) — RC_SECTION_UPDATE odmawia (narysuj przekrój ponownie: RC_SECTION).");
                    return;
                case SectionStatus.CzesciBezTytulu:
                    Msg(ed, "Wskazany obiekt należy do przekroju bez tytułu (tytuł usunięty) — nie da się go zaktualizować; usuń te obiekty albo cofnij usunięcie tytułu.");
                    return;
            }
            SectionStore.Evaluate(db, s);
            if (s.Status == SectionStatus.Aktualny)
            {
                Msg(ed, $"{s.Label}: przekrój aktualny — bez zmian.");
                return;
            }
            Process(doc, s, infos);
        }

        /// <summary>„Wszystkie nieaktualne”: każdy przekrój NIEAKTUALNY (i osierocony z obrysem w tym samym miejscu — pytanie).</summary>
        public static void UpdateAll(Document doc)
        {
            var db = doc.Database;
            var ed = doc.Editor;
            var infos = SectionStore.CheckAll(db, includeLegacy: false);
            var todo = infos.Where(s => !s.TitleId.IsNull &&
                                        (s.Status == SectionStatus.Nieaktualny || (s.Status == SectionStatus.Osierocony && s.OutlineHint != null)))
                            .ToList();
            if (todo.Count == 0)
            {
                Msg(ed, "Brak nieaktualnych przekrojów — nic do aktualizacji.");
                return;
            }
            int ok = 0;
            foreach (var s in todo)
                if (Process(doc, s, infos)) ok++;
            Msg(ed, $"Zaktualizowano przekroje: {ok} z {todo.Count}.");
        }

        private static bool Process(Document doc, SectionInfo s, List<SectionInfo> infos)
        {
            var db = doc.Database;
            var ed = doc.Editor;
            // zabezpieczenie liczności: obiekty z [1] → ten tytuł i jego GUID spoza listy rekordu — nie wiemy, czyje są
            int foreign = infos.Where(o => o.Status == SectionStatus.CzesciBezTytulu && o.Guid == s.Guid).Sum(o => o.Parts.Count);
            if (foreign > 0)
            {
                Msg(ed, $"{s.Label}: w rysunku są obiekty tego przekroju spoza jego listy ({foreign}) — aktualizacja przerwana (sprawdź RC_SECTION_CHECK).");
                return false;
            }
            if (s.Status == SectionStatus.Nieaktualny)
            {
                var slab = SingleBarEngine.HandleToObjectId(db, s.SlabPicked);
                return Update(doc, s, slab);
            }
            if (s.Status != SectionStatus.Osierocony) return false;

            if (s.OutlineHint == null)
            {
                Msg(ed, $"{s.Label}: osierocony — {string.Join("; ", s.Reasons)}; brak obrysu w tym samym miejscu — narysuj przekrój ponownie (RC_SECTION).");
                return false;
            }
            if (ScriptMode.IsNonInteractive())
            {
                Msg(ed, $"{s.Label}: osierocony — w tym samym miejscu jest obrys {s.OutlineHint}; tryb bez okien — pominięto (wskaż przekrój interaktywnie).");
                return false;
            }
            var pko = new PromptKeywordOptions(
                $"\n{Prefix} {s.Label}: obrys płyty usunięty; w tym samym miejscu jest zamknięty obrys {s.OutlineHint}. " +
                "Zaktualizować przekrój na tym obrysie? [Tak/Nie] <Nie>: ");
            pko.Keywords.Add("Tak");
            pko.Keywords.Add("Nie");
            pko.Keywords.Default = "Nie";
            pko.AppendKeywordsToMessage = false;
            var kr = ed.GetKeywords(pko);
            if (kr.Status != PromptStatus.OK || kr.StringResult != "Tak")
            {
                Msg(ed, $"{s.Label}: pominięto (osierocony).");
                return false;
            }
            var hint = SingleBarEngine.HandleToObjectId(db, s.OutlineHint);
            if (hint.IsNull || hint.IsErased) { Msg(ed, $"{s.Label}: obrys {s.OutlineHint} niedostępny — pominięto."); return false; }
            return Update(doc, s, hint);
        }

        // ── etap 3: aktualizacja ────────────────────────────────────────

        private static Point2d LabelPoint(DBText dt)
            => dt.HorizontalMode == TextHorizontalMode.TextLeft && dt.VerticalMode == TextVerticalMode.TextBase
                ? new Point2d(dt.Position.X, dt.Position.Y)
                : new Point2d(dt.AlignmentPoint.X, dt.AlignmentPoint.Y);

        private static string PosOfKey(string key)
        {
            int k = (key ?? "").LastIndexOf('|');
            return k >= 0 ? key.Substring(k + 1) : "";
        }

        /// <summary>
        /// Aktualizacja przekroju (NIEAKTUALNY / osierocony z obrysem zastępczym <paramref name="slabId"/>). Zwraca true, gdy narysowano.
        /// </summary>
        public static bool Update(Document doc, SectionInfo s, ObjectId slabId)
        {
            var db = doc.Database;
            var ed = doc.Editor;
            var rec = s.Record;
            string label = s.Label;
            if (rec == null || slabId.IsNull || slabId.IsErased)
            {
                Msg(ed, $"{label}: brak rekordu albo obrysu płyty — nie zaktualizowano.");
                return false;
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // nib: otwarta linia uskoku domykana jak w RC_SECTION (zapis w komendzie, ta sama grupa UNDO)
            NibAutoClose.EnsureForCommand(doc, slabId, Prefix);

            SectionStore.CurrentCut(db, s, out var p1, out var p2, out string letter);
            var rc = AutoRebarEngine.RecomputeSection(db, rec, slabId, p1, p2, letter);
            if (rc.Error != null) { Msg(ed, $"{label}: nie zaktualizowano — {rc.Error}."); return false; }
            var plan = rc.Plan;
            foreach (var w in plan.Warnings) Msg(ed, $"{label}: {w}");
            if (plan.Segments.Count == 0) { Msg(ed, $"{label}: nie zaktualizowano — linia cięcia nie przecina płyty."); return false; }

            // ── stan bieżący przekroju (tylko odczyt) ──
            var tl = rec.TopLeft;
            var byHandle = new Dictionary<string, SectionRecord.Part>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in rec.Parts) { string h = XLink.Norm(p.Handle); if (!byHandle.ContainsKey(h)) byHandle[h] = p; }
            var confirmed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var shifts = new List<(double X, double Y)>();   // próbki betonu (wierzchołek [0] odcinka)
            var slabNow = new List<(string Key, Point2d V0)>();
            var oldAbs = new List<(SectionLabelCarry.OldLabel L, Point2d P)>();
            var markPoly = new Dictionary<string, (Point2d V0, Point2d V1, Point2d V2)>(StringComparer.Ordinal);
            var markTxt = new Dictionary<string, Point2d>(StringComparer.Ordinal);
            Point2d? titleNow = null;
            bool hadMarks = false;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                if (!s.TitleId.IsErased && tr.GetObject(s.TitleId, OpenMode.ForRead) is MText title)
                    titleNow = new Point2d(title.Location.X, title.Location.Y);
                foreach (var (id, role, key) in s.Parts)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    string h = XLink.Norm(e.Handle.ToString());
                    confirmed.Add(h);
                    byHandle.TryGetValue(h, out var rp);
                    switch (role)
                    {
                        case "SLAB":
                            if (e is Polyline pl && pl.NumberOfVertices > 0)
                            {
                                var v = pl.GetPoint2dAt(0);
                                slabNow.Add((key, v));
                                if (rp?.Def != null) shifts.Add((v.X - tl.X - rp.Def.Value.X, v.Y - tl.Y - rp.Def.Value.Y));
                            }
                            break;
                        case "LBL":
                            if (e is DBText dt)
                                oldAbs.Add((new SectionLabelCarry.OldLabel { Text = (dt.TextString ?? "").Trim(), Kind = rp?.Kind ?? "", Def = rp?.Def },
                                            LabelPoint(dt)));
                            break;
                        case "MARK":
                            hadMarks = true;
                            if (e is Polyline mp && mp.NumberOfVertices >= 3)
                                markPoly[key] = (mp.GetPoint2dAt(0), mp.GetPoint2dAt(1), mp.GetPoint2dAt(2));
                            break;
                        case "MARKTXT":
                            if (e is MText mt) markTxt[key] = new Point2d(mt.Location.X, mt.Location.Y);
                            break;
                    }
                }
                tr.Commit();
            }
            (double X, double Y)? titleShift = null;
            if (titleNow.HasValue && rec.TitleDef.HasValue)
                titleShift = (titleNow.Value.X - tl.X - rec.TitleDef.Value.X, titleNow.Value.Y - tl.Y - rec.TitleDef.Value.Y);
            bool oldFormat = !rec.TitleDef.HasValue && !rec.Parts.Any(p => p.Def.HasValue);
            if (shifts.Count == 0 && !titleShift.HasValue)
            {
                // rekord etapu 1 (bez pozycji domyślnych): beton bieżący względem tego samego odcinka nowego planu
                foreach (var (key, v) in slabNow)
                    if (key.StartsWith("SEG#", StringComparison.Ordinal) && int.TryParse(key.Substring(4), NumberStyles.Integer, Inv, out int i)
                        && i < plan.Segments.Count && plan.Segments[i].Polygon.Count > 0)
                    {
                        var q = plan.Segments[i].Polygon[0];
                        shifts.Add((v.X - tl.X - (q.U - plan.UMin), v.Y - tl.Y - q.V));
                    }
            }
            var shift = SectionLabelCarry.SectionShift(shifts, titleShift);
            var newTl = new Point3d(tl.X + shift.X, tl.Y + shift.Y, tl.Z);

            // ── przesunięcia opisów, tytułu, liter znaczników ──
            foreach (var (l, p) in oldAbs) { l.CurX = p.X - tl.X - shift.X; l.CurY = p.Y - tl.Y - shift.Y; }
            var neu = plan.Labels.Select(lb => new SectionLabelCarry.NewLabel
            {
                Text = (lb.Text ?? "").Trim(),
                Kind = SectionLabelCarry.KindOf(lb.HasLeader, plan.Dots.Any(d => d.TinNib && d.PosNr == lb.Text)),
                DefX = lb.U - plan.UMin, DefY = lb.V
            }).ToList();
            var carry = SectionLabelCarry.Carry(oldAbs.Select(o => o.L).ToList(), neu);
            var opt = new AutoRebarEngine.SectionDrawOptions { DrawMarks = hadMarks };
            foreach (var kv in carry.Offsets) opt.LabelOffsets[kv.Key] = kv.Value;
            bool titleMoved = false;
            if (titleNow.HasValue && rec.TitleDef.HasValue)
            {
                var o = new Vector2d(titleNow.Value.X - tl.X - shift.X - rec.TitleDef.Value.X, titleNow.Value.Y - tl.Y - shift.Y - rec.TitleDef.Value.Y);
                if (o.Length > SectionLabelCarry.MinOffset) { opt.TitleOffset = o; titleMoved = true; }
            }
            foreach (var kv in markTxt)
            {
                if (!markPoly.TryGetValue(kv.Key, out var mp)) continue;
                var d = SectionLabelCarry.MarkTextDefault((mp.V0.X, mp.V0.Y), (mp.V1.X, mp.V1.Y), (mp.V2.X, mp.V2.Y));
                if (!d.HasValue) continue;
                var o = new Vector2d(kv.Value.X - d.Value.X, kv.Value.Y - d.Value.Y);
                if (o.Length > SectionLabelCarry.MinOffset) opt.MarkTextOffsets[kv.Key] = o;
            }
            // P5: opisy z listy rekordu, których już nie ma (usunięte ręcznie) — odtwarzane
            var restored = rec.Parts.Where(p => p.Role == "LBL" && !confirmed.Contains(XLink.Norm(p.Handle))).Select(p => PosOfKey(p.Key)).ToList();

            // ── zapis: jedna transakcja (usunięcie potwierdzonych części, chmurek i tytułu + nowy rysunek z rekordem) ──
            int erased = 0;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var (id, _, _) in s.Parts)
                    if (!id.IsErased) { tr.GetObject(id, OpenMode.ForWrite).Erase(); erased++; }
                foreach (var id in s.Clouds)
                    if (!id.IsErased) tr.GetObject(id, OpenMode.ForWrite).Erase();
                if (!s.TitleId.IsErased) tr.GetObject(s.TitleId, OpenMode.ForWrite).Erase();
                var newRec = AutoRebarEngine.BuildSectionRecord(rc.Ctx, plan, letter, newTl, rec.ThkOverridden, rec.SslOverridden, rec.Guid);
                AutoRebarEngine.DrawSectionWith(db, rc.Ctx, plan, newTl, letter, newRec, opt);
                tr.Commit();
            }
            sw.Stop();
            Log.Info($"SectionUpdater.Update({letter}): usunięto {erased} części, przesunięcie ({F(shift.X)}, {F(shift.Y)}), {sw.ElapsedMilliseconds} ms.");

            string un = carry.Unmatched.Count > 0 ? " (poz. " + string.Join(", ", carry.Unmatched.Distinct()) + ")" : "";
            Msg(ed, $"{letter}-{letter} zaktualizowany: przesunięte opisy zachowane: {carry.Kept}; bez dopasowania: {carry.Unmatched.Count}{un}.");
            if (!string.Equals(letter, rec.Letter, StringComparison.OrdinalIgnoreCase))
                Msg(ed, $"{letter}-{letter}: litera przekroju {rec.Letter} → {letter} (ze znaczników na rzucie).");
            if (Math.Sqrt(shift.X * shift.X + shift.Y * shift.Y) > SectionLabelCarry.MinOffset)
                Msg(ed, $"{letter}-{letter}: zachowano przesunięcie całego przekroju ({F(shift.X)}, {F(shift.Y)}).");
            if (titleMoved || opt.MarkTextOffsets.Count > 0)
                Msg(ed, $"{letter}-{letter}: zachowano przesunięcie " + string.Join(" i ", new[]
                    { titleMoved ? "tytułu" : null, opt.MarkTextOffsets.Count > 0 ? $"liter znaczników ({opt.MarkTextOffsets.Count})" : null }.Where(x => x != null)) + ".");
            if (restored.Count > 0)
                Msg(ed, $"{letter}-{letter}: opisy usunięte ręcznie — odtworzone: {restored.Count} (poz. {string.Join(", ", restored.Distinct())}).");
            if (oldFormat)
                Msg(ed, $"{letter}-{letter}: rekord bez pozycji domyślnych (przekrój sprzed RC_SECTION_UPDATE) — opisy i tytuł na pozycjach domyślnych; " +
                        "położenie przekroju ustalone z betonu względem nowego planu — zmiana geometrii betonu mogła zostać potraktowana jak przesunięcie przekroju, sprawdź położenie.");
            if (!hadMarks)
                Msg(ed, $"{letter}-{letter}: przekrój bez znaczników na rzutach — znaczników nie dorysowano.");
            ed.WriteMessage("\n");
            return true;
        }

        // ── etap 4: adopcja starego przekroju (P6) ──────────────────────

        private sealed class Marker
        {
            public ObjectId PolyId, TextId;
            public Point2d P;
            public Vector2d Dir, Nrm;
        }

        private sealed class MarkerPair
        {
            public Marker A, B;
            public Point2d P1, P2;
            public bool AlongX;
            public int Look;
            public ObjectId Slab = ObjectId.Null;
        }

        private static readonly HashSet<string> SectionLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AutoRebarEngine.SectionMarkLayer, AutoRebarEngine.SectionSlabLayer, AutoRebarEngine.SectionPileLayer, AutoRebarEngine.SectionBarLayer,
            AutoRebarEngine.SectionTextLayer, AutoRebarEngine.SectionTitleLayer, AutoRebarEngine.SectionDimLayer, AutoRebarEngine.SectionStatusLayer,
        };

        /// <summary>
        /// Warstwy, na których adopcja może usuwać wskazane obiekty — rysunek przekroju (DrawSection) bez znaczników na rzutach
        /// (SD-SECTION — usuwane osobno, tylko rozpoznane znaczniki tej litery) i bez warstwy chmurek. Oś pala (SD-Pile) — osobno.
        /// </summary>
        private static readonly HashSet<string> ErasableLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AutoRebarEngine.SectionSlabLayer, AutoRebarEngine.SectionPileLayer, AutoRebarEngine.SectionBarLayer,
            AutoRebarEngine.SectionTextLayer, AutoRebarEngine.SectionTitleLayer, AutoRebarEngine.SectionDimLayer,
        };

        private static bool HasApp(Entity e, string app)
        {
            using (var rb = e.GetXDataForApplication(app)) return rb != null;
        }

        /// <summary>Obiekt z danymi wtyczki (XData dowolnej aplikacji „RC_…”) — adopcja nigdy go nie usuwa.</summary>
        private static bool HasRcXData(Entity e)
        {
            using (var rb = e.XData)
            {
                if (rb == null) return false;
                foreach (TypedValue tv in rb)
                    if (tv.TypeCode == (short)DxfCode.ExtendedDataRegAppName
                        && Convert.ToString(tv.Value, Inv).StartsWith("RC_", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Typy obiektów, które rysuje DrawSection (adopcja usuwa tylko te).</summary>
        private static bool IsSectionType(Entity e)
            => e is Polyline || e is Line || e is DBText || e is MText || e is Solid || e is Circle || e is Dimension;

        /// <summary>
        /// Adopcja przekroju „nieznany” (stary tytuł „SECTION X-X” bez danych). Nic nie jest usuwane po zgadywaniu: usuwane są
        /// obiekty wskazane przez użytkownika (oknem; tryb bez okien — handle z RC_SG_ADOPT), tytuł i znaczniki SD-SECTION tej
        /// litery rozpoznane dokładnie po kształcie (DrawSection) i literze.
        /// </summary>
        public static bool Adopt(Document doc, SectionInfo legacy)
        {
            var db = doc.Database;
            var ed = doc.Editor;
            string L = legacy.Letter;
            bool script = ScriptMode.IsNonInteractive();

            // 0. litera jednoznaczna: jeden tytuł z tą literą (inaczej znaczniki mogą należeć do innego przekroju)
            int sameLetter = SectionStore.ReadAll(db, includeLegacy: true)
                .Count(s => !s.TitleId.IsNull && string.Equals(s.Letter, L, StringComparison.OrdinalIgnoreCase));
            if (sameLetter > 1)
            {
                Msg(ed, $"{L}-{L}: litera {L} użyta {sameLetter}× — adopcja niejednoznaczna (zmień literę innych przekrojów albo narysuj przekrój ponownie: RC_SECTION).");
                return false;
            }

            // 1. znaczniki przekroju tej litery → cięcie i strona (tylko jednoznaczne)
            var markers = FindLegacyMarkers(db, L);
            var pairs = PairMarkers(markers, out string why);
            if (pairs == null)
            {
                Msg(ed, $"{L}-{L}: przekrój bez danych — adopcja niemożliwa: {why} Narysuj przekrój ponownie (RC_SECTION) i usuń stary ręcznie.");
                return false;
            }

            // 2. obrys płyty przecięty linią cięcia (zewnętrzny, jednoznaczny); inaczej wskazanie użytkownika
            FindSlabs(db, pairs);
            var pair = pairs.Where(p => !p.Slab.IsNull).OrderBy(p => p.Slab.Handle.Value).FirstOrDefault();
            if (pair == null)
            {
                if (script)
                {
                    Msg(ed, $"{L}-{L}: nie rozpoznano jednoznacznie obrysu płyty przeciętego znacznikami — tryb bez okien, adopcja pominięta.");
                    return false;
                }
                var peo = new PromptEntityOptions($"\nWskaż obrys płyty przekroju {L}-{L} (rzut ze znacznikami): ");
                peo.SetRejectMessage("\nTo nie jest polilinia.");
                peo.AddAllowedClass(typeof(Polyline), true);
                var per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK) { Msg(ed, $"{L}-{L}: anulowano."); return false; }
                var slabPick = GaImportEngine.ResolveSlabPick(db, per.ObjectId, per.PickedPoint);   // obrys GA szeregu (2026-10-28)
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var pl = (Polyline)tr.GetObject(slabPick, OpenMode.ForRead);
                    var verts = GeometryHelper.GetPolylineVertices(pl);
                    pair = pairs.FirstOrDefault(p => Crossings(verts, p.P1, p.P2) >= 2);
                    tr.Commit();
                }
                if (pair == null) { Msg(ed, $"{L}-{L}: linia cięcia ze znaczników nie przecina wskazanego obrysu — adopcja przerwana."); return false; }
                pair.Slab = slabPick;
            }

            // 3. obiekty starego przekroju — wskazane przez użytkownika
            var chosen = new List<ObjectId>();
            string testList = script ? Environment.GetEnvironmentVariable("RC_SG_ADOPT") : null;
            if (!string.IsNullOrEmpty(testList))
            {
                Environment.SetEnvironmentVariable("RC_SG_ADOPT", null);   // jednorazowo
                foreach (var h in testList.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var id = SingleBarEngine.HandleToObjectId(db, h.Trim());
                    if (!id.IsNull && !id.IsErased) chosen.Add(id);
                }
            }
            else if (script)
            {
                Msg(ed, $"{L}-{L}: tryb bez okien bez listy obiektów (RC_SG_ADOPT) — adopcja pominięta.");
                return false;
            }
            else
            {
                var pso = new PromptSelectionOptions
                {
                    MessageForAdding = $"\nWskaż oknem obiekty starego przekroju {L}-{L} (beton, pręty, opisy, wymiary, pale, SSL) — zostaną usunięte: "
                };
                var sel = ed.GetSelection(pso);
                if (sel.Status != PromptStatus.OK) { Msg(ed, $"{L}-{L}: anulowano — nic nie usunięto."); return false; }
                chosen.AddRange(sel.Value.GetObjectIds());
            }

            // filtr wskazanych: usuwane tylko obiekty rysunku przekroju (warstwy i typy DrawSection, bez danych „RC_…”);
            // oś pala przekroju (SD-Pile, linia CENTER) tylko w obwiedni betonu / pali starego przekroju — pale rzutu zostają
            var toErase = new List<ObjectId>();
            var axisCand = new List<(ObjectId Id, Extents3d Ext)>();
            int skipped = 0;
            double minX = double.MaxValue, maxY = double.MinValue;
            Extents3d? box = null;
            var markerIds = new HashSet<ObjectId>(markers.SelectMany(m => new[] { m.PolyId, m.TextId }));
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (var id in chosen.Distinct())
                {
                    if (id == legacy.TitleId || markerIds.Contains(id)) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    if (id == pair.Slab || HasRcXData(e) || !IsSectionType(e)) { skipped++; continue; }
                    if (string.Equals(e.Layer, AutoRebarEngine.PileLayer, StringComparison.OrdinalIgnoreCase))
                    {
                        if (e is Line ln && string.Equals(ln.Linetype, "CENTER", StringComparison.OrdinalIgnoreCase))
                            axisCand.Add((id, ln.GeometricExtents));
                        else skipped++;   // pal rzutu (koło) / podpis — nie usuwamy
                        continue;
                    }
                    if (!ErasableLayers.Contains(e.Layer)) { skipped++; continue; }
                    toErase.Add(id);
                    bool slab = string.Equals(e.Layer, AutoRebarEngine.SectionSlabLayer, StringComparison.OrdinalIgnoreCase);
                    if (slab || string.Equals(e.Layer, AutoRebarEngine.SectionPileLayer, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var b = e.GeometricExtents;
                            if (box.HasValue) { var x = box.Value; x.AddExtents(b); box = x; } else box = b;
                        }
                        catch (System.Exception ex) { Log.Error("SectionUpdater.Adopt.Extents", ex); }
                    }
                    if (slab && e is Polyline pl)
                        foreach (var v in GeometryHelper.GetPolylineVertices(pl)) { minX = Math.Min(minX, v.X); maxY = Math.Max(maxY, v.Y); }
                }
                tr.Commit();
            }
            foreach (var (id, ext) in axisCand)
            {
                // oś pala przekroju: wystaje 33 mm nad i 35 mm pod pal (DrawSection) — zapas 100 mm
                bool inside = box.HasValue && ext.MinPoint.X >= box.Value.MinPoint.X - 100 && ext.MaxPoint.X <= box.Value.MaxPoint.X + 100
                              && ext.MinPoint.Y >= box.Value.MinPoint.Y - 100 && ext.MaxPoint.Y <= box.Value.MaxPoint.Y + 100;
                if (inside) toErase.Add(id); else skipped++;
            }
            if (toErase.Count == 0)
            {
                Msg(ed, $"{L}-{L}: nie wskazano obiektów starego przekroju — nic nie usunięto.");
                return false;
            }

            // 4. punkt wstawienia: lewy górny narożnik betonu starego przekroju (s-Slab), inaczej wskazanie
            Point3d topLeft;
            if (minX < double.MaxValue) topLeft = new Point3d(minX, maxY, 0);
            else if (script)
            {
                Msg(ed, $"{L}-{L}: wśród wskazanych obiektów nie ma betonu (warstwa {AutoRebarEngine.SectionSlabLayer}) — tryb bez okien, adopcja pominięta.");
                return false;
            }
            else
            {
                var pi = ed.GetPoint(new PromptPointOptions($"\nPunkt wstawienia przekroju {L}-{L} (lewy górny narożnik płyty): "));
                if (pi.Status != PromptStatus.OK) { Msg(ed, $"{L}-{L}: anulowano — nic nie usunięto."); return false; }
                topLeft = pi.Value;
            }

            // 5. przekrój: nib, dane z rysunku, okno parametrów
            NibAutoClose.EnsureForCommand(doc, pair.Slab, Prefix);
            var nrm = pair.AlongX ? new Vector2d(0, pair.Look) : new Vector2d(pair.Look, 0);
            var ctx = AutoRebarEngine.PrepareSection(db, pair.Slab, pair.P1, pair.P2, pair.P1 + nrm * 100);
            if (ctx == null) { Msg(ed, $"{L}-{L}: obrys płyty nie jest zamkniętą polilinią — adopcja przerwana."); return false; }
            foreach (var i in ctx.Info) ed.WriteMessage($"\n{Prefix} {i}");
            string letter = L;
            if (!script)
            {
                var dlg = new BricsCadRc.Dialogs.SectionDialog(ctx, L);
                if (Application.ShowModalWindow(dlg) != true) { Msg(ed, $"{L}-{L}: anulowano — nic nie usunięto."); return false; }
                letter = dlg.Letter;
            }
            var plan = SectionPlanner.Plan(ctx.Input);
            foreach (var w in plan.Warnings) Msg(ed, $"{letter}-{letter}: {w}");
            if (plan.Segments.Count == 0) { Msg(ed, $"{L}-{L}: linia cięcia nie przecina płyty — nic nie usunięto."); return false; }

            // 6. zapis: jedna transakcja — usunięcie wskazanych obiektów, starego tytułu i znaczników + nowy przekrój z rekordem
            int nMarks = 0;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in toErase) if (!id.IsErased) tr.GetObject(id, OpenMode.ForWrite).Erase();
                foreach (var id in markerIds) if (!id.IsNull && !id.IsErased) { tr.GetObject(id, OpenMode.ForWrite).Erase(); nMarks++; }
                if (!legacy.TitleId.IsErased) tr.GetObject(legacy.TitleId, OpenMode.ForWrite).Erase();
                AutoRebarEngine.DrawSectionRecorded(db, ctx, plan, topLeft, letter);
                tr.Commit();
            }
            Msg(ed, $"{letter}-{letter} przejęty: usunięte wskazane obiekty starego przekroju: {toErase.Count}; usunięte znaczniki na rzutach: {nMarks}; " +
                    $"pominięte wskazane obiekty (płyta, zbrojenie, dane RC_, inne warstwy): {skipped}; przekrój narysowany ponownie z danymi bezpiecznika.");
            ed.WriteMessage("\n");
            return true;
        }

        /// <summary>Znaczniki SD-SECTION bez danych (kształt jak w DrawSection) z literą <paramref name="letter"/> obok.</summary>
        private static List<Marker> FindLegacyMarkers(Database db, string letter)
        {
            var polys = new List<Marker>();
            var texts = new List<(ObjectId Id, Point2d P)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    if (!string.Equals(e.Layer, AutoRebarEngine.SectionMarkLayer, StringComparison.OrdinalIgnoreCase)) continue;
                    if (HasApp(e, AutoRebarEngine.SectionPartApp)) continue;   // znacznik przekroju z danymi
                    if (e is MText mt && string.Equals((mt.Text ?? "").Trim(), letter, StringComparison.OrdinalIgnoreCase))
                        texts.Add((id, new Point2d(mt.Location.X, mt.Location.Y)));
                    else if (e is Polyline pl && pl.NumberOfVertices == 4 && !pl.Closed)
                    {
                        Point2d v0 = pl.GetPoint2dAt(0), v1 = pl.GetPoint2dAt(1), p = pl.GetPoint2dAt(2), v3 = pl.GetPoint2dAt(3);
                        double d0 = v0.GetDistanceTo(p), d1 = v1.GetDistanceTo(p), d3 = v3.GetDistanceTo(p);
                        if (Math.Abs(d0 - 83) > 2 || Math.Abs(d1 - 294) > 2 || Math.Abs(d3 - 300) > 2) continue;
                        var dir = (v0 - p).GetNormal();
                        var nrm = (v1 - p).GetNormal();
                        if ((v3 - p).GetNormal().DotProduct(dir) < 0.999 || Math.Abs(nrm.DotProduct(dir)) > 0.01) continue;
                        polys.Add(new Marker { PolyId = id, P = p, Dir = dir, Nrm = nrm });
                    }
                }
                tr.Commit();
            }
            var res = new List<Marker>();
            var usedText = new HashSet<ObjectId>();
            foreach (var m in polys)
            {
                var lp = m.P + m.Dir * 214 + m.Nrm * 130;
                var best = texts.Where(t => !usedText.Contains(t.Id) && t.P.GetDistanceTo(lp) <= 300)
                                .OrderBy(t => t.P.GetDistanceTo(lp)).Select(t => t.Id).FirstOrDefault();
                if (best.IsNull) continue;
                usedText.Add(best);
                m.TextId = best;
                res.Add(m);
            }
            return res;
        }

        /// <summary>Pary znaczników (P1 ↔ P2 jednego rzutu); null + przyczyna, gdy niejednoznaczne.</summary>
        private static List<MarkerPair> PairMarkers(List<Marker> ms, out string why)
        {
            why = "";
            if (ms.Count == 0) { why = "nie znaleziono znaczników przekroju na rzutach (warstwa SD-SECTION)."; return null; }
            var free = new List<Marker>(ms);
            var pairs = new List<MarkerPair>();
            while (free.Count > 0)
            {
                var a = free[0];
                Marker best = null;
                double bestD = double.MaxValue;
                foreach (var b in free.Skip(1))
                {
                    var w = b.P - a.P;
                    if (w.Length < 100) continue;
                    if (a.Dir.DotProduct(b.Dir) > -0.999 || a.Nrm.DotProduct(b.Nrm) < 0.999 || w.GetNormal().DotProduct(a.Dir) < 0.999) continue;
                    if (w.Length < bestD) { bestD = w.Length; best = b; }
                }
                if (best == null) { why = $"znacznik bez pary ({ms.Count} znaczników)."; return null; }
                free.Remove(a); free.Remove(best);
                bool alongX = Math.Abs(a.Dir.X) >= Math.Abs(a.Dir.Y);
                var (p1, p2) = (alongX ? a.P.X <= best.P.X : a.P.Y <= best.P.Y) ? (a.P, best.P) : (best.P, a.P);
                pairs.Add(new MarkerPair
                {
                    A = a, B = best, P1 = p1, P2 = p2, AlongX = alongX,
                    Look = (alongX ? a.Nrm.Y : a.Nrm.X) >= 0 ? 1 : -1
                });
            }
            var f = pairs[0];
            double len = f.P1.GetDistanceTo(f.P2);
            if (pairs.Any(p => p.AlongX != f.AlongX || p.Look != f.Look || Math.Abs(p.P1.GetDistanceTo(p.P2) - len) > 1.0))
            {
                why = $"znaczniki niespójne ({pairs.Count} par o różnym kierunku, stronie albo długości cięcia — kilka przekrojów tej litery?).";
                return null;
            }
            return pairs;
        }

        /// <summary>Dla każdej pary: zewnętrzny zamknięty obrys przecięty linią cięcia (≥ 2 przecięcia) — jedyny; inaczej brak.</summary>
        private static void FindSlabs(Database db, List<MarkerPair> pairs)
        {
            var polys = new List<(ObjectId Id, List<Point2d> V, Extents2d Ext)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Polyline pl) || SectionLayers.Contains(pl.Layer)) continue;
                    if (!GeometryHelper.IsEffectivelyClosed(pl)) continue;
                    var v = GeometryHelper.GetPolylineVertices(pl);
                    if (v.Count < 3) continue;
                    polys.Add((id, v, new Extents2d(v.Min(p => p.X), v.Min(p => p.Y), v.Max(p => p.X), v.Max(p => p.Y))));
                }
                tr.Commit();
            }
            foreach (var pr in pairs)
            {
                var hit = polys.Where(p => Crossings(p.V, pr.P1, pr.P2) >= 2).ToList();
                var outer = hit.Where(h => !hit.Any(o => o.Id != h.Id && Inside(h.Ext, o.Ext))).ToList();
                if (outer.Count == 1) pr.Slab = outer[0].Id;
            }
        }

        private static bool Inside(Extents2d a, Extents2d b)
            => a.MinPoint.X >= b.MinPoint.X - 1e-6 && a.MinPoint.Y >= b.MinPoint.Y - 1e-6
               && a.MaxPoint.X <= b.MaxPoint.X + 1e-6 && a.MaxPoint.Y <= b.MaxPoint.Y + 1e-6;

        /// <summary>Liczba przecięć odcinka a–b z bokami zamkniętego wielokąta (wierzchołki, bez łuków).</summary>
        private static int Crossings(List<Point2d> v, Point2d a, Point2d b)
        {
            int n = 0;
            for (int i = 0; i < v.Count; i++)
            {
                Point2d c = v[i], d = v[(i + 1) % v.Count];
                double den = (b.X - a.X) * (d.Y - c.Y) - (b.Y - a.Y) * (d.X - c.X);
                if (Math.Abs(den) < 1e-12) continue;
                double t = ((c.X - a.X) * (d.Y - c.Y) - (c.Y - a.Y) * (d.X - c.X)) / den;
                double u = ((c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X)) / den;
                if (t >= 0 && t <= 1 && u >= 0 && u < 1) n++;
            }
            return n;
        }
    }
}
