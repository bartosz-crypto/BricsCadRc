using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// NIEUŻYWANE od 2026-10-20 (przeliczanie pogarszało — zapis psuł się już po Prepare GA); zostawione do diagnostyki.
    /// Kreskowania skopiowane z pomocniczej bazy wczytanej z DXF (mapy zbrojenia, szablon RC: A1-BL, detale PH) — 2026-10-20:
    /// w rysunku nie rysowały się („An error occurred while rendering entity … Unknown error”: logo A1-BL, kreskowania map, pali)
    /// i rysunek przestawał się zapisywać po imporcie map („Failed to save the file”), choć ten sam DXF otwarty w BricsCAD zapisuje się.
    /// Naprawa: ponowne ustawienie wzoru (wzory standardowe: kąt / skala / SetHatchPattern) i przeliczenie kreskowania (EvaluateHatch)
    /// — kreskowanie odbudowane w rysunku docelowym. Nic poza wzorem i obwiednią przeliczaną z istniejących pętli się nie zmienia.
    /// </summary>
    public static class HatchRepair
    {
        /// <summary>Przelicza wskazane kreskowania (inne obiekty — pomijane). Zwraca liczbę przeliczonych.</summary>
        public static int Reevaluate(Database db, IEnumerable<ObjectId> ids)
        {
            int n = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    if (id.IsNull || id.IsErased) continue;
                    try { if (tr.GetObject(id, OpenMode.ForRead) is Hatch h && Fix(h)) n++; }
                    catch (Exception ex) { Log.Error("HatchRepair.Reevaluate", ex); }
                }
                tr.Commit();
            }
            return n;
        }

        /// <summary>Wszystkie kreskowania rysunku: model, arkusze i definicje bloków (bez xrefów). Zwraca liczbę przeliczonych.</summary>
        public static int ReevaluateAll(Database db)
        {
            var ids = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId bid in bt)
                {
                    if (!(tr.GetObject(bid, OpenMode.ForRead) is BlockTableRecord btr) || btr.IsFromExternalReference || btr.IsDependent) continue;
                    foreach (ObjectId id in btr)
                        if (!id.IsErased && id.ObjectClass.DxfName == "HATCH") ids.Add(id);
                }
                tr.Commit();
            }
            return ids.Count == 0 ? 0 : Reevaluate(db, ids);
        }

        private static bool Fix(Hatch h)
        {
            h.UpgradeOpen();
            if (h.PatternType == HatchPatternType.PreDefined && !string.IsNullOrEmpty(h.PatternName))
            {
                // kąt i skala PRZED SetHatchPattern — linie wzoru liczone są przy jego ustawieniu
                double ang = h.PatternAngle, sc = h.PatternScale;
                bool dbl = h.PatternDouble;
                string name = h.PatternName;
                if (!h.IsSolidFill)
                {
                    h.PatternAngle = ang;
                    h.PatternScale = sc > 0 ? sc : 1.0;
                    h.PatternDouble = dbl;
                }
                h.SetHatchPattern(HatchPatternType.PreDefined, name);
            }
            h.EvaluateHatch(true);
            return true;
        }
    }
}
