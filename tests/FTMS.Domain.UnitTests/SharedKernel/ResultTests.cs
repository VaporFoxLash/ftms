using FTMS.SharedKernel.Constants;
using FTMS.SharedKernel.Results;

namespace FTMS.Domain.UnitTests.SharedKernel;

public class ResultTests
{
    [Fact]
    public void Success_carries_no_error()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Failure_carries_its_error_and_type()
    {
        var result = Result.Failure(Error.Conflict("transaction.illegal_transition", "nope"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("transaction.illegal_transition");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void Reading_the_value_of_a_failed_result_throws()
    {
        var result = Result.Failure<int>(Error.NotFound("x", "y"));

        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void A_value_implicitly_becomes_a_successful_result()
    {
        Result<string> result = "captured";

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("captured");
    }

    [Fact]
    public void ValidationError_reports_itself_as_a_validation_failure()
    {
        var error = new ValidationError(new Dictionary<string, string[]>
        {
            ["amount"] = ["Amount must be positive."],
        });

        error.Type.ShouldBe(ErrorType.Validation);
        error.Failures["amount"].ShouldHaveSingleItem();
    }
}

public class TransactionStatusIdsTests
{
    // design: doc 02 section 4. These GUIDs are seeded data and are referenced by the
    // doc 07 filtered index, so a change here is a data migration. Pin them in a test.
    [Fact]
    public void Seeded_status_ids_are_the_documented_values()
    {
        TransactionStatusIds.Active.ShouldBe(Guid.Parse("a1b2c3d4-0001-4000-8000-000000000001"));
        TransactionStatusIds.Inactive.ShouldBe(Guid.Parse("a1b2c3d4-0002-4000-8000-000000000002"));
        TransactionStatusIds.Pending.ShouldBe(Guid.Parse("a1b2c3d4-0003-4000-8000-000000000003"));
        TransactionStatusIds.Completed.ShouldBe(Guid.Parse("a1b2c3d4-0004-4000-8000-000000000004"));
        TransactionStatusIds.Cancelled.ShouldBe(Guid.Parse("a1b2c3d4-0005-4000-8000-000000000005"));
    }
}
