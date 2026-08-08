using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using SubVora.Api;
using SubVora.Application.Notifications;
using SubVora.Infrastructure.Notifications;
using SubVora.Application.Auth;
using SubVora.Application.Categories;
using SubVora.Application.Currency;
using SubVora.Application.Dashboard;
using SubVora.Application.Matching;
using SubVora.Application.PaymentSources;
using SubVora.Application.Subscriptions;
using SubVora.Application.Users;
using SubVora.Infrastructure.Catalog;
using SubVora.Infrastructure.Auth;
using SubVora.Infrastructure.Configuration;
using SubVora.Infrastructure.Currency;
using SubVora.Infrastructure.Data;
using SubVora.Infrastructure.Repositories;
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
    var connectionString = sp.GetRequiredService<IConfiguration>().GetRequiredConnectionString("Default");
    return new AppDbContext(AppDbContextOptionsFactory.Build(connectionString));
});

builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IValidator<RegisterRequest>, RegisterRequestValidator>();
builder.Services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
builder.Services.AddScoped<IValidator<ForgotPasswordRequest>, ForgotPasswordRequestValidator>();
builder.Services.AddScoped<IValidator<ResetPasswordRequest>, ResetPasswordRequestValidator>();
// Requests enqueue and return; a background service does the SMTP round trip. Sending inline made
// response time and status depend on whether the address existed, which is exactly what
// forgot-password and register are written to hide - see QueuedEmailSender.
builder.Services.AddSingleton<SmtpEmailSender>();
builder.Services.AddSingleton<QueuedEmailSender>();
builder.Services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<QueuedEmailSender>());
builder.Services.AddHostedService<EmailDispatchBackgroundService>();

builder.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
builder.Services.AddScoped<IValidator<CreateSubscriptionRequest>, CreateSubscriptionRequestValidator>();

builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<IValidator<CreateCategoryRequest>, CreateCategoryRequestValidator>();

builder.Services.AddScoped<IPaymentSourceRepository, PaymentSourceRepository>();
builder.Services.AddScoped<IValidator<CreatePaymentSourceRequest>, CreatePaymentSourceRequestValidator>();

builder.Services.AddScoped<ISubscriptionCatalogSearchRepository, SubscriptionCatalogSearchRepository>();
builder.Services.AddScoped<ISubscriptionMatchService, SubscriptionMatchService>();
builder.Services.AddScoped<IValidator<ResolveSubscriptionRequest>, ResolveSubscriptionRequestValidator>();

// Scoped, not singleton - depends on IFxRateService, which holds a scoped DbContext.
builder.Services.AddScoped<IBurnRateCalculator, BurnRateCalculator>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IValidator<UpdateUserProfileRequest>, UpdateUserProfileRequestValidator>();

builder.Services.AddScoped<IFxRateService, FxRateService>();
builder.Services.AddHttpClient<IExchangeRateClient, ExchangeRateHostClient>(client =>
{
    client.BaseAddress = new Uri("https://api.exchangerate.host/");
});
builder.Services.AddHostedService<FxRateRefreshBackgroundService>();

// No job advances next_billing_date any more. A date left in the past is how the app says a charge
// is outstanding, and a nightly roll-forward would erase that signal - the billing date now moves
// only when the user marks the charge paid (POST /api/v1/subscriptions/{id}/mark-paid).

// Adds any provider in subscription-catalog.json that the database does not have yet.
builder.Services.AddHostedService<SubscriptionCatalogSyncService>();

// Nothing else deletes from refresh_tokens or password_reset_codes; both otherwise grow forever.
builder.Services.AddHostedService<ExpiredCredentialCleanupBackgroundService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Same lazy-resolution reasoning as the DbContext registration above.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, configuration) =>
    {
        var jwtSecret = configuration.GetRequiredJwtSecret();
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

// Bounds the catalog-match endpoint only - not applied globally. Matching is a local pg_trgm
// scan now, so this caps CPU on a debounced-typing endpoint rather than a third-party bill.
// Limit/window are configurable so tests can use a small window instead of waiting on the
// real one; defaults to 30 requests/minute per authenticated user in the absence of config.
builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        // Otherwise a limiter doing its job is indistinguishable from an endpoint nobody calls -
        // there is no other record that a request was turned away. No partition key here: for the
        // "auth" policy that is the caller's IP, and the request log already carries it.
        context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("SubVora.RateLimiting")
            .LogWarning("Rate limit rejected {Method} {Path}.",
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path);

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
builder.Services.AddOpenApi(options =>
{
    var securityTransformer = new OpenApiSecurityTransformer();
    options.AddDocumentTransformer(securityTransformer);
    options.AddOperationTransformer(securityTransformer);
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Tagged "db" so the liveness endpoint below can exclude it. Everything registered here is a
// dependency check; liveness deliberately has none.
builder.Services.AddHealthChecks()
    .AddNpgSql(sp => sp.GetRequiredService<IConfiguration>().GetRequiredConnectionString("Default"), tags: ["db"]);

var app = builder.Build();

// Configure the HTTP request pipeline.
// "Docker" is the compose-local environment (its configuration comes from docker-compose.yml's
// environment block) - same dev convenience as Development, just against the containerized db
// service instead of localhost.
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

// Deployed behind a TLS-terminating proxy (see docs/DEPLOYMENT.md), which forwards plain HTTP.
// Without this, UseHttpsRedirection below sees scheme "http" and redirects every request straight
// back to a URL that arrives as "http" again - an infinite loop. It also restores the real client
// address, which the "auth" rate-limiter partition keys on; otherwise every request appears to come
// from the proxy and the per-IP login limit collapses into one global limit for all users.
//
// The proxy's own address is assigned dynamically and cannot be allow-listed. That is safe here
// because ForwardLimit defaults to 1: only the rightmost X-Forwarded-For entry is read, and that
// one is appended by the proxy itself, so a client-supplied value cannot win.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownIPNetworks = { },
    KnownProxies = { },
});

// One summary line per request - method, path, status, elapsed ms. Without it the request path is
// silent: the Microsoft.AspNetCore override above is at Warning, so a 200, a 401, a 404 and a 429
// all produced no output at all, leaving no record of who called what. Sits after
// UseForwardedHeaders so the client address it enriches with is the real one, and before
// UseExceptionHandler so a request that ends in a 500 still gets its line.
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        // User id only. Email addresses, tokens and reset codes must not reach the log sink, and
        // the id is enough to follow one account's activity across requests.
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is not null)
        {
            diagnosticContext.Set("UserId", userId);
        }
    };
});

app.UseExceptionHandler();

// Liveness: is this process up and serving? No dependency checks at all, which is what makes it
// safe for a platform to poll continuously. Render's healthCheckPath points here.
//
// This split exists because the database probe is expensive in a way that is easy to miss. Neon's
// free tier bills compute and scales to zero when idle, so an endpoint that opens a connection,
// polled by the platform every few seconds forever, holds that compute awake around the clock -
// exactly what DEPLOYMENT.md's keep-warm section routes the cron ping away from, and it was
// happening anyway via healthCheckPath.
//
// It is also the right semantics regardless of host: a failing health check restarts the instance,
// and a database blip should not restart an app that is running perfectly well.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

// Readiness: can this instance actually serve requests - database included? For deploy
// verification and manual checks, not for continuous polling.
app.MapHealthChecks("/health/ready");

// Kept as-is, and identical to /health/ready. Anything already pointing here - the curl line in
// DEPLOYMENT.md, a bookmark, an uptime monitor someone set up - keeps working unchanged.
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
