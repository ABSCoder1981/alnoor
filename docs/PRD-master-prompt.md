# MASTER PROMPT: Create a Professional PRD for ALNoor – Online Quran Classroom Platform

## 1. Your Role

Act as a Senior Product Manager, Solution Architect, Business Analyst and Technical Product Owner with extensive experience designing real-time video conferencing platforms, Android applications and SaaS products.

Your task is to analyze the complete requirements below and create a detailed, professional Product Requirements Document (PRD) for an application called **ALNoor**.

The PRD must be practical, technically accurate, implementation-ready and suitable for development by a small team using Claude Code, Android Studio, VS Code and step-by-step AI-assisted development.

Do not introduce unnecessary features. Prioritize reliability, audio clarity, ease of use, security and minimum infrastructure cost.

---

# 2. Product Overview

**Working Product Name:** ALNoor  
**Product Category:** Online Quran Teaching / Live Virtual Classroom  
**Initial Target Users:** Quran teachers and their students  
**Initial Deployment:** One teacher (my wife) conducting online Quran classes  
**Future Vision:** A SaaS platform that can be offered to other Quran teachers and Islamic educational institutions.

### Product Objective

Currently, Quran classes are conducted through WhatsApp group video calls.

The existing process has problems:

- Audio interruptions during Quran recitation.
- Voice clarity issues.
- Students sometimes face difficulty joining classes.
- Network interruptions affect the learning experience.
- Managing participants in WhatsApp is inconvenient.
- The teacher has limited control over the classroom.

We want to develop a dedicated application to provide a smooth, reliable and easy-to-use online Quran classroom experience.

The most important product objective is:

**Students and teachers should be able to join a live class easily and communicate with clear, uninterrupted audio under normal network conditions.**

Do not promise perfect audio under poor internet conditions. The application must handle network degradation gracefully.

---

# 3. Confirmed Business and Product Requirements

Treat the following decisions as confirmed requirements.

| Requirement | Decision |
|---|---|
| Initial use | Wife's existing Quran classes |
| Initial teacher count | 1 |
| Students per class | 5–9 |
| Maximum participants | 10, including teacher |
| Student devices | Primarily Android |
| Teacher devices | Primarily Android; teacher web-browser access should also be supported |
| Video | Teacher and students can enable video |
| Audio | Essential feature; prioritize voice clarity |
| Login | Phone number + password |
| RTC infrastructure | LiveKit Cloud free Build plan initially |
| Development approach | Step-by-step implementation with Claude guidance |
| Android technology | Native Kotlin |
| Android UI | Jetpack Compose |
| Teacher web application | React + TypeScript |
| Backend | ASP.NET Core Web API |
| Database | PostgreSQL |
| Future business model | Multi-teacher SaaS |
| Initial budget | Minimum possible |
| Initial V1 scope | Live audio/video classroom only |

The application must support a teacher conducting classes with up to nine students.

The teacher must be able to conduct a class from an Android phone without requiring a laptop.

---

# 4. Scope of Version 1 (MVP)

Keep the initial release limited to live classroom functionality.

## 4.1 Authentication

Implement:

- Phone number login.
- Password-based authentication.
- Secure password hashing.
- JWT access tokens.
- Refresh token mechanism.
- Logout.
- User account activation/deactivation.
- Role-based access control.

Roles:

1. Teacher
2. Student
3. Administrator (only if needed for initial management)

For V1, the teacher should be able to create student accounts or invite students through a controlled enrollment process.

Do not assume that public self-registration is required.

Do not implement OTP authentication in V1 unless a specific business requirement is identified.

## 4.2 Class Management

Teacher capabilities:

- Create a class.
- Edit class information.
- View enrolled students.
- Add students.
- Remove students.
- View upcoming classes.
- Start a live class.
- End a live class.

Student capabilities:

- View enrolled classes.
- View upcoming class information.
- Join an active class.
- Leave a class.

A class and a class session must be separate concepts. A recurring class should generate a separate session for every occurrence.

## 4.3 Live Classroom

This is the most important module.

### Teacher functionality

- Start classroom.
- Join classroom.
- Enable/disable microphone.
- Enable/disable camera.
- View all participants.
- See participant connection status.
- Mute a participant when necessary.
- Remove a participant.
- End classroom for everyone.
- See students joining and leaving.

