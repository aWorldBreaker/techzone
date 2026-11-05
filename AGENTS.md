# AGENTS.md — TechZone | Muse / Codex engineering rules

## Read-first contract

Before any source edit: read `README.md`, `docs/PLAN-STATUS.md`, `docs/IMPLEMENTATION-GUIDE.md`, `TEST-REPORT.md`, and local `SCOPE.md`, `MASTER.md`, `DATABASE-MIGRATION.md` if they exist. Read the actual source and `git status` before assuming older findings are current. External/global Muse `SOUL.md` and `MEMORY.md` may be used only when accessible; do not pretend they exist in this repo.

## Non-negotiable project decisions

- Keep **.NET 8 ASP.NET Core MVC/Razor + EF Core + SQLite + Identity**. No React/NestJS/PostgreSQL rewrite.
- Keep approved **light/navy** UI; do not revert to the older dark-tech prompt. Use existing CSS architecture.
- Respect demo-only checkout and VAT limitations: do not claim live payments, legal VAT invoices, or certified pricing/specifications.
- Preserve the current code structure and previous work, make minimal traceable changes and tests.
- Do not overwrite uncommitted work. No forced git resets or destructive migrations on the only DB copy.

## Priority gates

1. **Security:** fixed seed credentials/public deployment behavior; guest-order private data/guest warranty access; account access controls.
2. **Data/financial correctness:** product validation, consistent sale pricing, stock/quota concurrency, vouchers, loyalty, refunds and VAT snapshots.
3. **Migration:** verify on cloned DB with backup/restore.
4. **Frontend/3D:** verify committed image and GLB resources and rendered UI on desktop/mobile; tune only based on evidence.
5. **Acceptance:** build, tests, runtime smoke, 15 flows in `TEST-REPORT.md`.

## Evidence and status labels

- `OBSERVED_CODE`: exact behavior found in code (file/line or path).
- `BUILD_PASS`: actual build command completed successfully.
- `TEST_PASS`: actual test command succeeded.
- `RUNTIME_PASS`: browser/API flow exercised successfully.
- `NOT_RUN`, `BLOCKED`, `FAIL`: state exactly why.
- Mark hypotheses as `[Suy luận]`; label missing verification as `[Chưa xác minh]`. Never turn previous chat claims into new runtime PASS.

## Security / operations

- Never include passwords, API keys, PII or live credentials in issues, diffs, logs, or `MEMORY.md`.
- Demo account security must be remediated before internet-facing use. Avoid exposing guest order address and contact details by predictable order IDs.
- Migrations and destructive tests only on disposable copies unless explicitly authorized for a specific target.
- Do not push, create release, deploy, alter repo settings, or change production accounts without a separate explicit request. The user authorizes local implementation and docs only when specified in task.
- Do not edit global Muse `SOUL.md` without user request; do not invent a local copy.

## Handoff

After each implementation chunk: give changed file list, commands run, actual outcomes, risks, and next step; update `TEST-REPORT.md` with evidence. Do not mark an issue resolved just because a patch was drafted.