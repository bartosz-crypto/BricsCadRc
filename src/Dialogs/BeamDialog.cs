using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BricsCadRc.Core;

namespace BricsCadRc.Dialogs
{
    /// <summary>
    /// RC_BEAM — parametry belki / pogrubienia (rekord RC_BEAM v2). Wartości domyślne = praktyka biura (C12 / Holbrook ręczny,
    /// docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md K2 / K3) — „domyślne, do potwierdzenia”. H z opisu GA („450THK”).
    /// </summary>
    public class BeamDialog : Window
    {
        private readonly BeamParams _p;
        private readonly TextBox _h, _w, _nibW, _nibH, _sSp, _bCnt, _tCnt, _shape, _lapMul, _max,
                                 _sideDia, _sideSp, _sideThr, _nibTop, _nibBot, _covSide, _covBot, _covTopOther, _across, _uDrop,
                                 _uExtra, _endOff;
        private readonly ComboBox _covTop, _sideMode;
        // decyzja użytkownika 2026-10-11 (punkt A): średnice z list rozwijanych, cięcie strzemion 2 / 4 / Auto
        private readonly ComboBox _sDia, _bDia, _tDia, _uDia, _legs;
        private readonly CheckBox _generate, _both, _intoSquare, _atUb03, _squareLinks, _detail, _nib;
        // decyzja użytkownika 2026-10-11 „opcja dodania lub nie dodawania nibu”: nib z geometrii strefy (linia uskoku przed licem)
        private readonly bool _nibGeometry;

        /// <summary>Parametry po OK.</summary>
        public BeamParams Result { get; private set; }
        /// <summary>Po OK: generować pręty belki.</summary>
        public bool Generate { get; private set; }

