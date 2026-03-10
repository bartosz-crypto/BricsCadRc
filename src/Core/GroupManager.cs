using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Tworzy i zarzadza grupami BricsCAD (Group) dla ukladow pretow RC SLAB.
    /// Kazda pozycja pretow (np. "H12-01-200 B1") = jedna nazwana grupa.
    /// Dzieki grupowaniu user moze zaznaczac/przesuwac caly uklad jednym kliknieciem.
    /// </summary>
    public static class GroupManager
    {
        private const string GroupPrefix = "RC_BAR_";

        /// <summary>
        /// Tworzy nazowana grupe BricsCAD zawierajaca pręty + annotacje.
        /// </summary>
        /// <param name="db">Baza danych rysunku</param>
        /// <param name="positionNr">Numer pozycji (01, 02, ...)</param>
        /// <param name="ids">Lista ObjectId do pogrupowania (prety + annotacja)</param>
        /// <returns>Nazwa utworzonej grupy</returns>
        public static string CreateBarGroup(Database db, int positionNr, List<ObjectId> ids)
        {
            string groupName = $"{GroupPrefix}{positionNr:D3}";

            using var tr = db.TransactionManager.StartTransaction();
            var groupDict = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);

            // Jesli grupa o tej nazwie juz istnieje — usun ja i wszystkie jej encje
            if (groupDict.Contains(groupName))
            {
                var oldGroup = (Group)tr.GetObject(groupDict.GetAt(groupName), OpenMode.ForWrite);
                EraseGroupEntities(tr, oldGroup);
                oldGroup.Erase();
            }

            var group = new Group(groupName, selectable: true);
            groupDict.SetAt(groupName, group);
            tr.AddNewlyCreatedDBObject(group, true);

            foreach (var id in ids.Where(id => !id.IsNull))
                group.Append(id);

            tr.Commit();
            return groupName;
        }

        /// <summary>
        /// Tworzy osobna grupe dla annotacji (tekst + lider + doty) danej pozycji.
        /// Nazwa: RC_BAR_001_ANNOT — niezalezna od grupy pretow.
        /// </summary>
        public static string CreateAnnotGroup(Database db, int positionNr, List<ObjectId> ids)
        {
            string groupName = $"{GroupPrefix}{positionNr:D3}_ANNOT";

            using var tr = db.TransactionManager.StartTransaction();
            var groupDict = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);

            if (groupDict.Contains(groupName))
            {
                var oldGroup = (Group)tr.GetObject(groupDict.GetAt(groupName), OpenMode.ForWrite);
                EraseGroupEntities(tr, oldGroup);
                oldGroup.Erase();
            }

            var group = new Group(groupName, selectable: true);
            groupDict.SetAt(groupName, group);
            tr.AddNewlyCreatedDBObject(group, true);

            foreach (var id in ids.Where(id => !id.IsNull))
                group.Append(id);

            tr.Commit();
            return groupName;
        }

        /// <summary>
        /// Usuwa wszystkie encje nalezace do grupy (wywolywac przed Group.Erase()).
        /// </summary>
        private static void EraseGroupEntities(Transaction tr, Group group)
        {
            try
            {
                var ids = group.GetAllEntityIds();
                foreach (ObjectId id in ids)
                {
                    if (id.IsNull || id.IsErased) continue;
                    try
                    {
                        var obj = tr.GetObject(id, OpenMode.ForWrite) as DBObject;
                        obj?.Erase();
                    }
                    catch { /* encja juz usunieta lub zablokowana */ }
                }
            }
            catch { }
        }

        /// <summary>
        /// Zwraca liste wszystkich grup RC SLAB w rysunku.
        /// </summary>
        public static List<(string Name, Group Group)> GetAllBarGroups(Database db, Transaction tr)
        {
            var result = new List<(string, Group)>();
            var groupDict = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);

            foreach (DBDictionaryEntry entry in groupDict)
            {
                if (!entry.Key.StartsWith(GroupPrefix)) continue;
                var group = tr.GetObject(entry.Value, OpenMode.ForRead) as Group;
                if (group != null)
                    result.Add((entry.Key, group));
            }

            return result;
        }
    }
}
