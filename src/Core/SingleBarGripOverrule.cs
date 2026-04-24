using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    /// <summary>
    /// STUB — wyłączony overrule, zachowany dla przyszłego Plan C.
    ///
    /// Historia:
    /// Próbowaliśmy dodać custom GripOverrule dla SingleBar (Polyline z XData RC_SINGLE_BAR),
    /// żeby pokazywać tylko 2 gripy na osi bara (start + end) zamiast domyślnych
    /// 2N wierzchołków outline + midpoints.
    ///
    /// Empiryczny test (prompt_23) potwierdził że BricsCAD/Teigha NIE wywołuje IsApplicable
    /// dla GripOverrule zarejestrowanego na native typie Polyline. W AutoCAD działa
    /// (keanw.com 2012, identyczny wzorzec). W BricsCAD nie — sprawdzone z i bez filtrów,
    /// z różnymi kombinacjami SetXDataFilter/SetCustomFilter.
    ///
    /// Plan C (przyszłość):
    /// Zamienić SingleBar z "Polyline w ModelSpace" na "BlockReference do BTR zawierającego
    /// outline polyline". Na BlockReference GripOverrule w BricsCAD DZIAŁA (już używane
    /// w projekcie: AnnotGripOverrule dla RC_BAR_BLOCK / RC_BAR_ANNOT). Wtedy można
    /// przywrócić logikę custom gripów która była w prompt_17.
    ///
    /// Wariant pośredni (Plan B, obecnie wdrażany):
    /// Zostawić default BricsCAD gripy na Polyline. BarGeometryWatcher reaguje na modyfikacje
    /// polilinii bara (event ObjectModified) i:
    /// - Dla shape 00/99: przyjmuje zmianę LengthA z rzeczywistej długości osi outline.
    /// - Dla shape 11+: wycofuje zmianę przez regenerację outline z XData.
    /// </summary>
    public class SingleBarGripOverrule : GripOverrule
    {
        // Klasa NIE jest rejestrowana w PluginApp (zob. Plan C w komentarzu wyżej).
        // Poniższa metoda Register pozostaje jako "gotowy szkielet" gdyby refactor na BlockReference
        // wymagał przełączenia back on — wtedy trzeba zmienić typeof(Polyline) na typeof(BlockReference).

        public static void Register()
        {
            // no-op — patrz TODO Plan C w PluginApp.Initialize
        }

        public static void Unregister()
        {
            // no-op
        }
    }
}
