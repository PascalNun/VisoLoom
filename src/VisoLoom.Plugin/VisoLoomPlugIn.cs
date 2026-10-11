using System.Runtime.InteropServices;
using Rhino.PlugIns;
using Rhino.UI;

[assembly: Guid("a0c5a7cc-5b34-4f65-95c7-d6ed7c309e52")]
namespace VisoLoom.Plugin;

public sealed class VisoLoomPlugIn : PlugIn
{
    internal static VisoLoomPlugIn Instance { get; private set; } = null!;
    public VisoLoomPlugIn() => Instance = this;

    protected override void OptionsDialogPages(List<OptionsDialogPage> pages) => pages.Add(new ConnectionSettingsPage());

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        if (Rhino.RhinoApp.Version.Major != 8)
        {
            errorMessage = "VisoLoom targets Rhino 8 on macOS and Windows.";
            return LoadReturnCode.ErrorShowDialog;
        }

        ConnectionSettings.Load();
        Panels.RegisterPanel(this, typeof(VisoLoomPanel), "VisoLoom", typeof(VisoLoomPlugIn).Assembly, "VisoLoom.Plugin.Resources.VisoLoom.png", PanelType.PerDoc);
        return LoadReturnCode.Success;
    }

    protected override void OnShutdown()
    {
        ConnectionSettings.ClearSession();
        CaptureCoordinator.Shutdown();
    }
}
