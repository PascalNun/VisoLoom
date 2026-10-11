using System.Runtime.InteropServices;
using Rhino.PlugIns;
using Rhino.UI;

[assembly: Guid("a0c5a7cc-5b34-4f65-95c7-d6ed7c309e52")]
namespace VisoLoom.Plugin;

public sealed class VisoLoomPlugIn : PlugIn
{
    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        if (Rhino.RhinoApp.Version.Major < 8 || (Rhino.RhinoApp.Version.Major == 8 && Rhino.RhinoApp.Version.Minor < 21))
        {
            errorMessage = "VisoLoom requires Rhino 8.21 or later with the .NET 8 runtime.";
            return LoadReturnCode.ErrorShowDialog;
        }

        Panels.RegisterPanel(this, typeof(VisoLoomPanel), "VisoLoom", typeof(VisoLoomPlugIn).Assembly, "VisoLoom.Plugin.Resources.VisoLoom.png", PanelType.PerDoc);
        return LoadReturnCode.Success;
    }

    protected override void OnShutdown() => CaptureCoordinator.Shutdown();
}
