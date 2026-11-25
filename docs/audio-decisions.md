# Audio decisions (PRD section 18.5)

Results of the recitation tests and the defaults chosen from them. **Status: tests not run yet (Phase 2).** Fill this in during testing; repeat in Phase 6.

## How to run a test session

1. On the PC: `./scripts/livekit-dev.ps1` (keep it running). Then, in another terminal: `cd backend && dotnet run --project src/ALNoor.Api --launch-profile lan`.
2. **Teacher on the web:** `cd web && npm run dev`, open http://localhost:5173 and use the "LiveKit test room" section. The browser only allows the microphone on `localhost` or HTTPS, so the web client must run on the PC itself.
3. **Students on Android phones:** install the debug APK (`android/app/build/outputs/apk/debug/app-debug.apk`). Enter API address `http://<PC Wi-Fi IP>:5080`, the same room name, and role Student. The dev script prints the PC's Wi-Fi IP.
4. Everyone picks the **same audio settings** before joining. The room screen shows "Testing: …"; record that label with each rating.
5. Follow the protocol below, then change one setting, leave and rejoin.

Phones and PC must be on the same Wi-Fi. If Windows Firewall asks, allow `livekit-server` and the API on **private** networks only.

## Protocol (from PRD 18.5)

1. The teacher recites a fixed passage containing ghunnah, madd, qalqalah and soft letters.
2. Students on 3+ device types listen and rate clarity 1–5; the teacher listens to two students reciting.
3. Repeat for each combination: bitrate (24 / 32 / 48) × noise suppression × AGC × DTX, on Wi-Fi and 4G.
4. Repeat with a network limiter: 3% and 10% packet loss, 300 ms latency (Android developer options, router, or laptop hotspot shaper).
5. Choose the defaults with the best clarity; record them below.

Note: 4G testing needs the API and LiveKit to be reachable from the internet. The local dev server is Wi-Fi only, so 4G rounds use a LiveKit Cloud dev project, or wait for Phase 6.

## Fixed settings (not tested; decided by the PRD)

| Setting | Value | Reason |
|---|---|---|
| Codec | Opus | WebRTC default |
| Echo cancellation | On | Essential on phone speakers |
| RED (redundancy) | On | Helps with packet loss |
| Video | VP8, 640×360 @ 15 fps, simulcast, adaptive stream, dynacast | PRD 18.2 |

## Starting values (to confirm or change)

| Setting | Starting value |
|---|---|
| Bitrate | 32 kbps mono |
| DTX | On |
| Noise suppression | On |
| Auto gain control | Off |

Known limitation: on Android, some phones apply their own hardware noise suppression and echo cancellation regardless of these settings. If a phone sounds the same with NS on and off, note the model.

## Results

| Date | Network | Teacher device | Student device | Settings label | Clarity 1–5 (teacher → student) | Clarity 1–5 (student → teacher) | Notes (ghunnah, madd, soft letters, clipping) |
|---|---|---|---|---|---|---|---|
| | | | | | | | |

## Decisions

| Setting | Chosen default | Decided on | Evidence (rows above) |
|---|---|---|---|
| Bitrate | | | |
| DTX | | | |
| Noise suppression | | | |
| Auto gain control | | | |
