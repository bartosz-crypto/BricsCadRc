using Bricscad.Ribbon;
using Bricscad.Windows;

namespace BricsCadRc.App
{
    public static class RibbonBuilder
    {
        private const string TabId = "RC_SLAB_TAB";

        public static void Build()
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            // Nie dodawaj zakladki jesli juz istnieje (np. po RELOADA)
            if (ribbon.FindTab(TabId) != null) return;

            RibbonTab tab = new RibbonTab
            {
                Title = "RC SLAB",
                Id = TabId
            };

            tab.Panels.Add(BuildBarPanel());
            tab.Panels.Add(BuildGeneratePanel());
            tab.Panels.Add(BuildEditPanel());
            tab.Panels.Add(BuildCountPanel());

            ribbon.Tabs.Add(tab);
        }

        // ----------------------------------------------------------------
        // Panel 0: PRET (tworzenie pojedynczego preta + rozkład)
        // ----------------------------------------------------------------
        private static RibbonPanel BuildBarPanel()
        {
            var src = new RibbonPanelSource { Title = "Pret", Id = "RC_PANEL_BAR" };

            src.Items.Add(MakeButton("Nowy pret",    "RC_BAR",          "Tworzy pojedynczy pret w widoku elewacji (FLOW 1)"));
            src.Items.Add(MakeButton("Rozklad",      "RC_DISTRIBUTION", "Rozklada wybrany pret w planie (FLOW 2)"));

            return new RibbonPanel { Source = src };
        }

        // ----------------------------------------------------------------
        // Panel 1: ZBROJENIE (generowanie ukladow pretow)
        // ----------------------------------------------------------------
        private static RibbonPanel BuildGeneratePanel()
        {
            var src = new RibbonPanelSource { Title = "Zbrojenie", Id = "RC_PANEL_GEN" };

            src.Items.Add(MakeButton("Generuj z plyty", "RC_GENERATE_SLAB", "Generuje prety w obrysie polilinii plyty (z otuling)"));
            src.Items.Add(MakeButton("Generuj B1/B2",   "RC_GENERATE_BOT",  "Generuje uklad pretow - dolna warstwa (B1 i B2) [prostokat]"));
            src.Items.Add(MakeButton("Generuj T1/T2",   "RC_GENERATE_TOP",  "Generuje uklad pretow - gorna warstwa (T1 i T2) [prostokat]"));

            return new RibbonPanel { Source = src };
        }

        // ----------------------------------------------------------------
        // Panel 2: EDYCJA pretow
        // ----------------------------------------------------------------
        private static RibbonPanel BuildEditPanel()
        {
            var src = new RibbonPanelSource { Title = "Edycja", Id = "RC_PANEL_EDIT" };

            src.Items.Add(MakeButton("Edytuj pret",     "RC_EDIT_BAR",           "Edytuj rozstaw, liczbe lub opis wybranego preta"));
            src.Items.Add(MakeButton("Edytuj rozklad",  "RC_EDIT_DISTRIBUTION",  "Edytuj viewing direction istniejacego rozkladu pretow"));
            src.Items.Add(MakeButton("Koniec preta",    "RC_BAR_END",            "Edytuj symbol konca preta w rozkladzie (None/Circle/Hook)"));
            src.Items.Add(MakeButton("Aktualizuj",      "RC_UPDATE_BAR",         "Aktualizuje dlugosc pretow w rozkladach po edycji geometrii polilinii"));
            src.Items.Add(MakeButton("Repr. pret",      "RC_SET_REPR_BAR",       "Ustaw wybrany pret jako reprezentatywny (ukryj pozostale)"));
            src.Items.Add(MakeButton("Pokaz wszystkie", "RC_SHOW_ALL_BARS",      "Przywroc widocznosc wszystkich pretow w ukladzie"));

            return new RibbonPanel { Source = src };
        }

        // ----------------------------------------------------------------
        // Panel 3: BBS (zliczanie + tonaz)
        // ----------------------------------------------------------------
        private static RibbonPanel BuildCountPanel()
        {
            var src = new RibbonPanelSource { Title = "BBS", Id = "RC_PANEL_BBS" };

            src.Items.Add(MakeButton("Zlicz prety", "RC_COUNT_BBS", "Zlicz prety i oblicz tonaz wg BS8666"));

            return new RibbonPanel { Source = src };
        }

        // ----------------------------------------------------------------
        private static RibbonButton MakeButton(string label, string command, string tooltip)
        {
            return new RibbonButton
            {
                Text = label,
                CommandParameter = command,
                ToolTip = tooltip,
                Id = command,
                ButtonStyle = RibbonButtonStyle.LargeWithText
            };
        }
    }
}
