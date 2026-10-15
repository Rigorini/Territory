using System.Globalization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Localization;
using Microsoft.Net.Http.Headers;
using Territory.Api;
using Territory.Api.Cli;
using Territory.Api.Endpoints;
using Territory.Api.Logging;
using Territory.Api.Security;
using Territory.Infrastructure;

if (args.Length > 0 && args[0] == HashPasswordCommand.Name)
{
    return HashPasswordCommand.Run(Console.Out, Console.Error, HashPasswordCommand.ReadConsoleSecret);
}

if (args.Length > 0 && PersonalDataCommands.IsCommand(args[0]))
{
    var cli = Host.CreateApplicationBuilder(args);
    return await PersonalDataCommands.RunAsync(
        args, cli.Configuration, cli.Environment.ContentRootPath, Console.Out, Console.Error, CancellationToken.None);
}

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o =>
{
    o.AddServerHeader = false;
    o.Limits.MaxRequestBodySize = 1 * 1024 * 1024;
});

builder.AddTerritoryLogging();

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddTerritoryAuth();
builder.Services.AddProblemDetails();
builder.Services.AddTerritoryRateLimiting();
builder.Services.AddLocalization();
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString(DependencyInjection.ConnectionStringName)!, tags: ["ready"]);
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = true;
});

var app = builder.Build();

app.UseMiddleware<RequestLogContextMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("it") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
        ctx.Context.Response.Headers[HeaderNames.CacheControl] = StaticFileCachePolicy.For(ctx.File.Name)
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

// Secure by default: every API endpoint requires an admin session unless explicitly anonymous.
var api = app.MapGroup("/api")
    .RequireAuthorization()
    .AddEndpointFilter<AntiforgeryValidationFilter>()
    .AddEndpointFilter<PasswordChangeRequiredFilter>();
api.MapGet("/ping", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
api.MapAuthEndpoints();
api.MapSetupEndpoints();
api.MapAdminEndpoints();
api.MapRegistryEndpoints();
api.MapAssignmentEndpoints();
api.MapExportEndpoints();
api.MapBackupEndpoints();
api.Map("{**path}", () => Results.NotFound()).AllowAnonymous();
app.MapPublicEndpoints();

app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers[HeaderNames.CacheControl] = StaticFileCachePolicy.Revalidate
});

await app.RunAsync();
return 0;

public partial class Program;
