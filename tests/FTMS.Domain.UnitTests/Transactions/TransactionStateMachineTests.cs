using FTMS.Domain.Transactions;
using FTMS.SharedKernel.Results;

namespace FTMS.Domain.UnitTests.Transactions;

/// <summary>
/// The executable specification of the doc 02 section 5 state machine.
/// design: doc 08 decision 2 - every pair of statuses appears here declaring whether the
/// transition is legal, so this file reads as the specification. When a sixth status
/// arrives, the compiler and this matrix force a decision for every new pair.
/// </summary>
public class TransactionStateMachineTests
{
    // from, to, isLegal
    //
    // Active and Pending are the working states: they move freely between each other and
    // into both outcomes, and either can be archived.
    [Theory]
    [InlineData("Active", "Active", false)]
    [InlineData("Active", "Pending", true)]
    [InlineData("Active", "Completed", true)]
    [InlineData("Active", "Cancelled", true)]
    [InlineData("Active", "Inactive", true)]
    [InlineData("Pending", "Active", true)]
    [InlineData("Pending", "Pending", false)]
    [InlineData("Pending", "Completed", true)]
    [InlineData("Pending", "Cancelled", true)]
    [InlineData("Pending", "Inactive", true)]
    // Completed and Cancelled are terminal business outcomes. The only remaining act is to archive.
    [InlineData("Completed", "Active", false)]
    [InlineData("Completed", "Pending", false)]
    [InlineData("Completed", "Completed", false)]
    [InlineData("Completed", "Cancelled", false)]
    [InlineData("Completed", "Inactive", true)]
    [InlineData("Cancelled", "Active", false)]
    [InlineData("Cancelled", "Pending", false)]
    [InlineData("Cancelled", "Completed", false)]
    [InlineData("Cancelled", "Cancelled", false)]
    [InlineData("Cancelled", "Inactive", true)]
    // Inactive is the end of the road. Nothing comes back without a deliberate future
    // restore feature. Inactive to Inactive is not an edge in the diagram, but Deactivate()
    // succeeds there anyway as a no op, which is what makes DELETE idempotent (doc 05
    // section 7). That single exception is asserted separately below.
    [InlineData("Inactive", "Active", false)]
    [InlineData("Inactive", "Pending", false)]
    [InlineData("Inactive", "Completed", false)]
    [InlineData("Inactive", "Cancelled", false)]
    [InlineData("Inactive", "Inactive", false)]
    public void The_transition_table_matches_the_documented_state_machine(string from, string to, bool isLegal)
    {
        var source = StatusNamed(from);
        var target = StatusNamed(to);

        source.CanTransitionTo(target).ShouldBe(
            isLegal,
            $"doc 02 section 5 says {from} to {to} is {(isLegal ? "legal" : "illegal")}.");
    }

    [Theory]
    [InlineData("Active", "Pending", true)]
    [InlineData("Active", "Completed", true)]
    [InlineData("Active", "Cancelled", true)]
    [InlineData("Active", "Inactive", true)]
    [InlineData("Pending", "Active", true)]
    [InlineData("Pending", "Completed", true)]
    [InlineData("Pending", "Cancelled", true)]
    [InlineData("Pending", "Inactive", true)]
    [InlineData("Completed", "Active", false)]
    [InlineData("Completed", "Pending", false)]
    [InlineData("Completed", "Cancelled", false)]
    [InlineData("Completed", "Inactive", true)]
    [InlineData("Cancelled", "Active", false)]
    [InlineData("Cancelled", "Pending", false)]
    [InlineData("Cancelled", "Completed", false)]
    [InlineData("Cancelled", "Inactive", true)]
    [InlineData("Inactive", "Active", false)]
    [InlineData("Inactive", "Pending", false)]
    [InlineData("Inactive", "Completed", false)]
    [InlineData("Inactive", "Cancelled", false)]
    public void The_aggregate_enforces_the_same_table_its_methods_claim(string from, string to, bool expectSuccess)
    {
        var transaction = TransactionIn(StatusNamed(from));

        var result = AttemptTransitionTo(transaction, StatusNamed(to));

        result.IsSuccess.ShouldBe(expectSuccess);

        if (expectSuccess)
        {
            transaction.Status.Name.ShouldBe(to);
        }
        else
        {
            transaction.Status.Name.ShouldBe(from, "an illegal transition must leave the record untouched.");
            result.Error.Type.ShouldBe(ErrorType.Conflict, "doc 05 maps illegal transitions to 409.");
            result.Error.Code.ShouldBe("transaction.illegal_transition");
        }
    }

    [Fact]
    public void Deactivate_on_an_already_inactive_transaction_succeeds_as_a_no_op()
    {
        // design: doc 05 section 7 - deleting an already Inactive transaction returns 204
        // rather than an error, which is what clients and retry logic want from DELETE.
        var transaction = TransactionIn(TransactionStatus.Inactive);
        var modifiedBefore = transaction.ModifiedAtUtc;

        var result = transaction.Deactivate();

        result.IsSuccess.ShouldBeTrue();
        transaction.Status.ShouldBe(TransactionStatus.Inactive);
        transaction.ModifiedAtUtc.ShouldBe(modifiedBefore, "a no op must not look like a change to the audit trail.");
    }

    [Fact]
    public void Deactivate_succeeds_from_every_status()
    {
        foreach (var status in TransactionStatus.List)
        {
            var transaction = TransactionIn(status);

            transaction.Deactivate().IsSuccess.ShouldBeTrue($"DELETE must work on a {status.Name} transaction.");
            transaction.Status.ShouldBe(TransactionStatus.Inactive);
        }
    }

    [Fact]
    public void Every_successful_transition_raises_exactly_one_domain_event()
    {
        var transaction = TransactionIn(TransactionStatus.Active);
        transaction.ClearDomainEvents();

        transaction.Hold().IsSuccess.ShouldBeTrue();

        transaction.DomainEvents.Count.ShouldBe(1);
    }

    [Fact]
    public void A_refused_transition_raises_no_domain_event()
    {
        var transaction = TransactionIn(TransactionStatus.Cancelled);
        transaction.ClearDomainEvents();

        transaction.Complete().IsFailure.ShouldBeTrue();

        transaction.DomainEvents.ShouldBeEmpty();
    }

    internal static TransactionStatus StatusNamed(string name) =>
        TransactionStatus.TryFromName(name, out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(name), name, "Not a seeded status.");

    /// <summary>Builds a transaction sitting in the requested status by walking legal transitions.</summary>
    internal static Transaction TransactionIn(TransactionStatus status)
    {
        var transaction = TransactionBuilder.Active().Build();

        if (status == TransactionStatus.Active)
        {
            return transaction;
        }

        var result = AttemptTransitionTo(transaction, status);
        result.IsSuccess.ShouldBeTrue($"the arrangement step into {status.Name} must itself be legal.");

        return transaction;
    }

    /// <summary>Maps a target status onto the aggregate method that targets it.</summary>
    private static Result AttemptTransitionTo(Transaction transaction, TransactionStatus target) =>
        target.Name switch
        {
            nameof(TransactionStatus.Active) => transaction.Release(),
            nameof(TransactionStatus.Pending) => transaction.Hold(),
            nameof(TransactionStatus.Completed) => transaction.Complete(),
            nameof(TransactionStatus.Cancelled) => transaction.Cancel(),
            nameof(TransactionStatus.Inactive) => transaction.Deactivate(),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
}
