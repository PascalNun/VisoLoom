using Eto.Drawing;
using Eto.Forms;

namespace VisoLoom.Plugin;

internal sealed class WrappingLabel : Label
{
    public WrappingLabel()
    {
        SizeChanged += (_, _) => FitHeight();
        Load += (_, _) => FitHeight();
    }

    // Native Label setters do not consistently raise TextChanged on every host.
    public new string Text
    {
        get => base.Text;
        set
        {
            if (base.Text == value)
                return;
            base.Text = value;
            FitHeight();
        }
    }

    private void FitHeight()
    {
        if (Size.Width < 80)
            return;
        using var text = new FormattedText
        {
            Text = Text,
            Font = Font ?? SystemFonts.Default(),
            MaximumWidth = Math.Max(1, Size.Width - 4),
            Wrap = FormattedTextWrapMode.Word
        };
        int needed = Math.Max(1, (int)Math.Ceiling(text.Measure().Height) + 4);
        if (Height != needed)
            Height = needed;
    }

    protected override void OnApplyCascadingStyles()
    {
        base.OnApplyCascadingStyles();
        TextAlignment = TextAlignment.Left;
        Wrap = WrapMode.Word;
        FitHeight();
    }
}
