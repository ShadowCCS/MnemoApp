namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

public class CharBox : Box
{
    public string Character { get; }
    public double FontSize { get; }

    public CharBox(string character, double fontSize)
    {
        Character = character;
        FontSize = fontSize;
    }
}