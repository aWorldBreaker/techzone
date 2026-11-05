# TechZone — Plan alignment & audit status

**Snapshot:** GitHub `aWorldBreaker/techzone`, branch `main`, commit `a3eb8d2d42d02e03e6ebc3a848f368cd33e61c31` (2026-11-05). **Prepared:** 2026-11-06.

**Verification scope:** remote source/tree inspection only; **no new build, test run, browser smoke test, or deployment test performed during this documentation update**. Re-check HEAD and local working tree before action.

## Authoritative decisions

- Keep ASP.NET Core 8 MVC + Razor + EF Core SQLite + Identity. **No** migration to old React/NestJS/PostgreSQL prompt.
- Preserve light/navy UI (`#0F172A`, `#1E3A5F`) already chosen by user.
- Prioritize P0 security / P1 financial correctness, then assets/3D, 15 acceptance flows.
- Real payment, legal tax invoices and arbitrary third-party asset licences are **not** claimed.

## Status by milestone

| ID | Area | Source/tree status | Runtime/test status | Evidence / action |
|---|---|---|---|---|
| P0-old-1 | Admin views | `Views/Admin/Stock.cshtml`, `Customers.cshtml`, `Notifications.cshtml` exist; actions return matching names | NOT_RUN | Navigate all three after admin login; verify ViewModel types |
| P0-old-2 | Migrations + legacy DB | EF migration files and `Data/LegacyDbConverter.cs` exist | NOT_RUN | Clone legacy DB, backup, migrate, compare rows, restore |
| P0-old-3 | Checkout concurrency | Transaction/conditional stock updates/idempotency in `Services/CheckoutService.cs` | NOT_RUN | Run concurrent checkout and voucher tests; inspect results |
| SEC-01 | Demo admin credential | Fixed credentials created in `Data/SeedData.cs`, app startup seeds users | NOT_RUN | Disable/change demo bootstrap outside explicit demo; add tests |
| SEC-02 | Guest order privacy | Guest orders with `UserId == null` viewable by numeric ID in `Controllers/OrdersController.cs`; guest warranty lookup similarly ID-based | NOT_RUN | Add non-guessable guest access control and test forbidden access |
| SEC-03 | Login/email | Registration sets `EmailConfirmed=true`, login uses `lockoutOnFailure: false` | NOT_RUN | Align demo/public policy, lockout/rate limiting and test |
| DATA-01 | Product validation | `Models/Product.cs` lacks range checks; Admin form relies largely on model binding | NOT_RUN | Reject negative price, promo and stock server-side; test |
| SALE-01 | Price consistency | Product details computes `flash?.GiaSale ?? GiaBan`; checkout uses `PriceCalculationService` with slot pricing | NOT_RUN | Test same clock/quota in detail/cart/checkout |
| FIN-01 | Loyalty/refund/VAT | Dedicated Services + tests exist; VAT 8% rules declared `DaXacMinh=false` | NOT_RUN | Validate finance edge cases and mark legal tax verification separately |
| MEDIA-01 | Product images | 13 product images plus placeholder in `wwwroot/images` on HEAD | NOT_RUN | Verify image endpoints, references and reuse/licensing |
| MEDIA-02 | 3D viewer | 4 GLBs exist in `wwwroot/models`; map references 3 product IDs, gamepad not mapped | NOT_RUN | Browser test 3 mapped products and fallback; responsive/reduced-motion |
| QA-01 | Automated suite | `TechZone.Tests/` and runner exist, no `.github/workflows` in inspected Git tree | NOT_RUN | Run tests, optionally add CI after code readiness |
| QA-02 | Acceptance | No historical runtime results were imported into this file | NOT_RUN | Record 15 flows in `TEST-REPORT.md` |

## Discrepancies from previous audit

- **Corrected 2026-11-06:** Old audit based on commit `f321820e` reported missing product photos and four GLBs. Current HEAD `a3eb8d2` contains these files. Asset **presence** is observed; successful runtime rendering remains unverified.
- Older `README.md` said no .NET build had ever run and image uploads/views/feature groups were missing. These statements were stale and README has been updated. Prior user-reported SDK 8.0.427 build pass was not re-run during this documentation update.
- `TECHZONE_PROMPT_CODEX.md` from 2026-11-05 was a prior concept advocating React/NestJS/PostgreSQL and dark palette. Newer user decision supersedes these implementation choices.

## Sequencing & done criteria

1. Reconcile local `MASTER.md`, `SCOPE.md` and any runbook against current code; preserve originals.
2. Fix/test SEC-01/SEC-02, then SEC-03 and DATA-01.
3. Verify/mend SALE-01 and finance edge cases; inspect VAT rules, do not assert legal compliance.
4. Test legacy migration and restore on disposable DB copies.
5. Run browser smoke for images/GLBs, mobile, accessibility fallback.
6. Execute 15 acceptance flows; document exact commands/screenshots and `PASS/FAIL/BLOCKED/NOT_RUN` in `TEST-REPORT.md`.

A source-level fix is **not** `RUNTIME_PASS`. A `PASS` requires evidence from the current tree.