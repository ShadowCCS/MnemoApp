using System;
using System.Collections.Generic;

namespace Mnemo.Core.Models;

public enum BlockType
{
    Text,
    Heading1,
    Heading2,
    Heading3,
    BulletList,
    NumberedList,
    Checklist,
    Quote,
    Code,
    Divider
}

public class Block
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public BlockType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public Dictionary<string, object> Meta { get; set; } = new();
    public int Order { get; set; }
}
