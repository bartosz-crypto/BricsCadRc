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
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Mark),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)bar.Diameter),
                new TypedValue((int)DxfCode.ExtendedDataReal, bar.Spacing),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)bar.Count),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.ShapeCode),
                new TypedValue((int)DxfCode.ExtendedDataReal, bar.LengthA),
                new TypedValue((int)DxfCode.ExtendedDataReal, bar.LengthB),
                new TypedValue((int)DxfCode.ExtendedDataReal, bar.LengthC),
                new TypedValue((int)DxfCode.ExtendedDataReal, bar.LengthD),
                new TypedValue((int)DxfCode.ExtendedDataReal, bar.LengthE),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Position),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.LayerCode),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)bar.RepresentativeFlag)
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

            return new BarData
            {
                Mark              = (string)values[1].Value,
                Diameter          = (short)values[2].Value,
                Spacing           = (double)values[3].Value,
                Count             = (short)values[4].Value,
                ShapeCode         = (string)values[5].Value,
                LengthA           = (double)values[6].Value,
                LengthB           = (double)values[7].Value,
                LengthC           = (double)values[8].Value,
                LengthD           = (double)values[9].Value,
                LengthE           = (double)values[10].Value,
                Position          = (string)values[11].Value,
                LayerCode         = (string)values[12].Value,
                RepresentativeFlag = (short)values[13].Value
            };
        }

        /// <summary>Sprawdza czy entity ma dane RC_BAR.</summary>
        public static bool HasBarData(Entity entity)
        {
            return entity.GetXDataForApplication(AppName) != null;
        }
    }
}
