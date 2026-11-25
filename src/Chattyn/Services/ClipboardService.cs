using System.Runtime.InteropServices;
using System.Windows;

namespace Chattyn.Services;

public static class ClipboardService
{
    /// <summary>Copies text, retrying briefly because another app (or WoW) may hold the clipboard open.</summary>
    public static bool TrySetText(string text)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(30);
            }
            catch (ExternalException)
            {
                Thread.Sleep(30);
            }
        }
        return false;
    }
}
