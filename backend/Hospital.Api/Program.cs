using System.Diagnostics;

using Hospital.Api.Configuration;
using Hospital.Api.ErrorHandling;
using Hospital.Api.Middleware;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();

if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
}
else
{
    builder.Logging.AddJsonConsole();
}

builder.Services
    .AddOptions<FrontendOptions>()
    .BindConfiguration(FrontendOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(
        FrontendOptions.HasCanonicalHttpOrigin,
        "Frontend:Origin must be an absolute HTTP(S) origin without credentials, a path, query, fragment, or trailing slash.")
    .ValidateOnStart();

string frontendOrigin = builder.Configuration
    .GetRequiredSection(FrontendOptions.SectionName)
    .GetValue<string>(nameof(FrontendOptions.Origin))
    ?? throw new InvalidOperationException("Frontend:Origin configuration is required.");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(frontendOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        string traceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Extensions.TryAdd("traceId", traceId);
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRouting();
app.UseMiddleware<RouteTemplateCaptureMiddleware>();
app.UseCors();
app.UseAuthorization();

app.MapOpenApi();
app.MapHealthChecks("/health/live");
app.MapControllers();

app.Run();

public partial class Program;
