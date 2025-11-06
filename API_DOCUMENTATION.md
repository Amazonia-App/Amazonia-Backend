## Amazonia API Documentation

This document describes all public API endpoints, their authentication requirements, request/response formats, validation rules, data models, and error formats. It aggregates information from the controllers and DTO models referenced in the implementation.

### Base URL
- Production: https://your-domain.example
- Development: https://localhost:5001 (HTTPS) or http://localhost:5000 (HTTP)

All endpoints are prefixed with `/api` as shown below.

### Authentication
- The API uses Discord OAuth for authentication.
- Sessions are maintained via secure cookies set during the OAuth flow.
- Some endpoints require the caller to be an authenticated user; a subset requires the `Admin` role.

---

## Authentication Controller

Base route: `/api/Auth`

### 1) Login with Discord
- Method: GET
- Path: `/api/Auth/login`
- Description: Starts the Discord OAuth flow by issuing an authentication challenge with the "Discord" scheme and redirects the user to Discord's authorization page. The optional `returnUrl` parameter is currently ignored.
- Authentication: None (public)
- Query Parameters: `returnUrl` (string, optional, currently ignored)
- Request Body: None
- Success Response (302 Found): Redirects the client to Discord OAuth consent screen (Discord provider).
- Error Responses:
  - 500 Internal Server Error: Unexpected server error initiating OAuth

### 2) Discord OAuth Callback
- Method: GET
- Path: `/api/Auth/callback`
- Description: OAuth callback endpoint that completes the login and sets the session cookie, then redirects to the configured frontend URL.
- Authentication: None (public)
- Query Parameters: Standard OAuth return parameters (handled by middleware)
- Request Body: None
- Success Response (302 Found): Redirects to `FrontendUrl` from configuration.
- Error Responses:
  - 302 Found: Redirects to `/login?error=external_login_failed` when external auth fails
  - 500 Internal Server Error (JSON): `{ "message": "Frontend URL not configured" }`

### 3) Check Authentication Status
- Method: GET
- Path: `/api/Auth/Check`
- Description: Returns whether the current session is authenticated and the basic user info.
- Authentication: None (public)
- Query Parameters: None
- Request Body: None
- Success Response (200 OK):
```json
{
  "isAuthenticated": true,
  "user": {
    "id": "string",
    "discordId": 1234567890,
    "discordName": "string|null",
    "minecraftUsername": "string",
    "balance": 0.0,
    "roles": ["Admin", "Staff"]
  }
}
```
- Error Responses:
  - 200 OK with `{ "isAuthenticated": false }` when no user is logged in

### 4) Logout
- Method: POST
- Path: `/api/Auth/logout`
- Description: Invalidates the session cookie.
- Authentication: Authenticated user required
- Request Body: None
- Success Response (200 OK): `{ "message": "Logged out" }`
- Error Responses:
  - 401 Unauthorized: Not logged in

### 5) Get Current User Roles
- Method: GET
- Path: `/api/Auth/getRoles`
- Description: Returns the authenticated user's roles.
- Authentication: Authenticated user required
- Request Body: None
- Success Response (200 OK): `["Admin", "Staff"]`
- Error Responses:
  - 401 Unauthorized: Not logged in

### 6) Get All Roles
- Method: GET
- Path: `/api/Auth/getAllRoles`
- Description: Returns all available roles in the system.
- Authentication: Admin role required
- Request Body: None
- Success Response (200 OK): `{ "roles": ["Admin", "Staff", "MinecraftNameAdded"] }`
- Error Responses:
  - 401 Unauthorized: Not logged in
  - 403 Forbidden: Not an admin

### 7) Add User to Role
- Method: POST
- Path: `/api/Auth/AddToRole`
- Description: Adds a user to a specific role.
- Authentication: Admin role required
- Request Body (JSON):
```json
{
  "userId": "string",
  "role": "Admin"
}
```
- Validation:
  - `userId`: required, non-empty
  - `role`: required, one of available roles (see Roles below)
- Success Response (200 OK):
```json
{
  "message": "User added to role"
}
```
- Error Responses:
  - 400 Bad Request: Invalid role or user
  - 401 Unauthorized: Not logged in
  - 403 Forbidden: Not an admin
  - 404 Not Found: User not found

---

## Bank Controller

Base route: `/api/Bank`

### 1) Get Balance
- Method: GET
- Path: `/api/Bank/getBalance`
- Description: Gets the caller's current balance.
- Authentication: Authenticated user required
- Success Response (200 OK):
```json
{
  "balance": 0.0
}
```
- Error Responses:
  - 401 Unauthorized: Not logged in

### 2) Send Balance
- Method: POST
- Path: `/api/Bank/sendBalance`
- Description: Sends a positive amount from the caller to another user.
- Authentication: Authenticated user required
- Request Body (JSON) — SendBalanceRequest:
```json
{
  "receiverId": "string",
  "amount": 10.51
}
```
- Validation:
  - `receiverId`: required, non-empty
  - `amount`: required, decimal >= 10.01, supports 2 decimal places
