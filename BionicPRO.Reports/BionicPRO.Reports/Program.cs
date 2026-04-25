using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Minio;
using BionicPRO.Reports.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure MinIO
var minioEndpoint = builder.Configuration["Minio:Endpoint"]
    ?? throw new InvalidOperationException("Minio Endpoint not configured");
var minioAccessKey = builder.Configuration["Minio:AccessKey"]
    ?? throw new InvalidOperationException("Minio AccessKey not configured");
var minioSecretKey = builder.Configuration["Minio:SecretKey"]
    ?? throw new InvalidOperationException("Minio SecretKey not configured");

var minioUri = new Uri(minioEndpoint);

var minioClient = new MinioClient()
    .WithEndpoint(minioUri.Host, minioUri.Port)
    .WithSSL(minioUri.Scheme == "https")
    .WithCredentials(minioAccessKey, minioSecretKey)
    .Build();

builder.Services.AddSingleton<IMinioClient>(minioClient);
builder.Services.AddScoped<IMinioService, MinioService>();
builder.Services.AddScoped<IReportService, ReportService>();

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        var authority = builder.Configuration["Keycloak:Authority"]
                        ?? throw new InvalidOperationException("Keycloak Authority not configured");
        var audience = builder.Configuration["Keycloak:ClientId"]
                       ?? throw new InvalidOperationException("Keycloak Audience not configured");

        options.Authority = authority;
        options.Audience = audience;
        options.RequireHttpsMetadata = false;

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogError(context.Exception, "AUTH FAILED");
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Token validated successfully for user: {Username}",
                    context.Principal?.FindFirst("preferred_username")?.Value ?? "unknown");

                var identity = (ClaimsIdentity)context.Principal.Identity;
                var realmAccess = context.Principal.Claims
                            .FirstOrDefault(c => c.Type == "realm_access")?.Value;
                if (realmAccess != null)
                {
                    logger.LogInformation("realm_access not null");
                    using var doc = JsonDocument.Parse(realmAccess);
                    if (doc.RootElement.TryGetProperty("roles", out var roles))
                    {
                        logger.LogInformation("Found realm roles for user: {Username} count: {rolesCount}",
                            context.Principal?.FindFirst("preferred_username")?.Value ?? "unknown",
                            roles.GetArrayLength());
                        foreach (var r in roles.EnumerateArray())
                        {
                            identity?.AddClaim(new Claim(ClaimTypes.Role, r.GetString()!));
                            logger.LogInformation("Added realm role: {Role}", r.GetString());
                        }
                    }
                }

                var resourceAccess = context.Principal.Claims.FirstOrDefault(c => c.Type == "resource_access")?.Value;
                if (resourceAccess != null)
                {
                    using var doc = JsonDocument.Parse(resourceAccess);
                    foreach (var client in doc.RootElement.EnumerateObject())
                        if (client.Value.TryGetProperty("roles", out var roles))
                            foreach (var r in roles.EnumerateArray())
                            {
                                identity?.AddClaim(new Claim(ClaimTypes.Role, $"{client.Name}:{r.GetString()}"));
                                logger.LogInformation("Added resource role: {Role}", $"{client.Name}:{r.GetString()}");
                            }
                }

                return Task.CompletedTask;
            }
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudience = audience,
            NameClaimType = "preferred_username",
            RoleClaimType = ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization();
var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Application starting. Environment: {Environment}", app.Environment.EnvironmentName);
logger.LogInformation("Keycloak Authority: {Authority}", builder.Configuration["Keycloak:Authority"]);

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

logger.LogInformation("Application configured and ready to run");
app.Run();