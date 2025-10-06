using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MnemoApp.Core.AI.Services;
using MnemoApp.Core.AI.Models;
using MnemoApp.Core.Tasks.Models;
using MnemoApp.Data.Runtime;

namespace MnemoApp.Core.Tasks
{
    /// <summary>
    /// Task for creating a structured learning path from raw notes
    /// </summary>
    public class CreateLearningPathTask : MnemoTaskBase
    {
        private readonly IAIService _aiService;
        private readonly IModelSelectionService? _modelSelectionService;
        private readonly IRuntimeStorage _storage;
        private readonly string _notes;

        public CreateLearningPathTask(
            IAIService aiService, 
            IRuntimeStorage storage,
            string notes,
            IModelSelectionService? modelSelectionService = null)
            : base("Learning Path", "Generating based on your notes", TaskPriority.High, TaskExecutionMode.Exclusive, usingAI: true)
        {
            _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _notes = notes ?? throw new ArgumentNullException(nameof(notes));
            _modelSelectionService = modelSelectionService;
        }

        public override TimeSpan? EstimatedDuration => TimeSpan.FromMinutes(3);

        protected override async Task<TaskResult> ExecuteTaskAsync(IProgress<TaskProgress> progress, CancellationToken cancellationToken)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[CREATE_PATH] Starting learning path creation");
                
                // Step 1: Create the unit structure (path object)
                progress.Report(new TaskProgress(0.1, "Analyzing notes and creating structure..."));
                
                string? effectiveModelName = await ResolveModelNameAsync();
                if (string.IsNullOrWhiteSpace(effectiveModelName))
                {
                    return new TaskResult(false, ErrorMessage: "No AI model selected or available");
                }

                var structurePrompt = BuildStructurePrompt(_notes);
                var structureRequest = new AIInferenceRequest
                {
                    ModelName = effectiveModelName,
                    Prompt = structurePrompt,
                    MaxTokens = 2000
                };

                System.Diagnostics.Debug.WriteLine("[CREATE_PATH] Requesting path structure generation");
                var structureResponse = await _aiService.InferAsync(structureRequest, cancellationToken);
                
                if (!structureResponse.Success || string.IsNullOrWhiteSpace(structureResponse.Response))
                {
                    return new TaskResult(false, ErrorMessage: $"Failed to generate path structure: {structureResponse.ErrorMessage}");
                }

                System.Diagnostics.Debug.WriteLine($"[CREATE_PATH] Structure generated: {structureResponse.Response?.Substring(0, Math.Min(100, structureResponse.Response?.Length ?? 0))}...");

                // Clean and parse the JSON response
                PathStructure? pathStructure;
                try
                {
                    var jsonResponse = CleanJsonResponse(structureResponse.Response ?? "");
                    System.Diagnostics.Debug.WriteLine($"[CREATE_PATH] Cleaned JSON length: {jsonResponse.Length}");
                    System.Diagnostics.Debug.WriteLine($"[CREATE_PATH] Cleaned JSON: {jsonResponse.Substring(0, Math.Min(200, jsonResponse.Length))}...");
                    
                    // Validate JSON structure before parsing
                    if (!jsonResponse.Trim().StartsWith("{") || !jsonResponse.Trim().EndsWith("}"))
                    {
                        System.Diagnostics.Debug.WriteLine($"[CREATE_PATH] JSON doesn't start/end with braces");
                        return new TaskResult(false, ErrorMessage: "AI response is not valid JSON format");
                    }
                    
                    pathStructure = JsonSerializer.Deserialize<PathStructure>(jsonResponse, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true,
                        AllowTrailingCommas = true,
                        NumberHandling = JsonNumberHandling.AllowReadingFromString
                    });
                    
                    if (pathStructure == null || pathStructure.LearningPath == null || pathStructure.LearningPath.Length == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CREATE_PATH] Invalid structure - Title: {pathStructure?.Title ?? "null"}, Units: {pathStructure?.LearningPath?.Length ?? 0}");
                        return new TaskResult(false, ErrorMessage: "Invalid path structure returned from AI");
                    }

