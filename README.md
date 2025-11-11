## Amazonia API

Discord-integrated API for Minecraft server community management.

Badges: .NET 9.0 • ASP.NET Core • EF Core • SQLite

### Overview

Amazonia API is a backend system for managing a Minecraft server community. It provides Discord OAuth authentication, an in-game economy/banking system, transaction tracking, support ticket management, and user account management with Minecraft username linking. It targets Minecraft server administrators and community managers.

### Features

- **Discord OAuth Authentication**: Seamless login using Discord accounts.
- **Banking System**: Manage in-game currency with balance transfers and admin controls for setting/changing balances.
- **Transaction History**: Track all financial transactions between users (see `Core/Models/Transaction.cs`).
- **Support Ticket System**: Users can create tickets, add messages; admins manage ticket status (see `Core/Models/Ticket.cs` and `Core/Models/Message.cs`).
- **Role-Based Access Control**: Member and Admin roles with different permissions (see `Core/Models/Roles.cs`).
- **User Account Management**: Link Discord accounts with Minecraft usernames (see `Core/Models/AppUser.cs`).
- **RESTful API**: Well-documented endpoints with Swagger/OpenAPI support. See `API_DOCUMENTATION.md`.

### Architecture

- **Server** (`Server/`): ASP.NET Core Web API
  - Controllers (`Server/Controllers/`): `AuthController`, `BankController`, `TicketController`, `AccountController`
  - Handlers (`Server/Handlers/`): Business logic (`AccountHandler`, `BankHandler`, `HelperMethods`)
  - Database Context (`Server/DBContext/AppDbContext.cs`): Entity Framework Core context
  - DTOs (`Server/DTOModels/`): API request/response models
  - Migrations (`Server/Migrations/`): Database schema migrations
- **Core** (`Core/`): Shared library
  - Models (`Core/Models/`): `AppUser`, `Transaction`, `Ticket`, `Message`, `TicketStatus`, `Roles`
  - Interfaces (`Core/Interfaces/`): `IAccountHandler`, `IBankHandler`, `IHelperMethods`
- **Bot** (`Bot/`): Discord bot integration project (gRPC-based communication)

### Technology Stack

- **.NET 9.0**: Target framework
- **ASP.NET Core**: Web API framework
- **Entity Framework Core 9.0**: ORM with SQLite provider
- **ASP.NET Core Identity**: Authentication and authorization
- **Discord OAuth** (AspNet.Security.OAuth.Discord)
- **SQLite**: Database engine
- **Swagger/OpenAPI**: API documentation and testing
- **gRPC**: Inter-service communication for bot integration

### Prerequisites

- .NET 9.0 SDK or later
- A Discord application with OAuth2 credentials (Client ID and Client Secret)
- SQLite (included with .NET)
- IDE: Visual Studio 2022, JetBrains Rider, or VS Code with C# extensions
- Optional: mkcert or similar tool for local HTTPS certificates

### Getting Started

#### 1. Clone the Repository

```bash
git clone <your-repo-url>
cd AmazoniaApi
```

#### 2. Discord Application Setup

Step-by-step:

- Navigate to Discord Developer Portal (`https://discord.com/developers/applications`)
- Create a new application
- Navigate to OAuth2 section
- Add redirect URI: `https://localhost:7088/signin-discord` (or your configured URL)
- Copy Client ID and Client Secret for configuration
- Required OAuth2 scopes: `identify` and `email`

#### 3. Configuration

Copy and update configuration:

- Copy `Server/appsettings.Development.example.json` to `Server/appsettings.Development.json`
- Update the following in `Server/appsettings.Development.json`:
  - **Discord:ClientId**: Your Discord application Client ID
  - **Discord:ClientSecret**: Your Discord application Client Secret
  - **ConnectionStrings:DefaultConnection**: Database path (default: `Data Source=Core/SqliteTestServers/app.db`)
  - **Cors:AllowedOrigins**: Array of allowed frontend URLs (e.g., `["http://localhost:3000"]`)
  - **FrontendUrl**: Your frontend application URL
  - **Kestrel:Endpoints:Https:Url**: HTTPS endpoint URL (default: `https://localhost:7088`)
  - **Kestrel:Certificates**: Optional SSL certificate paths for local HTTPS

#### 4. Database Setup

