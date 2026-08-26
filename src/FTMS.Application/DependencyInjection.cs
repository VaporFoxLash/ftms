using System.Reflection;
using FluentValidation;
using FTMS.Application.Abstractions;
using FTMS.Application.Behaviors;
using FTMS.Application.Dispatching;
using Microsoft.Extensions.DependencyInjection;

namespace FTMS.Application;

/// <summary>
/// The Application layer's contribution to the composition root.
/// design: doc 03 section 3 - handlers registered in DI, cross cutting concerns as
/// decorators registered with Scrutor, nothing hidden behind reflection magic. The only
/// reflection here is one assembly scan at startup, which is what keeps adding a handler a
/// one file change instead of a two file change.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddScoped<IDispatcher, Dispatcher>();

        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // Decorator order matters and is deliberate. Scrutor wraps whatever is currently
        // registered, so the LAST Decorate call ends up outermost. Registering caching first
        // and validation second gives the runtime order:
        //
        //     Logging -> Validation -> Caching -> Handler
        //
        // Validation must sit outside caching: doc 05 says an unknown status is a 400, and a
        // caching decorator on the outside would compute a cache key from unvalidated input
        // before anything had rejected it. design: doc 04 section 5.
        services.Decorate(typeof(IQueryHandler<,>), typeof(CachingDecorator.QueryHandler<,>));

        services.Decorate(typeof(ICommandHandler<,>), typeof(ValidationDecorator.CommandHandler<,>));
        services.Decorate(typeof(IQueryHandler<,>), typeof(ValidationDecorator.QueryHandler<,>));

        services.Decorate(typeof(ICommandHandler<,>), typeof(LoggingDecorator.CommandHandler<,>));
        services.Decorate(typeof(IQueryHandler<,>), typeof(LoggingDecorator.QueryHandler<,>));

        return services;
    }
}
