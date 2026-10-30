using System;
using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using BricsCadRc.Core;

namespace BricsCadRc.Commands
{
    /// <summary>
    /// RC_FIX_BAR_LABELS (prośba Bartka 2026-10-30 — „mini-komenda, żeby przebudować labele, wcześniejsze wersje je popsuły”): naprawa
    /// powiązań pręt (RC_SINGLE_BAR [13] LabelHandle) ↔ opis (MLeader RC_BAR_LABEL [1]) po kopiowaniu bez przepięcia:
    ///   1. pary wzajemne (pręt → opis i opis → pręt) — zostają;
    ///   2. pozostałe opisy i pręty bez wzajemnej pary — dobierane po odległości tekstu / grota opisu od pręta (najbliższe najpierw, ≤ 3000);
    ///   3. dla każdego pręta z opisem: tekst opisu z pręta (liczba z rozkładów + „H12-17”, jak przy zmianie pręta) i grot na pręcie.
    /// Opisy bez pręta w pobliżu i pręty bez opisu — tylko komunikat (nic nie jest usuwane ani tworzone). Zaznaczenie — Enter = cały model.
    /// </summary>
    public static class LabelRepairCommands
    {
        private const string T = "[RC LABEL]";
        private const double MaxPairDist = 3000.0, ArrowOnBar = 300.0;

        private sealed class BarItem
        {
            public ObjectId Id; public string H; public string Label; public string Mark;
        }

        private sealed class LabelItem
        {
            public ObjectId Id; public string H; public string Bar; public Point3d Arrow, Text;
        }

