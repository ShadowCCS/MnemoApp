namespace Mnemo.Core.Models;

public abstract record BlockPayload;

public sealed record EmptyPayload : BlockPayload;

public sealed record EquationPayload(string Latex) : BlockPayload;

public sealed record ImagePayload(
    string Path = "",
    string Alt = "",
    double Width = 0,
    string Align = "left") : BlockPayload;

public sealed record CodePayload(string Language, string Source) : BlockPayload;

public sealed record ChecklistPayload(bool Checked) : BlockPayload;

/// <summary>Layout for <see cref="BlockType.TwoColumn"/> — split ratio is owned by the container, not column cells.</summary>
public sealed record TwoColumnPayload(double SplitRatio = 0.5) : BlockPayload;
