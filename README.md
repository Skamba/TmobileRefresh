# TmobileRefresh

A .NET CLI utility that automatically tops up T-Mobile Netherlands daily roaming bundles for Unlimited subscriptions.

## What this tool does

1. Authenticates with your T-Mobile username/password.
2. Resolves your linked subscription endpoint.
3. Polls roaming bundle usage.
4. Automatically requests a new bundle when the configured one is missing or near depletion.

## Usage

```bash
dotnet run -- <username> <password> [bundleCode] [estimatedBytesPerMs]
```

Arguments:

- `username` (required): Your T-Mobile login.
- `password` (required): Your T-Mobile password.
- `bundleCode` (optional): Bundle buying code to request. Default: `A0DAY01`.
- `estimatedBytesPerMs` (optional): Consumption estimate used to schedule checks. Default: `25`.

## Build

```bash
dotnet build
```

## Notes

- This project uses a polling-based strategy and may need tuning for your traffic profile.
- This program is not affiliated with T-Mobile.

## Disclaimer

This may not be in line with T-Mobile terms and conditions. Use at your own risk.