- Uses Entity Framework Core migrations
- Database is automatically created and migrated on first run (see `Server/Program.cs` lines 252-257)
- SQLite database file is created at `ConnectionStrings:DefaultConnection`
- Default roles (Member, Admin) are seeded automatically via `Server/Seeders/RoleSeeder.cs`

#### 5. SSL Certificates (Optional)

For local HTTPS development:

- Option 1: Use .NET development certificates (automatic in development mode)
- Option 2: Generate custom certificates using mkcert and place in `Server/certs/`
- Update certificate paths in `appsettings.Development.json` under `Kestrel:Certificates`

#### 6. Build and Run

```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run the Server project
cd Server
dotnet run
```

#### 7. Access the Application

- API Base URL: `https://localhost:7088` (HTTPS) or `http://localhost:5041` (HTTP in development)
- Swagger UI: `https://localhost:7088/swagger` (available in development mode)
- All API endpoints are prefixed with `/api`

### API Documentation

- Detailed API documentation is available in `API_DOCUMENTATION.md`.
- Covers all endpoints, authentication requirements, request/response formats, validation rules, and error handling.
- Interactive API testing available via Swagger UI when running in development mode.

### Development

#### Project Structure

- **Controllers**: Handle HTTP requests/responses, delegate business logic to handlers
- **Handlers**: Implement business logic, interact with database through DbContext
- **DTOs**: API contracts for requests/responses (`Server/DTOModels/`)
- **Models**: Domain entities and database tables (`Core/Models/`)
- **Interfaces**: Contracts for dependency injection (`Core/Interfaces/`)

#### Adding Migrations

```bash
cd Server
dotnet ef migrations add MigrationName
dotnet ef database update
```

#### Running Tests

- Currently no test projects in the solution
- Consider adding unit tests for handlers and integration tests for controllers

#### Code Style and Conventions

- Follow C# naming conventions (PascalCase for classes/methods, camelCase for parameters)
- Use nullable reference types (enabled in all projects)
- Leverage dependency injection for services
- Keep controllers thin; move business logic to handlers
- Use DTOs for all API contracts to separate domain models from API surface

### Authentication Flow

1. User initiates login via `GET /api/Auth/login`.
2. User is redirected to Discord authorization page.
3. After approval, Discord redirects to `GET /api/Auth/callback` with an authorization code.
4. Server exchanges the code for user information.
5. User record is created/updated in the database (see `Core/Models/AppUser.cs`).
6. User is signed in via ASP.NET Core Identity with a secure cookie.
7. Subsequent requests include the authentication cookie for authorization.

### Security Considerations

- Authentication cookies are HTTP-only and secure (see `Server/Program.cs` lines 157-164)
- CORS is configured to allow specific origins only (see `Server/appsettings.Development.json` under `Cors:AllowedOrigins`)
- Admin endpoints require `Admin` role authorization
- Discord OAuth state parameter is validated to prevent CSRF attacks
- Sensitive data logging is only enabled in development mode
- Balance mutations use optimistic concurrency control via RowVersion (see `AppUser.RowVersion` in `Core/Models/AppUser.cs`)

### Deployment

- Set environment to Production
- Configure production `appsettings.json` with:
  - Production database connection string
  - Production Discord OAuth redirect URIs
  - Production CORS origins
  - Valid SSL certificates
- Disable Swagger in production (automatically disabled, see `Server/Program.cs` lines 268-272)
- Use HTTPS redirection in production (automatically enabled, see `Server/Program.cs` line 275)
- Consider using environment variables for sensitive configuration
- Ensure database backups are configured
- Monitor application logs for authentication and admin operations

### Contributing

- Fork the repository and create feature branches
- Follow existing code style and conventions
- Update `API_DOCUMENTATION.md` when adding/modifying endpoints
- Test changes locally before submitting pull requests
- Include migrations for database schema changes

### License

Specify license information (if applicable), or add a license file.

### Support

- For API usage questions, refer to `API_DOCUMENTATION.md`.
- For bugs or feature requests, open an issue in the repository.
- For Discord OAuth setup issues, consult the Discord Developer Documentation.

### Acknowledgments

Thanks to the authors and maintainers of ASP.NET Core, EF Core, SQLite, Swagger/OpenAPI, and the Discord OAuth provider libraries.


