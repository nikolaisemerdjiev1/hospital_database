using System.Diagnostics;
using System.Globalization;
using System.Threading.RateLimiting;

using Hospital.Api.Configuration;
using Hospital.Api.Middleware;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hospital.Api.RateLimiting;

internal static partial class ApplicationRateLimitingExtensions
{
    private const string MedicationSearchPath = "/api/v1/medications";
    private const string ReadinessPath = "/health/ready";

    public static IServiceCollection AddApplicationRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetRequiredSection(
            PublicDemoRateLimitOptions.SectionName);
        PublicDemoRateLimitOptions limits = section.Get<PublicDemoRateLimitOptions>()
            ?? throw new InvalidOperationException(
                $"{PublicDemoRateLimitOptions.SectionName} configuration is required.");

        services
            .AddOptions<PublicDemoRateLimitOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        PartitionedRateLimiter<HttpContext> concurrencyLimiter =
            PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetConcurrencyLimiter(
                    ResolvePartitionKey(context),
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = limits.ConcurrentPermitLimit,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    }));

        PartitionedRateLimiter<HttpContext> burstLimiter = CreateFixedWindowLimiter(
            limits.BurstPermitLimit,
            limits.BurstWindowSeconds);
        PartitionedRateLimiter<HttpContext> generalLimiter = CreateFixedWindowLimiter(
            limits.GeneralPermitLimit,
            limits.GeneralWindowSeconds);
        PartitionedRateLimiter<HttpContext> mutationLimiter = CreateConditionalFixedWindowLimiter(
            IsMutation,
            limits.MutationPermitLimit,
            limits.MutationWindowSeconds);
        PartitionedRateLimiter<HttpContext> medicationLimiter = CreateConditionalFixedWindowLimiter(
            static context => context.Request.Path.StartsWithSegments(MedicationSearchPath),
            limits.MedicationSearchPermitLimit,
            limits.MedicationSearchWindowSeconds);
        PartitionedRateLimiter<HttpContext> readinessLimiter = CreateConditionalFixedWindowLimiter(
            static context => context.Request.Path.Equals(ReadinessPath),
            limits.ReadinessPermitLimit,
            limits.ReadinessWindowSeconds);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                concurrencyLimiter,
                burstLimiter,
                generalLimiter,
                mutationLimiter,
                medicationLimiter,
                readinessLimiter);
            options.OnRejected = WriteRejectionAsync;
        });

        return services;
    }

    private static PartitionedRateLimiter<HttpContext> CreateFixedWindowLimiter(
        int permitLimit,
        int windowSeconds) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                ResolvePartitionKey(context),
                _ => CreateFixedWindowOptions(permitLimit, windowSeconds)));

    private static PartitionedRateLimiter<HttpContext> CreateConditionalFixedWindowLimiter(
        Func<HttpContext, bool> applies,
        int permitLimit,
        int windowSeconds) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            string partitionKey = ResolvePartitionKey(context);
            return applies(context)
                ? RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => CreateFixedWindowOptions(permitLimit, windowSeconds))
                : RateLimitPartition.GetNoLimiter(partitionKey);
        });

    private static FixedWindowRateLimiterOptions CreateFixedWindowOptions(
        int permitLimit,
        int windowSeconds) =>
        new()
        {
            AutoReplenishment = true,
            PermitLimit = permitLimit,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            Window = TimeSpan.FromSeconds(windowSeconds),
        };

    private static string ResolvePartitionKey(HttpContext context)
    {
        string clientAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string? subject = context.User.FindFirst("sub")?.Value;

        return string.IsNullOrWhiteSpace(subject)
            ? $"anonymous:{clientAddress}"
            : $"subject:{subject}|address:{clientAddress}";
    }

    private static bool IsMutation(HttpContext context) =>
        !HttpMethods.IsGet(context.Request.Method) &&
        !HttpMethods.IsHead(context.Request.Method) &&
        !HttpMethods.IsOptions(context.Request.Method);

    private static async ValueTask WriteRejectionAsync(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        HttpResponse response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;

        int retryAfterSeconds = 1;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        }

        response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        string traceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        string routeTemplate = RequestRouteContext.GetRouteTemplate(context.HttpContext);
        ILogger logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(ApplicationRateLimitingExtensions));
        LogRequestRejected(
            logger,
            context.HttpContext.Request.Method,
            routeTemplate,
            traceId);

        IProblemDetailsService problemDetailsService = context.HttpContext.RequestServices
            .GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests.",
                Detail = "Please wait before trying again.",
                Type = "https://www.rfc-editor.org/rfc/rfc6585#section-4",
            },
        });
    }

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Rate limit rejected {RequestMethod} {RouteTemplate} with trace {TraceId}")]
    private static partial void LogRequestRejected(
        ILogger logger,
        string requestMethod,
        string routeTemplate,
        string traceId);
}
