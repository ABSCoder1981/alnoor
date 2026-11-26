# ALNoor – Online Quran Classroom

Live online Quran classes for one teacher and small groups of students: an Android app (student and teacher roles), a teacher web app, and an ASP.NET Core API. Audio and video go through LiveKit Cloud.

- Requirements: [docs/PRD.md](docs/PRD.md) (approved v1.0) is the source of truth.
- Engineering rules for contributors and AI assistants: [CLAUDE.md](CLAUDE.md).
- Status: **Phase 3 – accounts, authentication, students and classes** (PRD section 26). Phase 2 recitation tests are still to be run. Decisions: [docs/adr/](docs/adr/), audio tests: [docs/audio-decisions.md](docs/audio-decisions.md).

## Repository layout

| Folder | What | Stack |
|---|---|---|
| [backend/](backend/) | API – modular monolith (`src/ALNoor.Api`, `tests/ALNoor.Api.Tests`) | .NET 10 LTS, ASP.NET Core, EF Core 10, PostgreSQL 18 |
| [web/](web/) | Teacher web app | Vite 8, React 19, TypeScript 7 |
| [android/](android/) | Android app (both roles) | Kotlin 2.4, Jetpack Compose, AGP 9.5, minSdk 26 / targetSdk 36 |
| [docs/](docs/) | PRD and design documents | – |

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.404+ (pinned in `backend/global.json`) | `winget install Microsoft.DotNet.SDK.10` |
| PostgreSQL 18 **or** Docker Desktop | 18.x / latest | Either a native install (`winget install PostgreSQL.PostgreSQL.18`) or `docker compose up -d`. Docker needs WSL2 and CPU virtualization enabled in the BIOS; it also runs the LiveKit-server tests |
| Node.js | 24+ | For `web/` |
| Android Studio | latest stable | Provides the JDK (`jbr`) and Android SDK; API 37.2 platform is downloaded on first build |

## First-time setup

```bash
# 1. Database (choose one)
#    a) Docker:  cp .env.example .env, set POSTGRES_PASSWORD, then: docker compose up -d
#    b) Native PostgreSQL 18: create a login role "alnoor" that owns database "alnoor_dev"
#       (not a superuser) and put its password in .env as POSTGRES_APP_PASSWORD.

# 2. API secrets into dotnet user-secrets (connection string + a generated JWT signing key)
powershell -ExecutionPolicy Bypass -File scripts/dev-setup.ps1

# 3. Create the database schema
cd backend
dotnet tool restore            # installs dotnet-ef (pinned in backend/dotnet-tools.json)
dotnet ef database update --project src/ALNoor.Api

# 4. Create the first teacher account (PRD FR-AUTH-11). The password is asked for without echo
#    (or read from ALNOOR_ADMIN_PASSWORD); never pass it as an argument.
dotnet run --project src/ALNoor.Api -- seed-teacher --name "Teacher Name" --phone +919800000000
#    Forgotten teacher password (PRD Q9):
#    dotnet run --project src/ALNoor.Api -- reset-teacher-password --phone +919800000000

# 5. Local LiveKit server (Windows; keep this terminal open). Downloads a pinned, checksum-verified
#    livekit-server into .tools/, creates dev keys in .env and stores LiveKit settings in user-secrets.
cd ..
powershell -ExecutionPolicy Bypass -File scripts/livekit-dev.ps1
```

The API refuses to start without valid `ConnectionStrings:Default`, `Jwt:SigningKey` (base64, at least 32 bytes) and `LiveKit:*` settings (the LiveKit secret must be at least 32 characters).

## Run

| Part | Command | URL |
|---|---|---|
| API | `cd backend && dotnet run --project src/ALNoor.Api` (add `--launch-profile lan` so phones on the same Wi-Fi can reach it) | http://localhost:5080/health (DB included), `/health/live` (process only), `/openapi/v1.json` (Development only) |
| LiveKit (dev) | `scripts/livekit-dev.ps1` | `ws://<PC Wi-Fi IP>:7880` |
| Web | `cd web && npm install && npm run dev` | http://localhost:5173 – shows whether it can reach the API |
| Android | Open `android/` in Android Studio and run, or `cd android && ./gradlew assembleDebug` | Sign in; debug builds let you set the server address on the login screen and open the Phase 2 LiveKit test room from the home screen |

Teachers sign in on the web app (students use Android). In development it also has the Phase 2 LiveKit test room (not included in production builds). How to run a recitation test: [docs/audio-decisions.md](docs/audio-decisions.md).

The web app reads the API address from `VITE_API_BASE_URL` (see [web/.env.example](web/.env.example)); it defaults to `http://localhost:5080`.

## Test

| Part | Command |
|---|---|
| Backend | `cd backend && dotnet test`. Database tests use Testcontainers, or a native PostgreSQL when `ALNOOR_TEST_POSTGRES` is set to a connection string for a role with `CREATEDB` (each test class gets its own throwaway database). Tests that cannot run are **reported as skipped** (not passed); in CI they fail instead. LiveKit-server tests use Docker, or a running `livekit-server` when `ALNOOR_TEST_LIVEKIT` is set to its http URL (start it with `LIVEKIT_KEYS="fixture-key: fixture-secret-0123456789-abcdefghijklmnop"`) |
| Web | `cd web && npm test && npm run lint && npm run build` (on Windows, run from a terminal whose path starts with an upper-case `C:`; a lower-case `c:` makes Vitest load twice and fail) |
| Android | `cd android && ./gradlew testDebugUnitTest` |

CI ([.github/workflows/ci.yml](.github/workflows/ci.yml)) runs all three on every push and pull request, including the database tests and dependency vulnerability checks.

## Configuration and secrets

- Configuration keys follow PRD section 22.3; see [.env.example](.env.example). In environment variables, `__` separates sections (`ConnectionStrings__Default`).
- Development secrets: `dotnet user-secrets`. Server secrets: a `.env` file readable only by the deploy user. Never in Git, never in the web or Android code.
- Authentication (PRD 17): 15-minute access tokens (memory only); 30-day rotating refresh tokens, stored hashed on the server, in an HttpOnly SameSite=Strict cookie on the web and encrypted with an Android Keystore key on Android.
- CORS: only the origins listed in `Cors:AllowedOrigins` (Development: `http://localhost:5173`).

## Versions

All dependency versions are pinned exactly and were verified as the latest stable on 2026-11-25:

- **Backend:** `backend/Directory.Packages.props`, with central package management.
- **Web:** `web/package.json`, with exact versions.
- **Android:** `android/gradle/libs.versions.toml`.
- **Gradle:** `android/gradle/wrapper/gradle-wrapper.properties`, with a SHA-256 checksum.

Upgrade them on purpose, not with automatic ranges.
