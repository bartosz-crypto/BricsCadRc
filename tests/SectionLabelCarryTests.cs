using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>SectionLabelCarry — przeniesienie ręcznych przesunięć opisów przy RC_SECTION_UPDATE (bezpiecznik przekrojów, etap 3).</summary>
    [TestFixture]
    public class SectionLabelCarryTests
    {
        private static SectionLabelCarry.OldLabel Old(string t, string k, double dx, double dy, double cx, double cy)
            => new SectionLabelCarry.OldLabel { Text = t, Kind = k, Def = (dx, dy), CurX = cx, CurY = cy };

        private static SectionLabelCarry.NewLabel New(string t, string k, double dx, double dy)
            => new SectionLabelCarry.NewLabel { Text = t, Kind = k, DefX = dx, DefY = dy };

        [Test]
        public void MovedLabel_SameTextAndKind_OffsetCarried()
        {
            var r = SectionLabelCarry.Carry(
                new List<SectionLabelCarry.OldLabel> { Old("05", "DOT", 1000, -300, 1120, -220) },
                new List<SectionLabelCarry.NewLabel> { New("05", "DOT", 1050, -300) });
            Assert.That(r.Kept, Is.EqualTo(1));
            Assert.That(r.Offsets[0], Is.EqualTo((120.0, 80.0)));
            Assert.That(r.Unmatched, Is.Empty);
        }

        [Test]
        public void LabelAtDefault_NoOffset_NotCounted()
        {
            var r = SectionLabelCarry.Carry(
                new List<SectionLabelCarry.OldLabel> { Old("05", "DOT", 1000, -300, 1000.3, -300) },
                new List<SectionLabelCarry.NewLabel> { New("05", "DOT", 1000, -300) });
            Assert.That(r.Kept, Is.EqualTo(0));
            Assert.That(r.Offsets, Is.Empty);
            Assert.That(r.Unmatched, Is.Empty);
        }

        [Test]
        public void DifferentText_OrKind_NotMatched_ReportedAsUnmatched()
        {
            var r = SectionLabelCarry.Carry(
                new List<SectionLabelCarry.OldLabel> { Old("05", "DOT", 1000, -300, 1100, -300), Old("06", "BAR", 2000, -300, 2000, -200) },
                new List<SectionLabelCarry.NewLabel> { New("07", "DOT", 1000, -300), New("06", "DOT", 2000, -300) });
            Assert.That(r.Kept, Is.EqualTo(0));
            Assert.That(r.Unmatched, Is.EqualTo(new[] { "05", "06" }));
        }

        [Test]
        public void TooFar_Over300_NotMatched()
        {
            var r = SectionLabelCarry.Carry(
                new List<SectionLabelCarry.OldLabel> { Old("05", "DOT", 1000, -300, 1100, -300) },
                new List<SectionLabelCarry.NewLabel> { New("05", "DOT", 1300.5, -300) });
            Assert.That(r.Kept, Is.EqualTo(0));
            Assert.That(r.Unmatched, Is.EqualTo(new[] { "05" }));
            var r2 = SectionLabelCarry.Carry(
                new List<SectionLabelCarry.OldLabel> { Old("05", "DOT", 1000, -300, 1100, -300) },
                new List<SectionLabelCarry.NewLabel> { New("05", "DOT", 1300, -300) });
            Assert.That(r2.Kept, Is.EqualTo(1), "granica 300 mm włącznie");
        }

        [Test]
        public void Greedy_NearestDefaultWins_SameText()
        {
            // dwa opisy „05” (rzędy) — każdy do najbliższej nowej pozycji domyślnej
            var r = SectionLabelCarry.Carry(
                new List<SectionLabelCarry.OldLabel> { Old("05", "DOT", 1000, -300, 1000, -250), Old("05", "DOT", 1000, -375, 1040, -375) },
                new List<SectionLabelCarry.NewLabel> { New("05", "DOT", 1000, -370), New("05", "DOT", 1000, -305) });
            Assert.That(r.Kept, Is.EqualTo(2));
            Assert.That(r.Offsets[1], Is.EqualTo((0.0, 50.0)));
            Assert.That(r.Offsets[0], Is.EqualTo((40.0, 0.0)));
        }

        [Test]
        public void MoreOldThanNew_ExtraMovedReported()
        {
            var r = SectionLabelCarry.Carry(
                new List<SectionLabelCarry.OldLabel> { Old("05", "DOT", 1000, -300, 1010, -300), Old("05", "DOT", 1200, -300, 1210, -300) },
                new List<SectionLabelCarry.NewLabel> { New("05", "DOT", 1190, -300) });
            Assert.That(r.Kept, Is.EqualTo(1));
            Assert.That(r.Offsets[0], Is.EqualTo((10.0, 0.0)));
            Assert.That(r.Unmatched, Is.EqualTo(new[] { "05" }));
        }

        [Test]
        public void NoDefault_Stage1Record_Counted_NotMatched()
        {
            var o = new SectionLabelCarry.OldLabel { Text = "05", Kind = "", Def = null, CurX = 5, CurY = 5 };
            var r = SectionLabelCarry.Carry(new List<SectionLabelCarry.OldLabel> { o },
                                            new List<SectionLabelCarry.NewLabel> { New("05", "DOT", 5, 5) });
            Assert.That(r.NoDefault, Is.EqualTo(1));
            Assert.That(r.Kept, Is.EqualTo(0));
            Assert.That(r.Unmatched, Is.Empty);
        }

        [Test]
        public void KindOf_LeaderBarTinDot()
        {
            Assert.That(SectionLabelCarry.KindOf(true, true), Is.EqualTo("BAR"));
            Assert.That(SectionLabelCarry.KindOf(false, true), Is.EqualTo("TIN"));
            Assert.That(SectionLabelCarry.KindOf(false, false), Is.EqualTo("DOT"));
        }

        [Test]
        public void MedianShift_RobustToOneOutlier()
        {
            Assert.That(SectionLabelCarry.MedianShift(new List<(double, double)>()), Is.EqualTo((0.0, 0.0)));
            Assert.That(SectionLabelCarry.MedianShift(new List<(double, double)> { (0, -8000), (0, -8000), (350, -7900) }),
                        Is.EqualTo((0.0, -8000.0)));
            Assert.That(SectionLabelCarry.MedianShift(new List<(double, double)> { (10, 0), (20, 4) }), Is.EqualTo((15.0, 2.0)));
        }

        [Test]
        public void SectionShift_TitleMovedAlone_DoesNotMoveSection()
        {
            // jeden odcinek betonu na miejscu + tytuł przesunięty → przekrój na miejscu
            Assert.That(SectionLabelCarry.SectionShift(new List<(double, double)> { (0, 0) }, (200, 0)), Is.EqualTo((0.0, 0.0)));
            // dwa odcinki przesunięte razem, tytuł osobno → przesunięcie betonu
            Assert.That(SectionLabelCarry.SectionShift(new List<(double, double)> { (0, -8000), (0, -8000) }, (200, -8000)), Is.EqualTo((0.0, -8000.0)));
            // brak betonu → tytuł; brak wszystkiego → zero
            Assert.That(SectionLabelCarry.SectionShift(new List<(double, double)>(), (50, 60)), Is.EqualTo((50.0, 60.0)));
            Assert.That(SectionLabelCarry.SectionShift(new List<(double, double)>(), null), Is.EqualTo((0.0, 0.0)));
        }

        [Test]
        public void LeaderTip_StaysOnBar_OrDefault()
        {
            var bars = new List<(double, double)> { (100, 900) };
            Assert.That(SectionLabelCarry.LeaderTipU(500, 120, bars), Is.EqualTo(620));
            Assert.That(SectionLabelCarry.LeaderTipU(500, 450, bars), Is.EqualTo(500));
            Assert.That(SectionLabelCarry.LeaderTipU(500, 120, new List<(double, double)>()), Is.EqualTo(500));
        }

        [Test]
        public void MarkTextDefault_AsDrawSection()
        {
            // znacznik poziomy: p = (0, 0), dir = +X, nrm = +Y → litera (214, 130)
            var d = SectionLabelCarry.MarkTextDefault((83, 0), (0, 294), (0, 0));
            Assert.That(d!.Value.X, Is.EqualTo(214).Within(1e-9));
            Assert.That(d.Value.Y, Is.EqualTo(130).Within(1e-9));
            // pionowy, patrzenie −X: dir = −Y, nrm = −X
            var e = SectionLabelCarry.MarkTextDefault((1000, 1917), (706, 2000), (1000, 2000));
            Assert.That(e!.Value.X, Is.EqualTo(870).Within(1e-9));
            Assert.That(e.Value.Y, Is.EqualTo(1786).Within(1e-9));
            Assert.That(SectionLabelCarry.MarkTextDefault((0, 0), (0, 294), (0, 0)), Is.Null);
        }
    }
}
