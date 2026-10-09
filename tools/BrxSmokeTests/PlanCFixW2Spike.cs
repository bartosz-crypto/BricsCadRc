// W2-0 — spike zdarzeń klonowania BricsCAD V25 (docs/PLAN_POPRAWKI_W1_W2.md 4.1). BEZ zmian we wtyczce.
// Uruchamiany skryptem planc-w2spike.scr (run-planc-w2spike.ps1). Komendy RCSPIKE_W2_*.
//
// Loguje (planc-w2spike-log.txt): komendy (WillStart / Ended / Cancelled / Failed, CMDNAMES), zdarzenia Database
// (BeginDeepClone, BeginDeepCloneTranslation, DeepCloneEnded/Aborted, InsertMappingAvailable, WblockMappingAvailable,
// BeginInsert, InsertEnded/Aborted, Wblock*, ObjectAppended, ObjectErased) z kontekstem, Original==Destination,
// parami IdMapping (encje: klucz → wartość, IsCloned, IsPrimary) oraz stan powiązań obiektów RC po każdej komendzie
// ([13] pręta, [1] etykiety, [11]/[18] rozkładu, [16] opisu, [1] RC_BAR_LINK — kod DXF i wartość).
// Na czas spike'u BarCopyWatcher wtyczki jest odłączony (refleksja), żeby zobaczyć surowy wynik BricsCAD;
// RCSPIKE_W2_WATCHON podłącza go z powrotem (obserwacja DeepCloneObjects z EnsureUniqueBtr).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.BrxSmokeTests
{
    public partial class PlanCFixW1Tests
    {
        private const string SpikeLogFile = "planc-w2spike-log.txt";
        private static readonly List<string> _spike = new List<string>();
        private static readonly HashSet<Database> _spikeDbs = new HashSet<Database>();
        private static readonly HashSet<Document> _spikeDocs = new HashSet<Document>();
        private static int _spikeSeq;

        private static void SL(string line)
        {
            _spikeSeq++;
            _spike.Add(_spikeSeq.ToString("D5", Inv) + " " + line);
            try { File.WriteAllLines(Path.Combine(OutDir, SpikeLogFile), _spike, new UTF8Encoding(false)); }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("SpikeLog: " + ex.Message); }
        }

        private static string CmdNames()
        {
            try { return Convert.ToString(Application.GetSystemVariable("CMDNAMES"), Inv); }
            catch (System.Exception ex) { return "?" + ex.Message; }
        }

        private static string DbTag(Database db)
        {
            if (db == null) return "null";
            string f = "";
            try { f = Path.GetFileName(db.Filename ?? ""); }
            catch (System.Exception ex) { f = "?" + ex.Message; }
            return "db#" + db.GetHashCode().ToString("X", Inv) + (f.Length > 0 ? "(" + f + ")" : "");
        }

        private static string Cls(ObjectId id)
        {
            if (id.IsNull) return "null";
            try { return id.ObjectClass?.DxfName ?? "?"; }
            catch (System.Exception ex) { return "?" + ex.Message; }
        }

        private static bool IsEntityId(ObjectId id)
        {
            if (id.IsNull) return false;
            try { return id.ObjectClass != null && id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Entity))); }
            catch (System.Exception ex) { SL("  IsEntityId: " + ex.Message); return false; }
        }

        private static void LogMap(string ev, object sender, IdMapping m)
        {
            try
            {
                var sdb = sender as Database;
                SL($"EV {ev} sender={DbTag(sdb)} CMDNAMES={CmdNames()} ctx={m.DeepCloneContext} orig={DbTag(m.OriginalDatabase)} " +
                   $"dest={DbTag(m.DestinationDatabase)} orig==dest={(m.OriginalDatabase == m.DestinationDatabase)} dup={m.DuplicateRecordCloning}");
                int all = 0, ent = 0;
                foreach (IdPair p in m)
                {
                    all++;
                    if (!IsEntityId(p.Key)) continue;
                    ent++;
                    SL($"   pair K={p.Key.Handle}({Cls(p.Key)}{(p.Key.IsErased ? ",erased" : "")}) -> V={(p.Value.IsNull ? "null" : p.Value.Handle + "(" + Cls(p.Value) + ")")} " +
                       $"cloned={p.IsCloned} primary={p.IsPrimary} ownerXl={p.IsOwnerTranslated}");
                }
                SL($"   pairs all={all} entities={ent}");
            }
            catch (System.Exception ex) { SL($"EV {ev} WYJĄTEK " + ex); }
        }

        private static void SpikeAttachDb(Database db)
        {
            if (db == null || !_spikeDbs.Add(db)) return;
            db.BeginDeepClone            += (s, e) => LogMap("BeginDeepClone", s, e.IdMapping);
            db.BeginDeepCloneTranslation += (s, e) => LogMap("BeginDeepCloneTranslation", s, e.IdMapping);
            db.InsertMappingAvailable    += (s, e) => LogMap("InsertMappingAvailable", s, e.IdMapping);
            db.WblockMappingAvailable    += (s, e) => LogMap("WblockMappingAvailable", s, e.IdMapping);
            db.DeepCloneEnded            += (s, e) => SL($"EV DeepCloneEnded sender={DbTag(s as Database)} CMDNAMES={CmdNames()}");
            db.DeepCloneAborted          += (s, e) => SL($"EV DeepCloneAborted sender={DbTag(s as Database)} CMDNAMES={CmdNames()}");
            db.BeginInsert               += (s, e) => SL($"EV BeginInsert sender={DbTag(s as Database)} from={DbTag(e.From)} CMDNAMES={CmdNames()}");
            db.InsertEnded               += (s, e) => SL($"EV InsertEnded sender={DbTag(s as Database)} CMDNAMES={CmdNames()}");
            db.InsertAborted             += (s, e) => SL($"EV InsertAborted sender={DbTag(s as Database)} CMDNAMES={CmdNames()}");
            db.BeginWblockObjects        += (s, e) => SL($"EV BeginWblockObjects sender={DbTag(s as Database)} CMDNAMES={CmdNames()}");
            db.BeginWblockSelectedObjects += (s, e) => SL($"EV BeginWblockSelectedObjects sender={DbTag(s as Database)} CMDNAMES={CmdNames()}");
            db.BeginWblockEntireDatabase += (s, e) => SL($"EV BeginWblockEntireDatabase sender={DbTag(s as Database)} CMDNAMES={CmdNames()}");
            db.BeginWblockBlock          += (s, e) => SL($"EV BeginWblockBlock sender={DbTag(s as Database)} CMDNAMES={CmdNames()}");
            db.WblockNotice              += (s, e) => SL($"EV WblockNotice sender={DbTag(s as Database)} CMDNAMES={CmdNames()}");
            db.WblockEnded               += (s, e) => SL($"EV WblockEnded sender={DbTag(s as Database)} CMDNAMES={CmdNames()}");
            db.WblockAborted             += (s, e) => SL($"EV WblockAborted sender={DbTag(s as Database)} CMDNAMES={CmdNames()}");
            db.ObjectAppended += (s, e) =>
            {
                try
                {
                    var o = e.DBObject;
                    if (o is Entity) SL($"EV ObjectAppended {o.Handle}({o.GetRXClass().DxfName}) owner={o.OwnerId.Handle}({Cls(o.OwnerId)}) db={DbTag(s as Database)} {RcTag(o)}");
                }
                catch (System.Exception ex) { SL("EV ObjectAppended WYJĄTEK " + ex.Message); }
            };
            db.ObjectErased += (s, e) =>
            {
                try
                {
                    var o = e.DBObject;
                    if (o is Entity) SL($"EV ObjectErased {o.Handle}({o.GetRXClass().DxfName}) erased={e.Erased} db={DbTag(s as Database)} CMDNAMES={CmdNames()}");
                }
                catch (System.Exception ex) { SL("EV ObjectErased WYJĄTEK " + ex.Message); }
            };
        }

        private static void SpikeAttachDoc(Document doc)
        {
            if (doc == null || !_spikeDocs.Add(doc)) return;
            doc.CommandWillStart += (s, e) => SL($"CMD WillStart {e.GlobalCommandName} doc={DbTag((s as Document)?.Database)} CMDNAMES={CmdNames()}");
            doc.CommandEnded     += (s, e) => { SL($"CMD Ended {e.GlobalCommandName} CMDNAMES={CmdNames()}"); DumpState(s as Document, e.GlobalCommandName); };
            doc.CommandCancelled += (s, e) => { SL($"CMD Cancelled {e.GlobalCommandName} CMDNAMES={CmdNames()}"); DumpState(s as Document, e.GlobalCommandName); };
            doc.CommandFailed    += (s, e) => SL($"CMD Failed {e.GlobalCommandName} CMDNAMES={CmdNames()}");
            SpikeAttachDb(doc.Database);
        }

        /// <summary>Krótki opis XData RC obiektu (w ObjectAppended: czy XData jest już w klonie i z jakim handle).</summary>
        private static string RcTag(DBObject o)
        {
            var sb = new StringBuilder();
            foreach (var (app, idx) in new[] { ("RC_SINGLE_BAR", new[] { 1, 13, 14 }), ("RC_BAR_LABEL", new[] { 1 }),
                                                ("RC_BAR_BLOCK", new[] { 1, 11, 18 }), ("RC_BAR_ANNOT", new[] { 1, 16 }),
                                                ("RC_BAR_LINK", new[] { 1 }) })
            {
                ResultBuffer rb = null;
                try { rb = o.GetXDataForApplication(app); }
                catch (System.Exception ex) { sb.Append(app + ":? " + ex.Message); }
                if (rb == null) continue;
                using (rb)
                {
                    var v = rb.AsArray();
                    sb.Append(app).Append('{');
                    foreach (int i in idx)
                        sb.Append('[').Append(i.ToString(Inv)).Append("]=")
                          .Append(i < v.Length ? v[i].TypeCode.ToString(Inv) + ":" + Convert.ToString(v[i].Value, Inv) : "-").Append(' ');
                    sb.Append("} ");
                }
            }
            return sb.ToString();
        }

        private static void DumpState(Document doc, string cmd)
        {
            var db = doc?.Database;
            if (db == null) return;
            try
            {
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    int n = 0;
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        var o = tr.GetObject(id, OpenMode.ForRead);
                        string tag = RcTag(o);
                        if (tag.Length == 0) continue;
                        n++;
                        string pos = o is BlockReference br
                            ? "pos=" + P(br.Position) + " sf=" + br.ScaleFactors.X.ToString("0.###", Inv) + "," + br.ScaleFactors.Y.ToString("0.###", Inv)
                              + " rot=" + br.Rotation.ToString("0.####", Inv) + " btr=" + br.BlockTableRecord.Handle
                            : o is Polyline pl ? "v0=" + P(pl.GetPoint3dAt(0)) + " n=" + pl.NumberOfVertices.ToString(Inv)
                            : o is MLeader ? "mleader" : "";
                        SL($"   ST {id.Handle}({o.GetRXClass().DxfName}) {pos} {tag}");
                    }
                    SL($"   ST [{cmd}] obiektów RC: {n}");
                }
            }
            catch (System.Exception ex) { SL("   ST WYJĄTEK " + ex.Message); }
        }

        private static void WatcherCall(string method)
            => Call("BricsCadRc.Core.BarCopyWatcher", method);

        // ---- komendy spike'u ----

        /// <summary>Pasy Y = 20000·k: zestaw polilinii (x=0) i pręta-bloku (x=8000): pręt 00 A=3000 + etykieta + rozkład + opis,
        /// pierścień 44 (polilinia, x=5000) z RC_BAR_LINK.</summary>
        [CommandMethod("RCSPIKE_W2_SETUP", CommandFlags.Modal)]
        public void SpikeSetup()
        {
            _spike.Clear();
            _spikeSeq = 0;
            var db = ActiveDb();
            SL("W2-0 spike, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv) + ", wtyczka " + SafePluginLoc());
            try
            {
                SL("czyszczenie: " + CleanModelSpace(db).ToString(Inv));
                Call(SingleBarType, "EnsureAppIdRegistered", db);
                Call(BarBlockType, "EnsureAppIdRegistered", db);
                Call(AnnotType, "EnsureAppIdRegistered", db);
                SetFormat("Polyline");
                WatcherCall("Unregister");
                SL("BarCopyWatcher odłączony na czas spike'u");
                for (int k = 1; k <= 14; k++)
                {
                    double y0 = 20000.0 * k;
                    foreach (var (fmt, x0) in new[] { ("Polyline", 0.0), ("Block", 8000.0) })
                    {
                        var (bar, label) = PlaceBarWithLabel(db, fmt, new Def { A = 3000, At = new Point3d(x0, y0, 0) });
                        var (dist, annot) = Distribute(db, bar, x0 + 40, y0 + 1040, x0 + 3040, y0 + 2000);
                        SL($"pas {k} {fmt}: bar={bar.Handle} label={label.Handle} dist={dist.Handle} annot={annot.Handle} ext={Ext(db, new[] { bar, label, dist, annot })}");
                    }
                    var ring = PlaceBar(db, "Polyline", new Def { Code = W4Compat.RingCode, A = 300, At = new Point3d(5000, y0, 0) });
                    SL($"pas {k} 44: ring={ring.Handle} links={string.Join(",", LinksOf(db, ring).Select(i => i.Handle.ToString()))}");
                }
                foreach (Document d in Application.DocumentManager) SpikeAttachDoc(d);
                Application.DocumentManager.DocumentCreated += (s, e) => { SL("DOC Created " + DbTag(e.Document?.Database)); SpikeAttachDoc(e.Document); };
                SL("podpięto zdarzenia");
            }
            catch (System.Exception ex) { SL("SETUP WYJĄTEK " + ex); }
        }

        private static string Ext(Database db, IEnumerable<ObjectId> ids)
        {
            var e = new Extents3d();
            bool any = false;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                foreach (var id in ids)
                {
                    try
                    {
                        var ge = ((Entity)tr.GetObject(id, OpenMode.ForRead)).GeometricExtents;
                        if (!any) { e = ge; any = true; } else e.AddExtents(ge);
                    }
                    catch (System.Exception ex) { SL("Ext: " + ex.Message); }
                }
            return any ? P(e.MinPoint) + "-" + P(e.MaxPoint) : "?";
        }

        [CommandMethod("RCSPIKE_W2_MARK", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void SpikeMark() => SL("---- MARK ----");

        [CommandMethod("RCSPIKE_W2_WATCHON", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void SpikeWatchOn()
        {
            try { WatcherCall("Register"); SL("BarCopyWatcher podłączony z powrotem"); }
            catch (System.Exception ex) { SL("WATCHON WYJĄTEK " + ex.Message); }
        }

        /// <summary>WBLOCK przez API (zapas, gdy -WBLOCK w skrypcie nie zadziała): pas 8 do pliku tymczasowego.</summary>
        [CommandMethod("RCSPIKE_W2_APIWBLOCK", CommandFlags.Modal)]
        public void SpikeApiWblock()
        {
            var db = ActiveDb();
            try
            {
                var ids = new ObjectIdCollection();
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        try
                        {
                            var ge = ent.GeometricExtents;
                            if (ge.MinPoint.Y > 20000 * 9 - 3000 && ge.MaxPoint.Y < 20000 * 9 + 6000) ids.Add(id);
                        }
                        catch (System.Exception ex) { SL("APIWBLOCK ext: " + ex.Message); }
                    }
                }
                string path = Path.Combine(Path.GetTempPath(), "w2spike_api.dwg");
                SL($"APIWBLOCK {ids.Count} obiektów → {path}");
                using (var outDb = db.Wblock(ids, new Point3d(0, 180000, 0)))
                    outDb.SaveAs(path, DwgVersion.Current);
                SL("APIWBLOCK zapisano");
            }
            catch (System.Exception ex) { SL("APIWBLOCK WYJĄTEK " + ex); }
        }

        [CommandMethod("RCSPIKE_W2_END", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void SpikeEnd()
        {
            try { WatcherCall("Register"); }
            catch (System.Exception ex) { SL("END Register: " + ex.Message); }
            SL("koniec spike'u");
            _spike.Add("END");
            try { File.WriteAllLines(Path.Combine(OutDir, SpikeLogFile), _spike, new UTF8Encoding(false)); }
            catch (System.Exception ex) { Msg("\nSpike: błąd zapisu logu " + ex.Message + "\n"); }
        }
    }
}
