public static class IconManager
{
    public static string GetIconPath(string name, bool filled = false)
    {
        var folder = filled ? "filled" : "outline";
        return $"/Assets/Icons/Tabler/{folder}/{name}.svg";
    }
}
