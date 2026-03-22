using System;
using System.IO;

namespace Mnemo.Core.Services;

public static class ChatDatasetSettings
{
    public const string LoggingEnabledKey = "Developer.ChatDatasetLogging";

    /// <summary>%LocalAppData%\mnemo\chat_dataset</summary>
    public static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mnemo", "chat_dataset");

    public static string LogFilePath => Path.Combine(LogDirectory, "conversations.jsonl");
}
