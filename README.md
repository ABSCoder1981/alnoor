# ALNoor – Online Quran Classroom

Live online Quran classes for one teacher and small groups of students: an Android app (student and teacher roles), a teacher web app, and an ASP.NET Core API. Audio and video go through LiveKit Cloud.

- Requirements: [docs/PRD.md](docs/PRD.md) (approved v1.0) is the source of truth.
- Engineering rules for contributors and AI assistants: [CLAUDE.md](CLAUDE.md).
- Status: **Phase 2 – LiveKit proof of concept** (PRD section 26). Decisions: [docs/adr/](docs/adr/), audio tests: [docs/audio-decisions.md](docs/audio-decisions.md).

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
| Docker Desktop | latest | Needs WSL2 on Windows (`wsl --install`, then reboot). Runs local PostgreSQL and the database integration tests |
| Node.js | 24+ | For `web/` |
| Android Studio | latest stable | Provides the JDK (`jbr`) and Android SDK; API 37.2 platform is downloaded on first build |

## First-time setup

```bash
# 1. Local settings (never commit .env)
cp .env.example .env          # then set POSTGRES_PASSWORD to something local

# 2. Start PostgreSQL (bound to 127.0.0.1 only)
docker compose up -d

# 3. Give the API its connection string via user-secrets (not appsettings)
cd backend
dotnet user-secrets --project src/ALNoor.Api set "ConnectionStrings:Default" \
  "Host=localhost;Port=5432;Database=alnoor_dev;Username=alnoor;Password=<your POSTGRES_PASSWORD>"
dotnet tool restore            # installs dotnet-ef (pinned in backend/dotnet-tools.json)

# 4. Local LiveKit server (Windows; keep this terminal open). Downloads a pinned, checksum-verified
#    livekit-server into .tools/, creates dev keys in .env and stores LiveKit settings in user-secrets.
cd ..
powershell -ExecutionPolicy Bypass -File scripts/livekit-dev.ps1
```

The API refuses to start without valid `ConnectionStrings:Default` and `LiveKit:*` settings (the LiveKit secret must be at least 32 characters).

## Run

| Part | Command | URL |
|---|---|---|
| API | `cd backend && dotnet run --project src/ALNoor.Api` (add `--launch-profile lan` so phones on the same Wi-Fi can reach it) | http://localhost:5080/health (DB included), `/health/live` (process only), `/openapi/v1.json` (Development only) |
| LiveKit (dev) | `scripts/livekit-dev.ps1` | `ws://<PC Wi-Fi IP>:7880` |
| Web | `cd web && npm install && npm run dev` | http://localhost:5173 – shows whether it can reach the API |
| Android | Open `android/` in Android Studio and run, or `cd android && ./gradlew assembleDebug` | Debug builds open the Phase 2 LiveKit test client |

In development the web app also shows the Phase 2 LiveKit test room (not included in production builds). How to run a recitation test: [docs/audio-decisions.md](docs/audio-decisions.md).

The web app reads the API address from `VITE_API_BASE_URL` (see [web/.env.example](web/.env.example)); it defaults to `http://localhost:5080`.

## Test

| Part | Command |
|---|---|
| Backend | `cd backend && dotnet test` – PostgreSQL and LiveKit-server tests use Testcontainers and are **reported as skipped** (not passed) when Docker is not running; in CI they fail instead |
| Web | `cd web && npm test && npm run lint && npm run build` (on Windows, run from a terminal whose path starts with an upper-case `C:`; a lower-case `c:` makes Vitest load twice and fail) |
| Android | `cd android && ./gradlew testDebugUnitTest` |

CI ([.github/workflows/ci.yml](.github/workflows/ci.yml)) runs all three on every push and pull request, including the database tests and dependency vulnerability checks.

## Configuration and secrets

- Configuration keys follow PRD section 22.3; see [.env.example](.env.example). In environment variables, `__` separates sections (`ConnectionStrings__Default`).
- Development secrets: `dotnet user-secrets`. Server secrets: a `.env` file readable only by the deploy user. Never in Git, never in the web or Android code.
- CORS: only the origins listed in `Cors:AllowedOrigins` (Development: `http://localhost:5173`).

## Versions

All dependency versions are pinned exactly and were verified as the latest stable on 2026-11-25:

- **Backend:** `backend/Directory.Packages.props`, with central package management.
- **Web:** `web/package.json`, with exact versions.
- **Android:** `android/gradle/libs.versions.toml`.
- **Gradle:** `android/gradle/wrapper/gradle-wrapper.properties`, with a SHA-256 checksum.

Upgrade them on purpose, not with automatic ranges.
