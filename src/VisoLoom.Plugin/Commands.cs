using Rhino;
using Rhino.Commands;
using Rhino.UI;

namespace VisoLoom.Plugin;

public sealed class VisoLoomCommand : Command
{
    public override string EnglishName => "VisoLoom";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        Panels.OpenPanel(typeof(VisoLoomPanel));
        return Result.Success;
    }
}

public sealed class VisoLoomRenderCommand : Command
{
    public override string EnglishName => "VisoLoomRender";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        Panels.OpenPanel(typeof(VisoLoomPanel));
        var session = CaptureCoordinator.ForDocument(doc.RuntimeSerialNumber);
        if (!session.IsConfigured)
        {
            RhinoApp.WriteLine("Start Test VisoLoom, then wait for VisoLoom to connect automatically. Change… remains available for a manual local connection.");
            return Result.Nothing;
        }

        session.Start(doc);
        return Result.Success;
    }
}
