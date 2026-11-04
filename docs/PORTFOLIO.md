# OrderFlow | Portfolio Case Study

## Summary

OrderFlow is a locally runnable order-management REST API built with ASP.NET Core, PostgreSQL, and Docker Compose. It demonstrates a small-business workflow from catalog and inventory management through order fulfillment and sales reporting.

## Problem and solution

Order workflows need consistent prices, inventory reservations, auditable stock movements, and valid status transitions. OrderFlow centralizes these rules in a domain model and persists operations through EF Core transactions.

## Architecture and technologies

- .NET 10, ASP.NET Core Web API, C#
- ASP.NET Core Identity, JWT bearer authentication, Admin and Employee roles
- EF Core 10, PostgreSQL, database migrations
- FluentValidation, ProblemDetails, Serilog, Swagger/OpenAPI
- Docker Compose and xUnit

See README.md for architecture, data model, setup, and endpoint details.

## Demonstrated workflows

- Register and authenticate employees; bootstrap an administrator from local environment configuration.
- Manage customers and products, record receipts, reserve stock for orders, and release reservations on cancellation.
- Enforce order transitions and record status and inventory history.
- Calculate totals from server-side product prices and report sales analytics.

The local smoke workflow exercises authentication, catalog creation, receipts, order pricing and reservation, status changes, cancellation, inventory history, and analytics against a local database volume.

## Verification

The latest local verification passed the Release build with zero warnings or errors, 12 unit tests, PostgreSQL and API health checks, three recorded EF migrations, HTTP 200 from `/health` and Swagger, and the end-to-end smoke workflow.

## Screenshots

Screenshots have not yet been captured. Add genuine screenshots from a running instance before presenting a visual demo.

## Fiverr portfolio description

Built OrderFlow, a .NET order-management API with PostgreSQL persistence, JWT authentication, role-based access, inventory reservations, order lifecycle rules, audit history, analytics, Docker-based local setup, and automated tests. Includes Swagger documentation and reproducible local smoke validation.

## Limitations

- Automated tests are unit tests; database-backed API behavior is covered by local smoke validation rather than a dedicated integration-test project.
- No public deployment or production usage is claimed.
- No public repository URL is available until GitHub publication is completed.
