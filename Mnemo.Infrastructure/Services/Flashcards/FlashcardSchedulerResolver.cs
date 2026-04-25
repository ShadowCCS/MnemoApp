using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Flashcards;

public sealed class FlashcardSchedulerResolver : IFlashcardSchedulerResolver
{
    private readonly IReadOnlyDictionary<FlashcardSchedulingAlgorithm, IFlashcardScheduler> _byAlgorithm;

    public FlashcardSchedulerResolver(IEnumerable<IFlashcardScheduler> schedulers)
    {
        _byAlgorithm = schedulers.ToDictionary(s => s.Algorithm, s => s);
    }

    public IFlashcardScheduler Resolve(FlashcardSchedulingAlgorithm algorithm)
    {
        if (_byAlgorithm.TryGetValue(algorithm, out var scheduler))
            return scheduler;

        if (_byAlgorithm.TryGetValue(FlashcardSchedulingAlgorithm.Fsrs, out var fsrs))
            return fsrs;

        throw new InvalidOperationException("No flashcard scheduler is registered.");
    }
}
