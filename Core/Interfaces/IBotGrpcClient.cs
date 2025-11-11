using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AmazoniaApi.Core.Interfaces;

public interface IBotGrpcClient
{
    Task<(bool success, ulong discordMessageId, string? errorMessage)> SendMessageToChannelAsync(
        string channelId,
        string content,
        int databaseMessageId,
        CancellationToken cancellationToken = default);

    Task<(bool allVerified, List<int> missingMessageIds, List<int> mismatchedMessageIds, string? errorMessage)> VerifyTicketMessagesAsync(
        string channelId,
        List<(int databaseMessageId, ulong discordMessageId, string content)> messages,
        CancellationToken cancellationToken = default);

    Task<(bool success, string? errorMessage)> UpdateChannelPermissionsAsync(
        string channelId,
        bool readOnly,
        CancellationToken cancellationToken = default);

    Task<(bool success, List<(ulong discordMessageId, string content, ulong authorDiscordId, long timestamp)> messages, string? errorMessage)> GetAllChannelMessagesAsync(
        string channelId,
        CancellationToken cancellationToken = default);

    Task<(bool success, string? channelId, string? errorMessage)> CreateTicketChannelAsync(
        ulong discordUserId,
        string title,
        CancellationToken cancellationToken = default);

    Task<(bool success, string? errorMessage)> MoveTicketChannelToCategoryAsync(
        string channelId,
        string status,
        CancellationToken cancellationToken = default);
}
