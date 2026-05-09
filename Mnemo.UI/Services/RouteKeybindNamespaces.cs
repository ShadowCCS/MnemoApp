namespace Mnemo.UI.Services;

/// <summary>Maps navigation route to keybind namespace for local bindings.</summary>
public static class RouteKeybindNamespaces
{
    public static string? ForRoute(string? route)
    {
        if (string.IsNullOrEmpty(route))
            return null;

        return route switch
        {
            "overview" => "overview",
            "notes" => "notes",
            "mindmap" or "mindmap-detail" => "mindmap",
            "flashcards" or "flashcards-detail" => "flashcards",
            "settings" => "settings",
            "chat" => "chat",
            "path" or "path-detail" => "path",
            _ => route
        };
    }
}
