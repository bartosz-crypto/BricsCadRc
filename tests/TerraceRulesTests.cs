using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>Szereg płyt z belkami GB poza płytą (decyzja 2026-10-23, etap 1) — współrzędne z test11 (PLOT 304–307).</summary>
    [TestFixture]
    public class TerraceRulesTests
    {
        static List<(double X, double Y)> R(double x0, double y0, double x1, double y1)
            => new List<(double X, double Y)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };

        // obrys 11BF z GA (z kolankami 27857 / 27858)
        static readonly List<(double X, double Y)> Outer = new List<(double X, double Y)>
        {
            (8941, 27857), (14496, 27857), (14496, 27858), (22616, 27858), (22616, 27858), (33286, 27858), (33286, 27858), (41406, 27858),
            (41406, 27857), (46961, 27857), (46961, 18868), (40671, 18868), (40671, 21038), (34021, 21038), (34021, 18868), (21881, 18868),
            (21881, 21038), (15231, 21038), (15231, 18868), (8941, 18868), (8941, 27857)
        };
        static List<IList<(double X, double Y)>> Steps() => new List<IList<(double X, double Y)>>
            { R(9076, 19003, 15096, 27722), R(22016, 19003, 33886, 27723), R(40806, 19003, 46826, 27722) };
        static List<IList<(double X, double Y)>> Voids() => new List<IList<(double X, double Y)>>
            { R(15231, 21638, 21881, 27258), R(34021, 21638, 40671, 27258) };
        static List<TerraceRules.Box> Corners() => new List<TerraceRules.Box>
        {
            new TerraceRules.Box(40671, 21038, 41406, 21638), new TerraceRules.Box(21881, 21039, 22616, 21639),
            new TerraceRules.Box(15231, 21039, 14496, 21639), new TerraceRules.Box(34021, 21038, 33286, 21638),
            new TerraceRules.Box(40671, 27258, 41406, 27857), new TerraceRules.Box(33286, 27858, 34021, 27258),
            new TerraceRules.Box(22616, 27858, 21881, 27258), new TerraceRules.Box(14496, 27857, 15231, 27258),
        };
        static List<TerraceRules.Text> Texts()
        {
            var t = new List<TerraceRules.Text>();
            foreach (var (x, y) in new[] { (21321.0, 27495.0), (21227.0, 21167.0), (39936.0, 21205.0), (40155.0, 27448.0) })
                t.Add(new TerraceRules.Text { Value = "GB-600x450mm deep\nTOC=23.350", X = x, Y = y });
            foreach (var (x, y) in new[] { (14577.0, 27457.0), (14797.0, 21119.0), (33494.0, 21149.0), (41308.0, 21097.0), (41185.0, 27406.0),
                                           (22416.0, 27471.0), (22489.0, 21098.0), (33559.0, 27422.0) })
                t.Add(new TerraceRules.Text { Value = "525THK", X = x, Y = y });
            return t;
        }

        [Test]
        public void SlabOutline_StepPlusNib()
        {
            var vs = Voids();
            var s = TerraceRules.SlabOutline(R(9076, 19003, 15096, 27722), Outer, vs);
            Assert.That(s.Min(p => p.X), Is.EqualTo(8941).Within(0.5));
            Assert.That(s.Max(p => p.X), Is.EqualTo(15231).Within(0.5));
            Assert.That(s.Min(p => p.Y), Is.EqualTo(18868).Within(0.5));
            Assert.That(s.Max(p => p.Y), Is.EqualTo(27857).Within(1.5));
            Assert.That(s.Count, Is.EqualTo(4));
            var m = TerraceRules.SlabOutline(R(22016, 19003, 33886, 27723), Outer, vs);
            Assert.That(m.Min(p => p.X), Is.EqualTo(21881).Within(0.5));
            Assert.That(m.Max(p => p.X), Is.EqualTo(34021).Within(0.5));
        }

        [Test]
        public void SlabOutline_NoBoundary_NoOffset()
        {
            var s = TerraceRules.SlabOutline(R(0, 0, 1000, 1000), R(-135, -135, 1135, 2000), new List<IList<(double X, double Y)>>());
            Assert.That(s.Min(p => p.X), Is.EqualTo(-135));
            Assert.That(s.Max(p => p.Y), Is.EqualTo(1000));   // góra: obrys 1000 dalej — bez nibu
        }

        [Test]
        public void Analyze_Test11_FourGbBeams()
        {
            var r = TerraceRules.Analyze(Outer, Steps(), Voids(), Corners(), Texts(), 23.425);
            Assert.That(r.Slabs.Count, Is.EqualTo(3));
            var beams = r.Bands.Where(b => b.Label != null).OrderBy(b => b.Lo).ThenBy(b => b.CLo).ToList();
            Assert.That(beams.Count, Is.EqualTo(4), string.Join(" | ", r.Messages));
            var b0 = beams[0];   // 304–305 dołem
            Assert.That(b0.AlongX, Is.True);
            Assert.That(b0.Lo, Is.EqualTo(15231).Within(0.5));
            Assert.That(b0.Hi, Is.EqualTo(21881).Within(0.5));
            Assert.That(b0.CLo, Is.EqualTo(21038).Within(1.5));
            Assert.That(b0.ZoneLo, Is.EqualTo(14496).Within(0.5));
            Assert.That(b0.ZoneHi, Is.EqualTo(22616).Within(0.5));
            Assert.That(b0.ZoneHi - b0.ZoneLo, Is.EqualTo(8120).Within(0.5));
            Assert.That(b0.SlabLo, Is.EqualTo(0));
            Assert.That(b0.SlabHi, Is.EqualTo(1));
            Assert.That(b0.GbDepth, Is.EqualTo(450));
            Assert.That(b0.TopDrop, Is.EqualTo(75));
            Assert.That(b0.Height, Is.EqualTo(525));
            Assert.That(b0.ThkLo, Is.EqualTo(525));
            Assert.That(b0.ThkHi, Is.EqualTo(525));
            var b3 = beams[3];   // 306–307 górą
            Assert.That(b3.CHi, Is.EqualTo(27857).Within(1.5));
            Assert.That(b3.ZoneLo, Is.EqualTo(33286).Within(0.5));
            Assert.That(b3.ZoneHi, Is.EqualTo(41406).Within(0.5));
            Assert.That(b3.SlabLo, Is.EqualTo(1));
            Assert.That(b3.SlabHi, Is.EqualTo(2));
            Assert.That(r.UsedBoxes.Count, Is.EqualTo(8));
            Assert.That(r.UsedTexts.Count, Is.EqualTo(12));
            Assert.That(r.Messages, Is.Empty, string.Join(" | ", r.Messages));
        }

        // test12 (2026-10-23): współrzędne z GA dokładnie (krawędzie prawie osiowe — 33885,711 → 33885,756; obrys z kolankami 0,27 mm)
        [Test]
        public void Analyze_ExactGaCoordinates()
        {
            var outer = new List<(double X, double Y)>
            {
                (8941.07, 27857.42), (14496.07, 27857.42), (14496.07, 27857.69), (22616.07, 27857.69), (22616.07, 27857.95), (33285.71, 27857.95),
                (33285.71, 27857.69), (41405.71, 27857.69), (41405.71, 27857.42), (46960.71, 27857.42), (46960.71, 18867.95), (40670.71, 18867.95),
                (40670.71, 21038.45), (34020.71, 21038.45), (34020.71, 18867.95), (21881.07, 18867.95), (21881.08, 21038.26), (15231.08, 21038.26),
                (15231.07, 18867.95), (8941.07, 18867.95), (8941.07, 27857.42)
            };
            var steps = new List<IList<(double X, double Y)>>
            {
                new List<(double X, double Y)> { (9076.0666, 19002.9524), (15096.0666, 19002.9524), (15096.0666, 27722.4244), (9076.0666, 27722.4244) },
                new List<(double X, double Y)> { (22016.0673, 19002.9524), (33885.7111, 19002.9524), (33885.7557, 27723.0024), (22016.1119, 27723.0024) },
                new List<(double X, double Y)> { (40805.7104, 19002.9524), (46825.7104, 19002.9524), (46825.7104, 27722.4244), (40805.7104, 27722.4244) },
            };
            var voids = new List<IList<(double X, double Y)>>
            {
                new List<(double X, double Y)> { (15231.0778, 21638.2590), (21881.0778, 21638.2590), (21881.0666, 27257.6884), (15231.0666, 27257.6884) },
                new List<(double X, double Y)> { (34020.7104, 21638.4524), (40670.7104, 21638.4524), (40670.7104, 27257.6884), (34020.7104, 27257.6884) },
            };
            var r = TerraceRules.Analyze(outer, steps, voids, Corners(), Texts(), 23.425);
            var m = r.Slabs[1];
            Assert.That(m.Min(q => q.X), Is.EqualTo(21881.07).Within(0.1));
            Assert.That(m.Max(q => q.X), Is.EqualTo(34020.73).Within(0.1));
            Assert.That(r.Bands.Count(b => b.Label != null), Is.EqualTo(4), string.Join(" | ", r.Messages));
            Assert.That(r.Messages, Is.Empty, string.Join(" | ", r.Messages));
        }

        [Test]
        public void Analyze_NoCornerLines_ThkFallback()
        {
            var r = TerraceRules.Analyze(Outer, Steps(), Voids(), new List<TerraceRules.Box>(), Texts(), 23.425);
            var b0 = r.Bands.Where(b => b.Label != null).OrderBy(b => b.Lo).ThenBy(b => b.CLo).First();
            Assert.That(b0.ZoneLo, Is.EqualTo(15231 - 735).Within(0.5));
            Assert.That(b0.ZoneHi, Is.EqualTo(21881 + 735).Within(0.5));
        }

        [Test]
        public void Analyze_NoGbLabel_NoBeam()
        {
            var t = Texts().Where(x => !x.Value.StartsWith("GB")).ToList();
            var r = TerraceRules.Analyze(Outer, Steps(), Voids(), Corners(), t, 23.425);
            Assert.That(r.Bands.Count(b => b.Label != null), Is.EqualTo(0));
            Assert.That(r.Bands.Count, Is.EqualTo(4));
            Assert.That(r.Messages.Count(m => m.Contains("GB")), Is.EqualTo(4));
        }

        [Test]
        public void Analyze_NoToc_DropFromThk()
        {
            var t = Texts().Select(x => new TerraceRules.Text { Value = x.Value.Replace("\nTOC=23.350", ""), X = x.X, Y = x.Y }).ToList();
            var r = TerraceRules.Analyze(Outer, Steps(), Voids(), Corners(), t, 23.425);
            var b0 = r.Bands.First(b => b.Label != null);
            Assert.That(b0.TopDrop, Is.EqualTo(75));
            Assert.That(b0.Height, Is.EqualTo(525));
        }

        [TestCase("GB-600x450mm deep\nTOC=23.350", 600, 450, 23.35)]
        [TestCase("GB 750X525 DEEP TOC 22.1", 750, 525, 22.1)]
        public void TryParseGb(string t, int w, int d, double toc)
        {
            Assert.That(TerraceRules.TryParseGb(t, out int gw, out int gd, out double? gt), Is.True);
            Assert.That(gw, Is.EqualTo(w));
            Assert.That(gd, Is.EqualTo(d));
            Assert.That(gt!.Value, Is.EqualTo(toc).Within(1e-9));
        }

        [Test]
        public void TopDrop_FromLevels() => Assert.That(TerraceRules.TopDropFrom(23.425, 23.350), Is.EqualTo(75));

        [Test]
        public void SplitParts_SlabGbSlab()
        {
            var parts = TerraceRules.SplitParts(14541, 22571, new List<(double Lo, double Hi)> { (8941, 15231), (21881, 34021) });
            Assert.That(parts.Count, Is.EqualTo(3));
            Assert.That(parts[0], Is.EqualTo((14541.0, 15231.0, true)));
            Assert.That(parts[1], Is.EqualTo((15231.0, 21881.0, false)));
            Assert.That(parts[2], Is.EqualTo((21881.0, 22571.0, true)));
        }

        [Test]
        public void SplitParts_ShortPieceMerged()
        {
            var parts = TerraceRules.SplitParts(0, 1000, new List<(double Lo, double Hi)> { (-100, 20) });
            Assert.That(parts.Count, Is.EqualTo(1));
            Assert.That(parts[0].InSlab, Is.False);
            Assert.That(parts[0].Lo, Is.EqualTo(0));
        }
    }
}
