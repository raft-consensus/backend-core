using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using raft_backend.Configuration;
using raft_backend.Database;
using raft_backend.Middleware;
using raft_backend.Services;
using System.Threading.RateLimiting;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.AddPolicy("auth", context =>
    {
        var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.AddPolicy("credential-reveal", context =>
    {
        var partitionKey = context.User.Identity?.Name
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.AddPolicy("admin-ops", context =>
    {
        var partitionKey = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.AddPolicy("database-management", context =>
    {
        var partitionKey = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.AddPolicy("ai-key-management", context =>
    {
        var partitionKey = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.AddPolicy("ai-api", context =>
    {
        var apiKey = context.Request.Headers["X-API-Key"].ToString();
        var partitionKey = string.IsNullOrWhiteSpace(apiKey)
            ? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(apiKey))).ToLowerInvariant();

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    // Provisioning hits the real MySQL server (CREATE DATABASE/USER), so it's throttled
    // per-user to keep abuse low while still allowing a few retries per minute.
    options.AddPolicy("database-provisioning", context =>
    {
        var partitionKey = context.User.Identity?.Name
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection("Jwt"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<OAuthOptions>()
    .Bind(builder.Configuration.GetSection("OAuth"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<LifecycleJobOptions>()
    .Bind(builder.Configuration.GetSection("LifecycleJob"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<FrontendOptions>()
    .Bind(builder.Configuration.GetSection("Frontend"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<SqlServerProvisioningOptions>()
    .Bind(builder.Configuration.GetSection("SqlServerProvisioning"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<MySqlProvisioningOptions>()
    .Bind(builder.Configuration.GetSection("MySqlProvisioning"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<PostgresProvisioningOptions>()
    .Bind(builder.Configuration.GetSection("PostgresProvisioning"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<AiServiceOptions>()
    .Bind(builder.Configuration.GetSection("AiService"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<MongoProvisioningOptions>()
    .Bind(builder.Configuration.GetSection("MongoProvisioning"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.Configure<ExternalCellConnectionStrings>(builder.Configuration.GetSection("ConnectionStrings"));

var frontendOptions = builder.Configuration.GetSection("Frontend").Get<FrontendOptions>()
    ?? throw new InvalidOperationException("Missing configuration section: Frontend");

var origins = frontendOptions.GetAllowedOrigins();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(origins.ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var configuredKeysPath = builder.Configuration["DataProtection:KeysPath"];
var dataProtectionKeysPath = string.IsNullOrWhiteSpace(configuredKeysPath)
    ? Path.Combine(builder.Environment.ContentRootPath, "keys")
    : configuredKeysPath;

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("raft-backend");

var raftConnectionString = builder.Configuration.GetConnectionString("RaftDb")
    ?? throw new InvalidOperationException("Missing connection string: ConnectionStrings:RaftDb");

var sqlServerProvisioningConnectionString = builder.Configuration.GetConnectionString("SqlServerProvisioning")
    ?? BuildProvisioningConnectionString(raftConnectionString);

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Missing configuration section: Jwt");

var oauthOptions = builder.Configuration.GetSection("OAuth").Get<OAuthOptions>()
    ?? throw new InvalidOperationException("Missing configuration section: OAuth");

ValidateSecureRuntimeConfiguration(builder.Environment, frontendOptions, jwtOptions, oauthOptions, origins);

builder.Services.AddDbContext<RaftDbContext>(options =>
{
    options.UseSqlServer(raftConnectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure();
    });
});

builder.Services.AddDbContext<SqlServerProvisioningDbContext>(options =>
{
    options.UseSqlServer(sqlServerProvisioningConnectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure();
    });
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddCookie(AuthSchemes.External)
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtOptions.Issuer,
        ValidAudience = jwtOptions.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
        ClockSkew = TimeSpan.Zero
    };
})
.AddGoogle(AuthSchemes.Google, options =>
{
    options.ClientId = oauthOptions.GoogleClientId;
    options.ClientSecret = oauthOptions.GoogleClientSecret;
    options.SignInScheme = AuthSchemes.External;
    options.SaveTokens = true;
    options.Scope.Add("email");
    options.Scope.Add("profile");
    options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
    options.ClaimActions.MapJsonKey(ClaimTypes.Name, "name");
    options.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
    options.ClaimActions.MapJsonKey("picture", "picture");
})
.AddGitHub(AuthSchemes.GitHub, options =>
{
    options.ClientId = oauthOptions.GitHubClientId;
    options.ClientSecret = oauthOptions.GitHubClientSecret;
    options.SignInScheme = AuthSchemes.External;
    options.SaveTokens = true;
    options.Scope.Add("read:user");
    options.Scope.Add("user:email");
    options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
    options.ClaimActions.MapJsonKey(ClaimTypes.Name, "login");
    options.ClaimActions.MapJsonKey("urn:github:name", "name");
    options.ClaimActions.MapJsonKey("urn:github:avatar", "avatar_url");
    options.Events.OnCreatingTicket = async context =>
    {
        context.RunClaimActions(context.User);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("RaftBackend", "1.0"));

        using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
        foreach (var email in payload.RootElement.EnumerateArray())
        {
            if (email.TryGetProperty("primary", out var primary) && primary.GetBoolean() &&
                email.TryGetProperty("verified", out var verified) && verified.GetBoolean() &&
                email.TryGetProperty("email", out var address) && address.ValueKind == JsonValueKind.String)
            {
                var value = address.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    context.Identity?.AddClaim(new Claim(ClaimTypes.Email, value));
                }

                break;
            }
        }
    };
});

builder.Services.AddScoped<ISqlStoredProcedureExecutor, SqlStoredProcedureExecutor>();
builder.Services.AddScoped<ISqlServerCommandExecutor, SqlServerCommandExecutor>();
builder.Services.AddScoped<ISqlServerProvisioningService, SqlServerProvisioningService>();
builder.Services.AddScoped<IDatabaseProvisioningService, SqlServerProvisioningService>();
builder.Services.AddScoped<IDatabaseProvisioningService, MySqlProvisioningService>();
builder.Services.AddScoped<IDatabaseProvisioningService, PostgresProvisioningService>();
builder.Services.AddScoped<IDatabaseProvisioningService, MongoProvisioningService>();
builder.Services.AddScoped<IDatabaseProvisioningServiceResolver, DatabaseProvisioningServiceResolver>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IDatabaseInstanceService, DatabaseInstanceService>();
builder.Services.AddScoped<IAccessCredentialService, AccessCredentialService>();
builder.Services.AddScoped<IAuditEventService, AuditEventService>();
builder.Services.AddScoped<IPlatformMetricsService, PlatformMetricsService>();
builder.Services.AddScoped<IUserDashboardService, UserDashboardService>();
builder.Services.AddScoped<IAiApiKeyService, AiApiKeyService>();
builder.Services.AddScoped<IAiService, AiService>();
builder.Services.AddSingleton<ISecurePasswordGenerator, SecurePasswordGenerator>();
builder.Services.AddSingleton<IApiAvailabilityTracker, ApiAvailabilityTracker>();
builder.Services.AddHostedService<DatabaseLifecycleBackgroundService>();

var app = builder.Build();

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();

app.UseForwardedHeaders(forwardedHeadersOptions);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.Use(async (context, next) =>
{
    var availabilityTracker = context.RequestServices.GetRequiredService<IApiAvailabilityTracker>();

    try
    {
        await next();
    }
    finally
    {
        availabilityTracker.RecordResponse(context.Response.StatusCode);
    }
});

app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

static string BuildProvisioningConnectionString(string raftConnectionString)
{
    var builder = new SqlConnectionStringBuilder(raftConnectionString)
    {
        InitialCatalog = "master"
    };

    return builder.ConnectionString;
}

static void ValidateSecureRuntimeConfiguration(
    IWebHostEnvironment environment,
    FrontendOptions frontendOptions,
    JwtOptions jwtOptions,
    OAuthOptions oauthOptions,
    IReadOnlyCollection<string> allowedOrigins)
{
    if (allowedOrigins.Count == 0)
    {
        throw new InvalidOperationException("No valid frontend origins were configured. Set Frontend:BaseUrl or Frontend:Origins.");
    }

    if (!environment.IsDevelopment())
    {
        EnsureNotPlaceholder(jwtOptions.SigningKey, "Jwt:SigningKey");
        EnsureNotPlaceholder(oauthOptions.GoogleClientId, "OAuth:GoogleClientId");
        EnsureNotPlaceholder(oauthOptions.GoogleClientSecret, "OAuth:GoogleClientSecret");
        EnsureNotPlaceholder(oauthOptions.GitHubClientId, "OAuth:GitHubClientId");
        EnsureNotPlaceholder(oauthOptions.GitHubClientSecret, "OAuth:GitHubClientSecret");
        EnsureNotPlaceholder(frontendOptions.BaseUrl, "Frontend:BaseUrl");
    }

    if (jwtOptions.SigningKey.Length < 32)
    {
        throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters long.");
    }
}

static void EnsureNotPlaceholder(string value, string configurationKey)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Missing required configuration value: {configurationKey}.");
    }

    var normalized = value.Trim();
    if (normalized.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("REPLACE", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("TODO", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Configuration value {configurationKey} still looks like a placeholder.");
    }
}
