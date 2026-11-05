# TechZone — Implementation & verification guide for Muse

## Preconditions / first checks

```bash
pwd
git status --short
git branch --show-current
git rev-parse HEAD
git log -5 --oneline
dotnet --info
find . -maxdepth 3 -type f \( -iname 'AGENTS.md' -o -iname 'MASTER.md' -o -iname 'SCOPE.md' -o -iname '*GUIDE*.md' -o -iname '*RULE*.md' -o -iname 'DATABASE-MIGRATION.md' \) -print
```

Use the repo's current state as ground truth. If the local checkout differs from GitHub, **do not reset or discard user edits**; report divergence, then work safely within local checkout. Read all instructions found (user-provided `MASTER.md`/`SCOPE.md` only where they exist).

## Current stack / build

.NET 8 MVC, EF Core SQLite, Razor, Identity. From `TechZone.csproj` root:

```bash
dotnet restore TechZone.csproj
dotnet build TechZone.csproj
dotnet test TechZone.Tests/TechZone.Tests.csproj
```

If the environment's `dotnet` is missing from `PATH`, check `~/.dotnet/dotnet --version` (SDK 8.0.427 was previously reported available in the prior chat; re-check now). Do not claim success before executing. If `dotnet test` fails for testhost-transport reasons, verify logs and optionally use the included `TechZone.Tests.Runner`; do not hide other test failures behind this workaround.

## Security gate — first implementation chunk

- **Demo credentials:** `Data/SeedData.cs` creates demo accounts with known credentials. Separate local-demo bootstrap from public/prod; do not change an existing user's credential silently. Use environment-appropriate seeding; run a test showing default credentials aren't provisioned in a public config.
- **Guest order privacy:** `OrdersController.Details` and `HoaDon`, `ReturnsController.TraCuu` access guest orders by numeric ID when no `UserId`. Introduce non-guessable guest access token or verified contact + short-lived session scoped to order, without weakening authenticated owner/Admin checks. Add negative tests for another guest and unauthorized accounts. **Never use order number as proof of ownership.**
- **Login hardening:** Assess register auto-confirm and lockout disabled, distinguish intentional local demo from public security requirements.

## Financial/data gate

- Server-side validate admin product prices, promotion price and stock as well as category/brand IDs; form HTML `min` is not server validation.
- Centralize displayed price and checkout price in `PriceCalculationService` with consistent active Flash Sale, slot, quota and variant price rules. Test at slot boundaries and when a sale sells out.
- Verify stock/voucher limits, transaction rollback, replay/idempotency, cancel/return, loyalty tiers and points. Do not replace DB transactions with session-only logic.
- Validate VAT snapshot and discount allocation with tests; legal eligibility of 8% by category remains unverified (`DaXacMinh=false`). Do not silently change tax rates.

## Database migration safety

1. Create a clean disposable **copy** of any legacy SQLite DB and its WAL/SHM state; checkpoint safely or stop writers before copying.
2. Make a separate timestamped backup and record counts/foreign keys.
3. Run legacy converter only after verifying its invocation and expected schema; then `MigrateAsync` on the copy.
4. Verify data preservation, indexes, identity tables, schema and idempotent rerun.
5. Restore from the backup into a fresh temporary target and compare checks.
6. Never execute destructive migration against the sole `techzone.db` or production DB as a routine test.

## Asset / browser gate

- Current GitHub snapshot `a3eb8d2` includes `wwwroot/images` product files and `wwwroot/models/techzone_{smartphone,laptop,headphones,gamepad}.glb`. Check local `git status`, file presence, HTTP 200 and MIME mapping before reporting defects.
- Test 3D on mapped IDs (1,3,4) in actual browser: initial image, click 3D, rotate, zoom, reset, auto-rotate toggle, fullscreen, error fallback, narrow viewport. Gamepad is not mapped to seed's Logitech mouse.
- Ensure file licenses/attributions and alt text are appropriate before real publication; seed assets were designed as demo.

## Acceptance / evidence

- Use `TEST-REPORT.md`'s 15 scenarios. For each: record current commit, environment, exact command/browser action, observed result, and evidence pointer.
- Update issue status only when a corresponding test passes; keep `BLOCKED` or `NOT_RUN` if the environment prevents verification.
- Finish with changed files and diff summary, test commands and results, unresolved P0/P1/P2, recommended next execution chunk.
- Code changes are locally authorized by the user only for this follow-up execution prompt. Do not push/deploy/publish or delete a production DB absent separate explicit authorization.