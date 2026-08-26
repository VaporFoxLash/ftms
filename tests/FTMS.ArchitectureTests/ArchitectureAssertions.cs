using NetArchTest.Rules;

namespace FTMS.ArchitectureTests;

/// <summary>
/// NetArchTest ships a fluent assertion helper, but only in its FluentAssertions integration
/// package, and FluentAssertions is deliberately absent from FTMS for the same licensing
/// reason as MediatR (doc 08 decision 1). Ten lines on Shouldly cover it.
///
/// Worth more than the package anyway: this names the offending types. A bare
/// "IsSuccessful was false" tells a developer their build broke but not what broke it.
/// </summary>
internal static class ArchitectureAssertions
{
    internal static void ShouldHoldTrue(this TestResult result, string rule)
    {
        if (result.IsSuccessful)
        {
            return;
        }

        var offenders = result.FailingTypeNames ?? [];

        throw new ShouldAssertException(
            $"Architecture rule violated: {rule}"
            + Environment.NewLine
            + "Offending types:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Select(name => $"  - {name}")));
    }
}
