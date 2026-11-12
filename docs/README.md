# ForgeFlow Living Documentation

## Illustrated Guides (Persian / Offline-Ready)

| File | Description | Audience |
|------|-------------|----------|
| [`deployment-guide-fa.html`](deployment-guide-fa.html) / [`deployment-guide-fa.pdf`](deployment-guide-fa.pdf) | Persian deployment guide — 3 DbContexts migrations, `ForgeFlow.Bootstrap`, IIS + HTTPS + Data Protection, interactive Go-Live checklist + A4 PDF export | Deployment Manager |
| [`admin-guide-fa.html`](admin-guide-fa.html) / [`admin-guide-fa.pdf`](admin-guide-fa.pdf) | Persian admin walkthrough — 16 real app screenshots (updated for latest UI), 5-group menu map, end-to-end GPPS case study from Org/Site setup through Procurement, Lab Specs/COA, Production Orders, and Traceability + A4 PDF export | System Administrator |

### Bundled Offline Assets
- `assets/Vazirmatn.woff2` — local Persian typeface copied from `src/ForgeFlow.Web/wwwroot/fonts/` so both guides render cleanly offline without external CDN requests.
- `screenshots/login-light-fa.png` & `screenshots/login-dark-fa.png` — real captured screenshots of `/account/login` in `fa-IR`.

## Living Maintenance Plan (Update After Every Slice)

Whenever a new slice is implemented in `ForgeFlow`:

1. **Update version/slice header** in both HTML files:
   ```html
   <!-- SLICE: N | Last updated: YYYY-MM-DD | App version: main@<commit> -->
   ```
2. **Update Deployment Guide** (`deployment-guide-fa.html`):
   - Add any new EF Core migrations under `AuthDbContext`, `OrganizationDbContext`, or `DiagnosticsDbContext` in Section 5.
   - Document any new environment variables, configuration keys, IIS settings, or Bootstrap CLI changes in Sections 4 and 6.
   - Update the interactive Go-Live checklist (`#deploy-checklist`) if new operational verification steps are introduced.
3. **Update Admin Guide** (`admin-guide-fa.html`):
   - Update the 5-group menu table and permission table if new pages or permission codes are added.
   - Add or extend the step-by-step workflow and inline SVG diagrams for the new feature.
   - Capture/refresh screenshots in `docs/screenshots/`.
4. **Verify HTML rendering**:
   - Check RTL layout, zero horizontal overflow (desktop & mobile), and zero broken asset/image paths before committing.

## Capturing Authenticated Screenshots

- Anonymous pages (`/account/login`) can be captured directly when `ForgeFlow.Web` is running at `https://localhost:5001`.
- Authenticated pages (`/`, `/organizations`, `/items`, `/boms`, `/production-orders`, `/purchase-orders`, `/quality`, `/inventory`, `/traceability`, `/reports`) require an active session. To populate the remaining `<div class="screenshot-slot">` placeholders with real app captures, provide temporary local `testforgflow` login credentials in chat (never commit passwords to git or output).

## Slices Directory & Roadmap

- [`roadmap.md`](roadmap.md) — Completed baseline (`AUTH-01` through `Slice 13`) and proposed roadmap for `Slice 14+` (Sales & Dispatch, MRP, Costing, CMMS/OEE, Shop-Floor/Barcode, Integrations).
- `slices/` holds per-slice engineering design specs (`README.md`, `decisions.md`, `physical-erd.md`, `test-plan.md`).
