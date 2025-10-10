using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MnemoApp.Core.AI.Models;
using MnemoApp.Core.AI.Services;
using MnemoApp.Core.Tasks.Models;
using MnemoApp.Data.Runtime;

namespace MnemoApp.Core.Tasks;

public class GenerateUnitTask : MnemoTaskBase
{
    private readonly IAIService _aiService;
    private readonly IRuntimeStorage _storage;
    private readonly IModelSelectionService? _modelSelectionService;
    private readonly string _pathId;
    private readonly int _unitOrder;

    public GenerateUnitTask(
        IAIService aiService,
        IRuntimeStorage storage,
        string pathId,
        int unitOrder,
        IModelSelectionService? modelSelectionService = null)
        : base(
            $"Generating Unit {unitOrder}",
            $"Generating content for unit {unitOrder}",
            TaskPriority.Normal,
            TaskExecutionMode.Exclusive)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _pathId = pathId ?? throw new ArgumentNullException(nameof(pathId));
        _unitOrder = unitOrder;
        _modelSelectionService = modelSelectionService;
    }

    public override TimeSpan? EstimatedDuration => TimeSpan.FromMinutes(1);

    protected override async Task<TaskResult> ExecuteTaskAsync(IProgress<TaskProgress> progress, CancellationToken cancellationToken)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[GENERATE_UNIT] Starting generation for unit {_unitOrder} in path {_pathId}");
            
            progress.Report(new TaskProgress(0.1, $"Loading path data..."));

            // Load path data
            var pathKey = $"Content/Paths/{_pathId}";
            var pathData = _storage.GetProperty<PathData>(pathKey);

            if (pathData == null)
            {
                return new TaskResult(false, ErrorMessage: "Path not found");
            }

            if (pathData.Units == null)
            {
                return new TaskResult(false, ErrorMessage: "Path has no units");
            }

            // Find the unit
            var unit = pathData.Units.FirstOrDefault(u => u.Order == _unitOrder);
            if (unit == null)
            {
                return new TaskResult(false, ErrorMessage: $"Unit {_unitOrder} not found");
            }

            // Check if unit already has content
            if (!string.IsNullOrWhiteSpace(unit.Content))
            {
                System.Diagnostics.Debug.WriteLine($"[GENERATE_UNIT] Unit {_unitOrder} already has content, skipping");
                return new TaskResult(true, unit);
            }

            progress.Report(new TaskProgress(0.3, $"Generating content..."));

            // Resolve model name
            string? effectiveModelName = await ResolveModelNameAsync();
            if (string.IsNullOrWhiteSpace(effectiveModelName))
            {
                return new TaskResult(false, ErrorMessage: "No AI model selected or available");
            }

            // Generate content
            var contentPrompt = BuildContentPrompt(unit.Notes ?? "");
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

            unit.Content = contentResponse.Response;
            System.Diagnostics.Debug.WriteLine($"[GENERATE_UNIT] Unit {_unitOrder} content generated ({contentResponse.Response.Length} chars)");

            progress.Report(new TaskProgress(0.8, "Saving unit content..."));

            // Save updated path data
            _storage.SetProperty(pathKey, pathData);

            progress.Report(new TaskProgress(1.0, $"Unit {_unitOrder} generated successfully"));

            return new TaskResult(true, unit);
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[GENERATE_UNIT] Task cancelled for unit {_unitOrder}");
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GENERATE_UNIT] Task failed: {ex.Message}");
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

# [Module Title]

[Start with an engaging paragraph that introduces the topic naturally.]
Explain what this lesson or section covers and why it's worth learning. You can begin with a short scenario, question, or everyday example that motivates curiosity.  


## Understanding the Concept

Begin with a **clear explanation** of the main idea.  
Use full sentences and narrative flow — not just definitions. Imagine you're talking to an intelligent learner encountering the topic for the first time.

> **Example:** Instead of saying ""Momentum is mass times velocity,"" you might write:  
> ""Momentum describes how much motion an object carries — it depends on both how heavy it is and how fast it's moving.""

When useful, include a **short formula** or relationship:
$$
p = m \times v
$$

You can follow it with a quick intuitive interpretation or short real-world example.


## Digging Deeper

Explore **key principles, mechanisms, or relationships** that define the topic.  
Use natural transitions between ideas rather than subheadings for every detail. When comparisons or lists help, use simple formatting:

- Concept A — [brief explanation]
- Concept B — [brief explanation]

When math or structure matters, present it cleanly and explain what each part means.

> 💡 **Tip:** Blend examples directly into the explanation rather than isolating them in long sections.


## Example in Action

Walk through one **well-chosen example** that shows how the concept is applied.  
Explain your reasoning step by step in prose form — like a teacher thinking aloud — and include small equations or tables only when they clarify meaning.

**Example:**  
Let's say we want to find the force needed to accelerate a 2 kg object at 3 m/s².  
Using $F = ma$,  
a 6-newton force would be required.

Keep tone conversational yet precise.


## How It Connects and Why It Matters

After understanding the mechanics, discuss **real-world significance or interdisciplinary links**.  
Show how this topic appears in science, technology, everyday life, or even history.  
Use one or two short paragraphs to make the learner see the bigger picture.

You can include a simple comparison or visual table when relevant:

| Application | Field | Example |
|--------------|--------|----------|
| Energy | Physics | Kinetic energy of a moving car |
| Economics | Finance | Momentum investing trend |


## Quick Recap

End with a **short, narrative summary** — two or three sentences reminding the learner of the essence of the lesson.  
Optionally include:
- Key formula(s)
- One-sentence ""why it matters""

> **In short:** This section helped us understand [main idea], why it's useful, and how to apply it in real situations.

## Reflect & Practice (Optional)

Offer 1–3 thoughtful questions or short prompts for reflection.  
Keep them open-ended to encourage thinking rather than rote memorization.

**Try this:**
- How would you apply this concept in a different context?
- What assumptions does this idea rely on?
- Can you find a counterexample or limitation?



Input Notes:
{unitNotes}";
    }
}