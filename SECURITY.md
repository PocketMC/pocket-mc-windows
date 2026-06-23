# Security & Architecture Policy

PocketMC is an open-source, local-first Minecraft server management application for Windows. Because PocketMC executes server runtimes, handles downloads, and offers remote-control capabilities, it is designed from the ground up to follow strict security best practices.

This document serves as a guide for developers, moderators, and security-minded users to audit the security architecture of PocketMC.

---

## 1. Local-First & Data Ownership
* **Zero Cloud Lock-In:** Your servers, settings, world files, logs, and mods live entirely on your machine in the directory you select.
* **No Server Relays:** PocketMC does not route your game traffic or server console data through third-party proprietary servers. All remote web connections bind locally to your host.

---

## 2. Secure Local Credentials Storage
To support automatic cloud backups, PocketMC securely stores OAuth access and refresh tokens for Google Drive, Dropbox, and OneDrive. 
* **Mechanism:** Sensitive settings and authentication tokens are encrypted at rest using the **Windows Data Protection API (DPAPI)**.
* **Security Boundary:** DPAPI encrypts credentials utilizing cryptographic keys managed directly by Windows. The ciphertext is bound to your specific Windows user account, meaning other users or processes running under a different user context cannot decrypt the data.
* **Implementation:** See the [DataProtector.cs](PocketMC.Desktop/Infrastructure/Security/DataProtector.cs) file for the DPAPI encryption and decryption wrapper.

---

## 3. Executable & Runtime Integrity
To run Minecraft Java, Bedrock Dedicated Server, and PocketMine-MP, PocketMC automates runtime downloads (Java/PHP) and utility binaries (`playit.exe`, `cloudflared.exe`).
* **Java Packages:** Runtimes are fetched directly from the Eclipse Adoptium API. PocketMC validates the downloaded packages against their upstream SHA-256 checksums before extraction.
* **Playit Agent Verification:** The playit binary is validated via two verification levels:
  1. Validation against pinned SHA-256 checksums.
  2. Windows Authenticode digital signature checking. PocketMC verifies the executable signature using `X509Certificate.CreateFromSignedFile` to ensure the binary is signed by the trusted publisher.
* **Staged Execution:** All downloads are written to `.partial` files and fully validated *before* being promoted to their operational paths. If a validation step fails, the partial file is safely deleted, preventing corrupted or tampered executables from running.
* **Implementation:** 
  * Adoptium Java download and hash validation: [DownloaderService.cs](PocketMC.Desktop/Features/Instances/Services/DownloaderService.cs#L100-L111)
  * Signature and checksum checking: [DownloaderService.cs](PocketMC.Desktop/Features/Instances/Services/DownloaderService.cs#L255-L291)

---

## 4. Remote Dashboard & Console Security
PocketMC allows managing your server instances on the go via a remote web dashboard. The web dashboard is secured using:
* **Password Hashing:** PocketMC hashes dashboard passwords using **PBKDF2** (`Rfc2898DeriveBytes`) with **100,000 iterations of SHA-256** and a cryptographically strong 16-byte random salt.
* **Timing-Attack Protection:** Password comparisons are verified using a constant-time equality check (`CryptographicOperations.FixedTimeEquals`), eliminating side-channel timing analysis.
* **Cookie Security:** Auth sessions use cookies configured with `HttpOnly` (preventing script-based hijacking) and `SameSite = SameSiteMode.Strict` properties.
* **Session Invalidations:** Active sessions validate a dynamic `SecurityStamp`. Changing the dashboard password updates this stamp, rejecting existing cookie principals immediately.
* **Rate Limiting:** Web APIs and console logins are protected by an in-memory windowed rate limiter to mitigate brute-force guessing attacks.
* **Implementation:**
  * PBKDF2 hashing & verification: [RemoteAuthenticationService.cs](PocketMC.Desktop/Features/RemoteControl/Services/RemoteAuthenticationService.cs)
  * Session cookie & WebSocket authentication: [RemoteDashboardHost.cs](PocketMC.Desktop/Features/RemoteControl/Hosting/RemoteDashboardHost.cs#L95-L137)
  * API Rate limiter: [RemoteRequestLimiter.cs](PocketMC.Desktop/Features/RemoteControl/Services/RemoteRequestLimiter.cs)

---

## 5. Mitigation of Common Exploits

### Zip Slip Protection (Directory Traversal)
Extracting untrusted ZIP archives (such as Bedrock `.mcpack` files, mod packs, or external backup zip files) could expose an application to path traversal vulnerabilities.
* PocketMC sanitizes and validates every entry's target path before writing to disk. The application checks that the resolved canonical path strictly starts within the designated extraction root directory.
* **Implementation:** Check the path normalization and containment validation in [PathSafety.cs](PocketMC.Desktop/Infrastructure/Security/PathSafety.cs#L45-L56) and [SafeZipExtractor.cs](PocketMC.Desktop/Features/Instances/Backups/SafeZipExtractor.cs#L29-L33).

### Console & Command Injection
To prevent RCON or process input command exploits, console inputs are sanitized and validated. Log files are processed dynamically using line-by-line buffers to prevent log injection.

---

## 6. Telemetry & Privacy Controls
* **Anonymous Data:** Telemetry is strictly diagnostic (app startup, shutdown, install/upgrade actions, and server create/delete events) to help track app health. 
* **Identifiability:** No personally identifiable information (PII), server console logs, file contents, IP addresses, or Minecraft player names are collected. A randomly generated GUID Client ID is used to differentiate installs.
* **Opt-Out:** You can disable telemetry entirely at any time in **App Settings -> Enable Telemetry**.
* **Implementation:** See [TelemetryService.cs](PocketMC.Desktop/Features/Settings/TelemetryService.cs).

---

## 7. Reporting a Vulnerability
If you discover a security vulnerability in PocketMC, please report it privately:
* **Contact:** Open a confidential GitHub Security Advisory, or email the developer at [sahajitaliya33@gmail.com](mailto:sahajitaliya33@gmail.com).
* Please do not report security issues via public GitHub issues, Discord chats, or YouTube comments. We will investigate and respond to all reports within 48 hours.
