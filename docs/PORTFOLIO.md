# RalseiFlow | Portfolio Case Study

**Project:** Order, stock and business management workspace

**Stack:** React, TypeScript, C#, .NET 10, ASP.NET Core, PostgreSQL, EF Core, JWT, Docker Compose
**Repository:** [github.com/marcel322gg-png/OrderFlow](https://github.com/marcel322gg-png/OrderFlow)

## Overview

RalseiFlow is a branded workspace for customer and product records, stock movements, order fulfillment, and sales summaries. Its responsive React interface connects to an ASP.NET Core API and PostgreSQL database. The design uses warm ivory, forest green, sage, and terracotta with Space Grotesk, DM Sans, and IBM Plex Mono.

## Business value

Orders use current product prices supplied by the server, reserve available stock, and follow an explicit lifecycle. Inventory movements and order status changes are recorded, giving operators an API-level history of receipts, reservations, releases, fulfillment, and cancellation.

## Capabilities

- JWT login and Identity-backed users with Employee and Admin roles.
- Customer and product CRUD, searching, sorting, and bounded pagination.
- Multi-item orders with server-calculated totals and stock reservation.
- Validated status changes and order history.
- Stock receipts, reservation release, fulfillment adjustment, and transaction history.
- Date-filtered sales analytics.
- Dashboard metrics, recent orders, and low-stock watch sourced from the API.
- Responsive UI with loading/error feedback, confirmation prompts, and role-aware controls.
- Swagger/OpenAPI, ProblemDetails, request logging, and health endpoint.

## Architecture

The API project handles HTTP, authentication, and composition. Application defines contracts and validation; Domain owns entities and business rules; Infrastructure implements services and PostgreSQL persistence through EF Core. Docker Compose runs the API and database locally.

## Run and inspect

On Windows, start with `RUNLOCALLY.bat`; on Linux, use `./RUNLOCALLY.sh`. The workflow provisions local-only credentials, waits for database readiness, applies migrations, checks API health, and runs smoke checks. Browse the application at `http://localhost:8080/` and the API at `http://localhost:8080/swagger`. See the root README for configuration and examples.

## Testing and verification

Release verification passed: .NET build completed with 0 warnings and errors, all 13 unit tests passed, and the frontend production build completed with no reported npm vulnerabilities. Docker rebuilt the API image; PostgreSQL and API health checks passed, and the live OpenAPI document identifies `RalseiFlow API`. The local smoke workflow also passed authentication, catalogue, inventory, order lifecycle, history, and analytics checks. This case study does not imply a production deployment.

## Screenshots

Browse the [RalseiFlow screenshot gallery](../OrderFlow-Screenshots/index.html) for genuine captures from the running local application, Swagger, database, and container runtime.

## Limitations

Dedicated automated API integration tests and concurrency stress tests are not included. This is a portfolio project; no commercial clients, revenue, or production usage are claimed.