### Student functionality

- Join classroom.
- Enable/disable microphone.
- Enable/disable camera.
- View teacher and other students.
- Leave classroom.
- See connection status.
- Recover from temporary network interruptions.

### Classroom requirements

- Maximum 10 participants including teacher.
- All participants may enable video.
- Microphone and camera controls must be simple.
- Video should be off by default unless explicitly enabled.
- The interface must be usable on small Android screens.
- Support late joining.
- Support participant reconnection.
- Display clear joining, connected, reconnecting and disconnected states.
- Prevent unauthorized users from entering a classroom.

Do not add chat, file sharing, screen sharing, Quran content libraries or recording in V1.

---

# 5. Audio and Video Quality Requirements

Audio quality is the highest technical priority because the application is intended for Quran recitation and teaching.

Evaluate and implement:

- WebRTC-based real-time communication.
- Opus audio codec.
- Echo cancellation.
- Noise suppression, with testing to ensure it does not remove subtle Quran pronunciation sounds.
- Automatic gain control, after testing.
- Adaptive bitrate.
- Video quality adaptation.
- Reconnection handling.
- Audio-first fallback when network quality deteriorates.

Suggested starting configuration:

- Audio bitrate: approximately 24–32 kbps.
- Video starting quality: 360p at 15 FPS.
- Adaptive resolution based on network conditions.
- Camera disabled by default.
- Microphone enabled only after explicit user permission and classroom entry.

These are starting values, not final guarantees. Validate them through actual recitation tests on different Android devices and network conditions.

Monitor:

- Packet loss.
- Jitter.
- Round-trip time.
- Audio interruption duration.
- Reconnection count.
- Video resolution.
- Participant connection state.

Do not record students' Quran recitation for diagnostics.

Provide a clear audio-first fallback when video is causing network problems.

---

# 6. Video Infrastructure – LiveKit

Use LiveKit Cloud free Build plan for the initial pilot.

LiveKit is responsible for real-time audio/video transport.

Important architecture requirements:

1. Backend generates LiveKit access tokens.
2. Android and web clients connect directly to LiveKit.
3. LiveKit API secret must never be exposed in Android or browser code.
4. Backend verifies the logged-in user before issuing a token.
5. Backend verifies class enrollment and active session status.
6. Backend enforces the maximum participant limit.
7. Teacher permissions and student permissions must be controlled.
8. Validate LiveKit webhook signatures.
9. Process webhooks idempotently.
10. Use LiveKit server APIs for teacher moderation.

Use LiveKit Cloud initially to avoid operating real-time media infrastructure ourselves.

However, document:

- Free-plan participant-minute limits.
- Bandwidth and data-transfer limits.
- Concurrent participant limits.
- What happens when free allowances are exhausted.
- How usage will be monitored.
- Estimated monthly usage for different class schedules.

Do not assume the free plan will be sufficient indefinitely.

Provide a future migration assessment for self-hosted LiveKit.

Self-hosting should be considered only after evaluating VPS, bandwidth, TURN, firewall, domain, SSL, monitoring and maintenance costs.

Useful documentation:

- LiveKit Android SDK: https://docs.livekit.io/transport/sdk-platforms/android/
- LiveKit Android Compose: https://docs.livekit.io/transport/sdk-platforms/android-compose/
- LiveKit React components: https://docs.livekit.io/reference/components/react/
- LiveKit tokens: https://docs.livekit.io/frontends/reference/tokens-grants/
- LiveKit Cloud pricing: https://livekit.com/pricing

---

# 7. Proposed Technical Architecture

Use a modular monolith architecture initially.

Avoid microservices.

### Android Application

- Kotlin
- Jetpack Compose
- MVVM
- Kotlin Coroutines and Flow
- Hilt for dependency injection
- Retrofit or an equivalent HTTP client
- LiveKit Android SDK
- Room only where local persistence is required

### Teacher Web Application

- React
- TypeScript
- Vite
- LiveKit React components
- Responsive design
- Chrome and Edge support

### Backend

- ASP.NET Core Web API
- .NET 10 LTS, subject to current SDK and hosting compatibility
- Entity Framework Core
- PostgreSQL
- JWT authentication
- ASP.NET Core Identity or an equivalent secure authentication implementation
- LiveKit server-side token generation
- LiveKit webhooks

