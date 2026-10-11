# Toshiba sign-in diagnostics

A standalone console harness for investigating Toshiba cloud sign-in problems and checking the production HTTP service. It compiles the actual `ToshibaAcHttpService` source without starting the website, hosted services, MQTT, database, or device controls. It targets .NET 10 and needs no additional NuGet packages.

## Usage

From the repository root:

```powershell
dotnet run --project AcControl.ToshibaDiagnostics -- --self-test
dotnet run --project AcControl.ToshibaDiagnostics -- --check-config
dotnet run --project AcControl.ToshibaDiagnostics -- --live
```

Running with no arguments shows help and sends no requests.

- `--self-test` runs offline regression checks with fake HTTP responses and credentials, without reading user secrets.
- `--check-config` checks whether the required settings are present, without displaying their values or making a request.
- `--live` makes one login attempt using the production service.

Live login is never part of the offline checks or an automatic build/test.

Exit codes: `0` success/help, `1` login or offline-check failure, `2` invalid arguments, missing configuration, or cooldown refusal.

## Credentials and live attempts

The project deliberately shares `AcControl.Server`'s `UserSecretsId`. It loads `Toshiba:Username`, `Toshiba:Password`, and `Toshiba:DeviceId` directly from the existing user-secrets store. It does not copy, modify, or display their values. If the server's secret ID changes, update the diagnostics project's matching ID too.

Each `--live` run sends one POST to the fixed Toshiba login endpoint. It does not register a mobile device, fetch AC units, change AC settings, or persist the returned access token. HTTP redirects are disabled so credentials cannot be automatically forwarded to another URL. Request/response body logging and default HTTP client logging are disabled in this harness. It prints only configuration presence, HTTP status, response format, parsed Retry-After, token lifetime, whether required login fields are present, and safe transport error categories.

A file lock and timestamp in `obj/toshiba-login-cooldown.txt` prevent simultaneous diagnostics runs and enforce at least five minutes between live attempts. A 429 with no Retry-After extends that to fifteen minutes; a longer server Retry-After takes precedence. It does not retry or wait automatically. The file contains only a timestamp and is ignored by Git. Cleaning `obj` removes this local safeguard; it cannot coordinate with the running website, phone app, other checkouts or computers.

Keep `Toshiba:DeviceId` stable. The harness reuses the configured ID; it never rotates IDs to get around a rejection.

## Comparing request formats

To reproduce the login request used before the October 2026 fix, after the cooldown:

```powershell
dotnet run --project AcControl.ToshibaDiagnostics -- --live --legacy-request
```

This removes `Device-ID` and sends lower-case `username`/`password`, reproducing the previous `PostAsJsonAsync` request while retaining safe failure reporting. Both details change together, so a comparison cannot isolate which difference matters. Server policy and timing can also affect results. Each run remains subject to the same cooldown; do not run a retry loop.

## Regression coverage

`--self-test` covers the production request shape, token decoding, 429 and Retry-After handling, HTML failures, application-level rejection, invalid success responses, missing configuration, cancellation, device headers on authenticated calls, and the legacy comparison. Offline checks validate client behaviour; a deliberate live attempt is needed to check whether the cloud accepts a request.

## Troubleshooting

A 429 alone does not establish whether the cause is excessive traffic, credentials, or request compatibility. Preserve the HTTP status and respect any server cooldown. A transport failure without an HTTP response cannot establish whether Toshiba accepted or rejected the request.

For a persistent failure, compare the service request with a maintained integration or the working official app. Record the app/client version and request shape, keeping credential and token values local and redacted. Avoid guessing additional headers or repeatedly trying passwords.

## Issue history

### 11 October 2026: HTTP 429 during sign-in

The website repeatedly received HTTP 429 from `/api/Consumer/Login`, while the official Android app could sign in with the same account. This prompted the addition of the standalone diagnostics harness.

The service used the same endpoint as other integrations, but omitted `Device-ID` and relied on .NET's web JSON defaults, which lowercased its credential keys. It also ignored `IsSuccess` and logged failed response bodies before deserializing them as success objects.

At the time of investigation, the [community HTTP client](https://github.com/vmvelev/Toshiba-AC-control/blob/main/toshiba_ac/utils/http_api.py) sent a stable `Device-ID` on every request, suppressed its default User-Agent, and posted `Username`/`Password`. Its source comments attributed the device header to the Android app; the app was not independently inspected.

The production service was updated to:

- Send the configured stable `Device-ID` on login and authenticated requests, and preserve `Username`/`Password` casing in the login body.
- Read `token_type` and `expires_in` explicitly and validate `IsSuccess`, the access token, and consumer ID.
- Report failed login status and Retry-After without logging raw response bodies.
- Use a named HTTP client with redirects disabled and a 30-second timeout, without automatic retries.

Verification: all 12 offline checks passed, including numeric `StatusCode` responses and both Retry-After formats. A single live attempt with the updated service returned **HTTP 200**, `IsSuccess=true`, and populated access token and consumer ID. Website operation was subsequently confirmed with the same change.

The old request was not sent live during this investigation. The successful result did not isolate the device header from JSON casing or prove the cause of the previous 429 responses. The harness tested sign-in only; device discovery, registration, and control were outside its scope.

Related reports reviewed at the time:

- [KaSroka/Toshiba-AC-control #85](https://github.com/KaSroka/Toshiba-AC-control/issues/85): an open 429 login report, including failure after hours without requests. Its [HTTP client](https://github.com/KaSroka/Toshiba-AC-control/blob/master/toshiba_ac/utils/http_api.py) used a browser User-Agent and omitted the device header.
- [Homebridge Toshiba AC #3](https://github.com/jonaskpb/homebridge-toshiba-ac/issues/3): an open report of repeated 429 during login.
- [Community Home Assistant integration #25](https://github.com/vmvelev/home-assistant-toshiba_ac/issues/25): a matching report where the official app worked. The issue was closed, but the available issue content did not establish its resolution.

These observations describe the investigation date; linked implementations and issue status may change.
