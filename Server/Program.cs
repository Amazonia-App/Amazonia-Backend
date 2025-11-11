using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Security.Cryptography.X509Certificates;
using AmazoniaApi.Core.Interfaces;
using AmazoniaApi.Core.Models;
using AmazoniaApi.Server.DBContext;
using AmazoniaApi.Server.Handlers;
using AmazoniaApi.Server.Seeders;
using AmazoniaApi.Server.Services;

System.AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    // Set minimum level to Information to disable Debug logs
    logging.SetMinimumLevel(LogLevel.Information);
});

// Configuration
builder.Configuration.AddEnvironmentVariables();
builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

var botGrpcUrl = builder.Configuration["Bot:GrpcUrl"];
if (string.IsNullOrEmpty(botGrpcUrl))
{
    throw new InvalidOperationException("Bot:GrpcUrl is not configured. Please set it in appsettings.json or environment variables.");
}

// CORS - configurable via appsettings
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowNextJs", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
        else
        {
            // No origins configured - deny all
            policy.AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});

// Configure Kestrel endpoints
var certPath = builder.Configuration["Kestrel:Certificates:Default:Path"];
var keyPath = builder.Configuration["Kestrel:Certificates:Default:KeyPath"];

builder.WebHost.ConfigureKestrel(options =>
{
    if (!string.IsNullOrEmpty(certPath) && !string.IsNullOrEmpty(keyPath))
    {
        try
        {
            var serverPath = Path.Combine(builder.Environment.ContentRootPath, "..");
            var fullCertPath = Path.IsPathRooted(certPath) ? certPath : Path.Combine(serverPath, certPath);
            var fullKeyPath = Path.IsPathRooted(keyPath) ? keyPath : Path.Combine(serverPath, keyPath);

            if (File.Exists(fullCertPath) && File.Exists(fullKeyPath))
            {
                var certificate = X509Certificate2.CreateFromPemFile(fullCertPath, fullKeyPath);
                certificate = new X509Certificate2(certificate.Export(X509ContentType.Pfx));
                var httpsUrl = builder.Configuration["Kestrel:Endpoints:Https:Url"] ?? "https://localhost:7087";
                if (Uri.TryCreate(httpsUrl, UriKind.Absolute, out var httpsUri))
                {
                    options.ListenLocalhost(httpsUri.Port, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                        listenOptions.UseHttps(certificate);
                    });
                    return; // If HTTPS configured successfully, skip HTTP fallback
                }
            }
            else
            {
                Console.WriteLine($"⚠️  TLS certificate not found at '{fullCertPath}' or key at '{fullKeyPath}'. Falling back to development certificate.");
                builder.WebHost.UseSetting("Kestrel:Certificates:Default:Path", string.Empty);
                builder.WebHost.UseSetting("Kestrel:Certificates:Default:KeyPath", string.Empty);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Failed to load TLS certificate from configuration. Falling back to development certificate. Reason: {ex.Message}");
            builder.WebHost.UseSetting("Kestrel:Certificates:Default:Path", string.Empty);
            builder.WebHost.UseSetting("Kestrel:Certificates:Default:KeyPath", string.Empty);
        }
    }

    // Fallback: HTTPS in development, HTTP otherwise
    if (builder.Environment.IsDevelopment())
    {
        // Use HTTPS with development certificate on port 7087
        var httpsPort = 7087;
        options.ListenLocalhost(httpsPort, listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
            listenOptions.UseHttps(); // Uses the development certificate automatically
        });
        
        // Also listen on HTTP port 5041 for compatibility
        var httpPort = 5041;
        options.ListenLocalhost(httpPort, listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
        });
    }
    else
    {
        // PRODUCTION: luister op alles (Docker-proof)
        options.ListenAnyIP(8080, listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
        });
    }
});

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    // Fallback for placeholder/incorrect env override
    if (connectionString.Trim().Equals("your_connection_string_here", StringComparison.OrdinalIgnoreCase))
    {
        connectionString = "Data Source=Core/SqliteTestServers/app.db";
    }

    // If path is relative, make it relative to the project root
    if (!Path.IsPathRooted(connectionString))
    {
        var projectRoot = Path.Combine(builder.Environment.ContentRootPath, "..");
        var dbPath = connectionString.Replace("Data Source=", "");
        connectionString = $"Data Source={Path.Combine(projectRoot, dbPath)}";
    }

    // Ensure the SQLite directory exists and log the resolved path
    var absoluteDbPath = connectionString.Replace("Data Source=", "");
    var dbDirectory = Path.GetDirectoryName(absoluteDbPath);
    if (!string.IsNullOrEmpty(dbDirectory))
    {
        Directory.CreateDirectory(dbDirectory);
    }
    Console.WriteLine($"Using SQLite database at: {absoluteDbPath}");

    options.UseSqlite(connectionString);
    
    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
    }
});

// Identity
builder.Services.AddIdentity<AppUser, IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Cookies
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
    options.LoginPath = "/api/Auth/Login";
});