### Infrastructure

- LiveKit Cloud for real-time audio/video.
- PostgreSQL database.
- Backend hosted on a low-cost VPS or suitable .NET hosting provider.
- Teacher web app on a suitable static hosting provider.
- HTTPS for all production application endpoints.
- Firebase Cloud Messaging can be introduced later for push notifications.

### Architecture diagram

Include a clear architecture diagram showing:

Android Student App  
Android Teacher App  
Teacher Web App  
↓  
ASP.NET Core Web API  
↓  
PostgreSQL Database

The Android and web clients connect directly to LiveKit Cloud for real-time audio/video after receiving authorized tokens from the backend.

Clearly distinguish application APIs from real-time media communication.

---

# 8. Database Design

Create an appropriate normalized database design.

Consider these entities:

1. Users
2. Classes
3. ClassStudents
4. ClassSessions
5. Attendance
6. RefreshTokens
7. AuditLogs

Suggested fields:

### Users

- Id
- FullName
- PhoneNumber
- PasswordHash
- Role
- IsActive
- CreatedAt
- UpdatedAt

### Classes

- Id
- TeacherId
- Title
- Description
- StartTime
- EndTime
- IsActive
- CreatedAt

### ClassStudents

- Id
- ClassId
- StudentId
- EnrollmentStatus
- EnrolledAt

### ClassSessions

- Id
- ClassId
- RoomName
- StartedAt
- EndedAt
- Status

### Attendance

- Id
- SessionId
- StudentId
- JoinedAt
- LeftAt
- DurationSeconds

Include:

- Primary and foreign keys.
- Indexes.
- Unique constraints.
- UTC date/time handling.
- Phone numbers stored as strings.
- Secure token storage.
- Appropriate audit fields.

Design the database so that future multi-teacher SaaS support can be introduced without a major rewrite. Explain whether TenantId should be included in V1 or added later.

---

# 9. Backend API Requirements

Prepare detailed API specifications.

Include authentication:

- POST /api/auth/register
- POST /api/auth/login
- POST /api/auth/refresh
- POST /api/auth/logout
- GET /api/auth/me

Class management:

- POST /api/classes
- GET /api/classes
- GET /api/classes/{id}
- PUT /api/classes/{id}
- POST /api/classes/{id}/students
- DELETE /api/classes/{id}/students/{studentId}

Class sessions:

- POST /api/classes/{id}/sessions
- POST /api/sessions/{id}/join
- POST /api/sessions/{id}/leave
- POST /api/sessions/{id}/end
- GET /api/sessions/{id}/participants
- POST /api/sessions/{id}/participants/{userId}/remove
- POST /api/sessions/{id}/participants/{userId}/mute

Attendance:

- GET /api/classes/{id}/attendance
- GET /api/sessions/{id}/attendance
- GET /api/students/{id}/attendance

LiveKit webhook:

- POST /api/webhooks/livekit

For every API specify:

- HTTP method.
- Endpoint.
- Purpose.
- Authentication requirement.
- Authorization requirement.
- Request JSON.
- Response JSON.
- Validation rules.
- Error responses.

The join endpoint must verify the user, enrollment, session status and participant capacity before issuing a LiveKit token.

Never trust a role or permission supplied by the client.

---

# 10. Application UI/UX Requirements

Design a professional, modern, clean and responsive interface.

The application is intended for teachers and students who may not be technically experienced.

Prioritize:

- Simple navigation.
- Large controls.
- Clear icons.
- Easy-to-read text.
- Minimal steps to join a class.
- Clear microphone and camera status.
- Good contrast.
- Accessible touch targets.
- Responsive layouts.

Suggested design direction:

- Calm Islamic-inspired visual identity.
- Deep green, white and subtle gold accents.
- Avoid excessive decorative Islamic patterns.
- Avoid clutter.
- Keep the live classroom screen focused on participants and audio/video controls.

### Android Screens

1. Splash screen.
2. Login screen.
3. Teacher dashboard.
4. Student dashboard.
5. Class list.
6. Class details.
7. Student management.
8. Upcoming class screen.
9. Classroom waiting/joining screen.
10. Live classroom.
11. Participant list.
12. Class ended screen.
13. Attendance/history screen.
14. Profile/settings.

### Teacher Web Screens

