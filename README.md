# TmobileRefresh

A .NET CLI utility that automatically tops up Odido Netherlands daily roaming bundles for Unlimited subscriptions.

## What this tool does

1. Authenticates with your Odido username/password.
2. Resolves your linked subscription endpoint.
3. Polls roaming bundle usage.
4. Automatically requests a new bundle when the configured one is missing or near depletion.

## Usage

```bash
dotnet run -- <username> <password> [bundleCode] [estimatedBytesPerMs]
```

Arguments:

- `username` (required): Your Odido login.
- `password` (required): Your Odido password.
- `bundleCode` (optional): Bundle buying code to request. Default: `A0DAY01`.
- `estimatedBytesPerMs` (optional): Consumption estimate used to schedule checks. Default: `25`.

Optional environment variables for API compatibility:

- `ODIDO_API_BASE_URL`: Explicit API base URL (defaults to trying `https://capi.odido.nl` first, then legacy `https://capi.t-mobile.nl`).
- `ODIDO_CLIENT_ID`: Override mobile app client id.
- `ODIDO_BASIC_AUTH_TOKEN`: Override basic auth token used by `/login`.
- `ODIDO_SCOPE`: Override requested OAuth scope.
- `ODIDO_USER_AGENT`: Override user-agent header.

## Build

```bash
dotnet build
```

## Notes

- This project uses a polling-based strategy and may need tuning for your traffic profile.
- This program is not affiliated with Odido.

## Disclaimer

This may not be in line with Odido terms and conditions. Use at your own risk.
