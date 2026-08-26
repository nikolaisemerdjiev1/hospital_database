using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using Hospital.Core.Application;
using Hospital.Core.Medications;
using Hospital.Infrastructure;
using Hospital.Infrastructure.Medications;
using Hospital.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hospital.Api.IntegrationTests;

[Collection(AuthenticationDatabaseTestGroup.Name)]
public sealed class RxNormMedicationCatalogTests(AuthenticationDatabaseFixture database)
{
    [Fact]
    public async Task LiveSearchFiltersProductsPersistsThemAndThenUsesMemoryCache()
    {
        string rxCui = CreateRxCui();
        RecordingHttpMessageHandler handler = new((_, _) => JsonResponse(new
        {
            drugGroup = new
            {
                conceptGroup = new object[]
                {
                    new
                    {
                        tty = "SCD",
                        conceptProperties = new[]
                        {
                            new
                            {
                                rxcui = rxCui,
                                name = "Codexicillin 10 MG Oral Tablet",
                                synonym = (string?)"Codexicillin tablet",
                                psn = (string?)"Codexicillin 10 MG Oral Tablet",
                                tty = "SCD",
                                suppress = "N",
                            },
                            new
                            {
                                rxcui = rxCui,
                                name = "Duplicate product",
                                synonym = (string?)null,
                                psn = (string?)null,
                                tty = "SCD",
                                suppress = "N",
                            },
                        },
                    },
                    new
                    {
                        tty = "IN",
                        conceptProperties = new[]
                        {
                            new
                            {
                                rxcui = CreateRxCui(),
                                name = "Ignored ingredient",
                                synonym = (string?)null,
                                psn = (string?)null,
                                tty = "IN",
                                suppress = "N",
                            },
                        },
                    },
                },
            },
        }));

        await using ApplicationDbContext context = database.CreateContext();
        using IMemoryCache cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 20 });
        RxNormMedicationCatalog catalog = CreateCatalog(context, handler, cache);

        ApplicationResult<MedicationCatalogSearch> live = await catalog.SearchAsync(
            "codexicillin",
            10);
        ApplicationResult<MedicationCatalogSearch> cached = await catalog.SearchAsync(
            "codexicillin",
            10);

        Assert.True(live.IsSuccess);
        Assert.Equal(MedicationCatalogStatus.Live, live.Value!.CatalogStatus);
        MedicationCatalogItem item = Assert.Single(live.Value.Items);
        Assert.Equal(rxCui, item.RxCui);
        Assert.Equal("Codexicillin 10 MG Oral Tablet", item.DisplayName);
        Assert.Equal("SCD", item.ConceptType);
        Assert.Equal("RxNorm", item.Source);
        Assert.Equal(MedicationCatalogStatus.Cached, cached.Value!.CatalogStatus);
        Assert.Equal(1, handler.RequestCount);

        context.ChangeTracker.Clear();
        Medication persisted = await context.Medications
            .AsNoTracking()
            .SingleAsync(medication => medication.RxCui == rxCui);
        Assert.Equal(MedicationSource.RxNorm, persisted.Source);
        Assert.Equal(AuthTestClock.UtcNow, persisted.LastVerifiedAtUtc);
    }

    [Fact]
    public async Task EncodesTheQueryAndRetriesTransientResponses()
    {
        string rxCui = CreateRxCui();
        RecordingHttpMessageHandler handler = new((requestNumber, _) =>
        {
            if (requestNumber < 3)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            return JsonResponse(CreateSingleProductPayload(
                rxCui,
                "Iron Citrate 25 MG Oral Tablet"));
        });
        RxNormOptions options = CreateOptions();

        ServiceCollection services = new();
        services.AddSingleton<IOptions<RxNormOptions>>(Options.Create(options));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(AuthTestClock.UtcNow));
        services.AddInfrastructure(database.ConnectionString, options);
        services.AddHttpClient<RxNormClient>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IMedicationCatalog catalog = scope.ServiceProvider
            .GetRequiredService<IMedicationCatalog>();

        ApplicationResult<MedicationCatalogSearch> result = await catalog.SearchAsync(
            "iron citrate",
            10);

        Assert.True(result.IsSuccess);
        Assert.Equal(MedicationCatalogStatus.Live, result.Value!.CatalogStatus);
        Assert.Equal(3, handler.RequestCount);
        Assert.Contains("name=iron%20citrate", handler.LastRequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DependencyFailureUsesMatchingSeededFallback()
    {
        string query = $"Fallback{Guid.NewGuid():N}";
        await using ApplicationDbContext context = database.CreateContext();
        context.Medications.Add(new Medication
        {
            RxCui = CreateRxCui(),
            DisplayName = $"{query} 5 MG Tablet",
            Strength = "5 mg",
            DoseForm = "Oral tablet",
            Classification = "Demo",
            Source = MedicationSource.SeededFallback,
            CreatedAtUtc = AuthTestClock.UtcNow,
        });
        await context.SaveChangesAsync();

        RecordingHttpMessageHandler handler = new((_, _) =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using IMemoryCache cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 20 });
        RxNormMedicationCatalog catalog = CreateCatalog(context, handler, cache);

        ApplicationResult<MedicationCatalogSearch> result = await catalog.SearchAsync(query, 10);

        Assert.True(result.IsSuccess);
        Assert.Equal(MedicationCatalogStatus.Fallback, result.Value!.CatalogStatus);
        Assert.Single(result.Value.Items);
        Assert.Equal("SeededFallback", result.Value.Items[0].Source);
    }

    [Fact]
    public async Task SuccessfulEmptyLiveSearchDoesNotReturnFallbackData()
    {
        string query = $"NoLiveMatch{Guid.NewGuid():N}";
        await using ApplicationDbContext context = database.CreateContext();
        context.Medications.Add(new Medication
        {
            RxCui = CreateRxCui(),
            DisplayName = query,
            Source = MedicationSource.SeededFallback,
            CreatedAtUtc = AuthTestClock.UtcNow,
        });
        await context.SaveChangesAsync();

        RecordingHttpMessageHandler handler = new((_, _) => JsonResponse(new
        {
            drugGroup = new
            {
                conceptGroup = Array.Empty<object>(),
            },
        }));
        using IMemoryCache cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 20 });
        RxNormMedicationCatalog catalog = CreateCatalog(context, handler, cache);

        ApplicationResult<MedicationCatalogSearch> result = await catalog.SearchAsync(query, 10);

        Assert.True(result.IsSuccess);
        Assert.Equal(MedicationCatalogStatus.Live, result.Value!.CatalogStatus);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task DependencyFailureWithoutLocalMatchesReturnsUnavailable()
    {
        await using ApplicationDbContext context = database.CreateContext();
        RecordingHttpMessageHandler handler = new((_, _) =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using IMemoryCache cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 20 });
        RxNormMedicationCatalog catalog = CreateCatalog(context, handler, cache);

        ApplicationResult<MedicationCatalogSearch> result = await catalog.SearchAsync(
            $"Absent{Guid.NewGuid():N}",
            10);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailure.DependencyUnavailable, result.Failure);
        Assert.Equal("medication_catalog_unavailable", result.ErrorCode);
    }

    [Fact]
    public async Task ConcurrentCacheMissesUpsertOneMedicationWithoutFailingEitherRequest()
    {
        string rxCui = CreateRxCui();
        string query = $"Concurrent{Guid.NewGuid():N}";
        object payload = CreateSingleProductPayload(
            rxCui,
            $"{query} 10 MG Oral Tablet");
        await using ApplicationDbContext firstContext = database.CreateContext();
        await using ApplicationDbContext secondContext = database.CreateContext();
        using IMemoryCache firstCache = new MemoryCache(
            new MemoryCacheOptions { SizeLimit = 20 });
        using IMemoryCache secondCache = new MemoryCache(
            new MemoryCacheOptions { SizeLimit = 20 });
        RxNormMedicationCatalog firstCatalog = CreateCatalog(
            firstContext,
            new RecordingHttpMessageHandler((_, _) => JsonResponse(payload)),
            firstCache);
        RxNormMedicationCatalog secondCatalog = CreateCatalog(
            secondContext,
            new RecordingHttpMessageHandler((_, _) => JsonResponse(payload)),
            secondCache);

        ApplicationResult<MedicationCatalogSearch>[] results = await Task.WhenAll(
            firstCatalog.SearchAsync(query, 10),
            secondCatalog.SearchAsync(query, 10));

        Assert.All(results, result =>
        {
            Assert.True(result.IsSuccess);
            Assert.Equal(MedicationCatalogStatus.Live, result.Value!.CatalogStatus);
            Assert.Equal(rxCui, Assert.Single(result.Value.Items).RxCui);
        });
        await using ApplicationDbContext verificationContext = database.CreateContext();
        Assert.Equal(
            1,
            await verificationContext.Medications.CountAsync(
                medication => medication.RxCui == rxCui));
    }

    private static RxNormMedicationCatalog CreateCatalog(
        ApplicationDbContext context,
        HttpMessageHandler handler,
        IMemoryCache cache)
    {
        HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://rxnav.test/REST/"),
        };
        RxNormClient client = new(httpClient, NullLogger<RxNormClient>.Instance);
        return new RxNormMedicationCatalog(
            context,
            client,
            cache,
            Options.Create(CreateOptions()),
            new FixedTimeProvider(AuthTestClock.UtcNow));
    }

    private static RxNormOptions CreateOptions() => new()
    {
        BaseAddress = "https://rxnav.test/REST/",
        CacheTtlHours = 12,
        RetryAttempts = 2,
        AttemptTimeoutSeconds = 3,
        TotalRequestTimeoutSeconds = 10,
    };

    private static object CreateSingleProductPayload(string rxCui, string displayName) => new
    {
        drugGroup = new
        {
            conceptGroup = new[]
            {
                new
                {
                    tty = "SCD",
                    conceptProperties = new[]
                    {
                        new
                        {
                            rxcui = rxCui,
                            name = displayName,
                            synonym = (string?)null,
                            psn = displayName,
                            tty = "SCD",
                            suppress = "N",
                        },
                    },
                },
            },
        },
    };

    private static HttpResponseMessage JsonResponse(object payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"),
    };

    private static string CreateRxCui() =>
        Random.Shared
            .NextInt64(1_000_000_000, 9_999_999_999)
            .ToString(CultureInfo.InvariantCulture);

    private sealed class RecordingHttpMessageHandler(
        Func<int, HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            return Task.FromResult(responseFactory(RequestCount, request));
        }
    }

}
