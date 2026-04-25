using Mnemo.Core.Models.Flashcards;

namespace Mnemo.Core.Services;

/// <summary>
/// Resolves a scheduler implementation by algorithm.
/// </summary>
public interface IFlashcardSchedulerResolver
{
    IFlashcardScheduler Resolve(FlashcardSchedulingAlgorithm algorithm);
}
