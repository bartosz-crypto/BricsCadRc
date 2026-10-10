using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>SectionRecord — rekord przekroju (Xrecord NA_RC_SECTION): zapis / odczyt, tolerancja starszych rekordów.</summary>
    [TestFixture]
    public class SectionRecordTests
    {
        private static SectionRecord Sample()
        {
            var r = new SectionRecord
            {
                Guid = "0f8fad5b-d9cb-469f-a165-70867728950e", Letter = "B",
                P1 = (1234.5678901234, -0.1), P2 = (9876.5, 2000), P1Rel = (34.5678901234, 2000), P2Rel = (8676.5, 2000),
                CutAlongX = false, LookSign = -1, LookPt = (1.5, 2.5), TopLeft = (100000, 50000, 0), MinPoint = (1200, -2000.1),
                Thickness = 300, NibHeight = 140, CoverBottom = 45, CoverTop = 30, CoverNib = 25, UbTolerance = 0,
                Ssl = "21.925", DrawPiles = false, DescThickness = 225, DescSsl = null, ThkOverridden = true, SslOverridden = true,
                Hash = new string('a', 64), ResultDigest = new string('b', 64), Version = 1,
            };
            r.Components["DIST:000001A0"] = "Mark:2 H12-101-150 T IN NIB;Pos:101;X0:40.0";
            r.Components["CUT"] = "X:0;C:1800.0;S0:-200.0;S1:4200.0;L:-1";
            r.Parts.Add(new SectionRecord.Part { Handle = "0000ABCD", Role = "LBL", Key = "LBL#03|105" });
            r.Parts.Add(new SectionRecord.Part { Handle = "0000ABCE", Role = "MARK", Key = "V0.P1" });
            return r;
        }

        [Test]
        public void RoundTrip_AllFields()
        {
            var a = Sample();
            var b = SectionRecord.Parse(a.ToLines());
            Assert.That(b, Is.Not.Null);
            Assert.That(b!.ToLines(), Is.EqualTo(a.ToLines()));
            Assert.That(b.P1, Is.EqualTo(a.P1));
            Assert.That(b.MinPoint, Is.EqualTo(a.MinPoint));
            Assert.That(b.LookSign, Is.EqualTo(-1));
            Assert.That(b.CutAlongX, Is.False);
            Assert.That(b.DescSsl, Is.Null);
            Assert.That(b.DescThickness, Is.EqualTo(225));
            Assert.That(b.Parts[0].Key, Is.EqualTo("LBL#03|105"));
            Assert.That(b.Components["DIST:000001A0"], Does.Contain("T IN NIB"));
            Assert.That(b.StoredState().Hash, Is.EqualTo(a.Hash));
        }

        [Test]
        public void Ssl_NullVsEmpty_Distinguished()
        {
            var a = Sample(); a.Ssl = "";
            Assert.That(SectionRecord.Parse(a.ToLines())!.Ssl, Is.EqualTo(""));
            a.Ssl = null;
            Assert.That(SectionRecord.Parse(a.ToLines())!.Ssl, Is.Null);
            a.Ssl = "a;b=c";
            Assert.That(SectionRecord.Parse(a.ToLines())!.Ssl, Is.EqualTo("a;b=c"));
        }

        [Test]
        public void Parse_OlderShorterRecord_Defaults()
        {
            var r = SectionRecord.Parse(new[] { "FORMAT=1", "GUID=g", "LETTER=A", "THK=250" });
            Assert.That(r, Is.Not.Null);
            Assert.That(r!.Thickness, Is.EqualTo(250));
            Assert.That(r.NibHeight, Is.EqualTo(150));
            Assert.That(r.CoverTop, Is.EqualTo(35));
            Assert.That(r.DrawPiles, Is.True);
            Assert.That(r.Parts, Is.Empty);
            Assert.That(r.Hash, Is.EqualTo(""));
        }

        [Test]
        public void Parse_UnknownKeys_AndBrokenField_Tolerated()
        {
            var r = SectionRecord.Parse(new[] { "FORMAT=1", "FUTURE=xyz", "THK=abc", "NIBH=120", "PART=00000010", "bez znaku" });
            Assert.That(r, Is.Not.Null);
            Assert.That(r!.Thickness, Is.EqualTo(225));
            Assert.That(r.NibHeight, Is.EqualTo(120));
            Assert.That(r.Parts.Single().Handle, Is.EqualTo("00000010"));
            Assert.That(r.Parts.Single().Role, Is.EqualTo(""));
        }

        [Test]
        public void Parse_NotARecord_Null()
        {
            Assert.That(SectionRecord.Parse(new[] { "GUID=x" }), Is.Null);
            Assert.That(SectionRecord.Parse(null!), Is.Null);
        }
    }
}
