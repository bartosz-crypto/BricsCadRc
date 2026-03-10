using Teigha.DatabaseServices;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Zapis i odczyt danych preta (BarData) z XData obiektu w rysunku.
    /// Kazdy pret (Line) ma przypisane XData pod kluczem "RC_BAR".
    /// </summary>
    public static class XDataHelper
    {
        public const string AppName = "RC_BAR";

        /// <summary>Rejestruje AppId w bazie danych jesli jeszcze nie istnieje.</summary>
        public static void EnsureAppIdRegistered(Database db)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var regTable = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);

            if (!regTable.Has(AppName))
            {
                regTable.UpgradeOpen();
                var rec = new RegAppTableRecord { Name = AppName };
                regTable.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }

            tr.Commit();
        }

        /// <summary>Zapisuje BarData jako XData na entity.</summary>
        public static void Write(Entity entity, BarData bar)
        {
            var xdata = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                // [1]  Mark
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Mark),
                // [2]  Diameter
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)bar.Diameter),
                // [3]  Spacing
                new TypedValue((int)DxfCode.ExtendedDataReal, bar.Spacing),
                // [4]  Count
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)bar.Count),
                // [5]  ShapeCode
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.ShapeCode),
                // [6]  LengthA
                new TypedValue((int)DxfCode.ExtendedDataReal, bar.LengthA),
                // [7]  LengthB
                new TypedValue((int)DxfCode.ExtendedDataReal, bar.LengthB),
                // [8]  LengthC
                new TypedValue((int)DxfCode.ExtendedDataReal, bar.LengthC),
                // [9]  LengthD
                new TypedValue((int)DxfCode.ExtendedDataReal, bar.LengthD),
                // [10] LengthE
                new TypedValue((int)DxfCode.ExtendedDataReal, bar.LengthE),
                // [11] Position
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Position),
                // [12] LayerCode
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.LayerCode),
                // [13] RepresentativeFlag
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)bar.RepresentativeFlag),
                // [14] BarIndex — dla M07 BarFilter
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)bar.BarIndex),
                // [15] Direction — "X" lub "Y"
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Direction),
                // [16] IsLap — czy pret ma zaklad
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)bar.IsLap)
            );

            entity.XData = xdata;
        }

        /// <summary>Odczytuje BarData z XData entity. Zwraca null jesli brak danych RC_BAR.</summary>
        public static BarData Read(Entity entity)
        {
            ResultBuffer xdata = entity.GetXDataForApplication(AppName);
            if (xdata == null) return null;

            var values = xdata.AsArray();
            // values[0] = AppName, potem kolejne pola wg kolejnosci zapisu
            if (values.Length < 15) return null;

            var bar = new BarData
            {
                Mark               = (string)values[1].Value,
                Diameter           = (short)values[2].Value,
                Spacing            = (double)values[3].Value,
                Count              = (short)values[4].Value,
                ShapeCode          = (string)values[5].Value,
                LengthA            = (double)values[6].Value,
                LengthB            = (double)values[7].Value,
                LengthC            = (double)values[8].Value,
                LengthD            = (double)values[9].Value,
                LengthE            = (double)values[10].Value,
                Position           = (string)values[11].Value,
                LayerCode          = (string)values[12].Value,
                RepresentativeFlag = (short)values[13].Value
            };

            // Pola dodane w v0.2 — wczytuj jesli dostepne (backward compat)
            if (values.Length >= 17)
            {
                bar.BarIndex  = (short)values[14].Value;
                bar.Direction = (string)values[15].Value;
                bar.IsLap     = (short)values[16].Value;
            }

            return bar;
        }

        /// <summary>Sprawdza czy entity ma dane RC_BAR.</summary>
        public static bool HasBarData(Entity entity)
        {
            return entity.GetXDataForApplication(AppName) != null;
        }
    }
}
