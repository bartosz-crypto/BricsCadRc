using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bricscad.Ribbon;
using Bricscad.Windows;
using BricsCadRc.Core;

namespace BricsCadRc.App
{
    /// <summary>
    /// Wstążka „RC SLAB” (po angielsku). Główne akcje jako duże przyciski, pomocnicze jako kolumny małych.
    /// Generowanie warstw: jeden przycisk RC_GENERATE z wyborem w linii poleceń.
    /// Ikony: Resources/Icons/&lt;nazwa&gt;_32.png i _16.png (EmbeddedResource „BricsCadRc.Icons.*”).
    /// </summary>
    public static class RibbonBuilder
    {
        private const string TabId = "RC_SLAB_TAB";

        public static void Build()
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;
            if (ribbon.FindTab(TabId) != null) return;   // np. po ponownym NETLOAD

            var tab = new RibbonTab { Title = "RC SLAB", Id = TabId };

            // 1. Setup — przygotowanie rysunku
            tab.Panels.Add(Panel("Setup", "RC_PANEL_SETUP",
                Large("Prepare GA", "RC_PREPARE_GA", "prepare_ga",
                    "Prepare the RC drawing from a GA drawing: pick the plot, clean slab outline, piles and door thresholds as bottom and top plans, plot label (PLOT / SSL / thickness), titles and template frames. Then copies title block and slab notes from the GA layout."),
                Column(
                    Small("GA Texts", "RC_GA_TEXTS", "ga_texts",
                        "Copy texts from the GA layout to RC layouts: client / project, TITLE_1, RCxxx drawing numbers, SLAB NOTES, HYSTOOLS by slab thickness."),
                    Small("Reinf. Maps", "RC_IMPORT_MAP", "maps",
                        "Import reinforcement maps (reinf_maps.dxf): pick the plot and place the T1 / T2 / B1 / B2 frames."))));

            // 2. Reinforcement — generowanie
            tab.Panels.Add(Panel("Reinforcement", "RC_PANEL_GEN",
                Large("Generate", "RC_GENERATE", "generate",
                    "Generate slab reinforcement. Choose in the command line: Mesh (all layers), B1, B2, T1, T2, UB1, UB2, UBNib, Add (bottom additional bars B1/B2 ADD from imported reinforcement maps)."),
                Large("Section", "RC_SECTION", "section",
                    "Slab section from a real cut: pick the slab outline and the cut line (horizontal / vertical) on the bottom or top plan. Thickness and SSL from the plot label, nib from the outline, bars from the bottom and top plans; section marks on both plans."),
                Large("Opening Detail", "RC_OPENING_DETAIL", "opening",
                    "Opening detail: DETAIL 'n' frame on the plan plus H16 bars and U-bars around the opening."),
                Large("Punching", "RC_PUNCHING_AUTO", "punching",
                    "Punching from the xlsx report: PH1–9 tags at piles, detail notes (APPLICABLE FOR), bars 501 / 502 for the BBS."),
                Column(
                    Small("3D Model", "RC_MODEL_3D", "model3d",
                        "Preview 3D model of the slab reinforcement (B1, B2, UB, T1, T2, concrete with openings, piles) next to the drawing."),
                    Small("Summary Bars", "RC_PUNCHING_SUMMARY_BARS", "summary_bars",
                        "Summary bars 501 / 502 from PH zone counts."))));

            // 3. Bars — ręczne pręty
            tab.Panels.Add(Panel("Bars", "RC_PANEL_BAR",
                Large("New Bar", "RC_BAR", "new_bar", "Create a single bar (shape code BS 8666) in elevation."),
                Large("Distribution", "RC_DISTRIBUTION", "distribution", "Distribute the selected bar on the plan.")));

            // 4. Edit — kolumny małych przycisków
            tab.Panels.Add(Panel("Edit", "RC_PANEL_EDIT",
                Column(
                    Small("Edit Bar", "RC_EDIT_BAR", "edit_bar", "Edit the selected bar: shape, dimensions, diameter."),
                    Small("Edit Distribution", "RC_EDIT_DISTRIBUTION", "edit_distribution", "Edit bar count, spacing and cover of a distribution."),
                    Small("Edit Label", "RC_EDIT_LABEL", "edit_label", "Edit the distribution label (count, mark, diameter, spacing).")),
                Column(
                    Small("Bar End", "RC_BAR_END", "bar_end", "Bar end symbol in the distribution (None / Circle / Hook)."),
                    Small("Label Scale", "RC_SCALE_ANNOT", "annot_scale", "Visual scale of the distribution label (text, dots, arrows)."),
                    Small("Update Bars", "RC_UPDATE_BAR", "update", "Update bar lengths in distributions after editing the bar polyline.")),
                Column(
                    Small("Single Bar View", "RC_SET_REPR_BAR", "repr_bar", "Show only the selected (representative) bar of a distribution."),
                    Small("Show All Bars", "RC_SHOW_ALL_BARS", "show_all", "Show all bars of a distribution again."))));

            // 5. Schedule — BBS
            tab.Panels.Add(Panel("Schedule", "RC_PANEL_BBS",
                Large("BBS", "RC_BBS", "bbs",
                    "Bar bending schedule (.xls, Speedeck template) straight from the drawing: preview with warnings, BOTTOM / TOP layouts, accessories from SLAB AREA."),
                Column(
                    Small("Bar Schedule", "RC_SCHEDULE", "schedule", "Bar schedule to BS 8666:2020 in a dialog, CSV export."),
                    Small("Count Bars", "RC_COUNT_BBS", "count", "Count bars and tonnage (command line)."))));

            ribbon.Tabs.Add(tab);
        }

        // ----------------------------------------------------------------

        private static RibbonPanel Panel(string title, string id, params RibbonItem[] items)
        {
            var src = new RibbonPanelSource { Title = title, Id = id };
            foreach (var it in items) src.Items.Add(it);
            return new RibbonPanel { Source = src };
        }

        private static RibbonButton Large(string label, string command, string icon, string tooltip)
        {
            var b = Button(label, command, tooltip, RibbonButtonStyle.LargeWithText);
            var img = Icon(icon, 32);
            if (img != null) { b.LargeImage = img; b.Image = Icon(icon, 16); b.ShowImage = true; }
            return b;
        }

        private static RibbonButton Small(string label, string command, string icon, string tooltip)
        {
            var b = Button(label, command, tooltip, RibbonButtonStyle.SmallWithText);
            var img = Icon(icon, 16);
            if (img != null) { b.Image = img; b.LargeImage = Icon(icon, 32); b.ShowImage = true; }
            return b;
        }

        private static RibbonButton Button(string label, string command, string tooltip, RibbonButtonStyle style)
        {
            return new RibbonButton
            {
                Text = label,
                CommandParameter = command,
                ToolTip = tooltip,
                Id = command,
                ShowText = true,
                ButtonStyle = style
            };
        }

        /// <summary>Kolumna małych przycisków jeden pod drugim.</summary>
        private static RibbonRowPanel Column(params RibbonItem[] items)
        {
            var row = new RibbonRowPanel();
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) row.Items.Add(new RibbonRowBreak());
                row.Items.Add(items[i]);
            }
            return row;
        }

        private static ImageSource Icon(string name, int size)
        {
            try
            {
                var s = typeof(RibbonBuilder).Assembly.GetManifestResourceStream($"BricsCadRc.Icons.{name}_{size}.png");
                if (s == null) return null;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = s;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch (Exception ex)
            {
                Log.Error("RibbonBuilder.Icon", ex);
                return null;
            }
        }
    }
}
