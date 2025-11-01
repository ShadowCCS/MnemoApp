using System;

namespace MnemoApp.Modules.Notes.Models;

public class NoteData
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "Untitled";
    public Block[] Blocks { get; set; } = Array.Empty<Block>();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string[] Tags { get; set; } = Array.Empty<string>();
}

