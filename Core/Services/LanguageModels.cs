using System;
using System.Text.Json.Serialization;

namespace MnemoApp.Core.Services
{
    public class LanguageManifest
    {
        public required string Code { get; set; }
        public required string Name { get; set; }
        public string? IconPath { get; set; }
        public string? NativeName { get; set; }
        [JsonPropertyName("flag")]
        public string? Flag { get; set; }
        public bool IsCore { get; set; }
    }
}


