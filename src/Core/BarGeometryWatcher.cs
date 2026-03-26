using System;
using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// FEATURE E: Automatyczna aktualizacja rozkładów po rozciągnięciu polilinii pręta.
    ///
    /// Mechanizm:
    ///   1. Database.ObjectModified → jeśli RC_SINGLE_BAR zmienił długość → zakolejkuj ObjectId
    ///   2. Document.CommandEnded   → po zakończeniu komendy przetwórz kolejkę:
    ///      - zaktualizuj XData.LengthA na polilinii
    ///      - BarBlockEngine.UpdateBarLength() na każdym powiązanym RC_SLAB_BARS_nnn
    ///
    /// Rejestracja: BarGeometryWatcher.Register() / Unregister() z PluginApp.
    /// </summary>
    public static class BarGeometryWatcher
    {
        private static bool _active;
        private static readonly HashSet<ObjectId> _pending = new HashSet<ObjectId>();

        public static void Register()
        {
            if (_active) return;
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc?.Database == null) return;
                doc.Database.ObjectModified += OnObjectModified;
                doc.CommandEnded            += OnCommandEnded;
                _active = true;
            }
            catch { }
        }

        public static void Unregister()
        {
            if (!_active) return;
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc?.Database != null)
                    doc.Database.ObjectModified -= OnObjectModified;
                if (doc != null)
                    doc.CommandEnded -= OnCommandEnded;
            }
            catch { }
            _active = false;
            _pending.Clear();
        }

        // ----------------------------------------------------------------
        // ObjectModified — zakolejkuj pręt jeśli jest RC_SINGLE_BAR
        // Nie otwieramy tu transakcji — tylko rejestrujemy id do przetworzenia później
        // ----------------------------------------------------------------
        private static void OnObjectModified(object sender, ObjectEventArgs e)
        {
            try
            {
                if (!(e.DBObject is Polyline)) return;
                // Szybkie sprawdzenie XData bez otwierania nowej transakcji
                var xd = e.DBObject.GetXDataForApplication(SingleBarEngine.XAppName);
                if (xd != null)
                    _pending.Add(e.DBObject.ObjectId);
            }
            catch { }
        }

        // ----------------------------------------------------------------
        // CommandEnded — przetwarza kolejkę bezpiecznie po zamknięciu transakcji komendy
        // ----------------------------------------------------------------
        private static void OnCommandEnded(object sender, CommandEventArgs e)
        {
            if (_pending.Count == 0) return;
            var toProcess = new List<ObjectId>(_pending);
            _pending.Clear();

            var doc = sender as Document;
            if (doc?.Database == null) return;
            var db = doc.Database;

            foreach (var oid in toProcess)
            {
                try
                {
                    if (oid.IsNull || oid.IsErased) continue;

                    // 1. Odczytaj aktualną długość i mark
                    double newLength;
                    string mark;
                    string shapeCode;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var pline = tr.GetObject(oid, OpenMode.ForRead) as Polyline;
                        if (pline == null) { tr.Commit(); continue; }
                        var bar = SingleBarEngine.ReadBarXData(pline);
                        if (bar == null) { tr.Commit(); continue; }
                        shapeCode = bar.ShapeCode ?? "00";
                        mark      = bar.Mark;

                        // Tylko shape 00/99 (prosta) — dla innych pline.Length ≠ parametr A
                        if (shapeCode != "00" && shapeCode != "99")
                        {
                            tr.Commit();
                            continue;
                        }

                        newLength = pline.Length;
                        // Pomiń jeśli zmiana < 1mm (numeryczna niedokładność)
                        if (Math.Abs(newLength - bar.LengthA) < 1.0) { tr.Commit(); continue; }
                        tr.Commit();
                    }

                    // 2. Zaktualizuj XData.LengthA na polilinii (tylko shape 00/99)
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var pline = tr.GetObject(oid, OpenMode.ForWrite) as Polyline;
                        if (pline != null)
                        {
                            var bar = SingleBarEngine.ReadBarXData(pline);
                            if (bar != null)
                            {
                                bar.LengthA = newLength;
                                SingleBarEngine.WriteXData(pline, bar);
                            }
                        }
                        tr.Commit();
                    }

                    // 3. Znajdź powiązane rozkłady i przebuduj linie prętów
                    int posNr   = SingleBarEngine.ExtractPosNr(mark);
                    var distIds = BarBlockEngine.FindDistributionsByPosNr(db, posNr);
                    foreach (var distId in distIds)
                        BarBlockEngine.UpdateBarLength(db, distId, newLength);

                    doc.Editor?.WriteMessage(
                        $"\n[RC AUTO] Bar {mark} updated: {newLength:F0} mm  ({distIds.Count} distribution(s))\n");
                }
                catch { }
            }
        }
    }
}