1. Login.
2. Dashboard.
3. Class management.
4. Student management.
5. Waiting room.
6. Live classroom.
7. Participant management.
8. Attendance/history.

The teacher should be able to operate the main classroom workflow entirely from Android.

---

# 11. Security and Privacy Requirements

The platform may be used by minors, so security and privacy must be treated as core requirements.

Include:

- HTTPS.
- Secure password hashing.
- JWT and refresh token protection.
- Short-lived LiveKit access tokens.
- Server-side role validation.
- Enrollment validation.
- Participant capacity validation.
- Secure webhook signature verification.
- Rate limiting on authentication endpoints.
- Input validation.
- Database access restrictions.
- No secrets in source control.
- No personal information in LiveKit room names.
- Secure handling of camera and microphone permissions.
- Account deactivation and access revocation.

Do not enable classroom recording in V1.

Do not collect unnecessary student information.

Include a privacy policy and consent considerations, especially for classes involving minors, before public deployment.

---

# 12. Attendance Management

Attendance should be generated using verified session and participant events.

Requirements:

- Record student join time.
- Record student leave time.
- Handle temporary disconnections.
- Avoid duplicate attendance records.
- Calculate attendance duration.
- Show teacher a session attendance list.
- Show student attendance history.

Use LiveKit webhooks as authoritative evidence for participant and room events, combined with backend session state.

Explain how to distinguish temporary reconnection from final departure.

---

# 13. Network and Reconnection Handling

Create a clear reconnection strategy.

Possible states:

- CONNECTING
- CONNECTED
- RECONNECTING
- DISCONNECTED
- FAILED

Requirements:

- Detect connection loss.
- Display reconnection status.
- Use SDK recovery events.
- Avoid starting duplicate connections while automatic reconnection is active.
- Use bounded retry and backoff where appropriate.
- Refresh tokens when necessary.
- Restore microphone and camera preferences where safe.
- Reconcile participant state after reconnecting.

Test Wi-Fi-to-mobile-data switching, app backgrounding, screen locking and incoming phone calls.

Explain Android microphone foreground-service restrictions for supported Android versions.

Do not claim that all network failures can be recovered automatically.

---

# 14. Deployment and Infrastructure

Keep infrastructure cost as low as reasonably possible.

Prepare a deployment plan for:

- ASP.NET Core backend.
- PostgreSQL.
- Teacher web application.
- Android APK for the initial private pilot.
- LiveKit Cloud.

Include:

- Development environment.
- Testing environment.
- Production environment.
- Domain and HTTPS.
- Environment variables.
- Secret management.
- Database backup.
- Logging.
- Monitoring.
- Deployment rollback.

Never expose PostgreSQL directly to the public internet.

Provide an initial cost estimate in INR, with assumptions clearly stated. Separate free-tier allowances from paid costs and identify possible unexpected usage charges.

---

# 15. Testing Requirements

Prepare a detailed test plan.

Test at least:

- One teacher + one student.
- One teacher + five students.
- One teacher + nine students.
- All participants enabling video.
- Teacher muting a student.
- Teacher removing a participant.
- Student joining late.
- Student leaving and rejoining.
- Wi-Fi disconnection.
- Wi-Fi-to-mobile-data switching.
- Weak network conditions.
- Bluetooth headset.
- Wired headset.
- Speaker and microphone.
- Incoming phone call.
- Android background and screen lock.
- Camera and microphone permission denial.
- Unauthorized student attempting to join.
- Duplicate login/session scenarios.
- Maximum participant enforcement.
- Attendance correctness.
- Token expiry.
- Webhook failures and duplicate events.

Prepare measurable release acceptance criteria.

Audio clarity must be evaluated through actual Quran recitation tests, not only generic voice-call tests.

---

# 16. Development Roadmap

The development must be divided into small, independently testable phases.

Suggested sequence:

### Phase 1: Project Foundation
- Git repository.
- Android project.
- Backend project.
- Teacher web project.
- Database foundation.
- Development documentation.

### Phase 2: LiveKit Proof of Concept
- Android connects to LiveKit.
- Teacher and student join a room.
- Audio communication.
- Video communication.
- Test with multiple devices.

### Phase 3: Authentication and Backend
- Phone/password login.
- JWT.
- User roles.
- Database integration.
- Class management APIs.

