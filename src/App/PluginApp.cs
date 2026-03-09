using Bricscad.ApplicationServices;
using Teigha.Runtime;

[assembly: ExtensionApplication(typeof(BricsCadRc.App.PluginApp))]

namespace BricsCadRc.App
{
    public class PluginApp : IExtensionApplication
    {
        public void Initialize()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc?.Editor.WriteMessage("\n[RC SLAB] Plugin zaladowany. Wersja 0.1\n");

            RibbonBuilder.Build();
        }

        public void Terminate()
        {
            // cleanup jesli potrzebny
        }
    }
}
