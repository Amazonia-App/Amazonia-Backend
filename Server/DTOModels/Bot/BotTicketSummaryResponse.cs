using AmazoniaApi.Core.Models;

namespace AmazoniaApi.Server.DTOModels.Bot;

public sealed record BotTicketSummaryResponse(string ChannelId, ulong CreatorDiscordId, TicketStatus Status, string Title);

