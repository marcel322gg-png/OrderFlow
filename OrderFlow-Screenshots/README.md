# RalseiFlow screenshot gallery

These are genuine captures of the running RalseiFlow application, Swagger UI, Docker CLI, and live PostgreSQL metadata. App views use actual API-backed records created by the local smoke workflow and screenshot session; no screen mockups or external data are used. The sign-in image contains empty fields. No passwords, access tokens, or `.env` values appear in the images. The database image is a headless report generated from `information_schema` and PostgreSQL key/index metadata in the running database.

Open [the responsive gallery](index.html) in a browser.

| File | Screen |
|---|---|
| `01-swagger-overview.png` | Live Swagger UI with RalseiFlow API v1 selector and endpoint groups |
| `02-login.png` | Sign in |
| `03-dashboard.png` | Metrics, recent orders, stock watch |
| `04-customers.png` | Customer directory |
| `05-products.png` | Product catalogue and availability |
| `06-inventory.png` | Stock ledger and transaction history |
| `07-order-creation.png` | Order creation with a real customer and available product |
| `08-order-details-history.png` | Order details and initial history entry |
| `09-order-lifecycle.png` | Confirmed status and persisted history |
| `10-analytics.png` | Sales analytics and date filters |
| `11-docker-runtime.png` | Live `docker ps` output with healthy API/database containers and host ports |
| `12-postgresql-schema.png` | Live PostgreSQL public schema: six business tables, columns, types, nullability, and keys |
| `13-swagger-ralseiflow.png` | Live Swagger UI with RalseiFlow API branding |

To reproduce the application captures locally, start the stack with `RUNLOCALLY.bat`, install a temporary Playwright package and Chromium, then automate the local UI with an administrator account from the ignored `.local-credentials.txt`. Never add that credentials file or tokens to Git.