- Success Response (200 OK):
`{ "message": "Transaction successful" }`
- Error Responses:
  - 400 Bad Request: Validation failure (e.g., amount <= 0)
  - 401 Unauthorized: Not logged in
  - 404 Not Found: Recipient user not found
  - 409 Conflict: Insufficient funds

### 3) Change Balance
- Method: POST
- Path: `/api/Bank/changeBalance`
- Description: Adjusts a user's balance by a positive or negative delta.
- Authentication: Admin role required
- Request Body (JSON) — ChangeBalanceRequest:
```json
{
  "userId": "string",
  "amount": -5.25
}
```
- Validation:
  - `userId`: required
  - `amount`: required, non-zero decimal, supports 2 decimal places
- Success Response (200 OK):
`{ "message": "Balance changed successfully" }`
- Error Responses:
  - 400 Bad Request: Validation failure
  - 401 Unauthorized: Not logged in
  - 403 Forbidden: Not an admin
  - 404 Not Found: User not found

### 4) Set Balance
- Method: POST
- Path: `/api/Bank/setBalance`
- Description: Sets a user's balance to an exact value.
- Authentication: Admin role required
- Request Body (JSON) — SetBalanceRequest:
```json
{
  "userId": "string",
  "amount": 150.00
}
```
- Validation:
  - `userId`: required
  - `amount`: required, decimal >= 0, supports 2 decimal places
- Success Response (200 OK):
`{ "message": "Balance set successfully" }`
- Error Responses:
  - 400 Bad Request: Validation failure
  - 401 Unauthorized: Not logged in
  - 403 Forbidden: Not an admin
  - 404 Not Found: User not found

### 5) Get Transactions
- Method: GET
- Path: `/api/Bank/getTransactions`
- Description: Returns recent transactions for the authenticated user.
- Authentication: Authenticated user required
- Query Parameters:
  - `amount` (int, optional, default 10)
- Success Response (200 OK):
```json
{
  "transactions": [
    {
      "id": 1,
      "senderId": "string",
      "senderDiscordName": "string",
      "receiverId": "string",
      "receiverDiscordName": "string",
      "amount": 12.34,
      "timestamp": "2025-01-01T12:00:00Z"
    }
  ]
}
```

---

## Ticket Controller

Base route: `/api/Ticket`

### 1) Create Ticket
- Method: POST
- Path: `/api/Ticket`
- Description: Creates a new ticket entry.
- Authentication: Admin role required
- Request Body (JSON) — CreateTicketRequest:
```json
{
  "channelId": "string"
}
```
- Validation:
  - `channelId`: required
- Success Response (200 OK): Ticket object
```json
{
  "channelId": "string",
  "status": "Created",
  "creatorId": "string",
  "messages": []
}
```
- Error Responses:
  - 400 Bad Request: Validation failure
  - 401 Unauthorized: Not logged in

### 2) Add Message to Ticket
- Method: POST
- Path: `/api/Ticket/AddMessage`
- Description: Adds a message to an existing ticket.
- Authentication: Admin role required
- Request Body (JSON) — AddMessageRequest:
```json
{
  "channelId": "string",
  "content": "string"
}
```
- Validation:
  - `channelId`: required
  - `content`: required, length 1-5000
- Success Response (200 OK):
```json
{
  "id": 1,
  "senderId": "string",
  "content": "string",
  "timestamp": "2025-01-01T12:00:00Z"
}
```
- Error Responses:
  - 400 Bad Request: Validation failure
  - 401 Unauthorized: Not logged in
  - 404 Not Found: Ticket not found

### 3) Update Ticket Status
- Method: PUT
- Path: `/api/Ticket/UpdateStatus`
- Description: Updates the status of a ticket.
- Authentication: Admin role required
- Request Body (JSON) — UpdateTicketStatusRequest:
```json
{
  "channelId": "string",
  "status": "Open"
}
```
- Validation:
  - `channelId`: required
  - `status`: required, one of TicketStatus enum values
- Success Response (200 OK):
```json
{
  "channelId": "string",
  "status": "Closed"
}
```
- Error Responses:
  - 400 Bad Request: Validation failure
  - 401 Unauthorized: Not logged in
  - 403 Forbidden: Not an admin
  - 404 Not Found: Ticket not found

---

## Account Controller

Base route: `/api/Account`

### 1) Change Minecraft Username
- Method: PUT
- Path: `/api/Account/changeMinecraftUsername`
- Description: Updates the authenticated user's Minecraft username.
- Authentication: Authenticated user required
- Request Body (JSON) — ChangeMinecraftUsernameRequest:
```json
{
  "minecraftUsername": "string"
}
```
- Validation:
  - `minecraftUsername`: required, 1-16 characters; no regex enforced server-side
- Success Response (200 OK):
`{ "message": "Minecraft username updated successfully" }`
- Error Responses:
  - 400 Bad Request: Validation failure
  - 401 Unauthorized: Not logged in

