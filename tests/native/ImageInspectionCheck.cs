using System.Reflection;
using System.Text.Json;
using Eto.Drawing;
using Eto.Forms;
using VisoLoom.Core;

namespace VisoLoom.Plugin
{
    // The isolated assembly does not register a plug-in or read user preferences.
    internal sealed class VisoLoomPlugIn
    {
        public static VisoLoomPlugIn Instance => throw new InvalidOperationException("The fixture has no registered plug-in.");
        public Rhino.PersistentSettings Settings => throw new InvalidOperationException("The fixture does not use Rhino preferences.");
    }
}

namespace VisoLoom.NativeChecks
{
    public static class InspectionCheck
    {
        public static void Run(string report)
        {
            using var source = new Bitmap(1600, 1000, PixelFormat.Format24bppRgb);
            using var result = new Bitmap(1600, 1000, PixelFormat.Format24bppRgb);
            Draw(source, Colors.Blue, Colors.White);
            Draw(result, Colors.DarkOrange, Colors.White);
            var type = typeof(VisoLoom.Plugin.ImageInspectionWindow);
            using var dialog = (Dialog)type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single().Invoke(
                [source, result, "Authored comparison fixture · no Rhino capture or generation", "Blue source / orange result · fixed identical geometry", "Result"]);
            var view = (ImageInspection)type.GetField("view", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
            var wipe = type.GetField("wipe", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var divider = (Slider)type.GetField("divider", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
            var states = new List<object>();
            dialog.Shown += (_, _) => Record("opened");
            foreach (var button in Descendants(dialog).OfType<Button>())
                button.Click += (_, _) => Record(button.Text);
            divider.ValueChanged += (_, _) => Record("divider");
            try { dialog.ShowModal(RhinoOwner()); }
            finally { File.WriteAllText(report, JsonSerializer.Serialize(new { states }, new JsonSerializerOptions { WriteIndented = true })); }
            void Record(string action) => states.Add(new { action, view.ShowResult, view.IsFit, view.ZoomPercent, view.Scale, view.DisplayScale,
                x = view.Bounds.X, y = view.Bounds.Y, width = view.Bounds.Width, height = view.Bounds.Height,
                wipe = (bool)wipe.GetValue(dialog)!, divider = divider.Value });
        }

        private static Window? RhinoOwner() => Application.Instance.Windows.FirstOrDefault(w => w.Title.Contains("Rhino", StringComparison.OrdinalIgnoreCase));
        private static IEnumerable<Control> Descendants(Control control)
        {
            yield return control;
            if (control is Container container)
                foreach (var child in container.Controls)
                    foreach (var descendant in Descendants(child)) yield return descendant;
        }
        private static void Draw(Bitmap bitmap, Color accent, Color background)
        {
            using var graphics = new Graphics(bitmap);
            graphics.Clear(background);
            graphics.FillRectangle(Colors.Gray, 200, 200, 1200, 60);
            graphics.FillRectangle(Colors.Gray, 280, 260, 100, 520);
            graphics.FillRectangle(Colors.Gray, 1220, 260, 100, 520);
            graphics.FillRectangle(accent, 650, 500, 300, 250);
        }
    }
}
