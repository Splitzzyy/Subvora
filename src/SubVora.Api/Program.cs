using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using SubVora.Application.Alerts;
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

var builder = WebApplication.CreateBuilder(args);

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
});

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Swagger UI reads the spec .NET's native OpenAPI generator already produces above -
    // no second (Swashbuckle) generator, one source of truth for the document itself.
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "SubVora API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();

public partial class Program
{
}
