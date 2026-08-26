using System.Reflection;
using FTMS.Application.Abstractions;
using FTMS.Domain.Transactions;
using FTMS.Infrastructure.Persistence;
using FTMS.SharedKernel.Primitives;
using NetArchTest.Rules;

namespace FTMS.ArchitectureTests;

/// <summary>
/// The doc 03 layering as executable law.
///
/// design: doc 03 decision 6 and doc 08 decision 3 - these rules fail the build, not a code
/// review comment three months too late. Clean Architecture is a dependency rule before it is
/// anything else, and a dependency rule nothing enforces is a diagram.
/// </summary>
public class LayeringTests
{
    private static readonly Assembly SharedKernel = typeof(Entity).Assembly;
    private static readonly Assembly Domain = typeof(Transaction).Assembly;
    private static readonly Assembly Application = typeof(IDispatcher).Assembly;
    private static readonly Assembly Infrastructure = typeof(FtmsDbContext).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private const string SharedKernelNamespace = "FTMS.SharedKernel";
    private const string DomainNamespace = "FTMS.Domain";
    private const string ApplicationNamespace = "FTMS.Application";
    private const string InfrastructureNamespace = "FTMS.Infrastructure";
    private const string ApiNamespace = "FTMS.Api";

    [Fact]
    public void SharedKernel_depends_on_nothing_of_ours()
    {
        // It is the innermost ring. Everything depends on it, so churn here ripples
        // everywhere. design: doc 03 section 8.
        Types.InAssembly(SharedKernel)
            .Should()
            .NotHaveDependencyOnAny(DomainNamespace, ApplicationNamespace, InfrastructureNamespace, ApiNamespace)
            .GetResult()
            .ShouldHoldTrue("SharedKernel must not depend on any other FTMS layer");
    }

    [Fact]
    public void Domain_references_nothing_but_SharedKernel()
    {
        // design: doc 08 section 2 - the domain has zero infrastructure dependencies, which is
        // exactly what makes its tests run in microseconds.
        Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOnAny(ApplicationNamespace, InfrastructureNamespace, ApiNamespace)
            .GetResult()
            .ShouldHoldTrue("Domain must reference nothing but SharedKernel");
    }

    [Fact]
    public void Domain_knows_nothing_about_databases_http_or_caches()
    {
        Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Microsoft.Extensions.Caching",
                "System.Data",
                "Microsoft.Data.SqlClient")
            .GetResult()
            .ShouldHoldTrue("Domain must not know about databases, HTTP or caches");
    }

    [Fact]
    public void Application_never_references_Infrastructure()
    {
        // The Application layer defines what it needs; Infrastructure supplies it. An arrow
        // pointing the other way would make the whole structure decorative.
        Types.InAssembly(Application)
            .Should()
            .NotHaveDependencyOnAny(InfrastructureNamespace, ApiNamespace)
            .GetResult()
            .ShouldHoldTrue("Application must never reference Infrastructure or Api");
    }

    [Fact]
    public void Application_never_touches_EF_Core()
    {
        // Persistence is a detail. The moment a handler knows about DbContext, swapping the
        // read side for Dapper stops being a one class change (doc 03 section 5).
        Types.InAssembly(Application)
            .Should()
            .NotHaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.Data.SqlClient")
            .GetResult()
            .ShouldHoldTrue("Application must never touch EF Core");
    }

    [Fact]
    public void Application_never_touches_AspNetCore()
    {
        // Handlers return Result, not IActionResult. Only the API layer speaks HTTP.
        Types.InAssembly(Application)
            .Should()
            .NotHaveDependencyOn("Microsoft.AspNetCore")
            .GetResult()
            .ShouldHoldTrue("Application must never touch ASP.NET Core");
    }

    [Fact]
    public void Controllers_never_reference_the_DbContext()
    {
        // design: doc 08 section 2 - controllers never touch the DbContext. They build a
        // message, dispatch it, and translate the Result.
        Types.InAssembly(Api)
            .That()
            .HaveNameEndingWith("Controller")
            .Should()
            .NotHaveDependencyOnAny(
                typeof(FtmsDbContext).FullName!,
                "Microsoft.EntityFrameworkCore")
            .GetResult()
            .ShouldHoldTrue("Controllers must never reference the DbContext");
    }

    [Fact]
    public void Controllers_never_reference_a_domain_entity()
    {
        // If a controller can hold a Transaction, it can call a domain method, and business
        // logic starts leaking into the presentation layer one convenience at a time.
        Types.InAssembly(Api)
            .That()
            .HaveNameEndingWith("Controller")
            .Should()
            .NotHaveDependencyOn(typeof(Transaction).FullName!)
            .GetResult()
            .ShouldHoldTrue("Controllers must never reference a domain entity");
    }

    [Fact]
    public void No_handler_returns_an_entity_type_to_the_API_layer()
    {
        // design: doc 08 section 2. Handlers project into DTOs; leaking an aggregate would
        // hand the API layer a live domain object with mutating methods on it.
        var offenders = Application
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.GetInterfaces().Any(IsHandlerInterface))
            .SelectMany(type => type.GetInterfaces().Where(IsHandlerInterface))
            .Select(handlerInterface => new
            {
                Handler = handlerInterface,
                Response = handlerInterface.GetGenericArguments()[1],
            })
            .Where(pair => LeaksAnEntity(pair.Response))
            .Select(pair => $"{pair.Handler.Name} returns {pair.Response.Name}")
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Infrastructure_is_the_only_layer_that_knows_SQL_Server_exists()
    {
        foreach (var assembly in new[] { SharedKernel, Domain, Application })
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("Microsoft.Data.SqlClient")
                .GetResult()
                .ShouldHoldTrue("Only Infrastructure may know SQL Server exists");
        }

        Types.InAssembly(Infrastructure).GetTypes().ShouldNotBeEmpty();
    }

    [Fact]
    public void The_solution_carries_none_of_the_dependencies_the_design_ruled_out()
    {
        // design: doc 03 decision 1, doc 08 decision 1, and the consolidated register.
        // MediatR and FluentAssertions are commercially licensed; Dapper and Redis have
        // prepared seams and written triggers rather than premature implementations.
        string[] forbidden = ["MediatR", "FluentAssertions", "AutoMapper", "Dapper", "StackExchange.Redis"];

        var referenced = new[] { SharedKernel, Domain, Application, Infrastructure, Api }
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var package in forbidden)
        {
            referenced.ShouldNotContain(
                package,
                StringComparer.OrdinalIgnoreCase,
                $"{package} is deliberately absent from FTMS. See the consolidated decision register.");
        }
    }

    private static bool IsHandlerInterface(Type type) =>
        type.IsGenericType
        && (type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
            || type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>));

    /// <summary>
    /// True when the response type is, or wraps, something deriving from Entity. Unwraps one
    /// level of collection or envelope generic so PagedResult&lt;Transaction&gt; is caught too.
    /// </summary>
    private static bool LeaksAnEntity(Type response)
    {
        if (typeof(Entity).IsAssignableFrom(response))
        {
            return true;
        }

        return response.IsGenericType
            && response.GetGenericArguments().Any(argument => typeof(Entity).IsAssignableFrom(argument));
    }
}
