using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>K9 (decyzja użytkownika 2026-10-11): detal zbrojenia belki 1:25 — okno, przycinanie, miejsce detalu, detal → rzut.</summary>
    public class BeamDetailRulesTests
    {
        private static List<(double X, double Y)> Box(double x0, double y0, double x1, double y1)
            => new List<(double X, double Y)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };

        // Płyta 12000 × 6000, belka krawędziowa wzdłuż lewej krawędzi: nib 160, strefa x 160..760, y 160..5840 (końce 160 od krawędzi).
        private static readonly List<(double X, double Y)> Slab = Box(0, 0, 12000, 6000);

        [Test]
        public void Window_EdgeBeam_OutsideEdgeAndInnerMargin()
        {
            var w = BeamDetailRules.Window(Box(160, 160, 760, 5840), Slab);
            Assert.That(w.X0, Is.EqualTo(-150).Within(1e-9), "krawędź płyty − 150");
            Assert.That(w.X1, Is.EqualTo(760 + 450).Within(1e-9), "lico wewnętrzne + 450");
            Assert.That(w.Y0, Is.EqualTo(-150).Within(1e-9), "koniec przy krawędzi — krawędź − 150");
            Assert.That(w.Y1, Is.EqualTo(6150).Within(1e-9));
        }

        [Test]
        public void Window_InternalEnd_AlongMargin()
        {
            // koniec wewnętrzny (y = 3000) — daleko od krawędzi: + 450
            var w = BeamDetailRules.Window(Box(160, 160, 760, 3000), Slab);
            Assert.That(w.Y1, Is.EqualTo(3450).Within(1e-9));
            Assert.That(w.Y0, Is.EqualTo(-150).Within(1e-9));
        }

        [Test]
        public void Window_EdgeOutsideRange_NotUsed()
        {
            // L-płyta: krawędź x = 0 tylko dla y ∈ [0, 1000] — strefa y 2000..5000 jej nie widzi; krawędź x = 200 przy strefie
            var slab = new List<(double X, double Y)> { (0, 0), (12000, 0), (12000, 6000), (200, 6000), (200, 1000), (0, 1000) };
            var w = BeamDetailRules.Window(Box(360, 2000, 960, 5000), slab);
            Assert.That(w.X0, Is.EqualTo(200 - 150).Within(1e-9));
        }

        [Test]
        public void ClipToRect_InsideCrossingOutside()
        {
            var r = new BeamDetailRules.Rect(0, 0, 100, 100);
            var a = BeamDetailRules.ClipToRect(r, (10, 10), (20, 30));
            Assert.That(a.HasValue, Is.True);
            Assert.That(a!.Value.A, Is.EqualTo((10.0, 10.0)));
            var b = BeamDetailRules.ClipToRect(r, (-50, 50), (150, 50));
            Assert.That(b!.Value.A.X, Is.EqualTo(0).Within(1e-9));
            Assert.That(b.Value.B.X, Is.EqualTo(100).Within(1e-9));
            Assert.That(BeamDetailRules.ClipToRect(r, (-50, -10), (150, -10)).HasValue, Is.False);
            var d = BeamDetailRules.ClipToRect(r, (-50, -50), (50, 50));
            Assert.That(d!.Value.A.X, Is.EqualTo(0).Within(1e-9));
            Assert.That(d.Value.A.Y, Is.EqualTo(0).Within(1e-9));
        }

        [Test]
        public void ClipPolygon_SkipsEdgesOnOuter()
        {
            // linia uskoku: nib 160 tylko wzdłuż lewej krawędzi — pozostałe krawędzie leżą na obrysie (bez dublowania)
            var inner = Box(160, 0, 12000, 6000);
            var w = new BeamDetailRules.Rect(-300, -300, 1360, 6300);
            var segs = BeamDetailRules.ClipPolygon(w, inner, Slab);
            Assert.That(segs.Count, Is.EqualTo(1));
            Assert.That(segs[0].A.X, Is.EqualTo(160).Within(1e-9));
            Assert.That(BeamDetailRules.ClipPolygon(w, Slab).Count, Is.EqualTo(3), "obrys: lewa + przycięta dolna i górna");
        }

        [Test]
        public void FramePolygon_LZone_OffsetClippedToSlab_2026_10_13()
        {
            // płyta 0..10000, strefa L: górny pas (nib 135) + prawa noga do y = 7500 — ramka L: lico + 450 w płycie, krawędź + 150
            var slab = Box(0, 0, 10000, 10000);
            var zone = new List<(double X, double Y)> { (135, 9265), (9265, 9265), (9265, 7500), (9865, 7500), (9865, 9865), (135, 9865) };
            var f = BeamDetailRules.FramePolygon(zone, slab);
            Assert.That(f, Is.Not.Null);
            Assert.That(f.Count, Is.EqualTo(6), string.Join(" ", f));
            var expect = new[] { (-150.0, 8815.0), (8815.0, 8815.0), (8815.0, 7050.0), (10150.0, 7050.0), (10150.0, 10150.0), (-150.0, 10150.0) };
            foreach (var e in expect)
                Assert.That(f.Any(p => Math.Abs(p.X - e.Item1) < 1e-6 && Math.Abs(p.Y - e.Item2) < 1e-6), Is.True, $"{e} w {string.Join(" ", f)}");
            Assert.That(RectilinearPolygons.SignedArea(f), Is.GreaterThan(0));
            var w = BeamDetailRules.DetailWindow(zone, slab);
            Assert.That((w.X0, w.Y0, w.X1, w.Y1), Is.EqualTo((-150.0, 7050.0, 10150.0, 10150.0)));
            // przycięcie do ramki L: linia przez wnętrze płyty poza ramką odpada, odcinek wzdłuż krawędzi ramki zostaje
            var c = BeamDetailRules.ClipToPolygon(f, (0, 8000), (10000, 8000));
            Assert.That(c.Count, Is.EqualTo(1));
            Assert.That(c[0].A.X, Is.EqualTo(8815).Within(1e-6));
            Assert.That(c[0].B.X, Is.EqualTo(10000).Within(1e-6));
            var top = BeamDetailRules.ClipPolygon(f, slab);
            Assert.That(top.Sum(x => Math.Abs(x.B.X - x.A.X) + Math.Abs(x.B.Y - x.A.Y)),
                        Is.EqualTo(10000 + (10000 - 8815) + (10000 - 7050)).Within(1e-6), "obrys płyty w ramce: góra, lewa część, prawa część");
        }

        [Test]
        public void Place_SkipsOccupiedToTheRight()
        {
            var occ = new List<BeamDetailRules.Rect> { new BeamDetailRules.Rect(0, 0, 1000, 1000), new BeamDetailRules.Rect(1500, 500, 3000, 900) };
            var r = BeamDetailRules.Place(400, 400, occ, 0, 200, 100);
            Assert.That(r.X0, Is.EqualTo(3100).Within(1e-9));
            Assert.That(occ.Any(o => r.Overlaps(o, 100 - 1e-6)), Is.False);
            var free = BeamDetailRules.Place(400, 400, occ, 0, 2000, 100);
            Assert.That(free.X0, Is.EqualTo(0).Within(1e-9), "wolne miejsce — bez przesunięcia");
        }

        [Test]
        public void Shift_Congruent_MapToPlan()
        {
            var zone = Box(160, 160, 760, 5840);
            var anchor = Box(20160, 30160, 20760, 35840);
            var s = BeamDetailRules.ShiftOf(anchor, zone);
            Assert.That(s, Is.EqualTo((20000.0, 30000.0)));
            Assert.That(BeamDetailRules.Congruent(anchor, zone, s), Is.True);
            Assert.That(BeamDetailRules.Congruent(Box(20160, 30160, 20800, 35840), zone, s), Is.False, "strefa zmieniona po detalu");
            Assert.That(BeamDetailRules.MapToPlan((20500, 31000), s), Is.EqualTo((500.0, 1000.0)));
        }
    
        [Test]
        public void DetailMarkEnd_LikePlan()
        {
            // koniec cięcia 267 za ramką — jak na rzucie
            Assert.That(BeamDetailRules.DetailMarkEnd((100, 44097), (100, 43830)), Is.EqualTo((100.0, 43830.0)));
            // koniec w ramce (clip = koniec) — bez zmian
            Assert.That(BeamDetailRules.DetailMarkEnd((100, 500), (100, 500)), Is.EqualTo((100.0, 500.0)));
            // koniec daleko za ramką — 300 za ramką
            var e = BeamDetailRules.DetailMarkEnd((0, 1000), (0, 9000));
            Assert.That(e.X, Is.EqualTo(0).Within(1e-9));
            Assert.That(e.Y, Is.EqualTo(1300).Within(1e-9));
        }
}
}
