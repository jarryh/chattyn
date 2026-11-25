using WinForms = System.Windows.Forms;

namespace Chattyn.Services;

/// <summary>Notification-area icon with Show / Settings / Quit.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _showItem;

    public TrayIcon(Action onShow, Action onSettings, Action onQuit)
    {
        _showItem = new WinForms.ToolStripMenuItem("Show", null, (_, _) => onShow());
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(_showItem);
        menu.Items.Add(new WinForms.ToolStripMenuItem("Settings...", null, (_, _) => onSettings()));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(new WinForms.ToolStripMenuItem("Quit Chattyn", null, (_, _) => onQuit()));

        _icon = new WinForms.NotifyIcon
        {
            Icon = AppIcon.Create(),
            Text = "Chattyn",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) onShow();
        };
    }

    public void SetShowHotkey(string hotkey) => _showItem.Text = $"Show ({hotkey})";

    public void ShowBalloon(string title, string text, bool isError = false) =>
        _icon.ShowBalloonTip(5000, title, text, isError ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }
}
