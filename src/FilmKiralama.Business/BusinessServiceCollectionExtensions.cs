using FilmKiralama.Business.Auditing;
using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Validation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FilmKiralama.Business;

public static class BusinessServiceCollectionExtensions
{
    /// <summary>
    /// Registers the business services. Repositories must be registered separately; without an
    /// <see cref="IAuditLog"/> the audit events are discarded.
    /// </summary>
    public static IServiceCollection AddBusinessServices(this IServiceCollection services, RentalPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var rentalPolicy = policy ?? new RentalPolicy();
        if (rentalPolicy.Validate().Count > 0)
        {
            throw new ArgumentException("The rental settings are invalid.", nameof(policy));
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPasswordHasher>(_ => new PasswordHasher());
        services.TryAddSingleton<IAuditLog>(NullAuditLog.Instance);
        services.TryAddSingleton(SetupKeyThrottle.Default);
        services.TryAddSingleton(_ => SetupKeyEnvironment.FromProcess());
        services.TryAddSingleton(rentalPolicy);
        services.AddSingleton<IValidator<SignInInput>, SignInInputValidator>();
        services.AddSingleton<IValidator<SignUpInput>, SignUpInputValidator>();
        services.AddSingleton<IValidator<PasswordChangeInput>, PasswordChangeInputValidator>();
        services.AddSingleton<IValidator<FirstAdminInput>, FirstAdminInputValidator>();
        services.AddSingleton<IValidator<NewAccountInput>, NewAccountInputValidator>();
        services.AddSingleton<IValidator<AccountChangeInput>, AccountChangeInputValidator>();
        services.AddSingleton<IValidator<FilmFields>, FilmFieldsValidator>();
        services.AddSingleton<IValidator<RentalQuoteInput>, RentalQuoteInputValidator>();
        services.AddSingleton<SetupKeyGate>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<ISetupService, SetupService>();
        services.AddSingleton<IFilmService, FilmService>();
        services.AddSingleton<IRentalService, RentalService>();
        services.AddSingleton<IAccountService, AccountService>();
        return services;
    }
}
