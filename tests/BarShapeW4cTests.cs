using NUnit.Framework;
using BricsCadRc.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// Plan W4c (docs/PLAN_W4_BS8666.md): 44 = „kapelusz”, 75 = pierścień; znacznik [15] ShapeStd = 2020 w ogonie
    /// RC_SINGLE_BAR (BarRecordRules), rozpoznanie starego 44-pierścienia.
    /// </summary>
    [TestFixture]
    public class BarShapeW4cTests
    {
        private static List<string> Head() => Enumerable.Range(0, 14).Select(i => "H" + i).ToList();

        // ── BarRecordRules: znacznik ShapeStd ────────────────────────────────

        [Test]
        public void WithShapeStd_Polyline14_Adds14And15()
        {
            var r = BarRecordRules.WithShapeStd(Head(), "P1", "S2020");
            Assert.That(r.Count, Is.EqualTo(16));
            Assert.That(r[BarRecordRules.VersionIndex], Is.EqualTo("P1"));
            Assert.That(r[BarRecordRules.ShapeStdIndex], Is.EqualTo("S2020"));
        }

        [Test]
        public void WithShapeStd_Block15_Keeps14_Adds15()
        {
            var rec = Head(); rec.Add("V2");
            var r = BarRecordRules.WithShapeStd(rec, "P1", "S2020");
            Assert.That(r.Count, Is.EqualTo(16));
            Assert.That(r[14], Is.EqualTo("V2"));
            Assert.That(r[15], Is.EqualTo("S2020"));
        }

        [Test]
        public void WithShapeStd_Existing16_Replaces15_NoDuplicate_TailKept()
        {
            var rec = Head(); rec.Add("V1"); rec.Add("OLD"); rec.Add("X16");
            var r = BarRecordRules.WithShapeStd(rec, "P1", "S2020");
            Assert.That(r.Count, Is.EqualTo(17));
            Assert.That(r[14], Is.EqualTo("V1"));
            Assert.That(r[15], Is.EqualTo("S2020"));
            Assert.That(r[16], Is.EqualTo("X16"));
        }

        [Test]
        public void WithShapeStd_Idempotent()
        {
            var r = BarRecordRules.WithShapeStd(BarRecordRules.WithShapeStd(Head(), "P1", "S"), "P1", "S");
            Assert.That(r.Count, Is.EqualTo(16));
        }

        [Test]
        public void WithShapeStd_ShortRecord_Throws()
            => Assert.Throws<ArgumentException>(() => BarRecordRules.WithShapeStd(Head().Take(10).ToList(), "P1", "S"));

        [Test]
        public void MergeThenShapeStd_Block_NeverDuplicates14()
        {
            // pręt-blok: Merge ustawia [14] = 2, znacznik dopisuje [15]
            var existing = Head(); existing.Add("V2"); existing.Add("S2020");
            var m = BarRecordRules.Merge(Head(), existing, true, "V2");
            var r = BarRecordRules.WithShapeStd(m, "P1", "S2020");
            Assert.That(r.Count, Is.EqualTo(16));
            Assert.That(r[14], Is.EqualTo("V2"));
        }

        [Test]
        public void NeedsShapeStd_Only44()
        {
            Assert.That(BarRecordRules.NeedsShapeStd("44"), Is.True);
            Assert.That(BarRecordRules.NeedsShapeStd("75"), Is.False);
            Assert.That(BarRecordRules.NeedsShapeStd("00"), Is.False);
            Assert.That(BarRecordRules.NeedsShapeStd(null!), Is.False);
        }

        [Test]
        public void IsLegacyRing44_NoMarker_True()
        {
            Assert.That(BarRecordRules.IsLegacyRing44("44", 14, 0, null!), Is.True);              // polilinia sprzed W4c
            Assert.That(BarRecordRules.IsLegacyRing44("44", 15, 0, null!), Is.True);              // pręt-blok [14] = 2 bez [15]
            Assert.That(BarRecordRules.IsLegacyRing44("44", 16, 1070, (short)1999), Is.True);     // inna wartość
            Assert.That(BarRecordRules.IsLegacyRing44("44", 16, 1000, "2020"), Is.True);          // zły typ pola
        }

        [Test]
        public void IsLegacyRing44_Marker2020_False()
        {
            Assert.That(BarRecordRules.IsLegacyRing44("44", 16, 1070, (short)2020), Is.False);
            Assert.That(BarRecordRules.IsLegacyRing44("75", 14, 0, null!), Is.False);
            Assert.That(BarRecordRules.IsLegacyRing44("21", 14, 0, null!), Is.False);
        }

        // ── kształty ─────────────────────────────────────────────────────────

        [Test]
        public void Library_44Hat_75Ring_InPicker()
        {
            Assert.That(ShapeCodeLibrary.Get("44")!.Name, Is.EqualTo("Hat"));
            Assert.That(ShapeCodeLibrary.Get("75")!.Name, Does.Contain("Ring"));
            Assert.That(ShapeCodeLibrary.Get("75")!.Parameters, Is.EqualTo(new[] { "A", "B" }));
            var pick = ShapeCodeLibrary.GetPickable().Select(s => s.Code).ToList();
            Assert.That(pick, Does.Contain("44"));
            Assert.That(pick, Does.Contain("75"));
            Assert.That(ShapeCodeLibrary.Get("77"), Is.Null, "77 (helisa) nieobsługiwany");
        }

        [Test]
        public void Hat44_LocalOutline_NotNull_60Vertices()
        {
            var outline = BarFrameRules.LocalOutline("44", new[] { 500.0, 400.0, 500.0, 400.0, 300.0 }, 12);
            Assert.That(outline, Is.Not.Null);
            Assert.That(outline!.Count, Is.EqualTo(60));
        }

        [Test]
        public void Ring75_GeometryEqualsLegacyRing44()
        {
            // zmigrowany pierścień w umowie osi (etap W4d) rysowany tak samo jak dawny 44 (A = średnica, B = zakład bez wpływu
            // na rysunek); W4e: A = średnica zewnętrzna → oś o promieniu (A − d)/2, środek (A/2, A/2) bez zmian
            var pv = new[] { 300.0, 0, 0, 0, 0 };
            var w4d = BarGeometryBuilder.GetLocalPoints("75", pv, 12, BarGeometryOptions.W4d);
            var old = BarGeometryBuilder.GetLocalPoints("44", pv, 12, BarGeometryOptions.Legacy);
            Assert.That(w4d.Count, Is.EqualTo(9));
            Assert.That(w4d, Is.EqualTo(old));
            var now = BarGeometryBuilder.GetLocalPoints("75", pv, 12);
            Assert.That(now.Count, Is.EqualTo(9));
            foreach (var p in now)
                Assert.That(Math.Sqrt((p.X - 150) * (p.X - 150) + (p.Y - 150) * (p.Y - 150)), Is.EqualTo(144.0).Within(1e-9));
        }

        [Test]
        public void Ring75_Formula_LapB()
            => Assert.That(ShapeCodeLibrary.Get("75")!.CalculateTotalLength(new[] { 300.0, 0.0 }, 12),
                           Is.EqualTo(950.0).Within(1e-9));   // π·288 + 25 = 929.8 → 950

        [Test]
        public void Hat44_RecognisedByVariants_AfterRotateMirror()
        {
            var pv = new[] { 500.0, 400.0, 500.0, 400.0, 300.0 };
            var local = BarFrameRules.LocalOutline("44", pv, 12)!;
            var f = new BarFrame2(Math.PI / 6, true);
            var world = local.Select(p => { var w = f.Apply(p.X - local[0].X, p.Y - local[0].Y); return (10 + w.X, 20 + w.Y); }).ToList();
            Assert.That(BarFrameRules.TryFitVariants(world, "44", pv, 12, out var got, out _), Is.True);
            Assert.That(got.Angle, Is.EqualTo(Math.PI / 6).Within(1e-9));
            Assert.That(got.Mirrored, Is.True);
        }
    }
}
