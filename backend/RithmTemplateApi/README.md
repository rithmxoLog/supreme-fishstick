# RithmTemplateApi

The Web API project serving as the entry point for the RithmTemplate microservice.

## Purpose

This project is responsible for:

- **HTTP Request Handling**: Receiving and routing incoming HTTP requests
- **Authentication & Authorization**: JWT-based security enforcement
- **Input Validation**: Request model validation and sanitization
- **Response Serialization**: Transforming domain objects to HTTP responses
- **API Documentation**: OpenAPI/Swagger specification generation

## Architecture Role

```
┌─────────────────────────────────────────────────────────────┐
│                      RithmTemplateApi                       │
│  ┌─────────────┐  ┌────────────┐  ┌─────────────────────┐  │
│  │ Controllers │──│ Middleware │──│ Services (Notifiers)│  │
│  └──────┬──────┘  └────────────┘  └─────────────────────┘  │
└─────────┼───────────────────────────────────────────────────┘
          │
          ▼
┌─────────────────────────────────────────────────────────────┐
│                   RithmTemplate.Helpers                     │
│              (Business Logic & Integrations)                │
└─────────────────────────────────────────────────────────────┘
          │
          ▼
┌─────────────────────────────────────────────────────────────┐
│                    RithmTemplate.DAL                        │
│                  (Data Access Layer)                        │
└─────────────────────────────────────────────────────────────┘
```

## Key Principles

1. **Thin Controllers**: Controllers should only orchestrate HTTP concerns, delegating business logic to the Helpers layer
2. **No Direct DB Access**: Never reference `Microsoft.EntityFrameworkCore` directly; use the DAL abstraction
3. **Consistent Responses**: Use standardized response models (`ErrorResponse`, `PaginatedResponse`)
4. **Observable**: All endpoints should be measurable via health checks and metrics

## Project Structure

| Folder | Purpose |
|--------|---------|
| `Controllers/` | HTTP endpoint definitions |
| `Middleware/` | Cross-cutting HTTP concerns |
| `Models/` | DTOs for request/response |
| `Services/` | API-level services (notifications, etc.) |

## Configuration

Key settings in `appsettings.json`:

- `ConnectionStrings:DefaultContext` - PostgreSQL connection
- `ConnectionStrings:ValkeyConnection` - Redis/Valkey for caching
- `Jwt:*` - JWT authentication settings
- `SignalR:*` - Real-time notification hub URLs
- `ServiceRouter:*` - Inter-service communication URLs

## Running the API

```bash
# Development
dotnet run

# Production
dotnet run --configuration Release
```

Access Swagger UI at: `https://localhost:5001/swagger`
