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
