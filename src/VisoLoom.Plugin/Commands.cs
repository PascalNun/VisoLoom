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
            RhinoApp.WriteLine("Connect the selected model in VisoLoom Settings, or start Test VisoLoom for the automatic local capture test.");
            return Result.Nothing;
        }

        if (!session.PreviewIsCurrent(doc))
        {
            RhinoApp.WriteLine("Use Preview view in the VisoLoom panel and review it before rendering.");
            return Result.Nothing;
        }
        session.Start(doc);
        return Result.Success;
    }
}
