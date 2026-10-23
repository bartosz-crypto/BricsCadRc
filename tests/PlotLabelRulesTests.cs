using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>Nazwy płyt PLOT / GARAGE (POPRAWKI_WZORCA 22) — opis w GA, nagłówek raportu przebicia, klucz RC_PH.</summary>
    [TestFixture]
    public class PlotLabelRulesTests
    {
        [TestCase("PLOT 458", true)]
        [TestCase("PLOT 9-10", true)]
        [TestCase("  plot 6-8 ", true)]
        [TestCase("PLOTS 324-325", true)]
        [TestCase("PLOT 411 & 522", true)]
        [TestCase("GARAGE 321", true)]
        [TestCase("Garage 526-527", true)]
        [TestCase("GARAGES 477-478 & 526-527", true)]
        [TestCase("GARAGE DOOR", false)]
        [TestCase("GARAGE", false)]
        [TestCase("SSL=21.925", false)]
        [TestCase("225mm THK SLAB", false)]
        [TestCase("PLOT", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsPlotLabel(string? s, bool expected)
            => Assert.That(PlotLabelRules.IsPlotLabel(s!), Is.EqualTo(expected));

        [TestCase("PLOT 319 (9 piles)", "PLOT 319", 9)]
        [TestCase("PLOT 538 (10 piles)", "PLOT 538", 10)]
        [TestCase("PLOT 6-8", "PLOT 6-8", 0)]
        [TestCase("  PLOT 1 (1 pile)  ", "PLOT 1", 1)]
        // nawias nie na końcu — dzisiejsza logika: cały tekst, 0 pali (1:1 jak przed zmianą)
        [TestCase("PLOT 459-460 (13 piles) + 532-533", "PLOT 459-460 (13 piles) + 532-533", 0)]
        [TestCase("PLOT 530 (8 piles) + 545", "PLOT 530 (8 piles) + 545", 0)]
        [TestCase("GARAGE 321 (4 piles)", "GARAGE 321", 4)]
        [TestCase("GARAGE 526-527 (5 piles)", "GARAGE 526-527", 5)]
        [TestCase("garage 458", "garage 458", 0)]
        public void ReportHeader_Recognized(string a, string label, int piles)
        {
            Assert.That(PlotLabelRules.TryParseReportHeader(a, out var l, out var n), Is.True);
            Assert.That(l, Is.EqualTo(label));
            Assert.That(n, Is.EqualTo(piles));
        }

        [TestCase("PLOTS 3-4")]                 // jak dziś — nierozpoznane
        [TestCase("GARAGE DOOR")]
        [TestCase("EDGE (5)")]
        [TestCase("INTERNAL (2)")]
        [TestCase("FAIL (util > 100%): P32306 (1 pile(s))")]
        [TestCase("Summary")]
        [TestCase("Punching Check - DE736TQ004_ALL_260910")]
        [TestCase("")]
        [TestCase(null)]
        public void ReportHeader_NotRecognized(string? a)
            => Assert.That(PlotLabelRules.TryParseReportHeader(a!, out _, out _), Is.False);

        [TestCase("PLOT 538", true)]
        [TestCase("PLOT 459-460 (13 piles) + 532-533", true)]
        [TestCase("GARAGE 321", true)]
        [TestCase("garage 526-527", true)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("GARAGE DOOR", false)]
        [TestCase("P617", false)]
        public void IsReportPlotKey(string? key, bool expected)
            => Assert.That(PlotLabelRules.IsReportPlotKey(key!), Is.EqualTo(expected));

        // POPRAWKI_WZORCA 28: mapa ↔ płyta (AutoRebar Add) — cała etykieta, nie sam prefiks
        [TestCase("PLOT 526", "PLOT 526", true)]
        [TestCase("plot  526", "PLOT 526", true)]
        [TestCase("PLOT 526h = 225 mm | fck = 32", "PLOT 526", true)]
        [TestCase("PLOT 526 (B1)", "PLOT 526", true)]
        [TestCase("PLOT 526", "PLOT 52", false)]
        [TestCase("PLOT 52-53", "PLOT 52", false)]
        [TestCase("PLOT 52", "PLOT 526", false)]
        [TestCase("GARAGE 321", "GARAGE 32", false)]
        [TestCase("PLOT 526", null, false)]
        public void MapPlotMatches(string map, string? slab, bool expected)
            => Assert.That(PlotLabelRules.MapPlotMatches(map, slab!), Is.EqualTo(expected));

        // decyzja 2026-10-23: płyty połączone belkami GB w jednym obrysie — jedna pozycja listy Prepare GA
        [TestCase("PLOT 304|PLOT 305-306|PLOT 307", "PLOT 304-307")]
        [TestCase("PLOT 12|PLOTS 10-11", "PLOT 10-12")]
        [TestCase("PLOT 5", "PLOT 5")]
        [TestCase("PLOT 304|GARAGE 12", "PLOT 304 + GARAGE 12")]
        [TestCase("PLOT 3A|PLOT 4", "PLOT 3A + PLOT 4")]
        public void MergeLabels(string joined, string expected)
            => Assert.That(PlotLabelRules.MergeLabels(joined.Split('|')), Is.EqualTo(expected));
    }
}
