using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ToolShop.Domain.Entities;
using ToolShop.Infrastructure.Auth;
using ToolShop.Infrastructure.Identity;
using ToolShop.Infrastructure.Persistence;
using Scalar.AspNetCore;
using ToolShop.Domain.Authorization;
using ToolShop.API.Middleware;
using ToolShop.API.Health;
using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Hide the Kestrel "Server" header and cap request bodies (1 MB; no file uploads exist).
builder.WebHost.ConfigureKestrel(o =>
{
    o.AddServerHeader = false;
    o.Limits.MaxRequestBodySize = 1_048_576;
});

// --- Services ---

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Components ??= new();
        document.Components.SecuritySchemes = new Dictionary<string, Microsoft.OpenApi.IOpenApiSecurityScheme>
        {
            ["Bearer"] = new Microsoft.OpenApi.OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = Microsoft.OpenApi.ParameterLocation.Header,
                Description = "Enter your JWT access token."
            }
        };

        foreach (var operation in document.Paths.Values.Where(p => p.Operations is not null).SelectMany(p => p.Operations!))
        {
            operation.Value.Security ??= [];
            operation.Value.Security.Add(new Microsoft.OpenApi.OpenApiSecurityRequirement
            {
                [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        }

        return Task.CompletedTask;
    });
});
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

const string FrontendCorsPolicy = "FrontendCorsPolicy";

// CORS allowlist is configuration-driven (Cors:AllowedOrigins). No wildcard.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    is { Length: > 0 } configured
    ? configured
    : new[] { "http://localhost:5173" };

// Rate limiting: strict on auth endpoints (brute force), generous globally.
var authPermit = builder.Configuration.GetValue("RateLimiting:AuthPermitPerMinute", 20);
var globalPermit = builder.Configuration.GetValue("RateLimiting:GlobalPermitPerMinute", 600);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = globalPermit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    o.AddPolicy("auth", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddCheck<DbReadinessHealthCheck>("database", tags: new[] { "ready" });

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});


builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;

    // Login protection (brute-force lockout)
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured.");
if (!builder.Environment.IsDevelopment() && Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 32 bytes outside Development.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("Jwt:Audience is not configured.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // A deactivated (or removed) user loses access immediately, not only when the access token expires.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var active = userId is not null &&
                    await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive);
                if (!active) context.Fail("User is inactive or no longer exists.");
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in typeof(Permissions)
        .GetFields()
        .Where(f => f.IsLiteral && f.FieldType == typeof(string)))
    {
        var value = (string)permission.GetValue(null)!;
        options.AddPolicy(value, policy =>
            policy.RequireClaim("permission", value));
    }
});

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddScoped<ToolShop.Infrastructure.Audit.AuditService>();
builder.Services.AddScoped<ToolShop.Infrastructure.Inventory.InventoryService>();
builder.Services.AddScoped<ToolShop.Infrastructure.Purchases.PurchaseService>();
builder.Services.AddScoped<ToolShop.Infrastructure.Sales.OrderService>();
builder.Services.AddScoped<ToolShop.Infrastructure.Payments.PaymentService>();
builder.Services.AddScoped<ToolShop.Infrastructure.Finance.ExpenseService>();
builder.Services.AddScoped<ToolShop.Infrastructure.Finance.FinanceService>();
builder.Services.AddScoped<ToolShop.Infrastructure.Dashboard.DashboardService>();
builder.Services.AddScoped<ToolShop.Infrastructure.Reports.ReportsService>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// F2: reports use the shop's local day (default UTC+5, Tajikistan, no DST). Override with Reporting:UtcOffsetHours.
ToolShop.Infrastructure.Finance.ReportingClock.Configure(
    builder.Configuration.GetValue("Reporting:UtcOffsetHours", ToolShop.Infrastructure.Finance.ReportingClock.DefaultUtcOffsetHours));

var app = builder.Build();

// --- Middleware pipeline ---

app.UseExceptionHandler();

// Request id + structured request log (method, path WITHOUT query string, status, duration).
// Never logs bodies, headers, or tokens.
app.Use(async (ctx, next) =>
{
    var requestId = ctx.TraceIdentifier;
    ctx.Response.Headers["X-Request-Id"] = requestId;
    var sw = Stopwatch.StartNew();
    var log = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RequestLog");
    using (log.BeginScope(new Dictionary<string, object> { ["RequestId"] = requestId }))
    {
        try { await next(); }
        finally
        {
            sw.Stop();
            if (!ctx.Request.Path.StartsWithSegments("/health"))
                log.LogInformation("HTTP {Method} {Path} -> {Status} in {Elapsed} ms",
                    ctx.Request.Method, ctx.Request.Path.Value, ctx.Response.StatusCode, sw.ElapsedMilliseconds);
        }
    }
});

// Security headers.
app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        if (ctx.Request.Path.StartsWithSegments("/api"))
            h["Cache-Control"] = "no-store";
        return Task.CompletedTask;
    });
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Behind Nginx (TLS terminated there) set Https:Redirect=false.
// Forwarded headers: set ASPNETCORE_FORWARDEDHEADERS_ENABLED=true in the container.
if (app.Configuration.GetValue("Https:Redirect", true))
{
    if (!app.Environment.IsDevelopment()) app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors(FrontendCorsPolicy);

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("live")
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("ready")
});

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    await RoleSeeder.SeedAsync(roleManager);

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await AdminSeeder.SeedAsync(userManager, configuration, logger);

    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await ToolShop.Infrastructure.Finance.ExpenseCategorySeeder.SeedAsync(dbContext);
}

app.Run();

// Required so integration tests can use WebApplicationFactory<Program>.
public partial class Program { }
