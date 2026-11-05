# TEST-REPORT.md — TechZone acceptance execution log

**Prepared:** 2026-11-06. **Repository snapshot inspected:** `a3eb8d2d42d02e03e6ebc3a848f368cd33e61c31`.

> This is an **unexecuted checklist**, not proof of a passing test suite. Every scenario starts `NOT_RUN`. Muse must replace only with real observed evidence.

## Environment record

| Field | Current value / fill at execution |
|---|---|
| Branch / HEAD used | `NOT_RUN` — record `git rev-parse HEAD` |
| OS / SDK / runtime | `NOT_RUN` — record `dotnet --info` |
| DB under test / backup | `NOT_RUN` — **disposable copy only** |
| `dotnet restore` | `NOT_RUN` |
| `dotnet build` | `NOT_RUN` |
| `dotnet test` | `NOT_RUN` |
| HTTP startup / browser | `NOT_RUN` |

## Fifteen acceptance scenarios (proposed from previous plan; reconcile with local SCOPE if available)

| # | Flow to exercise | Status | Evidence / exact observed result |
|---|---|---|---|
| 01 | Startup fresh SQLite: migrate + seed, login page/home render | NOT_RUN | — |
| 02 | Legacy SQLite clone: backup, convert, migrate and restore | NOT_RUN | — |
| 03 | Account registration, login/logout, lockout/email policy under chosen environment | NOT_RUN | — |
| 04 | Guest order details + VAT view: unauthorized ID access rejected | NOT_RUN | — |
| 05 | Registered customer order: another customer forbidden; Admin permitted | NOT_RUN | — |
| 06 | Admin routes Dashboard, Stock, Customers, Notifications load | NOT_RUN | — |
| 07 | Admin product CRUD: valid save/upload and negative-price/stock rejections | NOT_RUN | — |
| 08 | Catalog/filter/detail/variant/cart price consistently displayed | NOT_RUN | — |
| 09 | Flash Sale standard + timed slots at boundaries and quota exhaustion | NOT_RUN | — |
| 10 | Voucher eligible/expired/quota/concurrent checkout scenarios | NOT_RUN | — |
| 11 | COD mock checkout, idempotency replay, concurrent last-stock reservation | NOT_RUN | — |
| 12 | Combo items, checkout discount and remaining quota/stock | NOT_RUN | — |
| 13 | Loyalty points/Platinum threshold and cancellation refunds/rollback | NOT_RUN | — |
| 14 | VAT snapshot invoice demo, 8%/10% config eligibility warning, totals | NOT_RUN | — |
| 15 | Product images + GLB viewer (desktop/mobile/reduced motion/error fallback) | NOT_RUN | — |

Status allowed: `PASS`, `FAIL`, `BLOCKED`, `NOT_RUN`. `OBSERVED_CODE` is **not** a PASS. Record actual evidence as links to logs/screenshots/test output or reproducible commands. Do not put credentials, phone numbers, real addresses or personal data in this report.

## Defect / verification log

| Date | Scenario / code path | Evidence | Next action |
|---|---|---|---|
| 2026-11-06 | Current GitHub tree includes assets previously reported missing | Git tree at `a3eb8d2`; runtime `NOT_RUN` | Browser smoke on current local checkout |

## Final sign-off

- Sign-off: **NOT RUN / NOT APPROVED**
- Remaining issues: See `docs/PLAN-STATUS.md`, reconcile with actual test failures.