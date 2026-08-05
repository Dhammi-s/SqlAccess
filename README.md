# SQL Access — Agency Connection Manager

A secure full-stack app to manage agency database connections stored in your **master DB** `agencies` table.

- **Backend:** ASP.NET Core Web API (.NET 10), EF Core + SQL Server, JWT auth
- **Frontend:** React + TypeScript (Vite), react-router, axios
- **Features:** single-user login, list agencies, add / edit, archive & restore, test connection, view full details, inspect & create database roles (SQL Server security)

## Security model

| Concern | How it's handled |
| --- | --- |
| DB passwords & connection strings | **Encrypted at rest** with AES-256-GCM (authenticated encryption). Decrypted only server-side, only when needed. |
| Agency list endpoint | Returns a **masked** password (`c••••x`), never plaintext. |
| Login password | Stored as a **PBKDF2-SHA256 hash** (210k iterations) in config — never plaintext, never in source. |
| Auth | Short-lived **JWT** (Bearer). Every `/api/agencies*` route requires it. |
| Secrets (keys, master conn string) | Kept in **.NET user-secrets** / env vars, never committed. `appsettings.json` has blank placeholders. |
| Errors | Global handler returns clean messages; no stack traces leak to clients in Production. |

## Prerequisites

- .NET 10 SDK
- Node.js 18+

## Configuration (already set on this machine)

Secrets are stored via `dotnet user-secrets` for the API project. Already configured:

- `Encryption:Key` (256-bit AES key)
- `Jwt:Key`
- `Auth:PasswordHash` (hash of your login password)
- `ConnectionStrings:MasterDb` — **password is a placeholder**; set the real one:

```bash
cd backend/SqlAccess.Api
dotnet user-secrets set "ConnectionStrings:MasterDb" "Data Source=188.40.211.2;User ID=db38045;Password=YOUR_REAL_PASSWORD;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Application Name=SqlAccess.Api"
```

### Rotating / regenerating secrets

```bash
# New 256-bit encryption key (base64):
node -e "console.log(require('crypto').randomBytes(32).toString('base64'))"
# New JWT signing key:
node -e "console.log(require('crypto').randomBytes(48).toString('base64'))"
```

> ⚠️ If you change `Encryption:Key`, existing encrypted rows can no longer be decrypted. Rotate deliberately.

## Login

- **Username:** `jasmeet singh`
- **Password:** the one you set up

To change the password, regenerate the hash and update the secret:

```bash
node -e "const c=require('crypto');const s=c.randomBytes(16);const k=c.pbkdf2Sync('NEW_PASSWORD',s,210000,32,'sha256');console.log('210000.'+s.toString('base64')+'.'+k.toString('base64'))"
# then:
dotnet user-secrets set "Auth:PasswordHash" "<paste hash>"
```

## Run (development)

Two terminals:

```bash
# 1) API  →  http://localhost:5095
cd backend/SqlAccess.Api
dotnet run --launch-profile http
```

```bash
# 2) Frontend  →  http://localhost:5173  (proxies /api to the API)
cd frontend
npm install
npm run dev
```

Open http://localhost:5173 and sign in.

## API reference

| Method | Route | Purpose |
| --- | --- | --- |
| POST | `/api/auth/login` | Get a JWT |
| GET | `/api/agencies?includeArchived=false` | List (secrets masked) |
| GET | `/api/agencies/{id}` | Full detail (secrets decrypted, for edit) |
| POST | `/api/agencies` | Create |
| PUT | `/api/agencies/{id}` | Update (blank password = keep existing) |
| DELETE | `/api/agencies/{id}?archived=true` | Archive / restore (soft delete) |
| POST | `/api/agencies/{id}/test` | Test stored connection |
| POST | `/api/agencies/test` | Test an ad-hoc connection string |
| GET | `/api/agencies/{id}/roles` | List SQL Server database roles on the agency's DB (count, fixed/custom, members) |
| POST | `/api/agencies/{id}/roles` | Create a database role; `{ "roleName": "...", "readOnly": true }` grants SELECT database-wide |
| POST | `/api/deploy/upload` | Upload a `.dacpac` (multipart). Returns a `dacpacId` |
| POST | `/api/deploy/run` | Script or publish the DACPAC to one agency DB |

### DACPAC schema deployment (multi-tenant)

The **Deploy schema** button (top of the agencies page) brings your GitHub Actions / `Deploy-AllTenants.ps1`
pipeline into the app. It uses **DacFx** (the managed engine behind `SqlPackage`) — no `sqlpackage.exe`,
**no SSDT/MSBuild** needed.

**Source — build straight from GitHub (default):**
1. Pick a **branch** (fetched live from the repo — e.g. `master`, `Development`).
2. Click **Build & generate scripts** / **Build & deploy now**. The server downloads that branch's source,
   reads the `.sqlproj` `<Build>` items, and compiles a DACPAC with DacFx.
3. (Or switch to **Upload a .dacpac** to use a prebuilt package instead.)

**Then, for each selected agency:**
- **Generate script** — non-destructive preview, downloadable `.sql` per tenant, or **Deploy** — publish.
- Toggle `BlockOnPossibleDataLoss` / `DropObjectsNotInSource` (same switches as the CI script; default off).
- Targets default to all active, non-archived agencies; deploys run one tenant at a time with live status so
  failures never cascade.

Target connections are built from each agency's `DbServer` / `DbName` / `DbUser` / `DbPassword` columns
(password decrypted server-side), exactly like the PowerShell script.

Config lives under `GitHub` in `appsettings.json`:

```json
"GitHub": { "Repo": "Dhammi-s/DB-WorkProvider360", "ProjectPath": "", "Token": "" }
```

- `Repo` — `owner/name`. `ProjectPath` — optional specific `.sqlproj` (auto-detected if blank).
- `Token` — optional GitHub PAT for **private** repos or to raise API rate limits. Put it in user-secrets:
  `dotnet user-secrets set "GitHub:Token" "ghp_…"`.

> **Note:** the server-side DacFx build compiles the **schema** model (all `<Build>` `.sql` files). The
> project's `Script.PostDeployment.sql` (seed/reference data) is **not** yet included — ask if you want
> post-deploy scripts run as a follow-up step.

### Database roles / security

Click an agency name (or **View**) to open its detail panel. It connects to that agency's **own**
database using the stored connection string and lists its roles from `sys.database_principals`
(showing fixed vs. custom roles and their members). You can create a new role inline — ticking
**Read-only** issues `GRANT SELECT` so the role can read all tables/views but not modify anything.

Role names are strictly validated (`^[A-Za-z_][A-Za-z0-9_]*$`) and additionally bracket-escaped with
`QUOTENAME` and executed via `sp_executesql` parameters, so the feature is safe against SQL injection.
Creating roles requires the agency's DB login to have `ALTER ANY ROLE` (e.g. `db_owner`/`db_securityadmin`).

## Notes on the `agencies` table

The app maps to your existing table. New rows store `DbPassword` and `ConnectionString` **encrypted** (prefixed `enc::v1::`). Pre-existing plaintext rows (e.g. AgencyId 11) are read transparently and become encrypted the next time you save them from the UI.
