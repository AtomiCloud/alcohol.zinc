# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Structure

This is a .NET 8 microservice using Clean Architecture with separate Domain and Application layers:

- **App/**: ASP.NET Core Web API application layer
- **Domain/**: Domain models and business logic
- **UnitTest/**: Unit tests using xUnit
- **IntTest/**: Integration tests using xUnit
- **infra/**: Kubernetes deployment configurations with Helm charts

## Development Commands

The project uses Task (Taskfile.yml) for development automation:

```bash
# Setup and restore dependencies
task setup

# Run application locally
task run

# Build application
task build

# Start development environment with hot reload
task dev

# Create Entity Framework migrations
task migration:create -- MigrationName

# Run unit tests
dotnet test UnitTest/

# Run integration tests
dotnet test IntTest/

# Run all tests
dotnet test
```

## Domain Architecture

The domain follows a layered approach with:

### Domain Modeling Pattern

The domain uses a three-tier modeling approach:

1. **Record** (`*Record`): Pure business data without identifiers
2. **Principal** (`*Principal`): Record + identifiers (primary keys, foreign keys)
3. **Model** (no suffix): Principal + related domain aggregates

Example:

- `HabitRecord`: Business data (task, day of week, notification time, stake, etc.)
- `HabitPrincipal`: HabitRecord + Id, PlanId, CharityId, UserId
- `Habit`: HabitPrincipal + UserPrincipal + CharityPrincipal (full aggregate)

### Core Domain Models

- **Habit**: Habit tracking with monetary stakes, charity linkage, and scheduling
- **User**: User management and authentication
- **Charity**: Charity organizations for habit stakes
- **Failure**: Domain model for tracking failures (recently added)

### Mapping Pattern

Each entity has two mappers serving different bounded contexts:

1. **Data Mapper** (`App/Modules/*/Data/*Mapper.cs`): Maps between Entity Framework data models and domain models

   - Handles persistence concerns (e.g., cents ↔ currency, basis points ↔ decimals)
   - Example: `HabitData` ↔ `HabitPrincipal`

2. **API Mapper** (`App/Modules/*/API/V1/*Mapper.cs`): Maps between HTTP request/response models and domain models
   - Handles serialization concerns (string formatting, culture-specific parsing)
   - Example: `CreateHabitReq`/`UpdateHabitReq` → `HabitRecord`, `HabitPrincipal` → `HabitRes`

### Data Flow

1. Controllers in `App/Modules/*/API/V1/` handle HTTP requests
2. API mappers convert requests to domain models
3. Repository pattern in `App/Modules/*/Data/` for data access
4. Data mappers convert between domain and Entity Framework models
5. Domain services in `Domain/*/Service.cs` for business logic

**Performance Note**: Only `Get(id)` operations return full aggregates (complete domain models with all relationships). List operations like `GetAll()` return only Principals to avoid expensive joins and improve performance.

## Key Technologies

- .NET 8 with nullable reference types enabled
- Entity Framework Core with PostgreSQL
- OpenTelemetry for observability (metrics, traces, logs)
- FluentValidation for input validation
- JWT authentication with configurable auth policies
- Redis caching with StackExchange.Redis
- MinIO for file storage
- Swagger/OpenAPI documentation

## Development Environment

- Uses Nix for development environment setup
- Kubernetes development with k3d clusters
- Tilt for local development orchestration
- Docker containers for deployment

## Configuration

Settings are managed through YAML files with environment-specific overrides:

- `App/Config/settings.yaml` (base configuration)
- `App/Config/settings.{landscape}.yaml` (environment-specific)
- Environment variables with `Atomi_` prefix

## Testing

- Unit tests use xUnit with FluentAssertions
- Integration tests include JUnit test logger for CI/CD
- Test projects reference the main App project for integration testing
- usually only Get for a specific entity using id will return the full model (full aggregate), because loading everything for action like list is very time consuming, does that make sense to you?
