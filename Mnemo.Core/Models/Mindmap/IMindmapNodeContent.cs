using System.Text.Json.Serialization;

namespace Mnemo.Core.Models.Mindmap;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TextNodeContent), "text")]
public interface IMindmapNodeContent
{
}
