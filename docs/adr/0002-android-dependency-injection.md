# ADR 0002 – Android dependency injection: manual container for now

- **Status:** Accepted (Phase 3, 2026-11-26). Revisit at the start of Phase 4.
- **Context:** PRD section 13 lists Hilt for Android DI.

## Context

Phase 3 adds the first real Android services: `AuthRepository` (tokens, refresh), `EncryptedRefreshTokenStore` (DataStore + Tink + Keystore) and one `AuthViewModel`. Hilt needs an annotation processor (KSP), a Gradle plugin and generated code. This project builds with AGP 9.5 (built-in Kotlin) and Kotlin 2.4. Adding Hilt now means checking KSP/Hilt plugin compatibility with that toolchain for three objects.

## Decision

Use a small manual container (`AppContainer`, created in `ALNoorApplication`) and a `ViewModelProvider.Factory`. Classes take their dependencies as constructor parameters, so a later switch to Hilt only touches the container and factories, not the classes.

## Consequences

- No annotation processing and no extra Gradle plugins; faster builds.
- Tests construct classes directly with fakes (e.g. `AuthRepositoryTest` uses an in-memory token store and MockWebServer).
- **Revisit trigger:** Phase 4 adds several repositories, the `ClassroomManager` (foreground-service scoped) and more ViewModels. If wiring by hand becomes repetitive or error-prone, switch to Hilt then, as the PRD intends.