### Phase 4: Classroom Implementation
- Teacher dashboard.
- Student dashboard.
- Live classroom UI.
- Participant controls.
- Teacher moderation.

### Phase 5: Attendance and Reliability
- Webhooks.
- Attendance tracking.
- Reconnection handling.
- Network adaptation.
- Error handling.

### Phase 6: Security and Testing
- Authentication testing.
- Authorization testing.
- Device testing.
- Audio quality testing.
- Network testing.

### Phase 7: Pilot Deployment
- Deploy backend.
- Configure LiveKit Cloud.
- Build Android APK.
- Deploy teacher web application.
- Conduct real classes.

Provide realistic estimates and dependencies for every phase.

Do not start building every module at once.

---

# 17. Claude-Assisted Development Approach

The project will be developed step-by-step using Claude guidance and Claude Code.

Create a development workflow in which Claude acts as:

- Solution architect.
- Senior Android developer.
- Senior .NET developer.
- React developer.
- Database designer.
- Code reviewer.
- Testing assistant.

For each development phase, provide:

1. Objective.
2. Detailed tasks.
3. Files and folders to create.
4. Dependencies to install.
5. Exact commands.
6. Implementation steps.
7. Complete code requirements.
8. Expected output.
9. How to test.
10. Common errors and troubleshooting.
11. Completion checklist.
12. Git commit recommendation.

Claude must not generate the entire application in one response.

Claude should first analyze the existing repository and architecture, then implement one small task at a time.

After every task:

- Build the project.
- Run tests.
- Fix compilation errors.
- Verify the functionality.
- Update documentation.
- Commit stable changes.

Avoid unnecessary refactoring and avoid changing working code without a reason.

Use current stable compatible package versions and verify SDK compatibility before selecting dependencies.

Never hardcode secrets or credentials.

---

# 18. Future SaaS Scope – Not Part of V1

Document these as future enhancements only:

- Multiple Quran teachers.
- Multiple organizations.
- Multi-tenant architecture.
- Teacher subscription plans.
- Class scheduling.
- Student self-registration.
- Push notifications.
- Payment integration.
- More advanced attendance reports.
- Admin dashboard.
- Custom branding for institutions.

Do not implement these in the first release unless a dependency is essential to avoid major future architectural problems.

---

# 19. Required PRD Deliverables

Create a complete professional PRD with the following sections:

1. Executive Summary.
2. Product Vision.
3. Business Objectives.
4. Target Users and Personas.
5. User Problems.
6. Product Scope.
7. Functional Requirements.
8. Non-functional Requirements.
9. User Stories.
10. Detailed User Workflows.
11. UI/UX Screen Requirements.
12. System Architecture.
13. Technology Stack.
14. LiveKit Integration Design.
15. Database Design and ER Diagram.
16. API Specification.
17. Authentication and Authorization.
18. Audio/Video Quality Strategy.
19. Attendance Management.
20. Network Recovery Strategy.
21. Security and Privacy.
22. Deployment Architecture.
23. Infrastructure Cost Estimate.
24. Testing Strategy.
25. Acceptance Criteria.
26. Development Phases and Timeline.
27. Risks and Mitigation.
28. Future SaaS Roadmap.
29. Assumptions and Open Questions.

Include Mermaid diagrams for:

- High-level architecture.
- Database ER diagram.
- Teacher starts class workflow.
- Student joins class workflow.
- LiveKit token authorization flow.
- Reconnection workflow.

Include practical tables wherever appropriate.

---

# 20. Important Instructions

1. Do not invent requirements that were not discussed.
2. Mark assumptions explicitly.
3. Identify missing decisions as open questions.
4. Do not over-engineer the architecture.
5. Keep the first version affordable and maintainable.
6. Prioritize audio quality over visual effects.
7. Do not include Quran content or recitation-management features in V1.
8. Do not include recording in V1.
9. Do not assume LiveKit Cloud free usage is unlimited.
10. Do not claim that video calls will work perfectly on every network.
11. Ensure Android is the primary teacher experience.
12. Keep the web application available as an additional teacher access option.
13. Explain technical decisions in simple language.
14. Make the PRD detailed enough that a developer can implement it without repeatedly guessing the requirements.

**Final instruction:** First analyze all requirements, identify contradictions and missing decisions, then prepare the complete PRD. Do not begin coding until the PRD is reviewed and approved.
