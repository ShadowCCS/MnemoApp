using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Mnemo.UI.Components.BlockEditor;

public static class BlockEditorLogger
{
    [Conditional("DEBUG")]
    public static void Log(string message, [CallerMemberName] string? caller = null)
    {
        Debug.WriteLine($"[{caller}] {message}");
    }

    [Conditional("DEBUG")]
    public static void LogKeyEvent(string key, bool handled, [CallerMemberName] string? caller = null)
    {
        Debug.WriteLine($"[{caller}] Key={key}, Handled={handled}");
    }

    [Conditional("DEBUG")]
    public static void LogTextChanged(string text, string previousText, bool menuVisible, [CallerMemberName] string? caller = null)
    {
        Debug.WriteLine($"[{caller}] Text='{text}', PreviousText='{previousText}', MenuVisible={menuVisible}");
    }

    [Conditional("DEBUG")]
    public static void LogError(string message, System.Exception? ex = null, [CallerMemberName] string? caller = null)
    {
        Debug.WriteLine($"[{caller}] ERROR: {message}");
        if (ex != null)
        {
            Debug.WriteLine($"[{caller}] Exception: {ex}");
        }
    }
}


