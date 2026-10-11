using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using VisoLoom.Core;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

[Guid("bbf16d98-c114-4f65-9790-6ee8dfe5806e")]
public sealed class VisoLoomPanel : Panel
{
    private readonly uint serial;
    private readonly CaptureCoordinator session;
    private readonly DropDown model = new();
    private readonly DropDown preset = new();
    private readonly TextArea prompt = new() { Height = 70 };
    private readonly Label disclosure = CreateLabel("");
    private readonly Label resultLabel = CreateLabel("Returned reference · no generation", bold: true);
    private readonly Button reviewPrompt = new() { Text = "Review prepared prompt…", Enabled = false };
    private readonly Button settings = new() { Text = "Settings…" };
    private readonly Label viewLabel = CreateLabel("");
    private readonly Label status = CreateLabel("");
    private readonly Label destination = CreateLabel("");
    private readonly DropDown resolution = new()
    {
        Items =
        {
            "Viewport",
            "2× Viewport",
            "4× Viewport",
            "Custom (same aspect)"
        },
        SelectedIndex = 0
    };
    private readonly NumericStepper width = new()
    {
        MinValue = 1,
        MaxValue = 16384,
        DecimalPlaces = 0,
        Value = 1920,
        Width = 80
    };
    private readonly NumericStepper height = new()
    {
        MinValue = 1,
        MaxValue = 16384,
        DecimalPlaces = 0,
        Value = 1080,
        Width = 80
    };
    private readonly Label dimensions = CreateLabel("");
    private readonly Button test = new() { Text = "Test capture" };
    private readonly Button cancel = new()
    {
        Text = "Cancel",
        Enabled = false
    };
    private readonly Button save = new()
    {
        Text = "Save PNG…",
        Enabled = false
    };
    private readonly Button reconnect = new()
    {
        Text = "Reconnect",
        Visible = false
    };
    private readonly Label capturedLabel = CreateLabel("Captured Rhino view", bold: true);
    private readonly Button change = new() { Text = "Change…" };
    // The scaled rows fill the panel; image pixel width must not set its minimum width.
    private readonly ImageView source = new() { Width = 1, Height = 170 };
    private readonly ImageView result = new() { Width = 1, Height = 170 };
    private Bitmap? sourceBitmap;
    private Bitmap? resultBitmap;
    private Guid? shownScene;
    private Guid? shownJob;
    private readonly UITimer timer = new() { Interval = 1 };
    public VisoLoomPanel(RhinoDoc doc)
    {
        serial = doc.RuntimeSerialNumber;
        session = CaptureCoordinator.ForDocument(serial);
        Padding = 12;
        var layout = new DynamicLayout
        {
            Spacing = new Size(8, 8)
        };
        layout.AddRow(CreateLabel("VisoLoom", bold: true));
        layout.AddRow(CreateLabel("Generative Representation for Rhino"));
        layout.AddRow(CreateLabel("View", bold: true));
        layout.AddRow(viewLabel);
        layout.AddRow(CreateLabel("Preset"));
        foreach (var item in PromptPresets.All)
            preset.Items.Add(item.Name);
        preset.SelectedIndex = Math.Max(0, PromptPresets.All.ToList().FindIndex(p => p.Id == session.SelectedPreset.Id));
        prompt.Text = session.UserPrompt;
        layout.AddRow(preset);
        layout.AddRow(CreateLabel("Prompt · optional"));
        layout.AddRow(prompt);
        layout.AddRow(CreateLabel("Resolution", bold: true));
        layout.AddRow(resolution);
        // Multi-control rows need their own grid, rather than widening every panel row.
        layout.AddRow(new TableLayout
        {
            Spacing = new Size(8, 8),
            Rows =
            {
                new TableRow(width, new Label { Text = "×" }, height, new TableCell(null, scaleWidth: true))
            }
        });
        layout.AddRow(dimensions);
        layout.AddRow(CreateLabel("Model", bold: true));
        layout.AddRow(model);
        layout.AddRow(destination);
        layout.AddRow(new TableLayout
        {
            Spacing = new Size(8, 8),
            Rows = { new TableRow(new TableCell(settings, scaleWidth: true), change) }
        });
        layout.AddRow(disclosure);
        layout.AddRow(new TableLayout
        {
            Spacing = new Size(8, 8),
            Rows =
            {
                new TableRow(new TableCell(test, scaleWidth: true), cancel)
            }
        });
        layout.AddRow(status);
        layout.AddRow(reviewPrompt);
        layout.AddRow(reconnect);
        layout.AddRow(capturedLabel);
        layout.AddRow(source);
        layout.AddRow(resultLabel);
        layout.AddRow(result);
        layout.AddRow(save);
        layout.AddRow(null);
        Content = new Scrollable
        {
            Content = layout,
            Border = BorderType.None,
            ExpandContentWidth = true
        };
        resolution.SelectedIndexChanged += (_, _) => RefreshView();
        width.ValueChanged += (_, _) => RefreshView();
        height.ValueChanged += (_, _) => RefreshView();
        change.Click += (_, _) => Configure();
        settings.Click += (_, _) => ConnectionSettingsControl.ShowSettings(this);
        preset.SelectedIndexChanged += (_, _) => session.SelectedPreset = PromptPresets.All[preset.SelectedIndex];
        prompt.TextChanged += (_, _) => session.UserPrompt = prompt.Text;
        reviewPrompt.Click += (_, _) =>
        {
            using var dialog = new Dialog { Title = "VisoLoom · Prepared prompt", ClientSize = new Size(560, 500), Resizable = true };
            var close = new Button { Text = "Close" };
            var layout = new DynamicLayout { Padding = 8, Spacing = new Size(8, 8) };
            layout.Add(new TextArea { ReadOnly = true, Text = session.Intent?.EffectivePrompt ?? "" }, yscale: true);
            layout.AddRow(close);
            dialog.Content = layout;
            dialog.AbortButton = close;
            close.Click += (_, _) => dialog.Close();
            dialog.ShowModal(this);
        };
        model.SelectedIndexChanged += (_, _) =>
        {
            if (model.SelectedIndex >= 0)
            {
                session.SelectedProvider = model.SelectedIndex == 1 ? "openai" : "local";
                Refresh();
                RefreshView();
            }
        };
        ConnectionSettings.Changed += ConnectionChanged;
        UpdateModels();
        test.Click += (_, _) =>
        {
            var document = RhinoDoc.FromRuntimeSerialNumber(serial);
            if (document is null)
                return;
            session.CustomSize = resolution.SelectedIndex == 3 ? new PixelSize((int)width.Value, (int)height.Value) : null;
            session.Multiplier = resolution.SelectedIndex switch
            {
                1 => 2,
                2 => 4,
                _ => 1
            };
            session.Start(document, PromptPresets.All[preset.SelectedIndex], prompt.Text);
        };
        reconnect.Click += (_, _) => session.Reconnect();
        cancel.Click += (_, _) => session.Cancel();
        save.Click += (_, _) => Save();
        session.Changed += SessionChanged;
        timer.Elapsed += (_, _) =>
        {
            RefreshView();
            _ = session.TryConnectAutomaticallyAsync();
        };
        timer.Start();
        UnLoad += (_, _) => timer.Stop();
        Load += (_, _) =>
        {
            timer.Start();
            Refresh();
        };
        Refresh();
    }

