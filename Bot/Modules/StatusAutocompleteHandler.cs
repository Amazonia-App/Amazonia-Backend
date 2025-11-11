using System;
using System.Linq;
using AmazoniaApi.Bot.Services;
using AmazoniaApi.Core.Models;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AmazoniaApi.Bot.Modules;

public class StatusAutocompleteHandler : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        try
        {
            var logger = services.GetService<ILogger<StatusAutocompleteHandler>>();
            logger?.LogInformation("StatusAutocompleteHandler.GenerateSuggestionsAsync called for parameter {ParameterName}", parameter?.Name ?? "unknown");

            // Get services from DI
            var ticketChannelCache = services.GetRequiredService<TicketChannelCache>();

            // Check if this is a ticket channel - if not, return empty results
            var channelId = context.Channel?.Id ?? 0;
            if (channelId == 0)
            {
                logger?.LogWarning("StatusAutocompleteHandler: Channel ID is 0");
                return Task.FromResult(AutocompletionResult.FromSuccess(Enumerable.Empty<AutocompleteResult>()));
            }

            // Check if channel is a ticket channel
            var isTicketChannel = ticketChannelCache.IsTicketChannel(channelId);
            if (!isTicketChannel)
            {
                // For autocomplete, we'll allow it to proceed - the command execution will validate
                // This prevents blocking autocomplete on slow API calls
                logger?.LogDebug("StatusAutocompleteHandler: Channel {ChannelId} is not in ticket cache, but proceeding", channelId);
            }

            var statusNames = Enum.GetNames<TicketStatus>();
            var userInput = autocompleteInteraction.Data.Current.Value as string ?? "";

            logger?.LogInformation("StatusAutocompleteHandler: User input: '{UserInput}', Available statuses: {StatusCount}", userInput, statusNames.Length);

            var choices = statusNames
                .Where(name => name.Contains(userInput, StringComparison.OrdinalIgnoreCase))
                .Select(name => new AutocompleteResult(name, name))
                .Take(25) // Discord limit is 25 choices
                .ToList();

            // If no input or all match, show all statuses
            if (string.IsNullOrWhiteSpace(userInput) || choices.Count == statusNames.Length)
            {
                choices = statusNames
                    .Select(name => new AutocompleteResult(name, name))
                    .Take(25)
                    .ToList();
            }

            logger?.LogInformation("StatusAutocompleteHandler: Returning {ChoiceCount} choices", choices.Count);

            return Task.FromResult(AutocompletionResult.FromSuccess(choices));
        }
        catch (Exception ex)
        {
            var logger = services.GetService<ILogger<StatusAutocompleteHandler>>();
            logger?.LogError(ex, "Exception in StatusAutocompleteHandler.GenerateSuggestionsAsync: {Message}", ex.Message);
            
            // Always return a valid result, even on error - return empty list to prevent "Loading Options Failed"
            return Task.FromResult(AutocompletionResult.FromSuccess(Enumerable.Empty<AutocompleteResult>()));
        }
    }
}

