using System.Collections.Concurrent;
using AmazoniaApi.Bot.Models;
using AmazoniaApi.Core.Models;

namespace AmazoniaApi.Bot.Services;

public sealed class TicketChannelCache
{
    private readonly ConcurrentDictionary<ulong, TicketInfo> _tickets = new();

    public bool AddTicket(ulong channelId, ulong creatorDiscordId, TicketStatus status)
    {
        var info = new TicketInfo(channelId, creatorDiscordId, status);
        return _tickets.TryAdd(channelId, info);
    }

    public bool AddOrUpdateTicket(TicketInfo info)
    {
        _tickets.AddOrUpdate(info.ChannelId, info, (_, _) => info);
        return true;
    }

    public bool RemoveTicket(ulong channelId) => _tickets.TryRemove(channelId, out _);

    public bool IsTicketChannel(ulong channelId) => _tickets.ContainsKey(channelId);

    public TicketInfo? GetTicketInfo(ulong channelId) =>
        _tickets.TryGetValue(channelId, out var info) ? info : null;

    public bool UpdateTicketStatus(ulong channelId, TicketStatus newStatus)
    {
        if (!_tickets.TryGetValue(channelId, out var info))
        {
            return false;
        }

        info.UpdateStatus(newStatus);
        return true;
    }

    public IEnumerable<TicketInfo> GetAllTickets() => _tickets.Values.ToArray();

    public void Clear() => _tickets.Clear();
}

