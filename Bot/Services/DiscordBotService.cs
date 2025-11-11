using System.Linq;
using System.Reflection;
using AmazoniaApi.Bot.Modules;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AmazoniaApi.Bot.Services;

public class DiscordBotService : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactionService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DiscordBotService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TicketChannelCache _ticketChannelCache;
    private readonly ApiClient _apiClient;
    
    // Track processed interactions to prevent duplicate processing
    private readonly HashSet<ulong> _processedInteractions = new();
    private readonly object _processedInteractionsLock = new();

    private readonly TaskCompletionSource<bool> _readyCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DiscordBotService(
        DiscordSocketClient client,
        InteractionService interactionService,
        IConfiguration configuration,
        ILogger<DiscordBotService> logger,
        IServiceProvider serviceProvider,
        TicketChannelCache ticketChannelCache,
        ApiClient apiClient)
    {
        _client = client;
        _interactionService = interactionService;
        _configuration = configuration;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _ticketChannelCache = ticketChannelCache;
        _apiClient = apiClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RegisterEventHandlers();

        var token = _configuration.GetValue<string>("Discord:BotToken")?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogCritical("Discord bot token is not configured.");
            return;
        }

        try
        {
            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to start Discord client. Verify the bot token and network connectivity.");
            return;
        }

        await _readyCompletionSource.Task.WaitAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Discord bot service...");

        await _client.StopAsync();
        await _client.LogoutAsync();

        await base.StopAsync(cancellationToken);
    }

    private void RegisterEventHandlers()
    {
        _client.Ready += OnClientReadyAsync;
        _client.InteractionCreated += OnInteractionCreatedAsync;
        _client.MessageReceived += OnMessageReceivedAsync;
        _client.Connected += OnConnectedAsync;
        _client.Disconnected += OnDisconnectedAsync;
        _client.LoggedIn += OnLoggedInAsync;
        _interactionService.Log += OnInteractionLogAsync;
        _interactionService.SlashCommandExecuted += OnSlashCommandExecutedAsync;
    }

    private async Task OnClientReadyAsync()
    {
        try
        {
            _logger.LogInformation("Discord client ready. Loading interaction modules...");

            await _interactionService.AddModulesAsync(Assembly.GetExecutingAssembly(), _serviceProvider);
            var loadedModuleCount = _interactionService.Modules.Count;
            _logger.LogInformation("Interaction modules loaded: {ModuleCount}", loadedModuleCount);

            var moduleNames = _interactionService.Modules.Select(module => module.Name).ToArray();
            _logger.LogInformation("Interaction modules discovered: {ModuleNames}", moduleNames.Length > 0 ? string.Join(", ", moduleNames) : "none");

            var ticketModuleInfo = _interactionService.Modules.FirstOrDefault(module => string.Equals(module.Name, nameof(TicketModule), StringComparison.Ordinal));
            if (ticketModuleInfo is not null)
            {
                _logger.LogInformation("TicketModule discovered with {CommandCount} slash commands.", ticketModuleInfo.SlashCommands.Count);
            }
            else
            {
                _logger.LogWarning("TicketModule not found among loaded interaction modules.");
            }

            using (var scope = _serviceProvider.CreateScope())
            {
                var resolvedTicketModule = scope.ServiceProvider.GetService<TicketModule>();
                if (resolvedTicketModule is not null)
                {
                    _logger.LogInformation("TicketModule resolved successfully from DI during readiness check.");
                }
                else
                {
                    _logger.LogWarning("TicketModule could not be resolved from DI during readiness check.");
                }
            }

            LogLoadedInteractionModules();

            var guildId = _configuration.GetValue<ulong?>("Discord:GuildId") ?? 0;

            if (guildId != 0)
            {
                await _interactionService.RegisterCommandsToGuildAsync(guildId, false);
                _logger.LogInformation("Registered slash commands to guild {GuildId}", guildId);
                LogRegisteredSlashCommands();
            }
            else
            {
                await _interactionService.RegisterCommandsGloballyAsync(false);
                _logger.LogInformation("Registered slash commands globally.");
                LogRegisteredSlashCommands();
            }

            await InitializeTicketCacheAsync();

            _readyCompletionSource.TrySetResult(true);
            _logger.LogInformation("Bot is ready.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed during bot ready sequence.");
            _readyCompletionSource.TrySetException(ex);
        }
    }

    private async Task InitializeTicketCacheAsync()
    {
        try
        {
            var tickets = await _apiClient.GetOpenTicketsAsync();
            _ticketChannelCache.Clear();
            foreach (var ticket in tickets)
            {
                _ticketChannelCache.AddOrUpdateTicket(ticket);
            }

            _logger.LogInformation("Initialized ticket cache with {Count} entries.", tickets.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize ticket cache.");
        }
    }

    private async Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        // Check if we've already processed this interaction
        lock (_processedInteractionsLock)
        {
            if (_processedInteractions.Contains(interaction.Id))
            {
                _logger.LogWarning("Interaction {InteractionId} already processed, ignoring duplicate.", interaction.Id);
                return;
            }
            _processedInteractions.Add(interaction.Id);
            
            // Clean up old interaction IDs (keep last 1000)
            if (_processedInteractions.Count > 1000)
            {
                var toRemove = _processedInteractions.Take(_processedInteractions.Count - 1000).ToList();
                foreach (var id in toRemove)
                {
                    _processedInteractions.Remove(id);
                }
            }
        }
        
        try
        {
            string? commandName = interaction switch
            {
                SocketSlashCommand slashCommandInteraction => slashCommandInteraction.CommandName,
                SocketUserCommand userCommand => userCommand.CommandName,
                SocketMessageCommand messageCommand => messageCommand.CommandName,
                _ => (interaction.Data as IApplicationCommandInteractionData)?.Name
            };
            var commandLogName = commandName ?? $"unknown (InteractionId: {interaction.Id})";

            _logger.LogInformation("Received interaction {Type} for command {CommandName} from {User} ({Id}) in guild {GuildId}", interaction.Type, commandLogName, interaction.User.Username, interaction.User.Id, interaction.GuildId ?? 0);

            // Create scope that will stay alive for the duration of the async operation
            using var scope = _serviceProvider.CreateScope();

            _logger.LogInformation("Interaction data: CommandName={CommandName}, Raw={RawData}", commandLogName, interaction.Data?.ToString());

            var context = new SocketInteractionContext(_client, interaction);

            if (!string.IsNullOrWhiteSpace(commandName))
            {
                var matchedSlashCommand = _interactionService.SlashCommands.FirstOrDefault(c => string.Equals(c.Name, commandName, StringComparison.OrdinalIgnoreCase));
                if (matchedSlashCommand is not null)
                {
                    var moduleName = matchedSlashCommand.Module?.Name ?? "unknown";
                    _logger.LogInformation("Command {CommandName} is associated with module {ModuleName} and method {MethodName}", commandLogName, moduleName, matchedSlashCommand.MethodName);

                    if (string.Equals(moduleName, nameof(TicketModule), StringComparison.Ordinal))
                    {
                        TicketModule? resolvedModuleInstance = null;
                        try
                        {
                            resolvedModuleInstance = scope.ServiceProvider.GetService<TicketModule>();
                        }
                        catch (Exception resolveException)
                        {
                            _logger.LogError(resolveException, "Error resolving TicketModule from scoped provider for command {CommandName}", commandLogName);
                        }

                        if (resolvedModuleInstance is not null)
                        {
                            _logger.LogInformation("Scoped provider resolved TicketModule for command {CommandName}", commandLogName);
                        }
                        else
                        {
                            _logger.LogWarning("Scoped provider returned null when resolving TicketModule for command {CommandName}", commandLogName);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("No slash command metadata found for command {CommandName} before execution.", commandLogName);
                }
            }
            else
            {
                _logger.LogWarning("Could not determine command name for interaction {InteractionId} before execution.", interaction.Id);
            }

            _logger.LogInformation("Executing command {CommandName} for interaction {InteractionId}", commandLogName, interaction.Id);
            // ExecuteCommandAsync should properly await the async method, but we need to ensure the scope stays alive
            IResult result;
            try
            {
                result = await _interactionService.ExecuteCommandAsync(context, scope.ServiceProvider);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during ExecuteCommandAsync for interaction {InteractionId}: {ExceptionType}, {Message}", interaction.Id, ex.GetType().Name, ex.Message);
                throw;
            }
            
            _logger.LogInformation("Command execution result: {Success} {Error} {Reason} for interaction {InteractionId}",
                result.IsSuccess, result.Error, result.ErrorReason, interaction.Id);
            
            // If result is success but method didn't execute (no response), manually invoke it
            if (result.IsSuccess && !interaction.HasResponded && commandName == "open-ticket")
            {
                _logger.LogWarning("Command {CommandName} reported success but interaction {InteractionId} has not been responded to. Manually invoking method as fallback.", commandLogName, interaction.Id);
                
                try
                {
                    // Manually resolve and invoke the internal method directly with context
                    var ticketModule = scope.ServiceProvider.GetService<TicketModule>();
                    if (ticketModule != null)
                    {
                        // Invoke the internal method directly with the context (no need to set Context property)
                        var method = typeof(TicketModule).GetMethod("CreateTicketInternalAsync", BindingFlags.Public | BindingFlags.Instance);
                        if (method != null)
                        {
                            _logger.LogInformation("Manually invoking CreateTicketInternalAsync for interaction {InteractionId}", interaction.Id);
                            // Invoke and handle the task
                            var task = (Task)method.Invoke(ticketModule, new object[] { context })!;
                            
                            // Fire and forget with error handling
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await task;
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error in manually invoked CreateTicketInternalAsync for interaction {InteractionId}", interaction.Id);
                                }
                            });
                        }
                        else
                        {
                            _logger.LogError("Could not find CreateTicketInternalAsync method via reflection");
                        }
                    }
                    else
                    {
                        _logger.LogError("Could not resolve TicketModule for manual invocation");
                    }
                }
                catch (Exception manualInvokeEx)
                {
                    _logger.LogError(manualInvokeEx, "Failed to manually invoke CreateTicketAsync for interaction {InteractionId}", interaction.Id);
                }
            }
            
            // Log detailed result information if it's an ExecuteResult
            if (result is ExecuteResult executeResult)
            {
                if (executeResult.Exception != null)
                {
                    _logger.LogError(executeResult.Exception, "Command {CommandName} execution raised an exception: {ExceptionType}, Message: {Message}", 
                        commandLogName, executeResult.Exception.GetType().Name, executeResult.Exception.Message);
                }
                _logger.LogDebug("ExecuteResult details - IsSuccess: {IsSuccess}, Error: {Error}, ErrorReason: {ErrorReason}, Exception: {HasException}",
                    executeResult.IsSuccess, executeResult.Error, executeResult.ErrorReason, executeResult.Exception != null);
            }
            
            if (result.IsSuccess)
            {
                _logger.LogInformation("Slash command {CommandName} executed successfully. Handler responded: {HasResponded}. ErrorReason={ErrorReason}", commandLogName, interaction.HasResponded, result.ErrorReason ?? "none");
                
                // Don't send fallback response here - the module handles its own responses
                // The async method may still be executing and will handle deferring/responding
                
                // Check if interaction was deferred but not followed up on (after a delay)
                if (interaction.HasResponded && interaction is SocketSlashCommand slashCmd)
                {
                    try
                    {
                        // Try to get the original response to see if it was properly updated
                        var originalResponse = await slashCmd.GetOriginalResponseAsync();
                        _logger.LogDebug("Original response retrieved for command {CommandName}: {Content}", commandLogName, originalResponse?.Content ?? "null");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not retrieve original response for command {CommandName}. This might indicate the response was not properly updated.", commandLogName);
                    }
                }
            }
            
            if (!result.IsSuccess)
            {
                _logger.LogWarning("Interaction execution failed: {Reason}", result.ErrorReason);
                if (result.Error == InteractionCommandError.UnknownCommand)
                {
                    _logger.LogWarning("No command handler found for interaction command {CommandName}.", commandLogName);
                }
                if (result is ExecuteResult failedExecuteResult && failedExecuteResult.Exception != null)
                {
                    _logger.LogError(failedExecuteResult.Exception, "Command {CommandName} execution raised an exception.", commandLogName);
                    
                    // If interaction was deferred but exception occurred, try to send error response
                    if (interaction.HasResponded && interaction is SocketSlashCommand failedSlashCmd)
                    {
                        try
                        {
                            await failedSlashCmd.ModifyOriginalResponseAsync(props =>
                            {
                                props.Content = "An error occurred while executing the command. Please try again later.";
                            });
                            _logger.LogInformation("Sent error response for failed command {CommandName}", commandLogName);
                        }
                        catch (Exception modifyEx)
                        {
                            _logger.LogError(modifyEx, "Failed to send error response for failed command {CommandName}", commandLogName);
                        }
                    }
                }
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while executing interaction.");
            if (interaction.Type == InteractionType.ApplicationCommand && !interaction.HasResponded)
            {
                try
                {
                    await interaction.RespondAsync("Sorry, something went wrong while executing that command.", ephemeral: true);
                }
                catch
                {
                    // ignored
                }
            }
        }
    }

    private async Task OnMessageReceivedAsync(SocketMessage rawMessage)
    {
        if (rawMessage.Source != MessageSource.User)
        {
            return;
        }

        if (rawMessage is not SocketUserMessage message || message.Channel is not SocketGuildChannel guildChannel)
        {
            return;
        }

        if (!_ticketChannelCache.IsTicketChannel(guildChannel.Id))
        {
            return;
        }

        try
        {
            var messageContent = message.Content ?? string.Empty;
            var trimmedContent = messageContent.Trim();

            string contentToStore;
            if (string.IsNullOrWhiteSpace(trimmedContent))
            {
                if (message.Attachments.Count == 0)
                {
                    _logger.LogDebug("Skipping message {MessageId} in channel {ChannelId}: no content or attachments.", message.Id, guildChannel.Id);
                    return;
                }

                var attachmentSummary = string.Join(", ", message.Attachments.Select(a => $"{a.Filename}: {a.Url}"));
                contentToStore = $"[Attachments] {attachmentSummary}";
                if (contentToStore.Length > 2000)
                {
                    _logger.LogWarning("Attachment summary truncated for message {MessageId} in channel {ChannelId}", message.Id, guildChannel.Id);
                    contentToStore = contentToStore[..2000];
                }
            }
            else
            {
                contentToStore = messageContent;
            }

            await _apiClient.StoreMessageAsync(
                guildChannel.Id.ToString(),
                message.Author.Id,
                contentToStore,
                message.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store message {MessageId} in channel {ChannelId}", message.Id, guildChannel.Id);
        }
    }

    private Task OnInteractionLogAsync(LogMessage message)
    {
        _logger.Log(MapLogSeverity(message.Severity), "{Source}: {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }

    private Task OnLoggedInAsync()
    {
        var currentUser = _client.CurrentUser;
        if (currentUser is null)
        {
            _logger.LogInformation("Discord client logged in.");
        }
        else
        {
            _logger.LogInformation("Discord client logged in as {Username}#{Discriminator} ({Id})", currentUser.Username, currentUser.Discriminator, currentUser.Id);
        }
        return Task.CompletedTask;
    }

    private Task OnConnectedAsync()
    {
        _logger.LogInformation("Discord gateway connected.");
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is null)
        {
            _logger.LogWarning("Discord gateway disconnected without an exception.");
        }
        else
        {
            _logger.LogWarning(exception, "Discord gateway disconnected due to an exception.");
        }

        return Task.CompletedTask;
    }

    private Task OnSlashCommandExecutedAsync(SlashCommandInfo command, IInteractionContext context, IResult result)
    {
        if (result.IsSuccess)
        {
            _logger.LogInformation("Slash command executed: {CommandName} (method {MethodName}) by {UserId} in guild {GuildId}", command.Name, command.MethodName, context.User.Id, (context.Guild?.Id ?? 0));
        }
        else
        {
            _logger.LogWarning("Slash command {CommandName} failed for user {UserId} in guild {GuildId}: {Error} {Reason}",
                command.Name,
                context.User.Id,
                context.Guild?.Id ?? 0,
                result.Error,
                result.ErrorReason);
        }

        return Task.CompletedTask;
    }

    private void LogLoadedInteractionModules()
    {
        foreach (var module in _interactionService.Modules)
        {
            var commandNames = module.SlashCommands.Select(c => $"{c.Name} (method {c.MethodName})").ToArray();
            var commandsDescription = commandNames.Length > 0 ? string.Join(", ", commandNames) : "none";
            _logger.LogInformation("Loaded interaction module {ModuleName} with slash commands: {Commands}", module.Name, commandsDescription);
        }
    }

    private void LogRegisteredSlashCommands()
    {
        foreach (var command in _interactionService.SlashCommands)
        {
            _logger.LogInformation("Registered slash command {CommandName} in module {ModuleName}", command.Name, command.Module.Name);
        }
    }

    /// <summary>
    /// Removes all guild slash commands for manual cleanup scenarios. Do not invoke during normal startup.
    /// </summary>
    private async Task DeleteGuildCommandsAsync(ulong guildId)
    {
        try
        {
            await _client.Rest.BulkOverwriteGuildCommands(Array.Empty<ApplicationCommandProperties>(), guildId, null);
            _logger.LogInformation("Deleted all existing guild slash commands for guild {GuildId}", guildId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete guild slash commands for guild {GuildId}", guildId);
        }
    }

    /// <summary>
    /// Removes all global slash commands for manual cleanup scenarios. Do not invoke during normal startup.
    /// </summary>
    private async Task DeleteGlobalCommandsAsync()
    {
        try
        {
            await _client.Rest.BulkOverwriteGlobalCommands(Array.Empty<ApplicationCommandProperties>(), null);
            _logger.LogInformation("Deleted all existing global slash commands.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete global slash commands.");
        }
    }

    private static LogLevel MapLogSeverity(LogSeverity severity) =>
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
}