// Discord Authentication (only if configured)
var discordClientId = builder.Configuration["Discord:ClientId"];
var discordClientSecret = builder.Configuration["Discord:ClientSecret"];
if (!string.IsNullOrWhiteSpace(discordClientId) && !string.IsNullOrWhiteSpace(discordClientSecret))
{
    Console.WriteLine("🔐 Discord OAuth Configuration:");
    Console.WriteLine($"   ClientId: {discordClientId}");
    Console.WriteLine($"   ClientSecret: {(string.IsNullOrWhiteSpace(discordClientSecret) ? "❌ MISSING" : "✅ configured")}");
    Console.WriteLine($"   CallbackPath: /signin-discord");
    Console.WriteLine("⚠️  Make sure your Discord OAuth app has these redirect URIs registered:");
    Console.WriteLine("   - https://localhost:7087/signin-discord");
    Console.WriteLine("   - http://localhost:5041/signin-discord");
    
    builder.Services.AddAuthentication().AddDiscord(options =>
    {
        options.ClientId = discordClientId;
        options.ClientSecret = discordClientSecret;
        options.Scope.Add("identify");
        options.Scope.Add("email");
        options.SaveTokens = false; // Disable unless required for token refresh
        options.CallbackPath = "/signin-discord";
        
        // Log the redirect URI that will be used
        options.Events.OnRedirectToAuthorizationEndpoint = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            var request = context.Request;
            var scheme = request.Scheme; // http or https
            var host = request.Host.Value; // localhost:7087 or localhost:5041
            
            // Build the redirect URI that Discord expects (this is what should be registered in Discord)
            var redirectUri = $"{scheme}://{host}{options.CallbackPath}";
            logger.LogInformation("Discord OAuth - Redirect URI being sent to Discord: {RedirectUri}", redirectUri);
            logger.LogInformation("Discord OAuth - Make sure this exact URI is registered in your Discord OAuth app settings");
            
            // Continue with the default redirect behavior
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        
        options.Events.OnCreatingTicket = async context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
            var signInManager = context.HttpContext.RequestServices.GetRequiredService<SignInManager<AppUser>>();

            if (context.Principal == null)
            {
                logger.LogError("Principal is null in Discord authentication");
                context.Fail("Principal is null");
                return;
            }

            var discordIdStr = context.Principal.FindFirst("urn:discord:id")?.Value ??
                               context.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = context.Principal.FindFirst(ClaimTypes.Email)?.Value;
            var username = context.Principal.FindFirst(ClaimTypes.Name)?.Value;

            if (string.IsNullOrWhiteSpace(discordIdStr) || !ulong.TryParse(discordIdStr, out var discordId))
            {
                logger.LogError("Invalid Discord ID: {DiscordId}", discordIdStr);
                context.Fail("Invalid Discord ID");
                return;
            }

            try
            {
                var user = await userManager.Users.FirstOrDefaultAsync(u => u.DiscordId == discordId);
                if (user == null)
                {
                    user = new AppUser
                    {
                        UserName = username ?? $"discord_{discordId}",
                        Email = email ?? $"discord_{discordId}@example.com",
                        DiscordId = discordId
                    };

                    var result = await userManager.CreateAsync(user);
                    if (!result.Succeeded)
                    {
                        logger.LogError("Failed to create user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
                        context.Fail($"Failed to create user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                        return;
                    }
                }

                await signInManager.SignInAsync(user, isPersistent: false);
                logger.LogInformation("Discord user signed in: {DiscordId}", discordId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during Discord authentication");
                context.Fail($"Authentication error: {ex.Message}");
            }
        };
        
        options.Events.OnRemoteFailure = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var frontendUrl = configuration["FrontendUrl"] ?? "http://localhost:3000";
            
            var errorMessage = context.Failure?.Message ?? "unknown_error";
            logger.LogError("Discord OAuth remote failure: {Error}", errorMessage);
            logger.LogError("Error details: {Details}", context.Failure?.ToString());
            
            // Log specific error details for debugging
            if (errorMessage.Contains("invalid_client"))
            {
                logger.LogError("Invalid client error - Check your Discord ClientSecret and ensure redirect URI matches exactly");
                logger.LogError("Expected redirect URI: {Scheme}://{Host}{CallbackPath}", 
                    context.Request.Scheme, context.Request.Host.Value, options.CallbackPath);
            }
            
            // Redirect to callback endpoint which will handle redirecting to frontend
            context.Response.Redirect($"/api/Auth/callback?error={Uri.EscapeDataString(errorMessage)}");
            context.HandleResponse();
            return Task.CompletedTask;
        };
    });
}
else
{
    builder.Services.AddAuthentication();
}

builder.Services.AddAuthorization();
builder.Services.AddControllers().AddControllersAsServices();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Enable HttpContext access
builder.Services.AddHttpContextAccessor();

// Register ClaimsPrincipal as a scoped service
builder.Services.AddScoped<ClaimsPrincipal>(serviceProvider =>
{
    var httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
    var httpContext = httpContextAccessor.HttpContext;
    return httpContext == null ? throw new InvalidOperationException("HttpContext is not available. Ensure the service is resolved within an HTTP request scope.") : httpContext.User;
});

// Dependency Injection
builder.Services.AddTransient<IHelperMethods, HelperMethods>();
builder.Services.AddScoped<IAccountHandler, AccountHandler>();
builder.Services.AddScoped<IBankHandler, BankHandler>();
builder.Services.AddSingleton<IBotGrpcClient, BotGrpcClient>();

var app = builder.Build();

// Apply migrations automatically
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Roles
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var roleSeeder = new RoleSeeder(roleManager);
    await roleSeeder.SeedRolesAsync();
}

// Middleware
// if (app.Environment.IsDevelopment())
// {
    app.UseSwagger();
    app.UseSwaggerUI();
// }
// else
// {
//     app.UseHttpsRedirection();
// }

app.UseCors("AllowNextJs");
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();