        public BeamDialog(BeamParams p, string heightSource, IList<string> info, bool nibGeometry = false, double nibGap = 0)
        {
            _p = p.Clone();
            _nibGeometry = nibGeometry;
            Title = "Belka / pogrubienie";
            Width = 600; SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            var root = new StackPanel { Margin = new Thickness(12) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int row = 0;
            void Row(string label, UIElement control, string hint = null)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 2), TextWrapping = TextWrapping.Wrap };
                Grid.SetRow(l, row); grid.Children.Add(l);
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(control);
                if (hint != null) sp.Children.Add(new TextBlock { Text = hint, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 250 });
                Grid.SetRow(sp, row); Grid.SetColumn(sp, 1); grid.Children.Add(sp);
                row++;
            }
            TextBox Field(string label, string value, string hint = null)
            {
                var tb = new TextBox { Text = value, Width = 80, Margin = new Thickness(0, 2, 6, 2) };
                Row(label, tb, hint);
                return tb;
            }
            CheckBox Check(string label, bool value, string hint = null)
            {
                var cb = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 6, 2) };
                Row(label, cb, hint);
                return cb;
            }
            void Header(string text)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var t = new TextBlock { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 2) };
                Grid.SetRow(t, row); Grid.SetColumnSpan(t, 2); grid.Children.Add(t);
                row++;
            }
            string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            string I(int v) => v.ToString(CultureInfo.InvariantCulture);

            ComboBox DiaBox(string label, IList<int> list, int current, string hint = null)
            {
                var cb = new ComboBox { Width = 140, Margin = new Thickness(0, 2, 6, 2) };
                foreach (var (d, text) in BeamParams.DiameterItems(list, current))
                    cb.Items.Add(new ComboBoxItem { Content = text, Tag = d });
                foreach (ComboBoxItem it in cb.Items) if ((int)it.Tag == current) { cb.SelectedItem = it; break; }
                if (cb.SelectedIndex < 0) cb.SelectedIndex = 0;
                Row(label, cb, hint);
                return cb;
            }

            Header("Geometria i otuliny");
            _h    = Field("Wysokość belki H [mm]", _p.Height > 0 ? F(_p.Height) : "",
                          _p.Height > 0 ? (heightSource ?? "") : "nie znaleziono — wpisz");
            _w    = Field("Szerokość [mm]", F(_p.Width), "z geometrii strefy (informacyjnie)");
            _w.IsReadOnly = true;   // pręty liczone z geometrii odcinków strefy
            _nibW = Field("Nib: szerokość [mm]", F(_p.NibWidth), "domyślne 135");
            _nibH = Field("Nib: wysokość [mm]", F(_p.NibHeight), "domyślne 150");
            // rekord [38]: zaznaczone = wg geometrii (z nibem), odznaczone = belka bez nibu; bez linii uskoku — pole nieaktywne (bez nibu)
            _nib = Check("Nib przy belce", nibGeometry && !_p.NoNib,
                         nibGeometry ? $"wg geometrii: linia uskoku {F(nibGap)} mm przed licem; odznaczone — belka bez nibu (lico zewnętrzne = krawędź płyty, bez UB 03 / T IN NIB / prętów w nibie)"
                                     : "brak linii uskoku przy belce — belka bez nibu");
            _nib.IsEnabled = nibGeometry;
            _covSide = Field("Otulina boczna / czołowa [mm]", F(_p.CoverSide), "domyślne 45 (40 + 5 zapasu)");
            _covBot  = Field("Otulina dolna [mm]", F(_p.CoverBottom), "spód belki i nibu; domyślne 45 (40 + 5 zapasu)");
            _covTop = new ComboBox { Width = 250, Margin = new Thickness(0, 2, 6, 2) };
            _covTop.Items.Add("A1: otulina górna 40 (35 + 5 zapasu; A = H − 85)");
            _covTop.Items.Add("A2: otulina górna 50 (A = H − 100; C12)");
            _covTop.Items.Add("inna (pole obok)");
            _covTop.SelectedIndex = System.Math.Abs(_p.CoverTop - BeamGeometry.CoverTopA1) < 0.5 ? 0
                                  : System.Math.Abs(_p.CoverTop - BeamGeometry.CoverTopA2) < 0.5 ? 1 : 2;
            Row("Strzemię: otulina górna", _covTop);
            _covTopOther = Field("   inna otulina górna [mm]", F(_p.CoverTop), "tylko dla „inna”");

            Header("Strzemiona");
            _sDia = DiaBox("Średnica", BeamParams.LinkDiameters, _p.StirrupDia, "domyślne H12 (CP: LINKS H12@200)");
            _legs = new ComboBox { Width = 140, Margin = new Thickness(0, 2, 6, 2) };
            _legs.Items.Add("Auto");
            _legs.Items.Add("2-cięte");
            _legs.Items.Add("4-cięte");
            _legs.SelectedIndex = _p.StirrupLegs == BeamParams.Legs2 ? 1 : _p.StirrupLegs == BeamParams.Legs4 ? 2 : 0;
            Row("Cięcie strzemion", _legs, LegsHint(_p));
            _sSp  = Field("Rozstaw [mm]", F(_p.StirrupSpacing), "domyślne 200");
            _shape = Field("Kod kształtu", _p.StirrupShape ?? "51", "51 (zamknięte)");
            _atUb03 = Check("Położenia UB 03 + 100", _p.StirrupsAtUb03, "domyślnie nie — niezależnie od UB 03, pierwsze 51 od lica");
            _squareLinks = Check("Strzemiona w kwadracie narożnika L", false, "od 2026-10-13 zawsze brak — kwadrat zbrojony U-barami");
            _squareLinks.IsEnabled = false;

            Header("Pręty podłużne i pionowe U-bary");
            _bDia = DiaBox("Dołem: średnica", BeamParams.MainDiameters, _p.BottomDia);
            _bCnt = Field("Dołem: liczba", I(_p.BottomCount));
            _tDia = DiaBox("Górą: średnica", BeamParams.MainDiameters, _p.TopDia);
            _tCnt = Field("Górą: liczba", I(_p.TopCount));
            _both = Check("Dół = góra jedną pozycją „2x4 … B+T”", _p.BothOneMark, "inaczej osobno B i T (obie w serii dolnej)");
            _across = Field("Rozstaw nominalny w opisie [mm]", F(_p.AcrossNominal), "„-150” (ostrzeżenie, gdy rzeczywisty > 175)");
            _lapMul = Field("Zakład: krotność Ø", F(_p.LapMultiple), "40·Ø → H12 500, H16 650, H20 800, H22 900, H25 1000, H32 1300");
            _max  = Field("Maks. długość pręta [mm]", F(_p.MaxLength));
            _endOff = Field("Koniec pręta prostego od lica [mm]", F(_p.StraightEndOffset), "0 = auto (otulina + Ø U-bara = 61; C12 ≈ 90)");
            _intoSquare = Check("Pręty proste w kwadrat narożnika", _p.CornerStraightIntoSquare, "domyślnie nie — przed kwadratem (C12)");
            _uDia = DiaBox("U-bary pionowe: średnica", BeamParams.MainDiameters, _p.UBarDia, "kształt 21, po 1 w kolumnie prętów");
            _uExtra = Field("U-bar końca: A = zakład + [mm]", F(_p.UBarArmExtra), "0 = zakład standardowy (H16: 650)");
            _uDrop = Field("U-bary zagnieżdżone: B − [mm]", F(_p.UBarDrop), "noga X narożnika / złącze T; 0 = auto 2·Ø (H16: 35)");

            Header("Pręty boczne i w nibie");
            _sideMode = new ComboBox { Width = 250, Margin = new Thickness(0, 2, 6, 2) };
            _sideMode.Items.Add("B1: brak");
            _sideMode.Items.Add("B2: zawsze");
            _sideMode.Items.Add("B3: wg H — 450: 1, 600: 2, 750: 3 … (domyślne)");
            _sideMode.SelectedIndex = _p.SideMode == 1 ? 0 : _p.SideMode == 2 ? 1 : 2;
            Row("Pręty boczne: tryb", _sideMode);
            _sideThr = Field("Pręty boczne: próg H [mm]", F(_p.SideThreshold), "nieużywany od 2026-10-14 (B3: H ≤ 375 brak)");
            _sideThr.IsEnabled = false;
            _sideDia = Field("Pręty boczne: średnica H", I(_p.SideDia), "domyślne H12; 0 = brak");
            _sideSp  = Field("Pręty boczne: rozstaw pionowy [mm]", F(_p.SideSpacing), "tylko tryb B2 (B3 — liczba z H)");
            _nibTop  = Field("Nib: dodatkowy pręt BN górą, średnica H", I(_p.NibTopDia), "0 = brak (domyślnie — pręty w nibie 2T+1B)");
            _nibBot  = Field("Nib: pręty w nibie (2T+1B), średnica H", I(_p.NibBottomDia), "domyślne H10 — 3 pręty jedną pozycją (2+1 T+B IN NIB); 0 = brak");

            Header("Rysunek");
            _detail = Check("Pręty belki w detalu 1:25", _p.DetailView,
                            "osobny widok nad rzutami (domyślnie); odznaczone — rzut 1:50 (pręty na rzucie dolnym)");

            root.Children.Add(new ScrollViewer { Content = grid, MaxHeight = 620, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            _generate = new CheckBox
            {
                Content = "Generuj pręty belki (strzemiona, pręty podłużne, pionowe U-bary, boczne, w nibie; poprzednie pręty tej belki zostaną zastąpione)",
                IsChecked = true, Margin = new Thickness(0, 8, 0, 0)
            };
            root.Children.Add(_generate);

            var lines = new List<string>(info ?? new List<string>())
            {
                "Wartości domyślne (praktyka biura C12 / Holbrook, do potwierdzenia): strzemiona H12 co 200, A = H − otulina górna − dolna, pierwsze 51 od lica;",
                "4 + 4 H16 jedną pozycją „2x4 H16-nn-150 B+T” (seria dolna, rzut dolny), zakład 40·Ø; końce, narożniki L i złącza T — pionowe U-bary 4 H16 kształt 21.",
                "Otuliny (decyzja 2026-10-10): boczna / czołowa 45, dolna 50."
            };
            root.Children.Add(new TextBox
            {
                Text = string.Join("\n", lines), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0), Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF8))
            });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "OK", Width = 100, Height = 26, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Anuluj", Width = 90, Height = 26, IsCancel = true };
            ok.Click += (s, e) => OnOk();
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = root;
        }

        /// <summary>Średnica z listy rozwijanej (Tag pozycji).</summary>
        private static int DiaOf(ComboBox cb) => cb.SelectedItem is ComboBoxItem it && it.Tag is int d ? d : 0;

        /// <summary>Podpowiedź: wynik trybu Auto dla belki krawędziowej (EN 1992-1-1 §9.2.2(8)) przy parametrach z rekordu (4 / 4 pręty).</summary>
        private static string LegsHint(BeamParams p)
        {
            if (p.Height <= 0 || p.Width <= 0) return "Auto: krawędziowa wg EN 1992-1-1 §9.2.2(8), w płycie 2-cięte";
            var layers = new List<(int n, int d)> { (p.BottomCount, p.BottomDia) };
            if (!p.UsesBothMark) layers.Add((p.TopCount, p.TopDia));
            var r = BeamGeometry.PlanLinkLegs(BeamParams.LegsAuto, p.Height, p.Width, p.CoverSide, p.CoverBottom, p.StirrupDia, layers);
            return string.Format(CultureInfo.InvariantCulture, "Auto (krawędziowa): s_t,max {0:0.#}, ramiona 2-cięte {1:0.#} → {2}-cięte; w płycie 2-cięte",
                                 r.StMax, r.Spacing2, r.Legs);
        }

        private void OnOk()
        {
            bool Num(TextBox tb, string name, double min, double max, out double v)
            {
                if (NumberParser.TryParseDouble(tb.Text, out v) && v >= min && v <= max) return true;
                MessageBox.Show(this, $"{name}: podaj liczbę {min}–{max}.", "Belka", MessageBoxButton.OK, MessageBoxImage.Warning);
                tb.Focus();
                return false;
            }
            if (!Num(_h, "Wysokość H", 150, 3000, out double h)) return;
            if (!Num(_w, "Szerokość", 150, 1500, out double w)) return;
            if (!Num(_nibW, "Szerokość nibu", 0, 255, out double nw)) return;
            if (!Num(_nibH, "Wysokość nibu", 0, h, out double nh)) return;
            if (!Num(_covSide, "Otulina boczna / czołowa", 15, 100, out double cs)) return;
            if (!Num(_covBot, "Otulina dolna", 15, 100, out double cb)) return;
            double ct = _covTop.SelectedIndex == 0 ? BeamGeometry.CoverTopA1 : _covTop.SelectedIndex == 1 ? BeamGeometry.CoverTopA2 : double.NaN;
            if (double.IsNaN(ct) && !Num(_covTopOther, "Otulina górna strzemienia", 15, 150, out ct)) return;
            double sd = DiaOf(_sDia);
            if (!Num(_sSp, "Rozstaw strzemion", 50, 400, out double ss)) return;
            double bd = DiaOf(_bDia);
            if (!Num(_bCnt, "Liczba dołem", 1, 12, out double bc)) return;
            double td = DiaOf(_tDia);
            if (!Num(_tCnt, "Liczba górą", 1, 12, out double tc)) return;
            if (!Num(_across, "Rozstaw nominalny w opisie", 50, 400, out double acr)) return;
            if (!Num(_lapMul, "Krotność zakładu", 10, 100, out double lapMul)) return;
            if (!Num(_max, "Maks. długość", 1000, 12000, out double max)) return;
            if (!Num(_endOff, "Koniec pręta prostego od lica", 0, 300, out double eo)) return;
            double ud = DiaOf(_uDia);
            if (!Num(_uExtra, "Naddatek ramienia U-bara", 0, 1000, out double ue)) return;
            if (!Num(_uDrop, "Obniżenie U-barów drugiego kierunku", 0, 200, out double udr)) return;
            bool Dia(TextBox tb, string name, out double v)
            {
                if (NumberParser.TryParseDouble(tb.Text, out v) && (v == 0 || (v >= 6 && v <= 40))) return true;
                MessageBox.Show(this, $"{name}: podaj 0 (brak) albo średnicę 6–40.", "Belka", MessageBoxButton.OK, MessageBoxImage.Warning);
                tb.Focus();
                return false;
            }
            if (!Num(_sideThr, "Próg H prętów bocznych", 0, 3000, out double sthr)) return;
            if (!Dia(_sideDia, "Średnica prętów bocznych", out double sdd)) return;
            if (!Num(_sideSp, "Rozstaw prętów bocznych", 100, 400, out double sds)) return;
            if (!Dia(_nibTop, "Średnica pręta BN górą", out double ntd)) return;
            if (!Dia(_nibBot, "Średnica prętów w nibie (2T+1B)", out double nbd)) return;
            double lap = BeamGeometry.Ceil25(lapMul * bd);
            // zakład liczony osobno dla prętów dołem i górą (LapFor) — warunek dla dłuższego
            if (System.Math.Max(lap, BeamGeometry.Ceil25(lapMul * td)) >= max) { MessageBox.Show(this, "Zakład musi być krótszy niż maks. długość pręta.", "Belka"); return; }
            string shape = (_shape.Text ?? "").Trim();
            if (shape.Length == 0) shape = "51";

            var r = _p.Clone();
            r.Height = h; r.Width = w; r.NibWidth = nw; r.NibHeight = nh;
            r.StirrupDia = (int)sd; r.StirrupSpacing = ss; r.StirrupShape = shape;
            r.BottomDia = (int)bd; r.BottomCount = (int)bc; r.TopDia = (int)td; r.TopCount = (int)tc; r.UBarDia = (int)ud;
            r.LapMultiple = lapMul; r.Lap = lap; r.MaxLength = max;
            r.SideDia = (int)sdd; r.SideSpacing = sds; r.SideThreshold = sthr; r.SideMode = _sideMode.SelectedIndex + 1;
            r.NibTopDia = (int)ntd; r.NibBottomDia = (int)nbd;
            r.CoverSide = cs; r.CoverBottom = cb; r.CoverTop = ct;
            r.AcrossNominal = acr; r.BothOneMark = _both.IsChecked == true; r.UBarDrop = udr; r.UBarArmExtra = ue;
            r.StraightEndOffset = eo; r.CornerStraightIntoSquare = _intoSquare.IsChecked == true;
            r.StirrupsAtUb03 = _atUb03.IsChecked == true; r.CornerSquareLinks = _squareLinks.IsChecked == true;
            r.DetailView = _detail.IsChecked == true;
            if (_nibGeometry) r.NoNib = _nib.IsChecked != true;   // bez linii uskoku — rekord [38] bez zmian
            r.StirrupLegs = _legs.SelectedIndex == 1 ? BeamParams.Legs2 : _legs.SelectedIndex == 2 ? BeamParams.Legs4 : BeamParams.LegsAuto;
            r.MigratedFromV1 = false; r.MigrationNote = null;
            Result = r;
            Generate = _generate.IsChecked == true;
            DialogResult = true;
        }
    }
}
