using FluentValidation;
using FTMS.Application.Abstractions;
using FTMS.Application.Behaviors;
using FTMS.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace FTMS.Application.UnitTests.Behaviors;

public sealed record SampleCommand(string Name) : ICommand<string>;

public sealed record SampleQuery(string Key) : IQuery<string>, ICachedQuery
{
    public string CacheKey => $"sample:{Key}";

    public TimeSpan Expiration => TimeSpan.FromSeconds(45);
}

public sealed record UncachedQuery : IQuery<string>;

internal sealed class SampleCommandValidator : AbstractValidator<SampleCommand>
{
    public SampleCommandValidator() =>
        RuleFor(command => command.Name).NotEmpty().WithMessage("Name is required.");
}

public class ValidationDecoratorTests
{
    private readonly ICommandHandler<SampleCommand, string> _inner =
        Substitute.For<ICommandHandler<SampleCommand, string>>();

    [Fact]
    public async Task A_valid_command_reaches_the_handler()
    {
        _inner.Handle(Arg.Any<SampleCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success("handled"));

        var decorator = new ValidationDecorator.CommandHandler<SampleCommand, string>(
            _inner, [new SampleCommandValidator()]);

        var result = await decorator.Handle(new SampleCommand("ok"), CancellationToken.None);

        result.Value.ShouldBe("handled");
    }

    [Fact]
    public async Task An_invalid_command_never_reaches_the_handler()
    {
        var decorator = new ValidationDecorator.CommandHandler<SampleCommand, string>(
            _inner, [new SampleCommandValidator()]);

        var result = await decorator.Handle(new SampleCommand(string.Empty), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        await _inner.DidNotReceive().Handle(Arg.Any<SampleCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validation_failures_arrive_as_a_camel_cased_errors_dictionary()
    {
        // design: doc 05 section 1 - Validation maps to 400 with an errors dictionary, and the
        // API speaks camelCase, so the keys must match the field names the client sent.
        var decorator = new ValidationDecorator.CommandHandler<SampleCommand, string>(
            _inner, [new SampleCommandValidator()]);

        var result = await decorator.Handle(new SampleCommand(string.Empty), CancellationToken.None);

        var error = result.Error.ShouldBeOfType<ValidationError>();
        error.Type.ShouldBe(ErrorType.Validation);
        error.Failures.ShouldContainKey("name");
        error.Failures["name"].ShouldContain("Name is required.");
    }

    [Fact]
    public async Task A_message_with_no_validators_passes_straight_through()
    {
        _inner.Handle(Arg.Any<SampleCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success("handled"));

        var decorator = new ValidationDecorator.CommandHandler<SampleCommand, string>(_inner, []);

        (await decorator.Handle(new SampleCommand(string.Empty), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
    }
}

public class CachingDecoratorTests
{
    private readonly ICacheService _cache = Substitute.For<ICacheService>();

    [Fact]
    public async Task A_query_that_does_not_opt_in_bypasses_the_cache_entirely()
    {
        // design: doc 03 section 7 - caching is opt in, which is the safe default for a
        // system of record. Get by id relies on this.
        var inner = Substitute.For<IQueryHandler<UncachedQuery, string>>();
        inner.Handle(Arg.Any<UncachedQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success("fresh"));

        var decorator = new CachingDecorator.QueryHandler<UncachedQuery, string>(
            inner, _cache, NullLogger<CachingDecorator.QueryHandler<UncachedQuery, string>>.Instance);

        var result = await decorator.Handle(new UncachedQuery(), CancellationToken.None);

        result.Value.ShouldBe("fresh");
        await _cache.DidNotReceiveWithAnyArgs().GetOrCreateAsync<object>(default!, default!, default);
    }

    [Fact]
    public async Task An_opted_in_query_goes_through_the_cache_under_its_own_key()
    {
        var inner = Substitute.For<IQueryHandler<SampleQuery, string>>();
        inner.Handle(Arg.Any<SampleQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success("fresh"));

        var realCache = new FakeCache();
        var decorator = new CachingDecorator.QueryHandler<SampleQuery, string>(
            inner, realCache, NullLogger<CachingDecorator.QueryHandler<SampleQuery, string>>.Instance);

        (await decorator.Handle(new SampleQuery("a"), CancellationToken.None)).Value.ShouldBe("fresh");
        (await decorator.Handle(new SampleQuery("a"), CancellationToken.None)).Value.ShouldBe("fresh");

        realCache.KeysRequested.ShouldAllBe(key => key == "sample:a");
        await inner.Received(1).Handle(Arg.Any<SampleQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_query_is_not_cached_and_its_error_still_reaches_the_caller()
    {
        // Caching a failure would turn a transient problem into a sticky one for 45 seconds.
        var inner = Substitute.For<IQueryHandler<SampleQuery, string>>();
        inner.Handle(Arg.Any<SampleQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<string>(Error.NotFound("nope", "nope")));

        var realCache = new FakeCache();
        var decorator = new CachingDecorator.QueryHandler<SampleQuery, string>(
            inner, realCache, NullLogger<CachingDecorator.QueryHandler<SampleQuery, string>>.Instance);

        var result = await decorator.Handle(new SampleQuery("a"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("nope");
        realCache.Stored.ShouldBeEmpty();
    }

    /// <summary>Enough of a cache to observe behaviour, without pulling IMemoryCache in here.</summary>
    private sealed class FakeCache : ICacheService
    {
        private readonly Dictionary<string, object> _entries = [];

        public List<string> KeysRequested { get; } = [];

        public IReadOnlyDictionary<string, object> Stored => _entries;

        public async Task<T?> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T?>> factory,
            TimeSpan expiration,
            CancellationToken cancellationToken = default)
            where T : class
        {
            KeysRequested.Add(key);

            if (_entries.TryGetValue(key, out var existing))
            {
                return (T)existing;
            }

            var created = await factory(cancellationToken);
            if (created is not null)
            {
                _entries[key] = created;
            }

            return created;
        }

        public void Remove(string key) => _entries.Remove(key);

        public void RemoveByPrefix(string prefix)
        {
            foreach (var key in _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                _entries.Remove(key);
            }
        }
    }
}
