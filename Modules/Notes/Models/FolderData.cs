using System;

namespace MnemoApp.Modules.Notes.Models;

public class FolderData
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Folder";
    public string ParentId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

