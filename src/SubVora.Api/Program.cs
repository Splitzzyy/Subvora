using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using SubVora.Api;
using SubVora.Application.Alerts;
using SubVora.Application.Devices;
using SubVora.Application.Auth;
using SubVora.Application.Categories;
using SubVora.Application.Currency;
using SubVora.Application.Dashboard;
using SubVora.Application.Matching;
using SubVora.Application.PaymentSources;
using SubVora.Application.Subscriptions;
using SubVora.Application.Users;
using SubVora.Infrastructure.Ai;
using SubVora.Infrastructure.Alerts;
using SubVora.Infrastructure.Auth;
using SubVora.Infrastructure.Currency;
using SubVora.Infrastructure.Data;
using SubVora.Infrastructure.Repositories;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logs to console, provider-agnostic (no APM vendor chosen yet - just makes
// logs parseable by whatever log aggregator ends up watching stdout). Levels mirror the
// previous default Logging:LogLevel values (Default: Information, Microsoft.AspNetCore: Warning).
builder.Host.UseSerilog((_, cfg) => cfg
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter()));

// Add services to the container.

// Connection string is resolved lazily (per-scope, from IConfiguration) rather than read
// once at startup, so WebApplicationFactory-based tests can override it after this file runs.
builder.Services.AddScoped(sp =>
{
    var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
    return new AppDbContext(AppDbContextOptionsFactory.Build(connectionString));
});

builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IValidator<RegisterRequest>, RegisterRequestValidator>();
builder.Services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();

builder.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
builder.Services.AddScoped<IValidator<CreateSubscriptionRequest>, CreateSubscriptionRequestValidator>();

builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<IValidator<CreateCategoryRequest>, CreateCategoryRequestValidator>();

builder.Services.AddScoped<IPaymentSourceRepository, PaymentSourceRepository>();
builder.Services.AddScoped<IValidator<CreatePaymentSourceRequest>, CreatePaymentSourceRequestValidator>();

builder.Services.AddScoped<IDeviceTokenRepository, DeviceTokenRepository>();
builder.Services.AddScoped<IValidator<RegisterDeviceTokenRequest>, RegisterDeviceTokenRequestValidator>();

builder.Services.AddScoped<ISubscriptionCatalogSearchRepository, SubscriptionCatalogSearchRepository>();
builder.Services.AddScoped<ISubscriptionMatchService, SubscriptionMatchService>();
builder.Services.AddScoped<IValidator<ResolveSubscriptionRequest>, ResolveSubscriptionRequestValidator>();
builder.Services.AddHttpClient<IEmbeddingClient, OpenAiEmbeddingClient>((sp, client) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var apiKey = configuration["OpenAI:ApiKey"]
        ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
    client.BaseAddress = new Uri("https://api.openai.com/v1/");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
});

// Scoped, not singleton - depends on IFxRateService, which holds a scoped DbContext.
builder.Services.AddScoped<IBurnRateCalculator, BurnRateCalculator>();
builder.Services.AddScoped<IUserRepository, UserRepository>();

builder.Services.AddScoped<IFxRateService, FxRateService>();
builder.Services.AddHttpClient<IExchangeRateClient, ExchangeRateHostClient>(client =>
{
    client.BaseAddress = new Uri("https://api.exchangerate.host/");
});
builder.Services.AddHostedService<FxRateRefreshBackgroundService>();

builder.Services.AddSingleton<IRenewalAlertScanner, RenewalAlertScanner>();
builder.Services.AddHostedService<RenewalAlertBackgroundService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Same lazy-resolution reasoning as the DbContext registration above.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, configuration) =>
    {
        var jwtSecret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
        var jwtIssuer = configuration["Jwt:Issuer"] ?? "SubVora";
        var jwtAudience = configuration["Jwt:Audience"] ?? "SubVora";

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

// Bounds OpenAI cost exposure on the AI-backed resolve endpoint only - not applied globally.
// Limit/window are configurable so tests can use a small window instead of waiting on the
// real one; defaults to 30 requests/minute per authenticated user in the absence of config.
builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return ValueTask.CompletedTask;
    };

    options.AddPolicy("ai-resolve", httpContext =>
    {
        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var permitLimit = configuration.GetValue("RateLimiting:AiResolve:PermitLimit", 30);
        var windowSeconds = configuration.GetValue("RateLimiting:AiResolve:WindowSeconds", 60);
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueLimit = 0,
        });
    });

    // Pre-auth endpoints (register/login/refresh) have no user claim yet, so this partitions
    // on caller IP instead - guards against credential-stuffing/brute-force.
    options.AddPolicy("auth", httpContext =>
    {
        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var permitLimit = configuration.GetValue("RateLimiting:Auth:PermitLimit", 10);
        var windowSeconds = configuration.GetValue("RateLimiting:Auth:WindowSeconds", 60);
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(ipAddress, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueLimit = 0,
        });
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddHealthChecks()
    .AddNpgSql(sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured."));

var app = builder.Build();

// Configure the HTTP request pipeline.
// "Docker" is the compose-local environment (see docker-compose.yml / appsettings.Docker.json) -
// same dev convenience as Development, just against the containerized db service instead of localhost.
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Docker"))
{
    // Dev convenience only - production migrations run as an explicit deploy step
    // (see .github/workflows/db-migrate.yml), never on app startup.
    using (var scope = app.Services.CreateScope())
    {
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }

    app.MapOpenApi();

    // Swagger UI reads the spec .NET's native OpenAPI generator already produces above -
    // no second (Swashbuckle) generator, one source of truth for the document itself.
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "SubVora API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseExceptionHandler();

app.MapHealthChecks("/health");

// No HTTPS endpoint is configured inside the container (see Dockerfile / ASPNETCORE_HTTP_PORTS) -
// TLS termination there is expected to happen upstream, so redirect only applies outside Docker.
if (!app.Environment.IsEnvironment("Docker"))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();

public partial class Program
{
}
