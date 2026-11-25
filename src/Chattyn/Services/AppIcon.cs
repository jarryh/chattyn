using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Chattyn.Services;

/// <summary>Draws the tray icon at runtime (a gold speech bubble), so no binary .ico is needed in the repo.</summary>
internal static partial class AppIcon
{
    public static Icon Create(int size = 32)
    {
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            var gold = Color.FromArgb(255, 201, 162, 39);
            var dark = Color.FromArgb(255, 27, 29, 35);
            float s = size;

            using var bubble = new GraphicsPath();
            bubble.AddEllipse(s * 0.06f, s * 0.06f, s * 0.88f, s * 0.70f);
            bubble.AddPolygon([new PointF(s * 0.22f, s * 0.62f), new PointF(s * 0.14f, s * 0.96f), new PointF(s * 0.46f, s * 0.72f)]);
            using (var fill = new SolidBrush(gold)) g.FillPath(fill, bubble);

            using var font = new Font("Segoe UI", s * 0.42f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            using var text = new SolidBrush(dark);
            var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("C", font, text, new RectangleF(0, s * 0.03f, s, s * 0.70f), format);
        }

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr hIcon);
}
