# ADR 0001 – LiveKit server integration in .NET

- **Status:** Accepted (Phase 2, 2026-11-25)
- **Context:** PRD section 13 ("LiveKit server" row) and section 26 Phase 2 ask for a decision on the .NET LiveKit library.

## Context

The backend needs two things from LiveKit:

1. **Access tokens** (Phase 2 onward): HS256 JWTs signed with the LiveKit API secret, carrying the grants in PRD section 14.3.
2. **Server API calls** (Phase 4–5): create room, list participants, mute track, remove participant, delete room (PRD section 14.5), plus **webhook verification** (section 14.6).

Options checked on NuGet on 2026-11-25:

| Option | Notes |
|---|---|
| Official LiveKit .NET server SDK | **Does not exist.** No package on NuGet is published by LiveKit. |
| `Livekit.Server.Sdk.Dotnet` 1.2.7 (community, single maintainer) | Most used (~471k downloads). Not verified on NuGet. Would hold our API secret and sit on the token-issuing path. |
| Other community packages (`LiveKit.AspNetCore.ServerSdk`, `C-sharp-livekit-sdk`, …) | Far less used; same single-maintainer risk. |
| **Own thin integration** | Tokens through Microsoft's `Microsoft.IdentityModel.JsonWebTokens` (already the basis of ASP.NET Core JWT auth in Phase 3). Server API as plain HTTP + JSON (LiveKit's Twirp endpoints, `POST /twirp/livekit.RoomService/<Method>`). |

## Decision

**Use our own thin integration; no third-party LiveKit .NET package.**

- `Features/LiveKit/LiveKitTokenService` builds join tokens with `JsonWebTokenHandler` (HS256).
- Phase 4 adds a small `LiveKitRoomClient` (typed `HttpClient`) for the five RoomService calls, authenticated with a short-lived admin token (`roomAdmin`/`roomCreate`/`roomList` grants, server-side only).
- Phase 5 adds webhook verification: validate the `Authorization` JWT with the API secret and compare its `sha256` claim with the SHA-256 of the raw body.

## Reasons

- **Small surface:** one token type, five RPC calls and one webhook check. A wrapper library saves very little code.
- **Security:** the API secret and token grants stay in code we review. Microsoft's JWT library is well maintained and patched.
- **Dependency risk:** an unofficial, single-maintainer package on the token path is a supply-chain and maintenance risk for a small team (CLAUDE.md: no unnecessary dependencies).
- **Verifiable:** tests prove a real LiveKit server (`livekit/livekit-server` container, same version as the dev script) accepts our tokens and rejects tampered or expired ones (`LiveKitServerAcceptanceTests`).

## Consequences

- We track LiveKit's token claim format and Twirp API ourselves. Both are stable, documented protocols; the acceptance tests run on every CI build and catch breaking changes when the server version is bumped.
- If LiveKit releases an official .NET server SDK, re-evaluate. Switching would only affect `Features/LiveKit`.
