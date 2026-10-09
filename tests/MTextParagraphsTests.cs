using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// POPRAWKI_WZORCA 24 — etykieta płyty z nagłówka mapy = pierwszy AKAPIT MText (nie MText.Text, który BricsCAD
    /// skleja: „PLOT 526h = 225 mm | …”). Teksty nagłówków — dosłownie z DXF map Holbrook
    /// (DE736TQ004_ALL_260910_punching_reinf_maps.dxf, DE736TQ004_part_2_…, DE736TQ004_PLOT_466_…, warstwa PH-SLAB-HEADER).
    /// </summary>
    [TestFixture]
    public class MTextParagraphsTests
    {
        private const string H526 =
            @"\C1;\LPLOT 526\l\P\C7;h = 225 mm  |  fck = 28 MPa  |  KWL 2155  |  n_piles = 10\P\C8;TOP: base H12@200 (540) + add H12@200 (540) = 1080 mm2/m\P\C8;BOT: base H10@200 (385) + add H10@200 (385) = 770 mm2/m\P\C8;DE736TQ004_ALL_260910";
        private const string HGarage =
            @"\C1;\LGARAGE 526-527\l\P\C7;h = 300 mm  |  fck = 28 MPa  |  KWL 2155  |  n_piles = 5\P\C8;TOP: base H12@200 (540) + add H12@200 (540) = 1080 mm2/m\P\C8;BOT: base H10@200 (385) + add H10@200 (385) = 770 mm2/m\P\C8;DE736TQ004_ALL_260910";
        private const string H329 =
            @"\C1;\LPLOT 329-330, 520-521\l\P\C7;h = 225 mm  |  fck = 28 MPa  |  KWL 2155  |  n_piles = 13\P\C8;TOP: base H12@200 (540) + add H12@200 (540) = 1080 mm2/m\P\C8;BOT: base H10@200 (385) + add H10@200 (385) = 770 mm2/m\P\C8;DE736TQ004_part_2";
        private const string H523 =
            @"\C1;\LPLOT 523\l\P\C7;h = 225 mm  |  fck = 28 MPa  |  KWL 2155  |  n_piles = 11\P\C8;TOP: base H12@200 (540) + add H12@200 (540) = 1080 mm2/m\P\C8;BOT: base H10@200 (385) + add H10@200 (385) = 770 mm2/m\P\C8;DE736TQ004_part_2";
        private const string H319 =
            @"\C1;\LPLOT 319\l\P\C7;h = 450 mm  |  fck = 28 MPa  |  KWL 2155  |  n_piles = 9\P\C8;TOP: base H12@200 (540) + add H12@200 (540) = 1080 mm2/m\P\C8;BOT: base H10@200 (385) + add H10@200 (385) = 770 mm2/m\P\C8;DE736TQ004_ALL_260910";
        private const string H466 =
            @"\C1;\LPLOT 466\l\P\C7;h = 225 mm  |  fck = 28 MPa  |  KWL 2155  |  n_piles = 11\P\C8;TOP: base H12@200 (540) + add H12@200 (540) = 1080 mm2/m\P\C8;BOT: base H10@200 (385) + add H10@200 (385) = 770 mm2/m\P\C8;DE736TQ004_PLOT_466";

        [TestCase(H526, "PLOT 526")]
        [TestCase(HGarage, "GARAGE 526-527")]
        [TestCase(H329, "PLOT 329-330, 520-521")]
        [TestCase(H523, "PLOT 523")]
        [TestCase(H319, "PLOT 319")]
        [TestCase(H466, "PLOT 466")]
        public void FirstParagraph_RealMapHeaders(string contents, string expected)
            => Assert.That(MTextParagraphs.First(contents), Is.EqualTo(expected));

        [Test]
        public void Split_RealHeader_AllParagraphs()
        {
            var p = MTextParagraphs.Split(H526);
            Assert.That(p, Has.Count.EqualTo(5));
            Assert.That(p[0], Is.EqualTo("PLOT 526"));
            Assert.That(p[1], Is.EqualTo("h = 225 mm  |  fck = 28 MPa  |  KWL 2155  |  n_piles = 10"));
            Assert.That(p[4], Is.EqualTo("DE736TQ004_ALL_260910"));
        }

        // Etykiety jednolinijkowe — wynik jak dawny FirstLine(MText.Text) (bez zmian)
        [TestCase("PLOT 6-8", "PLOT 6-8")]
        [TestCase("  PLOT 458  ", "PLOT 458")]
        [TestCase("PLOT  9-10", "PLOT  9-10")]          // białe znaki wewnątrz bez zmian (jak dawniej)
        [TestCase(@"{\fArial|b0|i0|c238|p34;PLOT 411 & 522}", "PLOT 411 & 522")]
        [TestCase(@"\pxqc;PLOT 1", "PLOT 1")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void FirstParagraph_SingleLine_Unchanged(string? contents, string expected)
            => Assert.That(MTextParagraphs.First(contents!), Is.EqualTo(expected));

        // Opis płyty w rysunku RC (GaImportEngine.RebuildLabel: „PLOT …\PSSL=…\P225mm THK SLAB”) — GuessPlot
        [Test]
        public void FirstParagraph_RcSlabDescription()
            => Assert.That(MTextParagraphs.First(@"PLOT 526\PSSL=21.925\P225mm THK SLAB"), Is.EqualTo("PLOT 526"));

        [Test]
        public void Split_EscapesAndNewlines()
        {
            Assert.That(MTextParagraphs.Split("A\r\nB\nC"), Is.EqualTo(new[] { "A", "B", "C" }));
            Assert.That(MTextParagraphs.First(@"PLOT 5\\6"), Is.EqualTo(@"PLOT 5\6"));
            Assert.That(MTextParagraphs.First(@"PLOT\~7"), Is.EqualTo("PLOT 7"));
            Assert.That(MTextParagraphs.First(@"\P\PPLOT 8\Pdalej"), Is.EqualTo("PLOT 8"));
        }

        // Etykiety zapisane dawnym odczytem (XData RC_MAP [1], AnalysisStore) — dopasowanie i naprawa
        [TestCase("PLOT 526h = 225 mm  |  fck = 28 MPa  |  KWL 2155  |  n_piles = 10TOP: base H12@200 (540)", "PLOT 526")]
        [TestCase("GARAGE 526-527h = 300 mm  |  fck = 28 MPa", "GARAGE 526-527")]
        [TestCase("PLOT 329-330, 520-521h = 225 mm  |  fck = 28 MPa", "PLOT 329-330, 520-521")]
        [TestCase("PLOT 526", "PLOT 526")]
        [TestCase("PLOT 6-8", "PLOT 6-8")]
        [TestCase("SSL=21.925", "SSL=21.925")]
        [TestCase("", "")]
        public void RepairGluedMapLabel(string label, string expected)
            => Assert.That(MTextParagraphs.RepairGluedMapLabel(label), Is.EqualTo(expected));

        [Test]
        public void SameMapLabel()
        {
            Assert.That(MTextParagraphs.SameMapLabel("PLOT 526h = 225 mm  |  fck = 28 MPa", "PLOT 526"), Is.True);
            Assert.That(MTextParagraphs.SameMapLabel("plot  526", "PLOT 526"), Is.True);
            Assert.That(MTextParagraphs.SameMapLabel("PLOT 52", "PLOT 526"), Is.False);
            Assert.That(MTextParagraphs.SameMapLabel("PLOT 526h = 225 mm", "PLOT 527"), Is.False);
        }
    }
}