### 2) Get User
- Method: GET
- Path: `/api/Account/getUser`
- Description: Fetches a user's profile by ID.
- Authentication: Authenticated user required
- Query Parameters:
  - `userId` (string, required)
- Success Response (200 OK): AppUserDto
```json
{
  "id": "string",
  "discordId": 1234567890,
  "discordName": "string|null",
  "minecraftUsername": "string",
  "balance": 0.0,
  "roles": ["Admin"]
}
```
- Error Responses:
  - 401 Unauthorized: Not logged in

### 3) Find User
- Method: GET
- Path: `/api/Account/findUser`
- Description: Finds users by a search term for admin operations.
- Authentication: Admin role required
- Query Parameters:
  - `query` (string, required): search by username, discord id, etc.
  - `page` (int, optional, default 1)
  - `pageSize` (int, optional, default 10, max 100)
- Success Response (200 OK): PagedResult<AppUserDto>
```json
{
  "items": [
    {
      "id": "string",
      "discordId": 1234567890,
      "discordName": "string|null",
      "minecraftUsername": "string",
      "balance": 0.0,
      "roles": ["Admin"]
    }
  ],
  "page": 1,
  "pageSize": 10,
  "totalCount": 1,
  "totalPages": 1
}
```
- Error Responses:
  - 400 Bad Request: Invalid pagination
  - 401 Unauthorized: Not logged in
  - 403 Forbidden: Not an admin

---

## Data Models

### AppUserDto
```json
{
  "id": "string",
  "discordId": 1234567890,
  "discordName": "string|null",
  "minecraftUsername": "string",
  "balance": 0.0,
  "roles": ["Admin", "Staff"]
}
```

### Ticket
```json
{
  "channelId": "string",
  "creatorId": "string",
  "status": "Created|Open|Closed",
  "messages": [
    {
      "id": 1,
      "senderId": "string",
      "content": "string",
      "timestamp": "2025-01-01T12:00:00Z"
    }
  ]
}
```

### Message
```json
{
  "id": 1,
  "senderId": "string",
  "content": "string",
  "timestamp": "2025-01-01T12:00:00Z"
}
```

### TicketStatus (enum)
- `Created`
- `Open`
- `Closed`

### Roles
- `Admin`
- `Staff`
- `MinecraftNameAdded`

---

## Authentication & Authorization Summary

### Public Endpoints
- `GET /api/Auth/login`
- `GET /api/Auth/callback`
- `GET /api/Auth/Check` (returns `isAuthenticated: false` for anonymous)

### Authenticated User Endpoints
- `POST /api/Auth/logout`
- `GET /api/Auth/getRoles`
- `GET /api/Bank/getBalance`
- `POST /api/Bank/sendBalance`
- `GET /api/Bank/getTransactions`
- `POST /api/Ticket`
- `POST /api/Ticket/AddMessage`
- `PUT /api/Account/changeMinecraftUsername`
- `GET /api/Account/getUser`

### Admin-only Endpoints
- `GET /api/Auth/getAllRoles`
- `POST /api/Auth/AddToRole`
- `POST /api/Auth/RemoveFromRole`
- `POST /api/Bank/changeBalance`
- `POST /api/Bank/setBalance`
- `PUT /api/Ticket/UpdateStatus`
- `GET /api/Account/findUser`

---

## Error Handling

Error responses vary by endpoint and generally return either `ModelState` validation details or a simple object with a `message` field. Refer to each endpoint's Error Responses for typical cases.

### Common Status Codes
- 200 OK: Successful request
- 201 Created: Resource created
- 204 No Content: Successful request with no response body
- 400 Bad Request: Validation or malformed input
- 401 Unauthorized: Missing or invalid authentication
- 403 Forbidden: Authenticated but lacks permissions
- 404 Not Found: Resource does not exist
- 409 Conflict: Business rule violation (e.g., insufficient funds)
- 500 Internal Server Error: Unexpected server error

---

## Notes

- Authentication: Discord OAuth sets a secure, HTTP-only session cookie after `/api/Auth/callback`. Routes are case-insensitive, but documented with their exact casing as implemented (e.g., `/api/Auth/Check`, `/api/Ticket/UpdateStatus`).
- Security:
  - All state-changing endpoints require CSRF-safe patterns and session cookies.
  - Role checks are enforced for admin endpoints.
  - Inputs are validated using DTO validation attributes; never trust client data.
- Decimal precision: Monetary values use decimal with 2 fractional digits; ensure correct rounding when displaying.
- Concurrency control: Balance mutations are applied atomically per user to prevent race conditions. Idempotency is recommended on client operations when possible.
- Logging: Authentication flows and admin operations are logged with user identifiers; avoid logging secrets.
- Discord integration: OAuth `state` is required and validated; keep client and server redirect URIs in sync.
- Pagination: `findUser` supports paging with sensible defaults and caps.
- Timestamps: All dates are in ISO 8601 UTC.

---

## Changelog
- 2025-10-30: Initial comprehensive API documentation added.