        [CommandMethod("RC_FIX_BAR_LABELS", CommandFlags.Modal)]
        public static void FixBarLabels()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;
            try
            {
                HashSet<ObjectId> only = null;
                var po = new PromptSelectionOptions { MessageForAdding = "\nWskaż pręty i opisy do naprawy (Enter — cały rysunek): " };
                var sel = ed.GetSelection(po);
                if (sel.Status == PromptStatus.OK) only = new HashSet<ObjectId>(sel.Value.GetObjectIds());
                else if (sel.Status != PromptStatus.Error && sel.Status != PromptStatus.None) return;

                var bars = new List<BarItem>();
                var labels = new List<LabelItem>();
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    foreach (var be in BarEntity.ScanModelSpace(tr, db, BarKinds.Polyline | BarKinds.Block))
                    {
                        if (only != null && !only.Contains(be.Entity.ObjectId)) continue;
                        var b = be.Read();
                        if (b == null) continue;
                        bars.Add(new BarItem { Id = be.Entity.ObjectId, H = be.Entity.Handle.Value.ToString("X8"), Label = b.LabelHandle ?? "", Mark = b.Mark ?? "" });
                    }
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased || (only != null && !only.Contains(id))) continue;
                        if (!(tr.GetObject(id, OpenMode.ForRead) is MLeader ml) || ml.GetXDataForApplication(SingleBarEngine.XLabelAppName) == null) continue;
                        Point3d arrow, text;
                        try { arrow = ml.GetFirstVertex(0); text = ml.TextLocation; }
                        catch (System.Exception ex) { Log.Error("RC_FIX_BAR_LABELS.Read", ex); continue; }
                        labels.Add(new LabelItem { Id = id, H = ml.Handle.Value.ToString("X8"), Bar = SingleBarEngine.ReadBarHandleFromLabel(ml) ?? "",
                                                   Arrow = arrow, Text = text });
                    }
                    tr.Commit();
                }
                if (labels.Count == 0) { ed.WriteMessage($"\n{T} Brak opisów prętów (RC_BAR_LABEL) w zaznaczeniu.\n"); return; }

                // 1) pary wzajemne
                var pair = new Dictionary<ObjectId, ObjectId>();   // pręt → opis
                var labelOf = labels.ToDictionary(l => l.H, l => l);
                foreach (var b in bars)
                    if (labelOf.TryGetValue(NormHex(b.Label), out var l) && XLink.Same(l.Bar, b.H)) pair[b.Id] = l.Id;
                int kept = pair.Count;
                var freeLabels = labels.Where(l => !pair.ContainsValue(l.Id)).ToList();
                var freeBars = bars.Where(b => !pair.ContainsKey(b.Id)).ToList();

                // 2) dobór po odległości (tekst opisu / grot do pręta)
                var cand = new List<(double d, BarItem b, LabelItem l)>();
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    foreach (var b in freeBars)
                    {
                        var be = BarEntity.Open(tr, b.Id, OpenMode.ForRead, BarKinds.Polyline | BarKinds.Block);
                        if (be == null) continue;
                        foreach (var l in freeLabels)
                        {
                            var ca = be.ClosestPoint(tr, l.Arrow); var ct = be.ClosestPoint(tr, l.Text);
                            double d = Math.Min(ca.HasValue ? ca.Value.DistanceTo(l.Arrow) : double.MaxValue,
                                                ct.HasValue ? ct.Value.DistanceTo(l.Text) : double.MaxValue);
                            if (d <= MaxPairDist) cand.Add((d, b, l));
                        }
                    }
                    tr.Commit();
                }
                var usedB = new HashSet<ObjectId>(); var usedL = new HashSet<ObjectId>();
                var newPairs = new List<(BarItem b, LabelItem l)>();
                foreach (var c in cand.OrderBy(c => c.d))
                {
                    if (usedB.Contains(c.b.Id) || usedL.Contains(c.l.Id)) continue;
                    usedB.Add(c.b.Id); usedL.Add(c.l.Id);
                    newPairs.Add((c.b, c.l));
                    pair[c.b.Id] = c.l.Id;
                }

                // 3) zapis powiązań, grot na pręcie, tekst z pręta
                int snapped = 0;
                using (BarGeometryWatcher.Suppress())
                using (doc.LockDocument())
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        SingleBarEngine.EnsureAppIdRegistered(db);
                        foreach (var (b, l) in newPairs)
                        {
                            var be = BarEntity.Open(tr, b.Id, OpenMode.ForWrite, BarKinds.Polyline | BarKinds.Block);
                            var data = be?.Read();
                            if (data == null) continue;
                            data.LabelHandle = l.H;
                            be.Write(data);
                            var ml = (MLeader)tr.GetObject(l.Id, OpenMode.ForWrite);
                            ml.XData = new ResultBuffer(
                                new TypedValue((int)DxfCode.ExtendedDataRegAppName, SingleBarEngine.XLabelAppName),
                                XLink.Write(b.H));
                        }
                        // grot na pręcie (opis z grotem z dala od pręta — punkt pręta najbliżej tekstu)
                        foreach (var kv in pair)
                        {
                            var be = BarEntity.Open(tr, kv.Key, OpenMode.ForRead, BarKinds.Polyline | BarKinds.Block);
                            if (be == null || !(tr.GetObject(kv.Value, OpenMode.ForRead) is MLeader ml)) continue;
                            Point3d arrow, text;
                            try { arrow = ml.GetFirstVertex(0); text = ml.TextLocation; }
                            catch (System.Exception ex) { Log.Error("RC_FIX_BAR_LABELS.Arrow", ex); continue; }
                            var ca = be.ClosestPoint(tr, arrow);
                            if (!ca.HasValue) continue;
                            Point3d target = ca.Value;
                            if (ca.Value.DistanceTo(arrow) > ArrowOnBar)
                            {
                                var ct = be.ClosestPoint(tr, text);
                                if (ct.HasValue) target = ct.Value;
                            }
                            if (target.DistanceTo(arrow) < 0.5) continue;
                            ml.UpgradeOpen();
                            ml.SetFirstVertex(0, target);
                            snapped++;
                        }
                        tr.Commit();
                    }
                    foreach (var b in bars.Where(x => pair.ContainsKey(x.Id)))
                        AnnotationEngine.UpdateBarLabelCount(db, b.H);
                }

                int lonelyLabels = labels.Count(l => !pair.ContainsValue(l.Id));
                int barsNoLabel = bars.Count(b => !pair.ContainsKey(b.Id));
                ed.WriteMessage($"\n{T} Pary pręt–opis: {kept} poprawnych, {newPairs.Count} naprawionych; groty przesunięte na pręt: {snapped}; " +
                                $"teksty opisów odświeżone z prętów: {pair.Count}." +
                                (lonelyLabels > 0 ? $" Opisy bez pręta w pobliżu (≤ {MaxPairDist:F0}): {lonelyLabels} — bez zmian." : "") +
                                (barsNoLabel > 0 ? $" Pręty bez opisu: {barsNoLabel}." : "") + "\n");
                Log.Info($"RC_FIX_BAR_LABELS: wzajemne {kept}, naprawione {newPairs.Count}, groty {snapped}, opisy bez pręta {lonelyLabels}, pręty bez opisu {barsNoLabel}.");
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_FIX_BAR_LABELS", ex);
                ed.WriteMessage($"\n*** ERROR *** {T} {ex.Message}\n");
            }
        }

        private static string NormHex(string h)
        {
            if (string.IsNullOrEmpty(h)) return "";
            return XLink.TryParse(h, out long v) && v != 0 ? v.ToString("X8") : h.Trim().ToUpperInvariant();
        }
    }
}
