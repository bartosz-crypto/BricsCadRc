using Bricscad.ApplicationServices;
using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Sekwencyjny licznik numerow pozycji pretow — przechowywany w NamedObjects rysunku.
    /// Dzieki temu kazdy rysunek ma swoj wlasny licznik, trwaly miedzy sesjami.
    /// </summary>
    public static class PositionCounter
    {
        private const string DictKey = "RC_SLAB_POS_COUNTER";

        /// <summary>
        /// Pozycje 01 i 02 są zarezerwowane dla UB (UB B1 = 01, UB B2 = 02).
        /// Automatyczna numeracja (AutoRebar, RC_BAR, RC_DISTRIBUTION) zaczyna od 03,
        /// żeby UB mogły zawsze dostać swoje numery niezależnie od kolejności komend.
        /// </summary>
        public const int FirstAutoNumber = 3;

        /// <summary>Numery ≥ 500 to osobna seria (RC_PUNCHING_SUMMARY_BARS: 501, 502…) — nie wpływają na licznik.</summary>
        public const int SeparateSeriesStart = 500;

        /// <summary>
        /// JEDEN przydział numerów dla wszystkich komend (RC_BAR, RC_DISTRIBUTION, RC_GENERATE_SLAB,
        /// AutoRebar): max(zapisany licznik, najwyższy numer użyty w rysunku) + 1, minimum 03.
        /// Wcześniej RC_BAR brał max z rysunku, a rozkłady tylko licznik — mogły dostać ten sam numer.
        /// </summary>
        public static int NextAuto(Database db, HashSet<int> used = null)
        {
            used ??= GetUsedPositionNumbers(db);
            int maxUsed = 0;
            foreach (int n in used)
                if (n < SeparateSeriesStart && n > maxUsed) maxUsed = n;
            int next = Math.Max(ReadStored(db), maxUsed) + 1;
            next = Math.Max(FirstAutoNumber, next);
            while (used.Contains(next)) next++;
            return next;
        }

        /// <summary>Zgodność wstecz — to samo co <see cref="NextAuto"/>.</summary>
        public static int NextAutoFree(HashSet<int> used)
        {
            var db = Application.DocumentManager.MdiActiveDocument?.Database;
            return db != null ? NextAuto(db, used) : GetNextFreeFrom(used, FirstAutoNumber);
        }

        private static int ReadStored(Database db)
        {
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(DictKey)) return 0;
            var xrec = (Xrecord)tr.GetObject(nod.GetAt(DictKey), OpenMode.ForRead);
            var vals = xrec.Data?.AsArray();
            int stored = vals != null && vals.Length > 0 ? (short)vals[0].Value : 0;
            // Stare rysunki: licznik mógł zostać podbity przez serię 501+ — ignorujemy to
            return stored >= SeparateSeriesStart ? 0 : stored;
        }

        /// <summary>
        /// Zwraca nastepny numer pozycji i zapisuje go w rysunku (automatyczny increment).
        /// </summary>
        [Obsolete("Use Peek + CommitUsed instead. Atomic reserve causes counter leak on dialog Cancel.")]
        public static int GetNext(Database db)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

            int next = 1;

            if (nod.Contains(DictKey))
            {
                var xrec = (Xrecord)tr.GetObject(nod.GetAt(DictKey), OpenMode.ForRead);
                var vals = xrec.Data?.AsArray();
                if (vals != null && vals.Length > 0)
                    next = (short)vals[0].Value + 1;

                xrec.UpgradeOpen();
                xrec.Data = new ResultBuffer(new TypedValue((int)DxfCode.Int16, (short)next));
            }
            else
            {
                var xrec = new Xrecord
                {
                    Data = new ResultBuffer(new TypedValue((int)DxfCode.Int16, (short)next))
                };
                nod.SetAt(DictKey, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }

            tr.Commit();
            return next;
        }

        /// <summary>
        /// Zwraca następny numer pozycji BEZ zapisu do dokumentu. Bezpieczne przed dialogiem —
        /// counter nie rośnie gdy user kliknie Cancel.
        /// </summary>
        public static int Peek(Database db) => NextAuto(db);

        /// <summary>
        /// Zapisuje użyty numer pozycji: max(stored, usedPosNr). Wywoływać tylko po pomyślnym
        /// dodaniu encji do rysunku — jeśli użytkownik anulował, nie wolno wywoływać.
        /// </summary>
        public static void CommitUsed(Database db, int usedPosNr) => Increment(db, usedPosNr);

        /// <summary>
        /// Zwraca zbiór wszystkich numerów pozycji już użytych w rysunku
        /// (skanuje RC_SINGLE_BAR i RC_BAR_BLOCK w model space).
        /// </summary>
        public static HashSet<int> GetUsedPositionNumbers(Database db)
        {
            var used = new HashSet<int>();
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

            foreach (ObjectId oid in ms)
            {
                try
                {
                    var ent = (Entity)tr.GetObject(oid, OpenMode.ForRead);

                    // RC_SINGLE_BAR (polilinia preta)
                    var singleBar = SingleBarEngine.ReadBarXData(ent);
                    if (singleBar != null)
                    {
                        int n = SingleBarEngine.ExtractPosNr(singleBar.Mark);
                        if (n > 0) used.Add(n);
                        continue;
                    }

                    // RC_BAR_BLOCK (blok rozkładu)
                    if (ent is BlockReference br)
                    {
                        var blockData = BarBlockEngine.ReadXData(br);
                        if (blockData != null)
                        {
                            int n = SingleBarEngine.ExtractPosNr(blockData.Mark);
                            if (n > 0) used.Add(n);
                        }
                    }
                }
                catch (System.Exception ex) { Log.Error("PositionCounter.GetUsedPositionNumbers", ex); }
            }
            return used;
        }

        /// <summary>Zwraca pierwszy wolny numer >= preferred nie będący w used.</summary>
        public static int GetNextFreeFrom(HashSet<int> used, int preferred)
        {
            int n = preferred > 0 ? preferred : 1;
            while (used.Contains(n)) n++;
            return n;
        }

        /// <summary>
        /// Aktualizuje przechowywany licznik do max(current, posNr),
        /// żeby kolejna sugestia startowała po użytym numerze.
        /// </summary>
        public static void Increment(Database db, int posNr)
        {
            if (posNr >= SeparateSeriesStart) return;   // osobna seria nie podbija licznika
            using var tr = db.TransactionManager.StartTransaction();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

            int current = 0;
            if (nod.Contains(DictKey))
            {
                var xrec = (Xrecord)tr.GetObject(nod.GetAt(DictKey), OpenMode.ForWrite);
                var vals = xrec.Data?.AsArray();
                if (vals != null && vals.Length > 0)
                    current = (short)vals[0].Value;
                if (posNr > current || current >= SeparateSeriesStart)   // stary licznik podbity serią 501+
                    xrec.Data = new ResultBuffer(new TypedValue((int)DxfCode.Int16, (short)posNr));
            }
            else
            {
                var xrec = new Xrecord
                {
                    Data = new ResultBuffer(new TypedValue((int)DxfCode.Int16, (short)posNr))
                };
                nod.SetAt(DictKey, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }

            tr.Commit();
        }

        /// <summary>Resetuje licznik do 0 (np. przy zaczynaniu nowego projektu).</summary>
        public static void Reset(Database db)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

            if (nod.Contains(DictKey))
            {
                var xrec = (Xrecord)tr.GetObject(nod.GetAt(DictKey), OpenMode.ForWrite);
                xrec.Data = new ResultBuffer(new TypedValue((int)DxfCode.Int16, (short)0));
            }

            tr.Commit();
        }
    }
}
