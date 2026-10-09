using System.Globalization;
using BricsCadRc.Core;
using Pt = System.ValueTuple<double, double>;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// NibLoopBuilder — automat domykania nibu (POPRAWKI_WZORCA 23). Współrzędne z GA Holbrook P02 (P01 dla 466),
    /// warstwa SD-PILED-RAFT: obrys + otwarte linie uskoku (160 mm od krawędzi, garaże 140), progi drzwi (efektywnie
    /// zamknięte), linie strefy 760 mm (319, 528-529, 539).
    /// </summary>
    [TestFixture]
    public class NibLoopBuilderTests
    {
        private const double Min = 50.0, Max = 255.0;   // NibDetector.MinNibWidth / MaxNibWidth

        // ── pomocnicze ──────────────────────────────────────────────────

        private static List<(double X, double Y)> Poly(params double[] xy)
        {
            var r = new List<(double X, double Y)>();
            for (int i = 0; i + 1 < xy.Length; i += 2) r.Add((xy[i], xy[i + 1]));
            return r;
        }

        private static NibLoopBuilder.Piece Pc(params double[] xy) => new NibLoopBuilder.Piece(Poly(xy));

        private static NibLoopBuilder.Result Run(List<(double X, double Y)> outline, params NibLoopBuilder.Piece[] pieces)
            => NibLoopBuilder.Build(outline, null, pieces, Min, Max);

        private static double Area(IList<(double X, double Y)> v)
        {
            double a = 0;
            for (int i = 0; i < v.Count; i++) { var p = v[i]; var q = v[(i + 1) % v.Count]; a += p.X * q.Y - q.X * p.Y; }
            return Math.Abs(a / 2);
        }

        private static void AssertOk(NibLoopBuilder.Result r, int vertices, double expectedArea, params int[] used)
        {
            Assert.That(r.Ok, Is.True, "Problemy: " + string.Join(" | ", r.Problems));
            Assert.That(r.Problems, Is.Empty);
            Assert.That(r.Loop.Count, Is.EqualTo(vertices), "wierzchołki pętli: " + string.Join(" ", r.Loop));
            Assert.That(r.LoopBulges.Count, Is.EqualTo(r.Loop.Count));
            Assert.That(Area(r.Loop), Is.EqualTo(expectedArea).Within(1.0), "pole pętli");
            Assert.That(r.Used, Is.EqualTo(used));
        }

        private static bool HasVertex(NibLoopBuilder.Result r, double x, double y, double tol = 1e-6)
            => r.Loop.Any(p => Math.Abs(p.X - x) <= tol && Math.Abs(p.Y - y) <= tol);

        // ── dane Holbrook ───────────────────────────────────────────────

        // PLOT 458: prostokąt, linia uskoku przez całą lewą krawędź (2 punkty, końce dokładnie na obrysie), próg drzwi
        private static readonly List<(double X, double Y)> O458 =
            Poly(833936.9520, 61440.2000, 826809.4520, 61440.2000, 826809.4520, 51387.7000, 833936.9520, 51387.7000);
        private static NibLoopBuilder.Piece Door458 => Pc(830998.2020, 50970.6589, 831498.2020, 51407.5078, 831998.2020, 50970.6589, 830998.2020, 50970.6589);
        private static NibLoopBuilder.Piece Nib458 => Pc(826969.4520, 61440.2000, 826969.4520, 51387.7000);

        [Test]
        public void Plot458_StraightLine_DoorThresholdSkipped()
        {
            var r = Run(O458, Door458, Nib458);
            AssertOk(r, 4, (833936.952 - 826969.452) * (61440.2 - 51387.7), 1);
            Assert.That(r.InBand, Is.EqualTo(1));
            Assert.That(HasVertex(r, 826969.452, 61440.2) && HasVertex(r, 826969.452, 51387.7), Is.True);
            Assert.That(NibLoopBuilder.LooksLikeNib(O458, r.Loop, Min, Max), Is.True);
        }

        // PLOT 321: obrys 8 wierzch. (wcięcie u dołu), linia uskoku 6 wierzch. z zawinięciami 800 / 1477 mm
        private static readonly List<(double X, double Y)> O321 = Poly(
            218923.9834, 54647.3520, 225714.0184, 54647.3520, 225714.0184, 45044.8520, 222338.9834, 45044.8520,
            222338.9834, 44369.8520, 219486.4834, 44369.8520, 219486.4834, 45044.8520, 218923.9834, 45044.8520);
        private static NibLoopBuilder.Piece Nib321 => Pc(
            224911.4834, 54647.3520, 224911.4834, 54487.3520, 225554.0184, 54487.3520, 225554.0184, 45204.8520,
            224236.4834, 45204.8520, 224236.4834, 45044.8520);

        [Test]
        public void Plot321_WrapsOnBothEnds_Literal()
        {
            var r = Run(O321, Nib321);
            double strip = 802.535 * 160 + 160 * (54487.352 - 45204.852) + 1477.535 * 160;
            AssertOk(r, 12, Area(O321) - strip, 0);
            Assert.That(HasVertex(r, 225554.0184, 54487.3520) && HasVertex(r, 224236.4834, 45204.8520), Is.True);
            Assert.That(HasVertex(r, 225714.0184, 54647.3520), Is.False, "narożnik pasa nibu nie należy do pętli");
        }

        // PLOT 527: koniec linii uskoku w wierzchołku obrysu
        private static readonly List<(double X, double Y)> O527 = Poly(
            1225014.7929, 57693.9587, 1214962.2579, 57693.9587, 1214962.2579, 54656.4587, 1214174.7579, 54656.4587,
            1214174.7579, 51241.4587, 1214962.2579, 51241.4587, 1214962.2579, 50453.9587, 1225014.7929, 50453.9587);

        [Test]
        public void Plot527_EndAtOutlineVertex()
        {
            var r = Run(O527, Pc(1225014.7929, 54068.9587, 1224854.7929, 54068.9587, 1224854.7929, 57533.9587,
                                 1215122.2579, 57533.9587, 1215122.2579, 54656.4587, 1214962.2579, 54656.4587));
            double strip = 160 * (57693.9587 - 54068.9587) + (1224854.7929 - 1214962.2579) * 160 + 160 * (57533.9587 - 54656.4587);
            Assert.That(r.Ok, Is.True, string.Join(" | ", r.Problems));
            Assert.That(Area(r.Loop), Is.EqualTo(Area(O527) - strip).Within(1.0));
            // koniec w wierzchołku obrysu: wierzchołek scalony z odcinkiem współliniowym (1215122 → 1214174)
            Assert.That(HasVertex(r, 1215122.2579, 54656.4587) && HasVertex(r, 1214174.7579, 54656.4587), Is.True);
            Assert.That(HasVertex(r, 1214962.2579, 54656.4587), Is.False);
            Assert.That(r.Loop.Count, Is.EqualTo(10));
            Assert.That(NibLoopBuilder.LooksLikeNib(O527, r.Loop, Min, Max), Is.True);
        }

        // GARAGE 321: U na 3 bokach, 140 mm
        [Test]
        public void Garage321_UOnThreeSides()
        {
            var o = Poly(1933607.9491, 56427.1519, 1937135.4491, 56427.1519, 1937135.4491, 49862.1519, 1933607.9491, 49862.1519);
            var r = Run(o, Pc(1933747.9491, 49862.1519, 1933747.9491, 56287.1519, 1936995.4491, 56287.1519, 1936995.4491, 49862.1519));
            AssertOk(r, 4, (1936995.4491 - 1933747.9491) * (56287.1519 - 49862.1519), 0);
        }

        // PLOT 324-325: LWPOLYLINE + LINE, dwa progi drzwi
        private static readonly List<(double X, double Y)> O324 =
            Poly(380294.8026, 55993.6460, 387294.8026, 55993.6460, 387294.8026, 46053.6460, 380294.8026, 46053.6460);
        private static NibLoopBuilder.Piece[] Pieces324 => new[]
        {
            Pc(381192.5526, 45591.6971, 381692.5526, 46028.5460, 382192.5526, 45591.6971, 381192.5526, 45591.6971),
            Pc(379811.6537, 50445.1460, 380248.5026, 50945.1460, 379811.6537, 51445.1460, 379811.6537, 50445.1460),
            Pc(387134.8026, 55993.6460, 387134.8026, 46213.6460, 382175.0526, 46213.6460),
            Pc(382175.0526, 46213.6460, 382175.0526, 46053.6460),   // LINE
        };
        private const double Strip324 = 160 * (55993.646 - 46213.646) + (387294.8026 - 382175.0526) * 160;

        [Test]
        public void Plot324_PolylinePlusLine()
        {
            var r = Run(O324, Pieces324);
            AssertOk(r, 6, Area(O324) - Strip324, 2, 3);
        }

        [Test]
        public void Plot324_Permutation_SameLoop()
        {
            var p = Pieces324;
            var rev = new NibLoopBuilder.Piece(Enumerable.Reverse(p[2].Points));
            var r = Run(O324, p[3], rev, p[1], p[0]);
            AssertOk(r, 6, Area(O324) - Strip324, 0, 1);
        }

        // PLOT 466 (P01): dwie otwarte polilinie łączone końcami
        [Test]
        public void Plot466_TwoPolylines()
        {
            var o = Poly(1271023.9399, 58121.9397, 1280063.9399, 58121.9397, 1280063.9399, 49756.9397, 1271023.9399, 49756.9397);
            var r = Run(o,
                Pc(1274762.9399, 49359.8986, 1275262.9399, 49796.7475, 1275762.9399, 49359.8986, 1274762.9399, 49359.8986),
                Pc(1275999.1899, 49756.9397, 1275999.1899, 49916.9397, 1279903.9399, 49916.9397),
                Pc(1279903.9399, 49916.9397, 1279903.9399, 58121.9397));
            double strip = (1280063.9399 - 1275999.1899) * 160 + 160 * (58121.9397 - 49916.9397);
            AssertOk(r, 6, Area(o) - strip, 1, 2);
        }

        // PLOT 525: nib na lewej krawędzi + część górnej
        [Test]
        public void Plot525_LeftAndPartOfTop()
        {
            var o = Poly(1271023.9399, 58121.9397, 1280063.9399, 58121.9397, 1280063.9399, 49756.9397, 1271023.9399, 49756.9397);
            var r = Run(o, Pc(1271183.9399, 49756.9397, 1271183.9399, 57961.9397, 1274413.9399, 57961.9397, 1274413.9399, 58121.9397));
            double strip = 160 * (57961.9397 - 49756.9397) + (1274413.9399 - 1271023.9399) * 160;
            AssertOk(r, 6, Area(o) - strip, 0);
        }

        // PLOT 319: nib + linia strefy 760 mm sklejana dziś przez JoinLoops (BLOKER) — linia 760 pomijana
        private static readonly List<(double X, double Y)> O319 = Poly(
            109020.2062, 51857.9293, 117947.7062, 51857.9293, 117947.7062, 46305.4293, 114685.2062, 46305.4293,
            114685.2062, 45405.4293, 112057.7062, 45405.4293, 112057.7062, 46305.4293, 109020.2062, 46305.4293,
            109020.2062, 47868.9401, 108430.2722, 48458.8741, 108430.2722, 49707.6847, 109020.2062, 50297.6187);

        [Test]
        public void Plot319_Zone760Ignored()
        {
            var r = Run(O319,
                Pc(109780.2062, 46465.4293, 109180.2062, 46465.4293, 109180.2062, 47935.2142, 108590.2722, 48525.1482,
                   108590.2722, 49641.4106, 109180.2062, 50231.3445, 109180.2062, 51697.9293, 117787.7062, 51697.9293,
                   117787.7062, 48805.4293),
                Pc(109780.2062, 46465.4293, 109780.2062, 51097.9293, 117187.7062, 51097.9293, 117187.7062, 48805.4293,
                   117787.7062, 48805.4293),
                Pc(109780.2062, 46465.4293, 109780.2062, 46305.4293),
                Pc(117787.7062, 48805.4293, 117947.7062, 48805.4293),
                Pc(117187.7062, 47099.9976, 117187.7062, 47065.4293));
            Assert.That(r.Ok, Is.True, string.Join(" | ", r.Problems));
            Assert.That(r.Used, Is.EqualTo(new[] { 0, 2, 3 }));
            Assert.That(r.InBand, Is.EqualTo(3));
            Assert.That(HasVertex(r, 109780.2062, 46305.4293) && HasVertex(r, 117947.7062, 48805.4293), Is.True);
            Assert.That(HasVertex(r, 114685.2062, 45405.4293), Is.True, "wcięcie dołu w pętli");
            Assert.That(NibLoopBuilder.LooksLikeNib(O319, r.Loop, Min, Max), Is.True);
        }

        // PLOT 528-529: linia 760 z końcami w wierzchołkach linii uskoku
        [Test]
        public void Plot528_Zone760EndsAtNibVertices()
        {
            var o = Poly(1326763.9367, 60149.5380, 1337394.9367, 60149.5380, 1337394.9367, 49647.0380, 1326763.9367, 49647.0380);
            var r = Run(o,
                Pc(1330847.9367, 49185.0892, 1331347.9367, 49621.9380, 1331847.9367, 49185.0892, 1330847.9367, 49185.0892),
                Pc(1332310.9367, 49185.0892, 1332810.9367, 49621.9380, 1333310.9367, 49185.0892, 1332310.9367, 49185.0892),
                Pc(1327523.9367, 49807.0380, 1327523.9367, 59989.5380),
                Pc(1327523.9367, 49647.0380, 1327523.9367, 49807.0380, 1326923.9367, 49807.0380, 1326923.9367, 59989.5380,
                   1327523.9367, 59989.5380, 1327523.9367, 60149.5380));
            double strip = 760 * 160 * 2 + 160 * (59989.538 - 49807.038);
            AssertOk(r, 8, Area(o) - strip, 3);
        }

        // PLOT 539: linia 760 kończy się na wierzchołkach nibu, nib z zawinięciem 600 mm
        [Test]
        public void Plot539_Zone760Ignored()
        {
            var o = Poly(1550118.1803, 58474.6894, 1555611.1803, 58474.6894, 1555611.1803, 47972.1894, 1550118.1803, 47972.1894);
            var r = Run(o,
                Pc(1556073.1291, 53173.1894, 1555636.2803, 53673.1894, 1556073.1291, 54173.1894, 1556073.1291, 53173.1894),
                Pc(1555451.1803, 48732.1894, 1550878.1803, 48732.1894, 1550878.1803, 57714.6894, 1553336.1803, 57714.6894,
                   1553336.1803, 58314.6894),
                Pc(1555611.1803, 48732.1894, 1555451.1803, 48732.1894, 1555451.1803, 48132.1894, 1550278.1803, 48132.1894,
                   1550278.1803, 58314.6894, 1553336.1803, 58314.6894, 1553336.1803, 58474.6894));
            Assert.That(r.Ok, Is.True, string.Join(" | ", r.Problems));
            Assert.That(r.Used, Is.EqualTo(new[] { 2 }));
            Assert.That(NibLoopBuilder.LooksLikeNib(o, r.Loop, Min, Max), Is.True);
        }

        // ── przerwy, tolerancja 4 mm ────────────────────────────────────

        private static readonly List<(double X, double Y)> Rect = Poly(0, 0, 10000, 0, 10000, 6000, 0, 6000);

        [Test]
        public void Gap1_5mm_ToOutline_Closes()
        {
            var r = Run(Rect, Pc(150, 1.5, 150, 6000 - 1.5));
            AssertOk(r, 4, (10000 - 150) * 6000.0, 0);
            Assert.That(HasVertex(r, 150, 0) && HasVertex(r, 150, 6000), Is.True, "koniec przyciągnięty do obrysu");
        }

        [Test]
        public void Gap1_5mm_BetweenPieces_Closes()
        {
            var r = Run(Rect, Pc(150, 0, 150, 3000), Pc(150, 3001.5, 150, 6000));
            AssertOk(r, 4, (10000 - 150) * 6000.0, 0, 1);
        }

        [TestCase("en-US")]
        [TestCase("pl-PL")]
        public void Gap12_5mm_Message(string culture)
        {
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                var r = Run(Rect, Pc(150, 12.5, 150, 6000));
                Assert.That(r.Ok, Is.False);
                Assert.That(r.InBand, Is.EqualTo(1));
                Assert.That(r.Problems.Single(), Is.EqualTo(
                    "przerwa 12.5 mm w punkcie (150.0, 12.5) — linia uskoku nie dochodzi do obrysu płyty (tolerancja 4 mm)."));
            }
            finally { CultureInfo.CurrentCulture = old; }
        }

        [Test]
        public void Gap12_5mm_BetweenPieces_Message()
        {
            var r = Run(Rect, Pc(150, 0, 150, 3000), Pc(150, 3012.5, 150, 6000));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Problems, Has.Some.StartsWith("przerwa 12.5 mm w punkcie (150.0, 3000.0) — linia uskoku nie jest ciągła"));
        }

        [Test]
        public void Corner1mmOffset_Closes_AxisAligned()
        {
            // nib lewy + górny; kawałki stykają się w narożniku z przesunięciem 1 mm
            var r = Run(Rect, Pc(150, 0, 150, 5850), Pc(151, 5850, 10000, 5850));
            Assert.That(r.Ok, Is.True, string.Join(" | ", r.Problems));
            Assert.That(HasVertex(r, 151, 5850) || HasVertex(r, 150, 5850), Is.True);
            Assert.That(r.Loop.Count, Is.EqualTo(4));
            Assert.That(Area(r.Loop), Is.EqualTo((10000 - 150) * 5850.0).Within(200.0));
        }

        // ── problemy ────────────────────────────────────────────────────

        [Test]
        public void Branch_ThreeEnds_Problem()
        {
            var r = Run(Rect, Pc(150, 0, 150, 3000), Pc(150, 3000, 150, 6000), Pc(150, 3000, 0, 3000));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Problems.Single(), Does.StartWith("rozgałęzienie linii uskoku w punkcie (150.0, 3000.0)"));
        }

        [Test]
        public void Line20mmFromEdge_OutOfRange()
        {
            var r = Run(Rect, Pc(20, 0, 20, 6000));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Problems.Single(), Is.EqualTo("linia uskoku 20 mm od krawędzi — poza zakresem nibu 50–255 mm."));
        }

        [Test]
        public void NoPieces_Silent()
        {
            var r = Run(Rect);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.InBand, Is.EqualTo(0));
            Assert.That(r.Problems, Is.Empty);
        }

        [Test]
        public void OnlyFarLinesAndDoor_Silent()
        {
            var r = Run(Rect, Pc(760, 0, 760, 6000), Pc(4000, -300, 4500, 20, 5000, -300, 4000, -300), Pc(0, 0, 10000, 0));
            Assert.That(r.InBand, Is.EqualTo(0), "linia 760, próg drzwi i linia na obrysie pomijane");
            Assert.That(r.Problems, Is.Empty);
        }

        [Test]
        public void TwoOpenPolylines_FormClosedLoop()
        {
            var r = Run(Rect, Pc(150, 150, 9850, 150, 9850, 5850), Pc(9850, 5850, 150, 5850, 150, 150));
            AssertOk(r, 4, 9700.0 * 5700.0, 0, 1);
        }

        [Test]
        public void NibLeftAndRight_TwoChains()
        {
            var r = Run(Rect, Pc(150, 0, 150, 6000), Pc(9850, 6000, 9850, 0));
            AssertOk(r, 4, 9700.0 * 6000.0, 0, 1);
            Assert.That(NibLoopBuilder.LooksLikeNib(Rect, r.Loop, Min, Max), Is.True);
        }

        [Test]
        public void OverlappingStrips_Problem()
        {
            // dwa łańcuchy odcinające zachodzące części tego samego narożnika
            var r = Run(Rect, Pc(0, 5800, 200, 5800, 200, 6000), Pc(0, 5850, 150, 5850, 150, 6000));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Problems.Single(), Does.StartWith("linie uskoku"));
        }

        [Test]
        public void CrossesOutline_Problem()
        {
            // wcięcie obrysu x 4000–5000 głębokości 200; linia y = 4850 przechodzi przez wcięcie (środki odcinków poza nim)
            var o = Poly(0, 0, 10000, 0, 10000, 5000, 5000, 5000, 5000, 4800, 4000, 4800, 4000, 5000, 0, 5000);
            var r = Run(o, Pc(0, 4850, 3000, 4850, 7000, 4850, 10000, 4850));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Problems.Single(), Does.StartWith("linia uskoku przecina obrys płyty w punkcie (5000.0, 4850.0)"));
        }

        [Test]
        public void StripTooLarge_Problem()
        {
            var o = Poly(0, 0, 10000, 0, 10000, 400, 0, 400);
            var r = Run(o, Pc(0, 200, 10000, 200));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Problems.Single(), Does.StartWith("linia uskoku dzieli płytę na dwie duże części"));
        }

        // ── łuki ────────────────────────────────────────────────────────

        [Test]
        public void ArcInPiece_ReversedPiece_BulgeSignKept()
        {
            // linia uskoku x = 150 z łukiem (wybrzuszenie w stronę krawędzi do x = 100) między y 2000 a 4000;
            // kawałek podany od góry do dołu — po odwróceniu znak wybrzuszenia musi dać ten sam łuk
            var piece = new NibLoopBuilder.Piece(Poly(150, 6000, 150, 4000, 150, 2000, 150, 0), new[] { 0.0, 0.05, 0.0 });
            var r = NibLoopBuilder.Build(Rect, null, new[] { piece }, Min, Max);
            Assert.That(r.Ok, Is.True, string.Join(" | ", r.Problems));
            int i = r.LoopBulges.FindIndex(b => Math.Abs(b) > 1e-9);
            Assert.That(i, Is.GreaterThanOrEqualTo(0));
            Assert.That(Math.Abs(r.LoopBulges[i]), Is.EqualTo(0.05).Within(1e-12));
            var a = r.Loop[i]; var b2 = r.Loop[(i + 1) % r.Loop.Count];
            // środek łuku: środek cięciwy przesunięty w prawo od kierunku o b·s
            double dx = b2.X - a.X, dy = b2.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            double mx = (a.X + b2.X) / 2 + dy / len * r.LoopBulges[i] * len / 2;
            double my = (a.Y + b2.Y) / 2 - dx / len * r.LoopBulges[i] * len / 2;
            Assert.That(mx, Is.EqualTo(100.0).Within(1e-6));
            Assert.That(my, Is.EqualTo(3000.0).Within(1e-6));
        }

        [Test]
        public void OutlineArc_CarriedIntoLoop()
        {
            // obrys z łukiem na prawej krawędzi (wybrzuszenie na zewnątrz), nib na lewej
            var o = Poly(0, 0, 10000, 0, 10000, 6000, 0, 6000);
            var ob = new[] { 0.0, 0.1, 0.0, 0.0 };
            var r = NibLoopBuilder.Build(o, ob, new[] { Pc(150, 0, 150, 6000) }, Min, Max);
            Assert.That(r.Ok, Is.True, string.Join(" | ", r.Problems));
            Assert.That(r.LoopBulges.Count(b => Math.Abs(b - 0.1) < 1e-12), Is.EqualTo(1));
        }

        [Test]
        public void EndOnOutlineArc_Problem()
        {
            // dolna krawędź jest łukiem (wybrzuszenie do wnętrza o 10 mm przy x = 150 — mniej niż przy środku)
            var o = Poly(0, 0, 10000, 0, 10000, 6000, 0, 6000);
            var ob = new[] { -0.002, 0.0, 0.0, 0.0 };
            // punkt łuku przy x = 150: y ≈ 4·s·b·(x/L)(1−x/L) ≈ 0.6 mm
            var r = NibLoopBuilder.Build(o, ob, new[] { Pc(150, 0, 150, 6000) }, Min, Max);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Problems.Single(), Does.Contain("na łuku obrysu"));
        }

        // ── scalanie współliniowych ─────────────────────────────────────

        [Test]
        public void CollinearPieces_Merged()
        {
            var r = Run(Rect, Pc(150, 0, 150, 1000, 150, 2000), Pc(150, 2000, 150, 4000), Pc(150, 4000, 150, 6000));
            AssertOk(r, 4, (10000 - 150) * 6000.0, 0, 1, 2);
        }

        // ── LooksLikeNib ────────────────────────────────────────────────

        [Test]
        public void LooksLikeNib_ManualClosedNib()
        {
            Assert.That(NibLoopBuilder.LooksLikeNib(Rect, Poly(130, 0, 10000, 0, 10000, 6000, 130, 6000), Min, Max), Is.True);
            Assert.That(NibLoopBuilder.LooksLikeNib(Rect, Poly(30, 0, 10000, 0, 10000, 6000, 30, 6000), Min, Max), Is.False);
            Assert.That(NibLoopBuilder.LooksLikeNib(Rect, Poly(300, 0, 10000, 0, 10000, 6000, 300, 6000), Min, Max), Is.False);
            Assert.That(NibLoopBuilder.LooksLikeNib(Rect, Rect, Min, Max), Is.False);
        }

        // ── Osiowość z tolerancją 4 mm (POPRAWKI_WZORCA 25 — integracja z nibem) ──────────────

        // Linia uskoku z szumem (koniec przesunięty w X): ≤ 4 mm — krawędź osiowa, nib rozpoznany; 4,1 mm — brak
        [TestCase(0.0115, true)]
        [TestCase(0.01, true)]
        [TestCase(3.9, true)]
        [TestCase(4.1, false)]
        public void LooksLikeNib_NoisyNibLine(double noise, bool expected)
        {
            var inner = Poly(130, 0, 10000, 0, 10000, 6000, 130 + noise, 6000);
            Assert.That(NibLoopBuilder.LooksLikeNib(Rect, inner, Min, Max), Is.EqualTo(expected));
        }

        // Linia uskoku „schodkami” (każda krawędź 3 mm od osi, rozrzut 6 mm) — pod kątem, brak nibu
        [Test]
        public void LooksLikeNib_ChainNoise_NotAxis()
        {
            var inner = Poly(130, 0, 10000, 0, 10000, 6000, 136, 6000, 133, 3000);
            Assert.That(NibLoopBuilder.LooksLikeNib(Rect, inner, Min, Max), Is.False);
        }

        // Obrys z szumem na krawędzi z nibem (linia uskoku idealna): ≤ 4 mm — nib, 4,1 mm — brak
        [TestCase(0.01, true)]
        [TestCase(3.9, true)]
        [TestCase(4.1, false)]
        public void LooksLikeNib_NoisyOutline(double noise, bool expected)
        {
            var outer = Poly(0, 0, 10000, 0, 10000, 6000, noise, 6000);
            var inner = Poly(130, 0, 10000, 0, 10000, 6000, 130, 6000);
            Assert.That(NibLoopBuilder.LooksLikeNib(outer, inner, Min, Max), Is.EqualTo(expected));
        }

        // Szum w obrysie i linii uskoku jednocześnie (różne krawędzie, różne kierunki)
        [TestCase(0.01)]
        [TestCase(3.9)]
        public void LooksLikeNib_NoisyBoth(double noise)
        {
            var outer = Poly(0, 0, 10000, noise, 10000, 6000, -noise, 6000);
            var inner = Poly(130, 0, 10000, noise, 10000, 6000, 130 - noise, 6000);
            Assert.That(NibLoopBuilder.LooksLikeNib(outer, inner, Min, Max), Is.True);
        }

        // Domykanie: otwarta linia uskoku z szumem (realny GA 523: 0,0115 mm) i 3,9 mm — pętla zapisana; 4,1 mm — komunikat
        [TestCase(0.0115, true)]
        [TestCase(0.01, true)]
        [TestCase(3.9, true)]
        [TestCase(4.1, false)]
        public void Build_NoisyNibLine(double noise, bool expected)
        {
            var r = Run(Rect, Pc(150, 0, 150 + noise, 6000));
            Assert.That(r.Ok, Is.EqualTo(expected), "Problemy: " + string.Join(" | ", r.Problems));
            if (expected)
            {
                Assert.That(r.Loop.Count, Is.EqualTo(4));
                Assert.That(NibLoopBuilder.LooksLikeNib(Rect, r.Loop, Min, Max), Is.True);
            }
            else Assert.That(r.Problems, Is.Not.Empty);
        }

        // Domykanie: obrys z szumem na krawędzi z nibem (PLOT 458, lewy dolny narożnik przesunięty), linia uskoku idealna
        [TestCase(0.0115, true)]
        [TestCase(3.9, true)]
        [TestCase(4.1, false)]
        public void Build_NoisyOutline_Plot458(double noise, bool expected)
        {
            var o = Poly(833936.9520, 61440.2000, 826809.4520, 61440.2000, 826809.4520 + noise, 51387.7000, 833936.9520, 51387.7000);
            var r = Run(o, Nib458);
            Assert.That(r.Ok, Is.EqualTo(expected), "Problemy: " + string.Join(" | ", r.Problems));
            if (expected) Assert.That(NibLoopBuilder.LooksLikeNib(o, r.Loop, Min, Max), Is.True);
        }
    }
}
