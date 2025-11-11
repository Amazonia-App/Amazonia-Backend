# Amazonia Discord Bot

## Overview

The Amazonia Discord bot provides support ticket automation for the Amazonia platform. It integrates with Discord via Discord.Net and communicates with the Amazonia API over HTTP and gRPC to keep ticket channels synchronized with the backend database.

Key capabilities:
- Slash command (`/open-ticket`) for end users to open private support channels.
- Automatic channel configuration, slowmode, and role-based permissions.
- Real-time ticket message ingestion and persistence through the Server API.
- gRPC surface for the API to push outbound messages and request message verification.

## Prerequisites

- .NET SDK 9.0 or later.
- Discord bot token with the required intents enabled.
- Running instance of the Amazonia Server API (development or production).
- Shared API key configured on both the bot and server.

## Configuration

1. Copy `appsettings.Development.json` and update the placeholders:
   - `Discord:BotToken`: your Discord bot token.
   - `Discord:GuildId`: optional guild ID for instant slash command updates (use `0` for global).
   - `Discord:StaffRoleId` or `Discord:StaffRoleName`: role granted access to ticket channels.
   - `Api:BaseUrl`: URL to the Amazonia Server API (e.g., `https://localhost:7088`).
   - `Api:ApiKey`: shared secret matching `Server:Bot:ApiKey`.
   - `Grpc:Port`: port for the bot's gRPC listener (default `5100`).

2. Environment variable overrides (useful for production deployments):
   - `AMAZONIA_BOT_Discord__BotToken`
   - `AMAZONIA_BOT_Discord__GuildId`
   - `AMAZONIA_BOT_Discord__StaffRoleId`
   - `AMAZONIA_BOT_Discord__StaffRoleName`
   - `AMAZONIA_BOT_Api__BaseUrl`
   - `AMAZONIA_BOT_Api__ApiKey`
   - `AMAZONIA_BOT_Grpc__Port`

## Discord Bot Setup

1. Create an application at the [Discord Developer Portal](https://discord.com/developers/applications).
2. Add a bot user and copy the token.
3. Enable privileged gateway intents: **Guilds**, **Guild Messages**, and **Message Content**.
4. Grant the bot permissions: Manage Channels, Manage Roles, Send Messages, View Channels, Read Message History.
5. Generate an OAuth2 invite URL with scopes `bot` and `applications.commands` and invite the bot to your server.

## Running the Bot

```bash
dotnet restore
dotnet run --project ./Bot/Bot.csproj
```

The console logs indicate when the bot is connected, registered commands, and initialized the ticket cache.

## Architecture

- `Program.cs`: Configures the .NET generic host, logging, HttpClient, gRPC endpoint, and Discord client.
- `DiscordBotService`: Background service managing the Discord connection, slash commands, and message ingestion.
- `BotGrpcService`: gRPC service responding to server requests (send messages, verify ticket contents).
- `ApiClient`: Wrapper around HttpClient for authenticated calls to the Server API.
- `TicketModule`: Slash command handler for ticket creation.
- `TicketChannelCache`: In-memory cache of active ticket channels for fast lookups.
- `ChannelPermissionManager`: Centralized logic for managing channel permission overwrites.

## Commands

- `/open-ticket`
  - Creates a private text channel for the user.
  - Applies 5-second slowmode and Staff role access.
  - Persists ticket metadata to the Amazonia backend.
  - Sends a welcome message with next steps.

## Troubleshooting

- **Bot not responding**: Verify the token, intents, and that the bot is online in Discord.
- **Slash commands missing**: Invite with `applications.commands` scope and ensure guild/global registration mode matches configuration.
- **Permission errors**: Confirm the bot role sits above the roles it needs to manage and has Manage Channels / Manage Roles permissions.
- **API failures**: Check the API base URL and API key align with the server configuration. Review server logs for validation errors.
- **gRPC connectivity issues**: Ensure the configured gRPC port is open and not blocked by firewalls. The server must target the same address.

## Development

- Additional slash commands can be added by creating new modules under `Bot/Modules` and registering them through the interaction service.
- Ticket behavior (welcome message, cache logic, permissions) can be tuned in the corresponding service classes.
- Unit testing can leverage Discord.Net test utilities or mock interfaces for the HTTP/gRPC layers.

## Production Deployment

- Store secrets (bot token, API key) in environment variables or a secret manager.
- Register commands globally (`Discord:GuildId = 0`) once stable.
- Run the bot under a process manager (systemd, Docker, Kubernetes) with health monitoring.
- Monitor logs for Discord API rate limits or authentication failures.

