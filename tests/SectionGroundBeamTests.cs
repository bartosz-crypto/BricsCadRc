using System.Collections.Generic;
using System.Linq;
using BricsCadRc.Core;
using NUnit.Framework;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// RC_SECTION — belka GB poza płytą (prośba Bartka 2026-10-23, wzór PLOT 304–307 D-D): GB 600 × 450, wierzch 75 poniżej SSL (TOC 23.350),
    /// pasy jak wspornik (Cantilever, TopDrop), narożnik w płycie jak strefa przy palu (wierzch płyty, spód −525).
    /// </summary>
    [TestFixture]
    public class SectionGroundBeamTests
    {
        // płyta 304 (8941..15231 × 18868..27857) ∪ strefa GB 14496..22616 × 21038..21638
        private static List<(double X, double Y)> Outline() => new List<(double X, double Y)>
        {
            (8941, 18868), (15231, 18868), (15231, 21038), (22616, 21038), (22616, 21638), (15231, 21638), (15231, 27857), (8941, 27857)
        };

        [Test]
        public void DD_AcrossGb_ProfileTopBelowSsl_TocMark()
        {
            var inp = new SectionPlanner.Input
            {
                Outline = Outline(), CutAlongX = false, CutCoord = 18556, S0 = 20500, S1 = 22200, LookSign = 1, Thickness = 225, NibHeight = 150, Ssl = "23.425"
            };
            inp.Beams.Add(new SectionPlanner.BeamBand { Handle = "1C3", S0 = 21038, S1 = 21638, Height = 525, TopDrop = 75, Cantilever = true, TocText = "23.350",
                                                     NibWidth = 0, NibHeight = 0, Cover = 50, CoverBottom = 50, StirrupDia = 12, StirrupA = 350 });
            var r = SectionPlanner.Plan(inp);
            var sg = r.Segments.Single();
            Assert.That(sg.U1 - sg.U0, Is.EqualTo(600).Within(1e-6), "tylko beton GB");
            Assert.That(sg.Prof.Any(p => System.Math.Abs(p.U1 - p.U0 - 600) < 1e-6 && p.Top == -75 && p.Bot == -525), Is.True,
                        string.Join(" | ", sg.Prof.Select(p => $"{p.U0}..{p.U1} {p.Top}/{p.Bot}")));
            Assert.That(r.SslText, Is.EqualTo("23.350"));
        }

        [Test]
        public void CC_AlongGb_SlabCornerAndGb()
        {
            // cięcie wzdłuż osi GB (y = 21338) z płyty 304 przez narożnik (14496..15096 w linii uskoku) do pasa GB
            var inp = new SectionPlanner.Input
            {
                Outline = Outline(), CutAlongX = true, CutCoord = 21338, S0 = 13500, S1 = 17000, LookSign = 1, Thickness = 225, NibHeight = 150, Ssl = "23.425"
            };
            inp.Nibs.Add(new SectionPlanner.NibEdge { Vertical = true, OuterCoord = 15231, InnerCoord = 15096, Lo = 18868, Hi = 27857 });
            inp.Beams.Add(new SectionPlanner.BeamBand { Handle = "1C3", S0 = 14496, S1 = 15096, Height = 525, TopDrop = 0, Cantilever = true, PileCap = true,
                                                     NibWidth = 0, NibHeight = 0, Cover = 50, CoverBottom = 50, StirrupDia = 12, StirrupA = 425 });
            inp.Beams.Add(new SectionPlanner.BeamBand { Handle = "1C3", S0 = 15096, S1 = 22616, Height = 525, TopDrop = 75, Cantilever = true, TocText = "23.350",
                                                     NibWidth = 0, NibHeight = 0, Cover = 50, CoverBottom = 50, StirrupDia = 12, StirrupA = 350 });
            var r = SectionPlanner.Plan(inp);
            var prof = r.Segments.SelectMany(s => s.Prof).ToList();
            string all = string.Join(" | ", prof.Select(p => $"{p.U0}..{p.U1} {p.Top}/{p.Bot}"));
            Assert.That(prof.Any(p => p.Top == 0 && p.Bot == -225), Is.True, "płyta 225: " + all);
            Assert.That(prof.Any(p => p.U0 <= 14496 + 1 && p.U1 >= 15096 - 1 && p.Top == 0 && p.Bot == -525), Is.True, "narożnik 525: " + all);
            Assert.That(prof.Any(p => p.U1 >= 16999 && p.Top == -75 && p.Bot == -525), Is.True, "GB −75 / −525: " + all);
        }
    }
}
