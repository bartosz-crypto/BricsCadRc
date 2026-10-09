using System;
using System.Collections.Generic;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Reguły rekordu XData RC_SINGLE_BAR niezależne od API BricsCAD (kompilowane także w testach jednostkowych).
    ///
    /// Rekord: [0] AppName … [13] LabelHandle (nagłówek, 14 pól) + ogon od [14]:
    /// [14] FormatVersion (Int16; 2 = pręt jako BlockReference — Plan C; 1 = polilinia z ogonem; brak pola = polilinia),
    /// [15] ShapeStd (Int16; 2020 = kształt wg BS 8666:2020 — W4c: znacznik „44” = kapelusz; „44” bez znacznika =
    ///      stary pierścień, czytany jako 75; 2021 = W4d: obrys zbudowany geometrią W4d (rysunki normy) — każdy pręt
    ///      utworzony / przebudowany od W4d; ≥ 2020 dla „44” = kapelusz).
    /// Zapis nagłówka nigdy nie gubi ogona i nigdy nie dubluje [14] ani [15].
    /// </summary>
    internal static class BarRecordRules
    {
        public const int   HeadCount          = 14;    // [0] AppName … [13] LabelHandle
        public const int   VersionIndex       = 14;    // [14] FormatVersion
        public const short BlockFormatVersion = 2;     // 2 = pręt jako BlockReference
        public const int   Int16TypeCode      = 1070;  // DxfCode.ExtendedDataInteger16

        // ---- W4c: znacznik normy kształtu ----
        public const int    ShapeStdIndex         = 15;    // [15] ShapeStd
        public const short  ShapeStd2020          = 2020;  // kształt wg BS 8666:2020
        public const short  ShapeStdW4d           = 2021;  // W4d: obrys wg geometrii W4d (rysunki Table 5), norma 2020
        public const short  PolylineFormatVersion = 1;     // [14] polilinii, gdy rekord dostaje ogon (znacznik [15])
        public const string HatShapeCode          = "44";  // BS 8666:2020: 44 = „kapelusz” (dawniej pierścień)

        /// <summary>Kształt, którego rekord MUSI mieć znacznik [15] = 2020 (dziś tylko 44 — odróżnia kapelusz od pierścienia).</summary>
        public static bool NeedsShapeStd(string shapeCode) => shapeCode == HatShapeCode;

        /// <summary>
        /// Stary pierścień: kod „44” i brak znacznika [15] = Int16 2020 (rekord sprzed W4c albo zapisany starą wtyczką,
        /// która gubi ogon [14+]). Parser podaje go w pamięci jako 75 (BarData.LegacyRing44).
        /// </summary>
        public static bool IsLegacyRing44(string shapeCode, int recordLength, int typeCodeAt15, object valueAt15)
            => shapeCode == HatShapeCode && ShapeStdOf(recordLength, typeCodeAt15, valueAt15) < ShapeStd2020;

        /// <summary>Wartość znacznika [15] (Int16) albo 0, gdy rekord jest krótszy / pole innego typu.</summary>
        public static int ShapeStdOf(int recordLength, int typeCodeAt15, object valueAt15)
            => recordLength > ShapeStdIndex && typeCodeAt15 == Int16TypeCode && valueAt15 is short s ? s : 0;

        /// <summary>
        /// W4d (runda 2): obrys pręta zbudowany geometrią W4d — znacznik [15] ≥ 2021. Brak (rekord sprzed W4d, stara wtyczka
        /// gubiąca ogon [14+]) → obrys historyczny (długość rozkładu Manual wg wzorca).
        /// </summary>
        public static bool IsW4dGeometry(int recordLength, int typeCodeAt15, object valueAt15)
            => ShapeStdOf(recordLength, typeCodeAt15, valueAt15) >= ShapeStdW4d;

        /// <summary>
        /// Jak WithShapeStd, ale nie obniża istniejącego znacznika: [15] ≥ shapeStd (current) → rekord bez zmian
        /// (np. kapelusz 44 z 2021 zapisany ponownie — zostaje 2021).
        /// </summary>
        public static List<T> WithShapeStdAtLeast<T>(List<T> record, T polylineVersion, T shapeStd, int shapeStdValue, Func<T, int> current)
        {
            if (record != null && record.Count > ShapeStdIndex && current(record[ShapeStdIndex]) >= shapeStdValue) return record;
            return WithShapeStd(record, polylineVersion, shapeStd);
        }

        /// <summary>
        /// Rekord (po Merge, ≥ 14 pól) ze znacznikiem [15] = shapeStd: brak [14] → dopisane polylineVersion ([14] = 1);
        /// [15] podmieniony albo dopisany. Nigdy nie dubluje [14] ani [15]; pola za [15] zostają.
        /// </summary>
        public static List<T> WithShapeStd<T>(List<T> record, T polylineVersion, T shapeStd)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (record.Count < HeadCount)
                throw new ArgumentException("Rekord RC_SINGLE_BAR krótszy niż nagłówek (" + record.Count + " pól).", nameof(record));
            if (record.Count == VersionIndex) record.Add(polylineVersion);
            if (record.Count == ShapeStdIndex) record.Add(shapeStd);
            else record[ShapeStdIndex] = shapeStd;
            return record;
        }

        /// <summary>Rekord pręta-bloku: pole [14] istnieje, ma typ Int16 i wartość 2.</summary>
        public static bool IsBlockFormat(int recordLength, int typeCodeAt14, object valueAt14)
            => recordLength > VersionIndex
               && typeCodeAt14 == Int16TypeCode
               && valueAt14 is short s && s == BlockFormatVersion;

        /// <summary>
        /// Nowy rekord: head (dokładnie 14 pól) + ogon istniejącego rekordu od [14] (krótszy rekord = brak ogona).
        /// setVersion → [14] podmieniony (gdy jest) albo dopisany (gdy go brak). Nigdy nie dubluje [14].
        /// </summary>
        public static List<T> Merge<T>(IReadOnlyList<T> head, IReadOnlyList<T> existing, bool setVersion, T version)
        {
            if (head == null) throw new ArgumentNullException(nameof(head));
            if (head.Count != HeadCount)
                throw new ArgumentException("Nagłówek RC_SINGLE_BAR musi mieć dokładnie " + HeadCount + " pól (jest " + head.Count + ").",
                                            nameof(head));

            int existingCount = existing?.Count ?? 0;
            var result = new List<T>(Math.Max(existingCount, HeadCount + 1));
            result.AddRange(head);

            if (existing != null)
                for (int i = HeadCount; i < existingCount; i++)
                    result.Add(existing[i]);

            if (setVersion)
            {
                if (result.Count > VersionIndex) result[VersionIndex] = version;
                else result.Add(version);
            }
            return result;
        }
    }
}