    internal static Label CreateLabel(string text, bool bold = false)
    {
        var label = new PanelLabel
        {
            Text = text,
            TextAlignment = TextAlignment.Left,
            Wrap = WrapMode.Word,
            Width = 1
        };
        if (bold)
            label.Font = SystemFonts.Bold();
        return label;
    }

    private sealed class PanelLabel : Label
    {
        protected override void OnApplyCascadingStyles()
        {
            base.OnApplyCascadingStyles();
            // Rhino can style labels when attaching the panel, after its constructor has run.
            TextAlignment = TextAlignment.Left;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            session.Changed -= SessionChanged;
            ConnectionSettings.Changed -= ConnectionChanged;
            timer.Dispose();
            sourceBitmap?.Dispose();
            resultBitmap?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ConnectionChanged(object? sender, EventArgs e)
    {
        UpdateModels();
        Refresh();
    }

    private void UpdateModels()
    {
        bool selectedOpenAi = session.SelectedProvider == "openai";
        model.Items.Clear();
        model.Items.Add("This computer · capture test");
        if (ConnectionSettings.IsReady || selectedOpenAi || ConnectionSettings.CloudAllowed)
            model.Items.Add(OpenAiImageProvider.Model.DisplayName + (ConnectionSettings.IsReady ? "" : " · needs setup"));
        model.SelectedIndex = selectedOpenAi ? 1 : 0;
        session.SelectedProvider = model.SelectedIndex == 1 ? "openai" : "local";
    }

    private void SessionChanged(object? sender, EventArgs e) => Refresh();
    private void Refresh()
    {
        status.Text = session.Status;
        reconnect.Visible = session.CanReconnect;
        reconnect.Enabled = !session.IsBusy;
        bool generating = session.SelectedProvider == "openai";
        destination.Text = generating ? "Cloud · OpenAI\napi.openai.com" : "Local reference server\n" + session.Endpoint.GetLeftPart(UriPartial.Authority);
        disclosure.Text = generating ? "Captured PNG and selected scene context → OpenAI" : "Captured PNG and scene metadata → this computer";
        test.Text = generating ? "Render" : "Test capture";
        change.Visible = !generating;
        cancel.Text = session.ActiveProvider == "openai" ? "Stop waiting" : "Cancel";
        resultLabel.Text = session.Result?.Metadata.IsUnchangedReference == false ? "Generated image · compare with source" : "Returned reference · no generation";
        test.Enabled = !session.IsBusy && !session.IsConnecting && session.IsConfigured;
        change.Enabled = !session.IsBusy && !session.IsConnecting;
        cancel.Enabled = session.IsBusy;
        save.Enabled = session.Result is not null;
        reviewPrompt.Enabled = session.Intent is { EffectivePrompt.Length: > 0 };
        resolution.Enabled = !session.IsBusy;
        model.Enabled = !session.IsBusy;
        preset.Enabled = prompt.Enabled = generating && !session.IsBusy;
        if (session.Capture is { } captured && captured.Manifest.SceneId != shownScene)
        {
            try
            {
                using var stream = new MemoryStream(captured.Png);
                var decoded = new Bitmap(stream);
                if (decoded.Width != captured.Manifest.Frame.Width || decoded.Height != captured.Manifest.Frame.Height)
                {
                    decoded.Dispose();
                    throw new InvalidOperationException();
                }

                capturedLabel.Text = $"Captured: {captured.ViewName} · {captured.Manifest.Frame.Width} × {captured.Manifest.Frame.Height} px";
                source.Image = decoded;
                sourceBitmap?.Dispose();
                sourceBitmap = decoded;
                shownScene = captured.Manifest.SceneId;
                result.Image = null;
                resultBitmap?.Dispose();
                resultBitmap = null;
                shownJob = null;
            }
            catch (Exception)
            {
                status.Text = "The host could not decode the captured PNG. The image is not displayed.";
            }
        }

        if (session.Result is { } output && output.Metadata.ResultId != shownJob)
        {
            try
            {
                using var stream = new MemoryStream(output.PngBytes);
                var decoded = new Bitmap(stream);
                if (decoded.Width != session.Capture!.Manifest.Frame.Width || decoded.Height != session.Capture.Manifest.Frame.Height)
                {
                    decoded.Dispose();
                    throw new InvalidOperationException();
                }

                result.Image = decoded;
                resultBitmap?.Dispose();
                resultBitmap = decoded;
                shownJob = output.Metadata.ResultId;
            }
            catch (Exception)
            {
                status.Text = "The host could not decode the returned PNG. The image is not displayed.";
                save.Enabled = false;
            }
        }

        RefreshView();
    }

    private void RefreshView()
    {
        var document = RhinoDoc.FromRuntimeSerialNumber(serial);
        var view = document?.Views.ActiveView;
        if (view is null)
        {
            viewLabel.Text = "No model viewport";
            test.Enabled = false;
            return;
        }

        var viewport = view.ActiveViewport;
        viewLabel.Text = viewport.Name + " · " + (viewport.IsParallelProjection ? "Parallel" : "Perspective") + " · " + viewport.DisplayMode.LocalName;
        bool custom = resolution.SelectedIndex == 3;
        width.Enabled = height.Enabled = custom && !session.IsBusy;
        try
        {
            var pixels = new PixelSize(viewport.Size.Width, viewport.Size.Height);
            var size = custom ? new PixelSize((int)width.Value, (int)height.Value) : ResolutionPlanner.ScaleViewport(pixels, resolution.SelectedIndex switch
            {
                1 => 2,
                2 => 4,
                _ => 1
            });
            dimensions.Text = $"{size.Width:N0} × {size.Height:N0} px · complete Rhino frame";
            if (session.SelectedProvider == "openai")
            {
                var plan = ResolutionPlanner.Plan(size, size, OpenAiImageProvider.Model.Resolution);
                dimensions.Text += $"\nModel: {plan.Native.Width:N0} × {plan.Native.Height:N0} px";
                if (!plan.IsExact)
                    dimensions.Text += " · surrounding padding removed; scaling recorded";
                if (plan.Native.Pixels > 2560L * 1440)
                    dimensions.Text += "\nThis model resolution is experimental.";
            }
            if ((long)pixels.Width * size.Height != (long)pixels.Height * size.Width)
                dimensions.Text = "Custom size must match the Rhino view's aspect ratio.";
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            dimensions.Text = "This size exceeds the capture limit. Choose a smaller size.";
        }
    }

    private void Configure()
    {
        var endpoint = new TextBox
        {
            Text = session.Endpoint.AbsoluteUri,
            Width = 300
        };
        var secret = new PasswordBox
        {
            Width = 300
        };
        var error = CreateLabel("The token is held in memory for this document session only.");
        var connect = new Button
        {
            Text = "Use local server"
        };
        var dismiss = new Button
        {
            Text = "Cancel"
        };
        using var dialog = new Dialog
        {
            Title = "VisoLoom · Manual local connection",
            Padding = 16,
            Resizable = false
        };
        var layout = new DynamicLayout
        {
            Spacing = new Size(8, 8)
        };
        layout.AddRow(CreateLabel("This computer only · unchanged reference", bold: true));
        layout.AddRow(CreateLabel("Server"));
        layout.AddRow(endpoint);
        layout.AddRow(CreateLabel("Session token"));
        layout.AddRow(secret);
        layout.AddRow(error);
        layout.AddRow(CreateLabel("The test launcher connects automatically. Manual settings take precedence for this session."));
        layout.AddRow(new TableLayout
        {
            Spacing = new Size(8, 8),
            Rows =
            {
                new TableRow(new TableCell(connect, scaleWidth: true), dismiss)
            }
        });
        dialog.Content = layout;
        dialog.DefaultButton = connect;
        dialog.AbortButton = dismiss;
        dismiss.Click += (_, _) => dialog.Close();
        connect.Click += (_, _) =>
        {
            try
            {
                session.Configure(new Uri(endpoint.Text), secret.Text);
                secret.Text = "";
                dialog.Close();
            }
            catch (Exception ex) when (ex is ArgumentException or UriFormatException or InvalidOperationException)
            {
                error.Text = ex.Message;
            }
        };
        timer.Stop();
        try
        {
            dialog.ShowModal(this);
        }
        finally
        {
            secret.Text = "";
            timer.Start();
        }
    }

    private void Save()
    {
        if (session.Result is not { } output || resultBitmap is null)
            return;
        using var dialog = new SaveFileDialog
        {
            Title = output.Metadata.IsUnchangedReference ? "Save unchanged reference" : "Save generated image",
            FileName = output.Metadata.IsUnchangedReference ? "VisoLoom-reference.png" : "VisoLoom-render.png"
        };
        dialog.Filters.Add(new FileFilter("PNG image", ".png"));
        if (dialog.ShowDialog(this) != DialogResult.Ok)
            return;
        try
        {
            string target = Path.GetFullPath(dialog.FileName);
            string temporary = Path.Combine(Path.GetDirectoryName(target)!, ".visoloom-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(output.PngBytes);
                    file.Flush(true);
                }

                File.Move(temporary, target, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status.Text = "The image could not be saved. Check the destination and free disk space.";
        }
    }
}
