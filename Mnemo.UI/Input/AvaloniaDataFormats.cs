using System.Reflection;
using Avalonia.Input;

namespace Mnemo.UI.Input;

/// <summary>
/// Builds <see cref="DataFormat{T}"/> for in-process drag-and-drop. Avalonia only exposes
/// <see cref="DataFormat.CreateStringApplicationFormat(string)"/> as <see cref="DataFormat{String}"/>;
/// arbitrary reference payloads require the internal <c>CreateApplicationFormat&lt;T&gt;</c> factory.
/// </summary>
internal static class AvaloniaDataFormats
{
    private static readonly MethodInfo CreateApplicationFormatOpen =
        typeof(DataFormat).GetMethod(
            "CreateApplicationFormat",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(string)],
            modifiers: null)
        ?? throw new MissingMethodException(typeof(DataFormat).FullName, "CreateApplicationFormat");

    public static DataFormat<T> CreateApplicationFormat<T>(string identifier)
        where T : class
    {
        var m = CreateApplicationFormatOpen.MakeGenericMethod(typeof(T));
        return (DataFormat<T>)m.Invoke(null, [identifier])!;
    }
}
