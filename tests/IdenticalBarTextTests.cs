using NUnit.Framework;
using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// K4 (przeniesienie e7b600d (a)+(b)): teksty okna „identyczny pręt” w RC_DISTRIBUTION i komunikatu po COPY
    /// o kopii z tym samym numerem pozycji — <see cref="IdenticalBarText"/> (czysta logika).
    /// </summary>
    [TestFixture]
    public class IdenticalBarTextTests
    {
        private sealed class B
        {
            public string Mark = "";
            public int Nr;
            public string Shape = "";
        }

        private static B Bar(string mark, int nr, string shape) => new B { Mark = mark, Nr = nr, Shape = shape };

        private static List<string> Msgs(IEnumerable<B> copies, IEnumerable<B> others)
            => IdenticalBarText.SameNumberCopyMessages(copies, others, b => b.Mark, b => b.Nr, (a, b) => a.Shape == b.Shape);

        [TestCase("H12-03", "H12-03")]
        [TestCase("H12-03 B1", "H12-03")]
        [TestCase("  H10-104  ", "H10-104")]
        [TestCase("", "?")]
        [TestCase(null, "?")]
        public void MarkKey_StripsSuffix(string? mark, string expected)
            => Assert.That(IdenticalBarText.MarkKey(mark), Is.EqualTo(expected));

        [Test]
        public void Group_CountsAndSortsCaseInsensitive()
        {
            var g = IdenticalBarText.Group(new[] { "H12-05", "h12-03", "H12-05 B1", "H12-03" });
            Assert.That(g.Select(kv => kv.Key + ":" + kv.Value), Is.EqualTo(new[] { "h12-03:2", "H12-05:2" }));
        }

        [Test]
        public void Dims_OnlyPositive_NoDecimals()
            => Assert.That(IdenticalBarText.Dims(new[] { 3000.4, 0, 250, -1, 0 }, 5), Is.EqualTo("3000/250"));

        [Test]
        public void Dims_OnlyShapeParameters()
        {
            // 00 (1 parametr): śmieci w B–E nie są wypisywane; 21 (3 parametry): D, E pominięte
            Assert.That(IdenticalBarText.Dims(new double[] { 3000, 250, 100, 0, 0 }, 1), Is.EqualTo("3000"));
            Assert.That(IdenticalBarText.Dims(new double[] { 500, 300, 500, 40, 60 }, 3), Is.EqualTo("500/300/500"));
        }

        [Test]
        public void DialogText_ListsNumbersWithCounts()
        {
            string t = IdenticalBarText.DialogText("H12-03", 12, "21", new double[] { 500, 300, 500, 0, 0 }, 3,
                                                   new[] { "H12-05", "H12-03", "H12-05" });
            Assert.That(t, Does.StartWith("Na rysunku jest już pręt identyczny z H12-03 (H12, kształt 21, 500/300/500 mm):"));
            Assert.That(t, Does.Contain("   • H12-03  (1 szt.)\n   • H12-05  (2 szt.)"));
            Assert.That(t, Does.EndWith("Kontynuować rozkład z wybranego pręta?"));
        }

        [Test]
        public void ScriptText_OneLine_WithPrefix()
        {
            string t = IdenticalBarText.ScriptText("H12-03", 12, null, new double[] { 3000, 250, 0, 0, 0 }, 1, new[] { "H12-07" });
            Assert.That(t, Is.EqualTo("[RC] Na rysunku jest już pręt identyczny z H12-03 (H12, kształt 00, 3000 mm): " +
                                      "H12-07 (1 szt.) — tryb skryptowy: rozkład bez pytania."));
            Assert.That(t, Does.Not.Contain("\n"));
        }

        [Test]
        public void SameNumberCopy_CopyWithSameNumberAndShape_OneMessagePerNumber()
        {
            var others = new[] { Bar("H12-03", 3, "A"), Bar("H12-04", 4, "B") };
            var copies = new[] { Bar("H12-03", 3, "A"), Bar("H12-03", 3, "A"), Bar("H12-04", 4, "B") };
            var m = Msgs(copies, others);
            Assert.That(m, Has.Count.EqualTo(2));
            Assert.That(m[0], Does.StartWith("[RC] Kopia pręta H12-03 (2 szt.) ma identyczne wymiary jak istniejący pręt"));
            Assert.That(m[1], Does.StartWith("[RC] Kopia pręta H12-04 (1 szt.)"));
        }

        [Test]
        public void SameNumberCopy_NoOriginal_OrOtherShape_OrNoNumber_NoMessage()
        {
            // MIRROR z usunięciem źródła: brak pręta spoza kopii
            Assert.That(Msgs(new[] { Bar("H12-03", 3, "A") }, new B[0]), Is.Empty);
            // ten sam numer, inny kształt
            Assert.That(Msgs(new[] { Bar("H12-03", 3, "A") }, new[] { Bar("H12-03", 3, "X") }), Is.Empty);
            // pręt bez numeru (testowy) — ani jako kopia, ani jako oryginał
            Assert.That(Msgs(new[] { Bar("H12-T", 0, "A") }, new[] { Bar("H12-T", 0, "A") }), Is.Empty);
            // inny numer, ten sam kształt
            Assert.That(Msgs(new[] { Bar("H12-03", 3, "A") }, new[] { Bar("H12-05", 5, "A") }), Is.Empty);
        }
    }
}
