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
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<OAuthOptions>(builder.Configuration.GetSection("OAuth"));
builder.Services.Configure<MySqlProvisioningOptions>(builder.Configuration.GetSection("MySqlProvisioning"));
builder.Services.Configure<LifecycleJobOptions>(builder.Configuration.GetSection("LifecycleJob"));
builder.Services.Configure<FrontendOptions>(builder.Configuration.GetSection("Frontend"));

var frontendOptions = builder.Configuration.GetSection("Frontend").Get<FrontendOptions>()
    ?? throw new InvalidOperationException("Missing configuration section: Frontend");

var origins = builder.Configuration
    .GetSection("Frontend:Origins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(origins)
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

var mySqlProvisioningConnectionString = builder.Configuration.GetConnectionString("MySqlProvisioning")
    ?? throw new InvalidOperationException("Missing connection string: ConnectionStrings:MySqlProvisioning");

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Missing configuration section: Jwt");

var oauthOptions = builder.Configuration.GetSection("OAuth").Get<OAuthOptions>()
    ?? throw new InvalidOperationException("Missing configuration section: OAuth");

builder.Services.AddDbContext<RaftDbContext>(options =>
{
    options.UseSqlServer(raftConnectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure();
    });
});

builder.Services.AddDbContext<MySqlDbContext>(options =>
{
    options.UseMySQL(mySqlProvisioningConnectionString);
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
builder.Services.AddScoped<IMySqlCommandExecutor, MySqlCommandExecutor>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IDatabaseInstanceService, DatabaseInstanceService>();
builder.Services.AddScoped<IAccessCredentialService, AccessCredentialService>();
builder.Services.AddScoped<IAuditEventService, AuditEventService>();
builder.Services.AddScoped<IPlatformMetricsService, PlatformMetricsService>();
builder.Services.AddScoped<IUserDashboardService, UserDashboardService>();
builder.Services.AddSingleton<ISecurePasswordGenerator, SecurePasswordGenerator>();
builder.Services.AddScoped<IMySqlProvisioningService, MySqlProvisioningService>();
builder.Services.AddHostedService<DatabaseLifecycleBackgroundService>();

var app = builder.Build();

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();

app.UseForwardedHeaders(forwardedHeadersOptions);

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();


