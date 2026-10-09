using System;
using Teigha.DatabaseServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Pliki analizy zapamiętane w rysunku (słownik NOD „RC_ANALYSIS”): mapy zbrojenia, raport przebicia
    /// i płyta (PLOT) wybrana przy imporcie. Zapisuje RC_IMPORT_ANALYSIS; RC_PUNCHING_AUTO bierze stąd raport
    /// i płytę bez ponownego pytania. Zostaje w DWG po zapisie.
    /// </summary>
    public static class AnalysisStore
    {
        private const string DictKey = "RC_ANALYSIS";

        public static void Save(Database db, string mapsPath, string reportPath, string plotLabel)
        {
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                var data = new ResultBuffer(
                    new TypedValue((int)DxfCode.Text, mapsPath ?? ""),
                    new TypedValue((int)DxfCode.Text, reportPath ?? ""),
                    new TypedValue((int)DxfCode.Text, plotLabel ?? ""));
                if (nod.Contains(DictKey))
                {
                    var xrec = (Xrecord)tr.GetObject(nod.GetAt(DictKey), OpenMode.ForWrite);
                    xrec.Data = data;
                }
                else
                {
                    var xrec = new Xrecord { Data = data };
                    nod.SetAt(DictKey, xrec);
                    tr.AddNewlyCreatedDBObject(xrec, true);
                }
                tr.Commit();
            }
            catch (Exception ex) { Log.Error("AnalysisStore.Save", ex); }
        }

        /// <summary>False, gdy w rysunku nie ma zapisanych plików analizy.</summary>
        public static bool TryLoad(Database db, out string mapsPath, out string reportPath, out string plotLabel)
        {
            mapsPath = reportPath = plotLabel = "";
            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains(DictKey)) return false;
                var v = ((Xrecord)tr.GetObject(nod.GetAt(DictKey), OpenMode.ForRead)).Data?.AsArray();
                if (v == null) return false;
                if (v.Length > 0) mapsPath   = v[0].Value as string ?? "";
                if (v.Length > 1) reportPath = v[1].Value as string ?? "";
                if (v.Length > 2) plotLabel  = v[2].Value as string ?? "";
                return true;
            }
            catch (Exception ex) { Log.Error("AnalysisStore.TryLoad", ex); return false; }
        }
    }
}
