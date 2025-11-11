using System;
using System.Net.Http.Headers;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AmazoniaApi.Bot.Services;

var contentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = Directory.Exists(contentRoot) ? contentRoot : AppContext.BaseDirectory
});

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables(prefix: "AMAZONIA_BOT_");

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddFilter("AmazoniaApi.Bot.Modules", LogLevel.Debug);

builder.Services.AddSingleton(provider =>
{
    var config = new DiscordSocketConfig
    {
        GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
        LogLevel = LogSeverity.Info,
        MessageCacheSize = 100
    };

    var client = new DiscordSocketClient(config);

    client.Log += message =>
    {
        var logger = provider.GetRequiredService<ILogger<DiscordSocketClient>>();
        logger.Log(MapLogSeverity(message.Severity), "{Source}: {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    };

    return client;
});

builder.Services.AddSingleton(provider =>
{
    var client = provider.GetRequiredService<DiscordSocketClient>();
    return new InteractionService(client, new InteractionServiceConfig
    {
        LogLevel = LogSeverity.Info,
        UseCompiledLambda = false,
        DefaultRunMode = RunMode.Async  // Ensure async methods are properly handled
    });
});

builder.Services.AddSingleton<TicketChannelCache>();
builder.Services.AddSingleton<ChannelPermissionManager>();
builder.Services.AddSingleton<ApiClient>();
builder.Services.AddSingleton<BotGrpcService>();
builder.Services.AddTransient<AmazoniaApi.Bot.Modules.TicketModule>();
builder.Services.AddHostedService<DiscordBotService>();

builder.Services.AddGrpc();

builder.Services.AddHttpClient(nameof(ApiClient), (provider, client) =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var baseUrl = configuration.GetValue<string>("Api:BaseUrl");
    if (!string.IsNullOrWhiteSpace(baseUrl))
    {
        client.BaseAddress = new Uri(baseUrl);
    }

    var apiKey = configuration.GetValue<string>("Api:ApiKey");
    if (!string.IsNullOrWhiteSpace(apiKey))
    {
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    }

    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.WebHost.ConfigureKestrel((context, options) =>
{
    var port = context.Configuration.GetValue("Grpc:Port", 5100);
    options.ListenAnyIP(port, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

var app = builder.Build();

app.MapGrpcService<BotGrpcService>();

await app.RunAsync();

static LogLevel MapLogSeverity(LogSeverity severity) =>
    severity switch
    {
        LogSeverity.Critical => LogLevel.Critical,
        LogSeverity.Error => LogLevel.Error,
        LogSeverity.Warning => LogLevel.Warning,
        LogSeverity.Info => LogLevel.Information,
        LogSeverity.Verbose => LogLevel.Debug,
        LogSeverity.Debug => LogLevel.Trace,
        _ => LogLevel.Information
    };