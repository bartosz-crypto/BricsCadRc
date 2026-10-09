using NUnit.Framework;
using BricsCadRc.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BricsCadRc.Tests
{
    /// <summary>W4d (runda 2): znacznik [15] = 2021 „obrys geometrii W4d”.</summary>
    [TestFixture]
    public class BarRecordRulesW4dMarkerTests
    {
        private const int I16 = 1070;

        [Test]
        public void Hat44_With2021_IsHat_NotLegacyRing()
        {
            Assert.That(BarRecordRules.IsLegacyRing44("44", 16, I16, (short)2021), Is.False);
            Assert.That(BarRecordRules.IsLegacyRing44("44", 16, I16, (short)2020), Is.False);
            Assert.That(BarRecordRules.IsLegacyRing44("44", 14, 0, null), Is.True);
            Assert.That(BarRecordRules.IsLegacyRing44("44", 16, I16, (short)1999), Is.True);
        }

        [Test]
        public void IsW4dGeometry_OnlyInt16AtLeast2021()
        {
            Assert.That(BarRecordRules.IsW4dGeometry(16, I16, (short)2021), Is.True);
            Assert.That(BarRecordRules.IsW4dGeometry(16, I16, (short)2020), Is.False, "kapelusz W4c (2020) — obrys sprzed W4d");
            Assert.That(BarRecordRules.IsW4dGeometry(15, I16, (short)2021), Is.False, "rekord krótszy");
            Assert.That(BarRecordRules.IsW4dGeometry(16, 1000, "2021"), Is.False, "inny typ pola");
            Assert.That(BarRecordRules.IsW4dGeometry(14, 0, null), Is.False);
        }

        [Test]
        public void WithShapeStdAtLeast_DoesNotLower2021_AddsWhenMissing()
        {
            int Cur(int v) => v;
            var rec = new List<int>(new int[14]) { 1, 2021 };
            Assert.That(BarRecordRules.WithShapeStdAtLeast(rec, 1, 2020, 2020, Cur)[15], Is.EqualTo(2021));
            var poly = new List<int>(new int[14]);
            var r2 = BarRecordRules.WithShapeStdAtLeast(poly, 1, 2020, 2020, Cur);
            Assert.That(r2.Count, Is.EqualTo(16));
            Assert.That(r2[14], Is.EqualTo(1));
            Assert.That(r2[15], Is.EqualTo(2020));
            var blk = new List<int>(new int[14]) { 2 };
            var r3 = BarRecordRules.WithShapeStd(blk, 1, 2021);
            Assert.That(r3, Has.Count.EqualTo(16));
            Assert.That(r3[14], Is.EqualTo(2), "pręt-blok: [14] = 2 bez zmian");
            Assert.That(r3[15], Is.EqualTo(2021));
        }
    }

    /// <summary>
    /// Testy BarRecordRules (Plan C, etap 2a): scalanie nagłówka [0]–[13] RC_SINGLE_BAR z ogonem [14+]
    /// istniejącego rekordu i rozpoznanie formatu pręta-bloku ([14] = Int16 2).
    /// Pola symulowane napisami: "H0".."H13" = nowy nagłówek, "E0".. = istniejący rekord, "V" = wersja.
    /// </summary>
    [TestFixture]
    public class BarRecordRulesTests
    {
        private static List<string> Head() => Enumerable.Range(0, 14).Select(i => "H" + i).ToList();
        private static List<string> Existing(int n) => Enumerable.Range(0, n).Select(i => "E" + i).ToList();

        // ---- Merge ----

        [Test]
        public void Merge_NoExisting_Gives14()
        {
            var r = BarRecordRules.Merge(Head(), new List<string>(), false, "V");
            Assert.That(r, Is.EqualTo(Head()));
        }

        [Test]
        public void Merge_Existing8_Gives14()
        {
            var r = BarRecordRules.Merge(Head(), Existing(8), false, "V");
            Assert.That(r, Is.EqualTo(Head()));
        }

        [Test]
        public void Merge_Existing14_Gives14()
        {
            var r = BarRecordRules.Merge(Head(), Existing(14), false, "V");
            Assert.That(r, Is.EqualTo(Head()));
        }

        [Test]
        public void Merge_Existing15_NoSetVersion_Gives15_TailKept()
        {
            var r = BarRecordRules.Merge(Head(), Existing(15), false, "V");
            Assert.That(r.Count, Is.EqualTo(15));
            Assert.That(r.Take(14), Is.EqualTo(Head()));
            Assert.That(r[14], Is.EqualTo("E14"));
        }

        [Test]
        public void Merge_Existing15_SetVersion_Gives15_Not16()
        {
            var r = BarRecordRules.Merge(Head(), Existing(15), true, "V");
            Assert.That(r.Count, Is.EqualTo(15));
            Assert.That(r.Take(14), Is.EqualTo(Head()));
            Assert.That(r[14], Is.EqualTo("V"));
        }

        [Test]
        public void Merge_Existing14_SetVersion_Gives15()
        {
            var r = BarRecordRules.Merge(Head(), Existing(14), true, "V");
            Assert.That(r.Count, Is.EqualTo(15));
            Assert.That(r.Take(14), Is.EqualTo(Head()));
            Assert.That(r[14], Is.EqualTo("V"));
        }

        [Test]
        public void Merge_NoExisting_SetVersion_Gives15()
        {
            var r = BarRecordRules.Merge(Head(), new List<string>(), true, "V");
            Assert.That(r.Count, Is.EqualTo(15));
            Assert.That(r[14], Is.EqualTo("V"));
        }

        [Test]
        public void Merge_Existing17_TailKept()
        {
            var r = BarRecordRules.Merge(Head(), Existing(17), false, "V");
            Assert.That(r.Count, Is.EqualTo(17));
            Assert.That(r.Take(14), Is.EqualTo(Head()));
            Assert.That(r.Skip(14), Is.EqualTo(new[] { "E14", "E15", "E16" }));
        }

        [Test]
        public void Merge_Existing17_SetVersion_ReplacesOnly14()
        {
            var r = BarRecordRules.Merge(Head(), Existing(17), true, "V");
            Assert.That(r.Count, Is.EqualTo(17));
            Assert.That(r.Skip(14), Is.EqualTo(new[] { "V", "E15", "E16" }));
        }

        [Test]
        public void Merge_ExistingNull_Gives14()
        {
            var r = BarRecordRules.Merge(Head(), null!, false, "V");
            Assert.That(r, Is.EqualTo(Head()));
        }

        [TestCase(0)]
        [TestCase(13)]
        [TestCase(15)]
        public void Merge_HeadNot14_Throws(int n)
        {
            var head = Enumerable.Range(0, n).Select(i => "H" + i).ToList();
            Assert.Throws<ArgumentException>(() => BarRecordRules.Merge(head, Existing(15), true, "V"));
        }

        [Test]
        public void Merge_DoesNotModifyInputs()
        {
            var head = Head();
            var ex = Existing(15);
            BarRecordRules.Merge(head, ex, true, "V");
            Assert.That(head, Is.EqualTo(Head()));
            Assert.That(ex, Is.EqualTo(Existing(15)));
        }

        // ---- IsBlockFormat ----

        [Test]
        public void IsBlockFormat_Int16_2_True()
            => Assert.That(BarRecordRules.IsBlockFormat(15, 1070, (short)2), Is.True);

        [Test]
        public void IsBlockFormat_Int16_1_False()
            => Assert.That(BarRecordRules.IsBlockFormat(15, 1070, (short)1), Is.False);

        [Test]
        public void IsBlockFormat_AsciiString_2_False()
            => Assert.That(BarRecordRules.IsBlockFormat(15, 1000, "2"), Is.False);

        [Test]
        public void IsBlockFormat_Length14_False()
            => Assert.That(BarRecordRules.IsBlockFormat(14, 1070, (short)2), Is.False);

        [Test]
        public void IsBlockFormat_Int32Value_False()
            => Assert.That(BarRecordRules.IsBlockFormat(15, 1070, 2), Is.False);

        [Test]
        public void IsBlockFormat_NullValue_False()
            => Assert.That(BarRecordRules.IsBlockFormat(15, 1070, null!), Is.False);
    }
}
