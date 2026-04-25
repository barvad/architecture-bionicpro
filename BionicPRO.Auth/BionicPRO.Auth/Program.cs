using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Security.Claims;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);


Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/auth-.txt", rollingInterval: RollingInterval.Day, outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();


builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "bionicpro.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.None;
        options.ExpireTimeSpan = TimeSpan.FromHours(24);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        options.Authority = builder.Configuration["Keycloak:Authority"];
        options.ClientId = builder.Configuration["Keycloak:ClientId"];
        options.ClientSecret = builder.Configuration["Keycloak:ClientSecret"];

        options.ResponseType = "code";
        options.UsePkce = true;
        options.RequireHttpsMetadata = false;

        options.SaveTokens = true;

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.Scope.Add("roles");
        options.GetClaimsFromUserInfoEndpoint = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "preferred_username",
            RoleClaimType = ClaimTypes.Role
        };

        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();

                logger.LogInformation("=== OnTokenValidated event started ===");

                var identity = (ClaimsIdentity?)context.Principal?.Identity;
                if (identity == null)
                {
                    logger.LogWarning("Identity is null in OnTokenValidated");
                    return Task.CompletedTask;
                }

                logger.LogInformation("Principal identity found. Username: {Username}", identity.Name);
                logger.LogInformation("Identity claims: {Claims}", string.Join("; ", identity.Claims.Select(c => $"{c.Type}: {c.Value}")));

                // Проверяем SecurityToken
                var accessToken = context.SecurityToken as JwtSecurityToken;
                logger.LogDebug("SecurityToken type: {TokenType}, is JwtSecurityToken: {IsJwt}", context.SecurityToken?.GetType().Name, accessToken != null);

                if (accessToken == null && context.TokenEndpointResponse != null)
                {
                    logger.LogDebug("SecurityToken is null, attempting to parse from TokenEndpointResponse");
                    try
                    {
                        var handler = new JwtSecurityTokenHandler();
                        accessToken = handler.ReadJwtToken(context.TokenEndpointResponse.AccessToken);
                        logger.LogDebug("Successfully parsed access token from TokenEndpointResponse");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to parse access token from TokenEndpointResponse");
                    }
                }

                if (accessToken == null)
                {
                    logger.LogWarning("Access token could not be obtained");
                    logger.LogDebug("TokenEndpointResponse is null: {IsNull}", context.TokenEndpointResponse == null);
                    return Task.CompletedTask;
                }

                logger.LogInformation("Access token obtained. Expiry: {Expiry}, Issuer: {Issuer}", accessToken.ValidTo, accessToken.Issuer);
                logger.LogInformation("Access token claims count: {Count}", accessToken.Claims.Count());
                logger.LogInformation("Access token claims: {Claims}", string.Join("; ", accessToken.Claims.Select(c => $"{c.Type}: {c.Value}")));

                // Ищем realm_access
                var realmAccess = accessToken.Claims.FirstOrDefault(c => c.Type == "realm_access")?.Value;
                if (realmAccess != null)
                {
                    logger.LogInformation("realm_access found: {RealmAccess}", realmAccess);
                    try
                    {
                        var roles = JsonDocument.Parse(realmAccess).RootElement.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Where(r => r != null)!;
                        foreach (var role in roles)
                        {
                            identity.AddClaim(new Claim(ClaimTypes.Role, role!));
                            logger.LogInformation("Realm role added for user {Username}: {Role}", identity.Name, role);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing realm_access claims");
                    }
                }
                else
                {
                    logger.LogWarning("realm_access claim not found in token");
                }

                // Ищем resource_access
                var resourceAccess = accessToken.Claims.FirstOrDefault(c => c.Type == "resource_access")?.Value;
                if (resourceAccess != null)
                {
                    logger.LogInformation("resource_access found: {ResourceAccess}", resourceAccess);
                    try
                    {
                        using var doc = JsonDocument.Parse(resourceAccess);
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            logger.LogDebug("Processing resource: {ResourceName}", prop.Name);
                            if (prop.Value.TryGetProperty("roles", out var rolesElement))
                            {
                                foreach (var r in rolesElement.EnumerateArray())
                                {
                                    var rv = r.GetString();
                                    if (!string.IsNullOrEmpty(rv))
                                    {
                                        identity.AddClaim(new Claim(ClaimTypes.Role, $"{prop.Name}:{rv}"));
                                        logger.LogInformation("Resource role added for user {Username}: {ResourceRole}", identity.Name, $"{prop.Name}:{rv}");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing resource_access claims");
                    }
                }
                else
                {
                    logger.LogWarning("resource_access claim not found in token");
                }

                logger.LogInformation("=== OnTokenValidated completed. Final identity claims: {FinalClaims} ===", string.Join("; ", identity.Claims.Select(c => $"{c.Type}: {c.Value}")));
                return Task.CompletedTask;
            },

            OnRedirectToIdentityProviderForSignOut = async context =>
            {
                var postLogout = context.Properties?.RedirectUri ?? builder.Configuration["Keycloak:PostLogoutRedirectUri"] ?? "/";

                context.ProtocolMessage.PostLogoutRedirectUri = postLogout;

                try
                {
                    var idToken = await context.HttpContext.GetTokenAsync("id_token");
                    if (!string.IsNullOrEmpty(idToken))
                    {
                        context.ProtocolMessage.IdTokenHint = idToken;
                    }
                }
                catch { }
            }
        };

        // Signed out callback path (where provider will redirect after logout)
        options.SignedOutCallbackPath = "/signout-callback-oidc";
    });


var keysPath = Path.Combine(builder.Environment.ContentRootPath, "keys");
Directory.CreateDirectory(keysPath);

builder.Services.AddDataProtection()
    .SetApplicationName("BionicPRO.Auth")
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

builder.Services.AddHttpClient();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000",
                "https://localhost:3000")
            .AllowCredentials()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithHeaders()
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10)); ;
    });
});

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/", () => "BionicPRO Auth Service");

app.Run();