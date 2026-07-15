using System.Collections.Concurrent;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Hospital.Api.IntegrationTests;

public sealed class HospitalApiFactory : WebApplicationFactory<Program>
{
    public TestLogSink LogSink { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Authentication:Auth0:Audience", "https://hospital-coordination-api-test");
        builder.UseSetting("Authentication:Auth0:Domain", "auth.test.local");
        builder.UseSetting(
            "Authentication:Auth0:RoleClaim",
            "https://hospital.test/claims/app_role");
        builder.UseSetting("Frontend:Origin", "http://localhost:5173");
        builder.UseSetting(
            "ConnectionStrings:HospitalDatabase",
            "Host=127.0.0.1;Port=1;Database=hospital_unavailable;Username=test;Password=test;Timeout=1;Command Timeout=1;Pooling=false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILoggerProvider>();
            services.AddSingleton<ILoggerProvider>(LogSink);
            services.AddControllers().AddApplicationPart(typeof(TestExceptionController).Assembly);
        });
    }
}

public sealed class TestLogSink : ILoggerProvider
{
    private readonly ConcurrentQueue<TestLogEntry> entries = new();

    public IReadOnlyCollection<TestLogEntry> Entries => entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, entries);

    public void Clear()
    {
        while (entries.TryDequeue(out _))
        {
        }
    }

    public void Dispose()
    {
    }

    private sealed class TestLogger(
        string categoryName,
        ConcurrentQueue<TestLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue(new TestLogEntry(
                categoryName,
                eventId,
                logLevel,
                formatter(state, exception)));
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static NoopScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

public sealed record TestLogEntry(
    string CategoryName,
    EventId EventId,
    LogLevel LogLevel,
    string Message);