                    // Set a default title if none provided
                    if (string.IsNullOrWhiteSpace(pathStructure.Title))
                    {
                        pathStructure.Title = "Learning Path";
                        System.Diagnostics.Debug.WriteLine($"[CREATE_PATH] Using default title: {pathStructure.Title}");
                    }
                }
                catch (JsonException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CREATE_PATH] JSON parsing failed: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[CREATE_PATH] Error path: {ex.Path}");
                    System.Diagnostics.Debug.WriteLine($"[CREATE_PATH] Error line: {ex.LineNumber}, position: {ex.BytePositionInLine}");
                    System.Diagnostics.Debug.WriteLine($"[CREATE_PATH] Raw response length: {structureResponse.Response?.Length}");
                    return new TaskResult(false, ErrorMessage: $"Failed to parse AI response: {ex.Message}");
                }

                progress.Report(new TaskProgress(0.4, $"Created {pathStructure.LearningPath.Length} units. Generating first unit content..."));

                // Step 2: Generate content for the first unit
                var firstUnit = pathStructure.LearningPath[0];
                System.Diagnostics.Debug.WriteLine($"[CREATE_PATH] Generating content for first unit: {firstUnit.Title}");

                var contentPrompt = BuildContentPrompt(firstUnit.Notes ?? "");
                var contentRequest = new AIInferenceRequest
                {
                    ModelName = effectiveModelName,
                    Prompt = contentPrompt,
                    MaxTokens = 3000
                };

                progress.Report(new TaskProgress(0.5, "Generating educational content..."));
                var contentResponse = await _aiService.InferAsync(contentRequest, cancellationToken);
                
                if (!contentResponse.Success || string.IsNullOrWhiteSpace(contentResponse.Response))
                {
                    return new TaskResult(false, ErrorMessage: $"Failed to generate unit content: {contentResponse.ErrorMessage}");
                }

                firstUnit.Content = contentResponse.Response;
                System.Diagnostics.Debug.WriteLine($"[CREATE_PATH] First unit content generated ({contentResponse.Response.Length} chars)");

                progress.Report(new TaskProgress(0.8, "Saving learning path to database..."));

                // Step 3: Store in SQLite database
                var pathId = Guid.NewGuid().ToString();
                var pathData = new PathData
                {
                    Id = pathId,
                    Title = pathStructure.Title ?? "Untitled Path",
                    CreatedAt = DateTime.UtcNow,
                    Units = pathStructure.LearningPath
                };

                await StorePathAsync(pathData);
                System.Diagnostics.Debug.WriteLine($"[CREATE_PATH] Path stored with ID: {pathId}");

                progress.Report(new TaskProgress(1.0, "Learning path created successfully"));

                return new TaskResult(true, pathData);
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("[CREATE_PATH] Task cancelled");
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CREATE_PATH] Task failed: {ex.Message}");
                return new TaskResult(false, ErrorMessage: ex.Message);
            }
        }

        private async Task<string?> ResolveModelNameAsync()
        {
            var selectedModel = _modelSelectionService?.SelectedModel;
            
            if (string.IsNullOrWhiteSpace(selectedModel))
            {
                var names = await _aiService.GetAllNamesAsync();
                if (names.Count > 0)
                {
                    selectedModel = names[0];
                }
            }

            return selectedModel;
        }

        private string CleanJsonResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return "{}";

            // Remove any leading/trailing whitespace
            response = response.Trim();

            System.Diagnostics.Debug.WriteLine($"[CREATE_PATH] Raw AI response: '{response}'");
            System.Diagnostics.Debug.WriteLine($"[CREATE_PATH] Response length: {response.Length}");

            // Check if response contains template-like text instead of JSON
            if (response.Contains("{system_prompt}") || response.Contains("{notes}") || response.Contains("{title}"))
            {
                System.Diagnostics.Debug.WriteLine($"[CREATE_PATH] AI returned template text instead of JSON: {response}");
                return "{}";
            }

            // Find the first { and last } to extract just the JSON
            var firstBrace = response.IndexOf('{');
            var lastBrace = response.LastIndexOf('}');

            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                response = response.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            // Additional cleaning: remove any trailing characters that might cause issues
            response = response.Trim();

            // If there are any characters after the last }, remove them
            var lastBraceIndex = response.LastIndexOf('}');
            if (lastBraceIndex >= 0 && lastBraceIndex < response.Length - 1)
            {
                response = response.Substring(0, lastBraceIndex + 1);
            }

            System.Diagnostics.Debug.WriteLine($"[CREATE_PATH] Final cleaned JSON: '{response}'");
            return response;
        }

        private string BuildStructurePrompt(string notes)
        {
            return $@"You are a JSON API that organizes study material into a structured learning path.

IMPORTANT: You must respond with ONLY a valid JSON object. No explanations, no markdown, no code blocks, no additional text.

JSON Schema Required:
{{
  ""title"": ""string - Main topic title (required)"",
  ""learning_path"": [
    {{
      ""order"": ""integer - sequence number"",
      ""title"": ""string - descriptive unit title"",
      ""notes"": ""string - subset of original notes""
    }}
  ]
}}

Rules:
- Create 3-7 logical units from the notes
- Each unit must have order, title, and notes fields
- Output exactly matching JSON structure above
- The ""title"" field should be a descriptive main topic title derived from the notes
- No text before or after the JSON object

Notes to process:
{notes}

Respond with JSON only:";
        }

        private string BuildContentPrompt(string unitNotes)
        {
            return $@"You are an expert educational content generator. Given a notes input, your task is to create a structured Markdown document that follows the tone, layout, and style of a professional learning path module, similar to a textbook or course section.

Content Style Guide:

Use Markdown for structure and formatting (#, ##, lists, tables, etc.).

Use LaTeX syntax (inside $$ ... $$ or $ ... $) for mathematical formulas.

Keep a clear, educational tone.

Explain concepts logically, starting from motivation → theory → examples → applications → summary.

Integrate visual or tabular aids where appropriate (e.g., example tables, structured comparisons).

Ensure each output is self-contained, educational, and well-formatted.

Output Format Template:

# [Concept Title]

## Learning Objectives
- [Objective 1]
- [Objective 2]
- [Objective 3]

## 1. Overview / Motivation
[Explain why this concept matters and where it's used.]

## 2. Core Concepts / Theory

### 2.1 Statement of the Principle
[Provide the main formula or relationship in LaTeX.]
Example:  
$$a^2 + b^2 = c^2$$

### 2.2 Explanation and Visualization
[Describe the meaning behind the formula, intuitive visualization, and conceptual depth.]

### 2.3 Applications and Examples
[Show how it's applied. Include at least one numeric example.]
Example:  
If $a=3$ and $b=4$, then:  
$$3^2 + 4^2 = 25 = 5^2$$

| Example | Side a | Side b | Side c |
|----------|---------|---------|---------|
| 1 | 3 | 4 | 5 |
| 2 | 5 | 12 | 13 |

### 2.4 Limitations and Extensions
[Explain any constraints and how the concept generalizes to other cases.]

## 3. Summary & Key Takeaways
- [Key Point 1]
- [Key Point 2]
- [Key Point 3]

Input Notes:
{unitNotes}";
        }

        private async Task StorePathAsync(PathData pathData)
        {
            // Store in unified Content table
            var pathKey = $"Content/Paths/{pathData.Id}";
            _storage.SetProperty(pathKey, pathData);

            // Maintain a global list of all path IDs
            var listKey = "Content/Paths/list";
            var existing = _storage.GetProperty<string[]>(listKey) ?? Array.Empty<string>();
            if (Array.IndexOf(existing, pathData.Id) < 0)
            {
                var updated = new string[existing.Length + 1];
                Array.Copy(existing, updated, existing.Length);
                updated[existing.Length] = pathData.Id;
                _storage.SetProperty(listKey, updated);
            }

            await Task.CompletedTask;
        }
    }

    #region Data Models

    public class PathStructure
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("learning_path")] public UnitStructure[]? LearningPath { get; set; }
    }

    public class UnitStructure
    {
        [JsonPropertyName("order")] public int Order { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("notes")] public string? Notes { get; set; }
        public string? Content { get; set; }
    }

    public class PathData
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public UnitStructure[]? Units { get; set; }
    }

    #endregion
}
