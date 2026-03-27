using System;
using System.Collections.ObjectModel;
using Mnemo.Core.Models;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Services;

/// <summary>
/// Maps pipeline status keys into a linear process thread for the assistant bubble.
/// </summary>
public sealed class ChatProcessThreadTracker
{
    private readonly ObservableCollection<ChatProcessStepViewModel> _steps;

    public ChatProcessThreadTracker(ObservableCollection<ChatProcessStepViewModel> steps) =>
        _steps = steps;

    public void OnPipelineKey(string key, Func<string, string> localize)
    {
        if (string.IsNullOrEmpty(key))
            return;

        if (ChatPipelineStatusKeys.TryParseRunningTool(key, out var toolName))
        {
            Advance(
                localize("PipelineStatusRunningTool"),
                ChatProcessPhaseKind.Tool,
                toolName);
            return;
        }

        if (IsRoutingKey(key))
        {
            BumpRouting(localize);
            return;
        }

        if (key == ChatPipelineStatusKeys.PreparingModel)
        {
            if (LastIsActive(ChatProcessPhaseKind.Model))
                return;
            Advance(localize(ChatPipelineStatusKeys.PreparingModel), ChatProcessPhaseKind.Model);
            return;
        }

        if (key == ChatPipelineStatusKeys.Generating)
        {
            if (LastIsActive(ChatProcessPhaseKind.Generating))
                return;
            Advance(localize(ChatPipelineStatusKeys.Generating), ChatProcessPhaseKind.Generating);
            return;
        }

        if (key == ChatPipelineStatusKeys.ContinuingAfterTool)
        {
            Advance(localize(ChatPipelineStatusKeys.ContinuingAfterTool), ChatProcessPhaseKind.Continuing);
            return;
        }
    }

    /// <summary>Marks all steps complete (call when the assistant turn ends).</summary>
    public void CompleteThread()
    {
        foreach (var s in _steps)
        {
            s.IsActive = false;
            s.IsComplete = true;
        }
    }

    private static bool IsRoutingKey(string key) =>
        key == ChatPipelineStatusKeys.LoadingSkills
        || key == ChatPipelineStatusKeys.Classifying
        || key == ChatPipelineStatusKeys.Routing
        || key == ChatPipelineStatusKeys.ReadingSkill;

    private void BumpRouting(Func<string, string> localize)
    {
        var label = localize(ChatPipelineStatusKeys.RoutingCombined);
        if (_steps.Count > 0)
        {
            var last = _steps[^1];
            if (last.PhaseKind == ChatProcessPhaseKind.Routing && last.IsActive)
            {
                last.Label = label;
                return;
            }
        }

        CompleteActive();
        _steps.Add(new ChatProcessStepViewModel
        {
            Label = label,
            PhaseKind = ChatProcessPhaseKind.Routing,
            IsActive = true,
            IsComplete = false
        });
    }

    private void Advance(string label, ChatProcessPhaseKind kind, string? detail = null)
    {
        CompleteActive();
        _steps.Add(new ChatProcessStepViewModel
        {
            Label = label,
            Detail = detail,
            PhaseKind = kind,
            IsActive = true,
            IsComplete = false
        });
    }

    private bool LastIsActive(ChatProcessPhaseKind kind) =>
        _steps.Count > 0 && _steps[^1].PhaseKind == kind && _steps[^1].IsActive;

    private void CompleteActive()
    {
        foreach (var s in _steps)
        {
            if (!s.IsActive)
                continue;
            s.IsActive = false;
            s.IsComplete = true;
        }
    }
}
