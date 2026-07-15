using Hospital.Core.Profiles;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Hospital.Api.Authentication;

internal static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        IConfigurationSection auth0Section = configuration.GetRequiredSection(
            Auth0Options.SectionName);
        Auth0Options auth0 = auth0Section.Get<Auth0Options>()
            ?? throw new InvalidOperationException(
                $"{Auth0Options.SectionName} configuration is required.");

        services
            .AddOptions<Auth0Options>()
            .Bind(auth0Section)
            .ValidateDataAnnotations()
            .Validate(
                Auth0Options.HasCanonicalDomain,
                "Authentication:Auth0:Domain must be a canonical DNS host name without a scheme, port, path, or whitespace.")
            .Validate(
                Auth0Options.HasCanonicalAudience,
                "Authentication:Auth0:Audience must be an absolute HTTPS resource identifier without credentials, a query, fragment, or whitespace.")
            .Validate(
                Auth0Options.HasCanonicalRoleClaim,
                "Authentication:Auth0:RoleClaim must be an absolute HTTPS claim URI without credentials, a query, fragment, or whitespace.")
            .ValidateOnStart();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = auth0.Authority;
                options.Audience = auth0.Audience;
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.SaveToken = false;
                options.EventsType = typeof(ApiJwtBearerEvents);
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    IgnoreTrailingSlashWhenValidatingAudience = false,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = "name",
                    RoleClaimType = auth0.RoleClaim,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                };
            });

        services.AddScoped<ApiJwtBearerEvents>();
        services.AddScoped<ILocalUserResolver, LocalUserResolver>();
        services.AddScoped<IAuthorizationHandler, LocalProfileAuthorizationHandler>();

        AuthorizationPolicy fallbackPolicy =
            new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new LocalProfileRequirement())
                .Build();

        services
            .AddAuthorizationBuilder()
            .SetFallbackPolicy(fallbackPolicy)
            .AddPolicy(
                AuthorizationPolicies.ProfileResolved,
                policy => AddLocalProfileRequirement(policy, requiredProfileType: null))
            .AddPolicy(
                AuthorizationPolicies.Patient,
                policy => AddLocalProfileRequirement(policy, ProfileType.Patient))
            .AddPolicy(
                AuthorizationPolicies.Doctor,
                policy => AddLocalProfileRequirement(policy, ProfileType.Doctor))
            .AddPolicy(
                AuthorizationPolicies.Pharmacist,
                policy => AddLocalProfileRequirement(policy, ProfileType.Pharmacist))
            .AddPolicy(
                AuthorizationPolicies.Administrator,
                policy => AddLocalProfileRequirement(policy, ProfileType.Administrator));

        return services;
    }

    private static void AddLocalProfileRequirement(
        AuthorizationPolicyBuilder policy,
        ProfileType? requiredProfileType)
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new LocalProfileRequirement(requiredProfileType));
    }
}
