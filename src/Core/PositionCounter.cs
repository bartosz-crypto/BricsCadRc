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
        public static int Peek(Database db)
        {
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (nod.Contains(DictKey))
            {
                var xrec = (Xrecord)tr.GetObject(nod.GetAt(DictKey), OpenMode.ForRead);
                var vals = xrec.Data?.AsArray();
                if (vals != null && vals.Length > 0)
                    return (short)vals[0].Value + 1;
            }
            return 1;
        }

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
                catch { }
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
            using var tr = db.TransactionManager.StartTransaction();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

            int current = 0;
            if (nod.Contains(DictKey))
            {
                var xrec = (Xrecord)tr.GetObject(nod.GetAt(DictKey), OpenMode.ForWrite);
                var vals = xrec.Data?.AsArray();
                if (vals != null && vals.Length > 0)
                    current = (short)vals[0].Value;
                if (posNr > current)
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
