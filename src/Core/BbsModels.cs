using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BricsCadRc.Core
{
    // =====================================================================
    //  Modele BBS (generator .xls wg szablonu Speedeck — jak ASD-BBS w AsdRcSlab).
    //  Ten plik NIE używa API BricsCAD (testowalny poza CAD-em).
    // =====================================================================

    public enum BbsLayer { Bottom = 0, Top = 1 }

    /// <summary>Jeden wiersz BBS (kolumny B–M szablonu).</summary>
    public sealed class BbsBarRow
    {
        public int    BarMark   { get; set; }          // 3, 4, 101, 501 …
        public int    Diameter  { get; set; }          // 12 → „H12”
        public string ShapeCode { get; set; } = "00";  // „00”, „13” …
        public int    NoMembers { get; set; } = 1;
        public int    NoEach    { get; set; }
        public int    Total     => NoMembers * NoEach;

        /// <summary>Długość cięcia [mm] (BS 8666, jak w opisach programu); NaN = nieznana.</summary>
        public double Length    { get; set; } = double.NaN;

        /// <summary>Wymiary w kolumnach I–M: [0]=A [1]=B [2]=C [3]=D [4]=E/R; NaN = pusta komórka.</summary>
        public double[] Dims    { get; } = { double.NaN, double.NaN, double.NaN, double.NaN, double.NaN };

        public BbsLayer Layer   => BarMark >= 100 ? BbsLayer.Top : BbsLayer.Bottom;

        /// <summary>Uwaga do wiersza (podgląd w dialogu, nie trafia do arkusza).</summary>
        public string Note      { get; set; } = "";

        // ---- wyliczane / do podglądu ----
        public string TypeSize  => "H" + Diameter;
        public bool   IsStraight => ShapeCode == "00" || ShapeCode == "0";
        public double LinearMass => BarData.GetLinearMass(Diameter);
        public double MassKg    => double.IsNaN(Length) ? 0 : Total * Length / 1000.0 * LinearMass;

        public string LayerText => Layer == BbsLayer.Top ? "TOP" : "BOTTOM";
        public string MarkText  => BarMark.ToString("00", CultureInfo.InvariantCulture);
        public string LengthText => double.IsNaN(Length) ? "?" : Length.ToString("0", CultureInfo.InvariantCulture);
        public string DimA => DimText(0);
        public string DimB => DimText(1);
        public string DimC => DimText(2);
        public string DimD => DimText(3);
        public string DimE => DimText(4);
        public string MassText  => MassKg.ToString("0.0", CultureInfo.InvariantCulture);

        private string DimText(int i)
        {
            if (i == 0 && IsStraight) return "STR";
            double v = Dims[i];
            return double.IsNaN(v) || v == 0 ? "" : v.ToString("0", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Layout rysunku z blokiem tytułowym A1-BL.</summary>
    public sealed class BbsLayoutInfo
    {
        public string LayoutName    { get; set; }
        public string DrawingNumber { get; set; }
        public Dictionary<string, string> Attributes { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Attr(string tag) =>
            Attributes != null && Attributes.TryGetValue(tag, out var v) ? (v ?? "") : "";

        /// <summary>Skrót numeru rysunku: „SO302HH001-RC3410” → „RC3410”.</summary>
        public string ShortNumber
        {
            get
            {
                string drg = !string.IsNullOrWhiteSpace(DrawingNumber) ? DrawingNumber : (LayoutName ?? "");
                int dash = drg.LastIndexOf('-');
                return dash >= 0 && dash < drg.Length - 1 ? drg.Substring(dash + 1) : drg;
            }
        }
    }

    public enum BbsLayerAssignment { Skip = 0, Bottom = 1, Top = 2, BottomAndTop = 3 }

    public sealed class BbsLayoutAssignment
    {
        public BbsLayoutInfo      Layout     { get; set; }
        public BbsLayerAssignment Assignment { get; set; } = BbsLayerAssignment.Skip;

        // do DataGrid
        public string LayoutName    => Layout?.LayoutName ?? "";
        public string DrawingNumber => Layout?.DrawingNumber ?? "";
        public string Title3        => Layout?.Attr("TITLE_3") ?? "";
    }

    /// <summary>Dane nagłówka i przypisania layoutów (jak BbsGenerationContext w ASD).</summary>
    public sealed class BbsGenerationContext
    {
        public List<BbsLayoutAssignment> Assignments { get; set; } = new List<BbsLayoutAssignment>();
        public string ContractNo   { get; set; } = "";
        public string AddressLine1 { get; set; } = "";
        public string AddressLine2 { get; set; } = "";
        public string AddressLine3 { get; set; } = "";
        public string Revision     { get; set; } = "C1";
        public string PlotSuffix   { get; set; } = "";

        public string TricTrakType { get; set; } = "TT40";   // TT40 / TT50
        public string TricTrakQty  { get; set; } = "";
        public string HystoolsType { get; set; } = "DK165";  // DK90 / DK165
        public string HystoolsQty  { get; set; } = "";

        /// <summary>Pole SLAB AREA z notatek rysunku [m²]; null = brak.</summary>
        public double? SlabAreaM2  { get; set; }

        // Reguła z BS8666_Calculator (HyStool): net = area / (2 × 1 m), total = ROUNDUP(net × 1.1).
        public const double AccessorySpacingM  = 1.0;
        public const double AccessoryAllowance = 0.10;

        public static int? SuggestAccessoryQty(double? areaM2)
        {
            if (!areaM2.HasValue || areaM2.Value <= 0) return null;
            double total = areaM2.Value / (2.0 * AccessorySpacingM) * (1.0 + AccessoryAllowance);
            return (int)Math.Ceiling(Math.Round(total, 9));   // jak Excel ROUNDUP (15 cyfr)
        }

        public string BuildTricTrakLine() =>
            "TRIC-TRAK " + (TricTrakType ?? "TT40") + " " + (TricTrakQty ?? "").Trim() + "No. X 2m";

        public string BuildHystoolsLine() =>
            "CONTINUOUS HYSTOOLS " + (HystoolsType ?? "DK165") + " " + (HystoolsQty ?? "").Trim() + "No. X 2m";

        private IEnumerable<BbsLayoutAssignment> Assigned(params BbsLayerAssignment[] kinds) =>
            Assignments.Where(a => a.Layout != null && kinds.Contains(a.Assignment));

        public List<BbsLayoutInfo> BottomLayouts =>
            Assigned(BbsLayerAssignment.Bottom, BbsLayerAssignment.BottomAndTop).Select(a => a.Layout).ToList();
        public List<BbsLayoutInfo> TopLayouts =>
            Assigned(BbsLayerAssignment.Top, BbsLayerAssignment.BottomAndTop).Select(a => a.Layout).ToList();
        public List<BbsLayoutInfo> BottomAndTopLayouts =>
            Assigned(BbsLayerAssignment.BottomAndTop).Select(a => a.Layout).ToList();

        /// <summary>„PLOT 341-343. REINFORCEMENT DETAILS” → „PLOT 341-343” (przed pierwszą kropką).</summary>
        public static string ExtractPlotSuffix(string title1)
        {
            if (string.IsNullOrWhiteSpace(title1)) return "";
            string t = title1.Trim();
            int dot = t.IndexOf('.');
            return dot < 0 ? "" : t.Substring(0, dot).Trim();
        }

        /// <summary>Kontekst startowy z layoutów (jak ASD): Contract = DRAWING_NUMBER przed „-”,
        /// adres = PROJ_1..3, plot = TITLE_1 przed kropką, rewizja = REV (domyślnie C1).</summary>
        public static BbsGenerationContext BuildInitial(List<BbsLayoutInfo> layouts)
        {
            var ctx = new BbsGenerationContext();
            foreach (var l in layouts)
                ctx.Assignments.Add(new BbsLayoutAssignment { Layout = l });

            if (layouts.Count > 0)
            {
                var first = layouts[0];
                if (!string.IsNullOrEmpty(first.DrawingNumber))
                    ctx.ContractNo = first.DrawingNumber.Split('-')[0];
                ctx.AddressLine1 = first.Attr("PROJ_1");
                ctx.AddressLine2 = first.Attr("PROJ_2");
                ctx.AddressLine3 = first.Attr("PROJ_3");
                ctx.PlotSuffix   = ExtractPlotSuffix(first.Attr("TITLE_1"));
                string rev = first.Attr("REV").Trim();
                if (rev.Length > 0) ctx.Revision = rev;
            }
            return ctx;
        }
    }

    public sealed class BbsGenerateResult
    {
        public bool   Success     { get; set; }
        public string Message     { get; set; } = "";
        public int    BottomPages { get; set; }
        public int    TopPages    { get; set; }
        public int    BottomRows  { get; set; }
        public int    TopRows     { get; set; }
        public string OutputPath  { get; set; }
    }
}
