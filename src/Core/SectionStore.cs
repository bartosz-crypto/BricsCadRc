using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    public enum SectionStatus { Aktualny, Nieaktualny, Osierocony, Nieznany, Kopia, CzesciBezTytulu, Uszkodzony }

    /// <summary>Przekrój na rysunku (tytuł + części) i jego stan (bezpiecznik przekrojów).</summary>
    public sealed class SectionInfo
    {
        public ObjectId TitleId = ObjectId.Null;
        public string TitleHandle = "";
        public string Guid = "";
        public string Letter = "";
        /// <summary>Litera z treści tytułu („SECTION B-B”); "" gdy nie rozpoznano.</summary>
        public string TitleTextLetter = "";
        public SectionRecord Record;
        public SectionStatus Status = SectionStatus.Aktualny;
        public List<string> Reasons = new List<string>();
        public List<string> Info = new List<string>();
        public List<string> Warnings = new List<string>();
        /// <summary>Części potwierdzone w obu kierunkach (na liście rekordu i z [1] → ten tytuł, [4] = GUID).</summary>
        public List<(ObjectId Id, string Role, string Key)> Parts = new List<(ObjectId, string, string)>();
        /// <summary>Chmurki „NIEAKTUALNY” tego przekroju (rola CLOUD, [1] → ten tytuł).</summary>
        public List<ObjectId> Clouds = new List<ObjectId>();
        public string SlabPicked = "", SlabOuter = "", SelfLink = "";
        public bool NewVersionDiffers;
        /// <summary>Osierocony: handle zamkniętego obrysu w tym samym miejscu (podpowiedź dla RC_SECTION_UPDATE); null = brak.</summary>
        public string OutlineHint;

        public string Label => Letter.Length > 0 ? Letter + "-" + Letter : "?";
    }

    /// <summary>
    /// Odczyt przekrojów RC_SECTION z rysunku i ocena ich aktualności (bezpiecznik przekrojów, etap 1).
    /// <see cref="ReadAll"/> — jeden przebieg przestrzeni modelu (tytuły RC_SECTION, części RC_SECTION_PART, stare tytuły);
    /// szybkie wyjście, gdy w rysunku nie ma RegApp przekrojów (i nie szukamy starych tytułów). <see cref="Evaluate"/> —
    /// przeliczenie (AutoRebarEngine.RecomputeSection, tylko odczyt) i werdykt SectionFingerprint.Compare.
    /// <see cref="SyncClouds"/> — chmurki „NIEAKTUALNY” (zapis tylko, gdy stan się zmienia). Bez cache (etap 1, czas w logu).
    /// </summary>
    public static class SectionStore
    {
        private static readonly Regex TitleRx = new Regex(@"SECTION\s+([A-Z0-9]{1,3})\s*-\s*([A-Z0-9]{1,3})", RegexOptions.IgnoreCase);

        private sealed class PartRec
        {
            public ObjectId Id;
            public string Handle, TitleLink, Role, Key, Guid;
        }

        public static string StatusText(SectionStatus s)
        {
            switch (s)
            {
                case SectionStatus.Aktualny:        return "aktualny";
                case SectionStatus.Nieaktualny:     return "NIEAKTUALNY";
                case SectionStatus.Osierocony:      return "osierocony";
                case SectionStatus.Nieznany:        return "nieznany";
                case SectionStatus.Kopia:           return "kopia";
                case SectionStatus.CzesciBezTytulu: return "części bez tytułu";
                case SectionStatus.Uszkodzony:      return "uszkodzony";
                default:                            return s.ToString();
            }
        }

        /// <summary>Czy rysunek ma RegApp przekrojów (szybkie wyjście — rysunek bez nowych przekrojów).</summary>
        public static bool HasRegApps(Database db)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                bool has = rat.Has(AutoRebarEngine.SectionTitleApp) || rat.Has(AutoRebarEngine.SectionPartApp);
                tr.Commit();
                return has;
            }
        }

        /// <summary>
        /// Przekroje rysunku (bez przeliczania). includeLegacy — także stare tytuły „SECTION X-X” (AP-TEXT) bez danych → Nieznany.
        /// Statusy ustalane tu: Uszkodzony, Kopia, CzesciBezTytulu, Nieznany; pozostałe — <see cref="Evaluate"/>.
        /// </summary>
        public static List<SectionInfo> ReadAll(Database db, bool includeLegacy)
        {
            var list = new List<SectionInfo>();
            if (db == null) return list;
            if (!includeLegacy && !HasRegApps(db)) return list;

            var parts = new List<PartRec>();
            var titles = new List<SectionInfo>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    try
                    {
                        using (var rb = e.GetXDataForApplication(AutoRebarEngine.SectionPartApp))
                        {
                            var v = rb?.AsArray();
                            if (v != null)
                            {
                                parts.Add(new PartRec
                                {
                                    Id = id, Handle = XLink.Norm(e.Handle.ToString()),
                                    TitleLink = v.Length > 1 ? XLink.Read(v[1]) : "",
                                    Role = v.Length > 2 ? Convert.ToString(v[2].Value, CultureInfo.InvariantCulture) : "",
                                    Key = v.Length > 3 ? Convert.ToString(v[3].Value, CultureInfo.InvariantCulture) : "",
                                    Guid = v.Length > 4 ? Convert.ToString(v[4].Value, CultureInfo.InvariantCulture) : ""
                                });
                                continue;
                            }
                        }
                        using (var rb = e.GetXDataForApplication(AutoRebarEngine.SectionTitleApp))
                        {
                            var v = rb?.AsArray();
                            if (v != null)
                            {
                                var s = new SectionInfo
                                {
                                    TitleId = id, TitleHandle = XLink.Norm(e.Handle.ToString()),
                                    Guid = v.Length > 2 ? Convert.ToString(v[2].Value, CultureInfo.InvariantCulture) : "",
                                    Letter = v.Length > 3 ? Convert.ToString(v[3].Value, CultureInfo.InvariantCulture) : "",
                                    SlabPicked = v.Length > 4 ? XLink.Read(v[4]) : "",
                                    SlabOuter = v.Length > 5 ? XLink.Read(v[5]) : "",
                                    SelfLink = v.Length > 6 ? XLink.Read(v[6]) : "",
                                    TitleTextLetter = TitleLetter(e),
                                };
                                s.Record = AutoRebarEngine.ReadRecord(tr, e);
                                titles.Add(s);
                                continue;
                            }
                        }
                        if (includeLegacy && e is MText mt
                            && string.Equals(mt.Layer, AutoRebarEngine.SectionTitleLayer, StringComparison.OrdinalIgnoreCase))
                        {
                            string l = TitleLetter(mt);
                            if (l.Length > 0)
                                list.Add(new SectionInfo
                                {
                                    TitleId = id, TitleHandle = XLink.Norm(mt.Handle.ToString()), Letter = l, TitleTextLetter = l,
                                    Status = SectionStatus.Nieznany,
                                    Reasons = { "przekrój bez danych bezpiecznika (narysowany przed nim albo starą wtyczką) — aktualności nie da się sprawdzić" }
                                });
                        }
                    }
                    catch (System.Exception ex) { Log.Error("SectionStore.ReadAll", ex); }
                }
                tr.Commit();
            }

            var claimed = new HashSet<ObjectId>();
            // ten sam GUID w kilku tytułach (PASTECLIP / INSERT — BricsCAD tłumaczy [6], więc kopia ma poprawne Self):
            // pierwszy (najmniejszy handle) = oryginał, pozostałe = kopia
            var firstOfGuid = titles.Where(t => !string.IsNullOrEmpty(t.Guid)).GroupBy(t => t.Guid)
                                    .ToDictionary(g => g.Key, g => g.OrderBy(t => HandleValue(t.TitleHandle)).First());
            foreach (var s in titles)
            {
                if (s.Record == null || string.IsNullOrEmpty(s.Guid))
                {
                    s.Status = SectionStatus.Uszkodzony;
                    s.Reasons.Add(s.Record == null ? "brak albo uszkodzony rekord przekroju (Xrecord NA_RC_SECTION)" : "brak identyfikatora przekroju");
                }
                else if (!XLink.Same(s.SelfLink, s.TitleHandle))
                {
                    s.Status = SectionStatus.Kopia;
                    s.Reasons.Add("kopia przekroju bez przepięcia danych (skopiowana poza wtyczką?) — usuń albo narysuj przekrój ponownie");
                }
                else if (!ReferenceEquals(firstOfGuid[s.Guid], s))
                {
                    s.Status = SectionStatus.Kopia;
                    s.Reasons.Add($"kopia przekroju {firstOfGuid[s.Guid].Label} (ten sam identyfikator co tytuł {firstOfGuid[s.Guid].TitleHandle} — wklejona / wstawiona bez przepięcia) — usuń albo narysuj przekrój ponownie");
                }
                if (s.Record != null && s.Letter.Length > 0 && s.TitleTextLetter.Length > 0
                    && !string.Equals(s.TitleTextLetter, s.Letter, StringComparison.OrdinalIgnoreCase))
                    s.Warnings.Add($"litera w tytule ({s.TitleTextLetter}) ≠ litera przekroju ({s.Letter})");

                // części: potwierdzone w obu kierunkach (lista rekordu ↔ [1] tytuł i [4] GUID)
                var listed = new HashSet<string>((s.Record?.Parts ?? new List<SectionRecord.Part>()).Select(p => XLink.Norm(p.Handle)));
                foreach (var p in parts)
                {
                    if (!XLink.Same(p.TitleLink, s.TitleHandle) || p.Guid != s.Guid) continue;
                    if (p.Role == AutoRebarEngine.RoleCloud) { s.Clouds.Add(p.Id); claimed.Add(p.Id); continue; }
                    // kopia (ten sam GUID / [6] ≠ handle): jej części z [1] → ta kopia (lista rekordu wskazuje oryginał)
                    if (!listed.Contains(p.Handle) && s.Status != SectionStatus.Kopia) continue;
                    s.Parts.Add((p.Id, p.Role, p.Key));
                    claimed.Add(p.Id);
                }
            }
            list.InsertRange(0, titles);

            // części bez tytułu (pogrupowane po GUID): [1] puste, tytuł usunięty / bez danych albo GUID się nie zgadza
            foreach (var g in parts.Where(p => !claimed.Contains(p.Id)).GroupBy(p => p.Guid ?? ""))
            {
                var orphan = new SectionInfo { Guid = g.Key, Status = SectionStatus.CzesciBezTytulu };
                foreach (var p in g)
                {
                    if (p.Role == AutoRebarEngine.RoleCloud) orphan.Clouds.Add(p.Id);
                    else orphan.Parts.Add((p.Id, p.Role, p.Key));
                }
                orphan.Reasons.Add($"obiekty przekroju bez tytułu: {orphan.Parts.Count}" +
                                   (orphan.Clouds.Count > 0 ? $", chmurki: {orphan.Clouds.Count}" : "") +
                                   " (skopiowane albo tytuł usunięty)");
                list.Add(orphan);
            }

            // litera użyta więcej niż raz (przekroje z tytułem)
            foreach (var dup in list.Where(s => !s.TitleId.IsNull && s.Letter.Length > 0)
                                    .GroupBy(s => s.Letter.ToUpperInvariant()).Where(g => g.Count() > 1))
                foreach (var s in dup)
                    s.Warnings.Add(s.Record != null && s.Status != SectionStatus.Nieznany && !s.Parts.Any(p => p.Role == "MARK")
                        ? $"kopia przekroju {dup.Key} (bez znaczników na rzucie) — litera użyta {dup.Count()}×"
                        : $"litera {dup.Key} użyta {dup.Count()}×");
            return list;
        }

        private static long HandleValue(string hex) => XLink.TryParse(hex, out long v) ? v : long.MaxValue;

        internal static string TitleLetter(Entity e)
        {
            string text = e is MText mt ? (mt.Contents ?? "") : e is DBText dt ? (dt.TextString ?? "") : "";
            var m = TitleRx.Match(text);
            return m.Success && string.Equals(m.Groups[1].Value, m.Groups[2].Value, StringComparison.OrdinalIgnoreCase)
                ? m.Groups[1].Value.ToUpperInvariant() : "";
        }

        /// <summary>
        /// Przeliczenie i werdykt przekroju (tylko odczyt). Dotyczy przekrojów bez statusu ustalonego w ReadAll
        /// (Uszkodzony / Kopia / Nieznany / CzesciBezTytulu — bez zmian). Cięcie ze znaczników rzutu wskazanego (V0.P1 / V0.P2,
        /// wierzchołek [2]); brak znacznika — wartości bezwzględne z rekordu. Litera bieżąca — z liter znaczników.
        /// </summary>
        public static void Evaluate(Database db, SectionInfo s)
        {
            if (s == null || s.Record == null || s.TitleId.IsNull) return;
            if (s.Status != SectionStatus.Aktualny && s.Status != SectionStatus.Nieaktualny) return;
            s.Reasons.Clear(); s.Info.Clear();
            var rec = s.Record;
            try
            {
                var slabId = SingleBarEngine.HandleToObjectId(db, s.SlabPicked);
                if (slabId.IsNull || slabId.IsErased)
                {
                    s.Status = SectionStatus.Osierocony;
                    s.Reasons.Add("obrys płyty usunięty (przekrój bez źródła)");
                    string hint = OutlineAt(db, rec.MinPoint);
                    s.OutlineHint = hint;
                    if (hint != null) s.Info.Add($"w tym samym miejscu jest zamknięty obrys {hint} — przekrój można narysować ponownie na nim (RC_SECTION_UPDATE)");
                    return;
                }

                CurrentCut(db, s, out var p1, out var p2, out string letter);
                var rc = AutoRebarEngine.RecomputeSection(db, rec, slabId, p1, p2, letter);
                if (rc.Error != null)
                {
                    s.Status = SectionStatus.Osierocony;
                    s.Reasons.Add(rc.Error);
                    return;
                }
                var v = SectionFingerprint.Compare(rec.StoredState(), rc.State);
                s.Status = v.Stale ? SectionStatus.Nieaktualny : SectionStatus.Aktualny;
                s.Reasons.AddRange(v.Reasons);
                s.Info.AddRange(v.Info);
                s.NewVersionDiffers = v.NewVersionDiffers;
                if (rc.NibOpen)
                {
                    s.Status = SectionStatus.Nieaktualny;
                    s.Reasons.Add("otwarta linia uskoku nibu do domknięcia (do sprawdzenia — RC_SECTION domyka nib przed rysowaniem)");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("SectionStore.Evaluate", ex);
                s.Status = SectionStatus.Uszkodzony;
                s.Reasons.Add("błąd przeliczenia: " + ex.Message);
            }
        }

        /// <summary>
        /// Bieżące cięcie i litera przekroju (Evaluate, RC_SECTION_UPDATE): cięcie ze znaczników rzutu wskazanego (V0.P1 / V0.P2,
        /// wierzchołek [2]); brak znacznika — wartości bezwzględne z rekordu. Litera — z liter znaczników, gdy różna od rekordu.
        /// </summary>
        public static void CurrentCut(Database db, SectionInfo s, out Point2d p1, out Point2d p2, out string letter)
        {
            var rec = s.Record;
            p1 = new Point2d(rec.P1.X, rec.P1.Y); p2 = new Point2d(rec.P2.X, rec.P2.Y);
            var letters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                Point2d? m1 = null, m2 = null;
                foreach (var (id, role, key) in s.Parts)
                {
                    if (id.IsErased) continue;
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (role == "MARK" && e is Polyline pl && pl.NumberOfVertices >= 3)
                    {
                        if (key == "V0.P1") m1 = pl.GetPoint2dAt(2);
                        else if (key == "V0.P2") m2 = pl.GetPoint2dAt(2);
                    }
                    else if (role == "MARKTXT" && e is MText mt)
                    {
                        string t = (mt.Text ?? "").Trim();
                        if (t.Length > 0) letters.Add(t);
                    }
                }
                tr.Commit();
                if (m1.HasValue && m2.HasValue) { p1 = m1.Value; p2 = m2.Value; }
            }
            letter = rec.Letter;
            var other = letters.Where(l => !string.Equals(l, rec.Letter, StringComparison.OrdinalIgnoreCase)).ToList();
            if (other.Count > 0) letter = other.OrderBy(x => x, StringComparer.Ordinal).First().ToUpperInvariant();
        }

        /// <summary>ReadAll + Evaluate każdego przekroju; czas w logu.</summary>
        public static List<SectionInfo> CheckAll(Database db, bool includeLegacy)
        {
            var sw = Stopwatch.StartNew();
            var list = ReadAll(db, includeLegacy);
            foreach (var s in list) Evaluate(db, s);
            sw.Stop();
            Log.Info($"SectionStore.CheckAll: przekrojów {list.Count}, {sw.ElapsedMilliseconds} ms.");
            return list;
        }

        /// <summary>Zamknięta polilinia o MinPoint w tym samym miejscu (≤ 1 mm) — podpowiedź dla przekroju osieroconego.</summary>
        private static string OutlineAt(Database db, (double X, double Y) min)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Polyline pl) || !GeometryHelper.IsEffectivelyClosed(pl)) continue;
                    var v = GeometryHelper.SquaredOrSame(GeometryHelper.GetPolylineVertices(pl));
                    if (v.Count < 3) continue;
                    if (Math.Abs(v.Min(p => p.X) - min.X) <= 1.0 && Math.Abs(v.Min(p => p.Y) - min.Y) <= 1.0)
                        return XLink.Norm(pl.Handle.ToString());
                }
                tr.Commit();
            }
            return null;
        }

        /// <summary>
        /// Chmurki „NIEAKTUALNY”: przekrój NIEAKTUALNY bez chmurki → chmurka; aktualny z chmurką → usunięcie; chmurki bez tytułu →
        /// usunięcie. Inne statusy (osierocony, kopia, uszkodzony, nieznany) — bez zmian. Zapis tylko, gdy coś się zmienia (wtedy jedna
        /// transakcja). Zwraca (dodane, usunięte). Wołać w komendzie (zablokowany dokument).
        /// </summary>
        public static (int Added, int Removed) SyncClouds(Database db, List<SectionInfo> infos)
        {
            var add = infos.Where(s => !s.TitleId.IsNull && s.Status == SectionStatus.Nieaktualny && s.Clouds.Count == 0).ToList();
            var del = infos.Where(s => (s.Status == SectionStatus.Aktualny || s.Status == SectionStatus.CzesciBezTytulu) && s.Clouds.Count > 0).ToList();
            if (add.Count == 0 && del.Count == 0) return (0, 0);
            int added = 0, removed = 0;
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var s in del)
                    {
                        removed += AutoRebarEngine.RemoveCloud(tr, s.Clouds);
                        s.Clouds.Clear();
                    }
                    foreach (var s in add)
                    {
                        var ext = SectionExtents(tr, s);
                        if (!ext.HasValue) continue;
                        AutoRebarEngine.DrawCloud(tr, db, s.TitleId, s.Guid, ext.Value);
                        added++;
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex) { Log.Error("SectionStore.SyncClouds", ex); }
            if (added + removed > 0) Log.Info($"SectionStore.SyncClouds: chmurki dodane {added}, usunięte {removed}.");
            return (added, removed);
        }

        /// <summary>Zakres rysunku przekroju: tytuł + części poza znacznikami na rzutach.</summary>
        private static Extents3d? SectionExtents(Transaction tr, SectionInfo s)
        {
            Extents3d? ext = null;
            void Add(ObjectId id)
            {
                if (id.IsNull || id.IsErased) return;
                try
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) return;
                    var b = e.GeometricExtents;
                    if (ext.HasValue) { var x = ext.Value; x.AddExtents(b); ext = x; }
                    else ext = b;
                }
                catch (System.Exception ex) { Log.Error("SectionStore.SectionExtents", ex); }
            }
            Add(s.TitleId);
            foreach (var (id, role, _) in s.Parts)
                if (role != "MARK" && role != "MARKTXT") Add(id);
            return ext;
        }
    }
}
