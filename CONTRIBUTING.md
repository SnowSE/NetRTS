# Contributing to NetRTS

Thank you for your interest in contributing to NetRTS! This project follows strict architectural and testing guidelines.

## Development Workflow

We follow a **Test-First Development (TDD)** approach.

1.  **Define Requirements**: Understand the feature or bug you are working on.
2.  **Write Tests**: Write BDD scenarios (Gherkin) in `tests/NetRts.ContractTests` and unit tests in `tests/NetRts.UnitTests`.
3.  **Ensure Failure**: Run the tests and ensure they fail.
4.  **Implement**: Write the minimum amount of code required to make the tests pass.
5.  **Refactor**: Clean up the code while ensuring tests remain green.

## Architecture

We use **Clean Architecture** with the following layers:

- **Domain**: Core entities, value objects, and business rules. No dependencies.
- **Application**: CQRS handlers (MediatR), interfaces, and application services.
- **Infrastructure**: Persistence (EF Core), background services, and external integrations.
- **Api**: Minimal APIs, SignalR hubs, and middleware.
- **Client**: Blazor WebAssembly frontend.
- **Contracts**: Shared DTOs and event models.

## Coding Standards

- Use C# 12 features (primary constructors, collection expressions, etc.).
- Adhere to the `.editorconfig` rules.
- Add XML documentation to all public members.
- Use structured logging with Serilog.

## Pull Request Process

1.  Create a new branch for your feature or fix.
2.  Ensure all tests pass locally (`dotnet test`).
3.  Submit a PR with a clear description of the changes.
4.  Maintain high test coverage (minimum 80%).
