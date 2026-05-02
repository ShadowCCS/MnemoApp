using System.Text.Json;
using Mnemo.Core.Enums;
using Mnemo.Core.Models;
using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.Flashcards;

namespace Mnemo.Infrastructure.Tests.Flashcards;

public sealed class FlashcardDeckServiceTests
{
    [Fact]
    public async Task InMemory_RecordSessionOutcome_UpdatesDueDateForReview()
    {
        var service = new InMemoryFlashcardDeckService(CreateSchedulerResolver());
        var deck = await SeedSingleCardDeckAsync(service);
        var card = deck.Cards[0];
        var initialDue = card.DueDate;
        var now = DateTimeOffset.UtcNow;

        var result = new FlashcardSessionResult(
            deck.Id,
            new FlashcardSessionConfig(FlashcardSessionType.Review, deck.Id, null, null, false, null),
            now.AddMinutes(-5),
            now,
            new[] { new FlashcardSessionCardResult(card.Id, FlashcardReviewGrade.Good, now) });

        await service.RecordSessionOutcomeAsync(result);
        var updated = await service.GetDeckByIdAsync(deck.Id);

        Assert.NotNull(updated);
        var updatedCard = Assert.Single(updated!.Cards, c => c.Id == card.Id);
        Assert.True(updatedCard.DueDate > initialDue);
    }

    [Fact]
    public async Task InMemory_RecordSessionOutcome_DoesNotMutateScheduleForQuickPractice()
    {
        var service = new InMemoryFlashcardDeckService(CreateSchedulerResolver());
        var deck = await SeedSingleCardDeckAsync(service);
        var card = deck.Cards[0];
        var initialDue = card.DueDate;
        var now = DateTimeOffset.UtcNow;

        var result = new FlashcardSessionResult(
            deck.Id,
            new FlashcardSessionConfig(FlashcardSessionType.Quick, deck.Id, null, null, false, null),
            now.AddMinutes(-5),
            now,
            new[] { new FlashcardSessionCardResult(card.Id, FlashcardReviewGrade.Good, now) });

        await service.RecordSessionOutcomeAsync(result);
        var updated = await service.GetDeckByIdAsync(deck.Id);

        Assert.NotNull(updated);
        var updatedCard = Assert.Single(updated!.Cards, c => c.Id == card.Id);
        Assert.Equal(initialDue, updatedCard.DueDate);
    }

    [Fact]
    public async Task InMemory_RecordSessionOutcome_DoesNotMutateScheduleForCram()
    {
        var service = new InMemoryFlashcardDeckService(CreateSchedulerResolver());
        var deck = await SeedSingleCardDeckAsync(service);
        var card = deck.Cards[0];
        var initialDue = card.DueDate;
        var now = DateTimeOffset.UtcNow;

        var result = new FlashcardSessionResult(
            deck.Id,
            new FlashcardSessionConfig(FlashcardSessionType.Cram, deck.Id, null, null, true, null),
            now.AddMinutes(-5),
            now,
            new[] { new FlashcardSessionCardResult(card.Id, FlashcardReviewGrade.Easy, now) });

        await service.RecordSessionOutcomeAsync(result);
        var updated = await service.GetDeckByIdAsync(deck.Id);
        Assert.NotNull(updated);
        var updatedCard = Assert.Single(updated!.Cards, c => c.Id == card.Id);
        Assert.Equal(initialDue, updatedCard.DueDate);
    }

    [Fact]
    public async Task PersistentDeckService_PersistsStateAcrossInstances()
    {
        var storage = new InMemoryStorageProvider();
        var logger = new NullLoggerService();

        var schedulerResolver = CreateSchedulerResolver();
        var serviceA = new PersistentFlashcardDeckService(storage, logger, schedulerResolver);
        var deck = await SeedSingleCardDeckAsync(serviceA);
        var now = DateTimeOffset.UtcNow;
        var result = new FlashcardSessionResult(
            deck.Id,
            new FlashcardSessionConfig(FlashcardSessionType.Review, deck.Id, null, null, false, null),
            now.AddMinutes(-3),
            now,
            new[] { new FlashcardSessionCardResult(deck.Cards[0].Id, FlashcardReviewGrade.Good, now) });
        await serviceA.RecordSessionOutcomeAsync(result);

        var serviceB = new PersistentFlashcardDeckService(storage, logger, schedulerResolver);
        var reloadedDeck = await serviceB.GetDeckByIdAsync(deck.Id);
        Assert.NotNull(reloadedDeck);
        Assert.True(reloadedDeck!.RetentionScore >= 0);
        var trend = await serviceB.GetDeckRetentionTrendAsync(deck.Id, 14);
        Assert.Equal(14, trend.Count);
        Assert.Contains(trend, t => t.ReviewsCount > 0);
    }

    private sealed class InMemoryStorageProvider : IStorageProvider
    {
        private readonly Dictionary<string, string> _storage = new(StringComparer.Ordinal);

        public Task<Result> SaveAsync<T>(string key, T data)
        {
            _storage[key] = JsonSerializer.Serialize(data);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<T?>> LoadAsync<T>(string key)
        {
            if (!_storage.TryGetValue(key, out var value))
                return Task.FromResult(Result<T?>.Failure("not found"));

            var deserialized = JsonSerializer.Deserialize<T>(value);
            return Task.FromResult(Result<T?>.Success(deserialized));
        }

        public Task<Result> DeleteAsync(string key)
        {
            _storage.Remove(key);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class NullLoggerService : ILoggerService
    {
        public void Log(LogLevel level, string category, string message, Exception? exception = null) { }
    }

    private static async Task<FlashcardDeck> SeedSingleCardDeckAsync(IFlashcardDeckService service)
    {
        var now = DateTimeOffset.UtcNow;
        const string deckId = "test-deck";
        const string cardId = "test-card";
        var deck = new FlashcardDeck(
            deckId,
            "Test",
            null,
            null,
            Array.Empty<string>(),
            null,
            0,
            new[]
            {
                new Flashcard(
                    cardId,
                    deckId,
                    "Q",
                    "A",
                    FlashcardType.Classic,
                    Array.Empty<string>(),
                    now,
                    null,
                    null,
                    null)
            },
            FlashcardSchedulingAlgorithm.Fsrs);
        await service.SaveDeckAsync(deck);
        return deck;
    }

    private static IFlashcardSchedulerResolver CreateSchedulerResolver()
    {
        var fsrs = new FsrsFlashcardScheduler(FlashcardFsrsParameters.Default);
        var schedulers = new IFlashcardScheduler[]
        {
            fsrs,
            new Sm2FlashcardScheduler(),
            new LeitnerFlashcardScheduler(),
            new BaselineFlashcardScheduler()
        };
        return new FlashcardSchedulerResolver(schedulers);
    }
}
