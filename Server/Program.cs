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

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    if (builder.Environment.IsDevelopment())
    {
        logging.SetMinimumLevel(LogLevel.Debug);
    }
    else
    {
        logging.SetMinimumLevel(LogLevel.Information);
    }
});

// Configuration
builder.Configuration.AddEnvironmentVariables();
builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

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
        var serverPath = Path.Combine(builder.Environment.ContentRootPath, "..");
        var fullCertPath = Path.IsPathRooted(certPath) ? certPath : Path.Combine(serverPath, certPath);
        var fullKeyPath = Path.IsPathRooted(keyPath) ? keyPath : Path.Combine(serverPath, keyPath);

        if (File.Exists(fullCertPath) && File.Exists(fullKeyPath))
        {
            var certificate = new X509Certificate2(X509Certificate2.CreateFromPemFile(fullCertPath, fullKeyPath).Export(X509ContentType.Pfx));
            var httpsUrl = builder.Configuration["Kestrel:Endpoints:Https:Url"] ?? "https://localhost:7087";
            if (Uri.TryCreate(httpsUrl, UriKind.Absolute, out var httpsUri))
            {
                options.ListenLocalhost(httpsUri.Port, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                    listenOptions.UseHttps(certificate);
                });
            }
            return; // If HTTPS configured, skip HTTP fallback
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
if (!string.IsNullOrWhiteSpace(discordClientId))
{
    builder.Services.AddAuthentication().AddDiscord(options =>
    {
        options.ClientId = discordClientId;
        options.ClientSecret = builder.Configuration["Discord:ClientSecret"] ?? string.Empty;
        options.Scope.Add("identify");
        options.Scope.Add("email");
        options.SaveTokens = false; // Disable unless required for token refresh
        options.CallbackPath = "/signin-discord";
        options.Events.OnCreatingTicket = async context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
            var signInManager = context.HttpContext.RequestServices.GetRequiredService<SignInManager<AppUser>>();

            if (context.Principal == null)
            {
                logger.LogError("Principal is null in Discord authentication");
                return;
            }

            var discordIdStr = context.Principal.FindFirst("urn:discord:id")?.Value ??
                               context.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = context.Principal.FindFirst(ClaimTypes.Email)?.Value;
            var username = context.Principal.FindFirst(ClaimTypes.Name)?.Value;

            if (!ulong.TryParse(discordIdStr, out var discordId))
            {
                logger.LogError("Invalid Discord ID: {DiscordId}", discordIdStr);
                return;
            }

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
                    return;
                }
            }

            await signInManager.SignInAsync(user, isPersistent: false);
            logger.LogInformation("Discord user signed in: {DiscordId}", discordId);
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
else
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowNextJs");
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();