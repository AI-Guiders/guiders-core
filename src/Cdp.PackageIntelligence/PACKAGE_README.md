# AIGuiders.Cdp.PackageIntelligence

In-proc L1 package intelligence for CDP habitat:

- **Audit** — list vulnerable packages (`dotnet list package --vulnerable` JSON parse)
- **Latest** — resolve latest stable (or prerelease) from NuGet feeds
- **Upgrade plan** — suggest target versions for known vulnerabilities
- **Supply chain** — NuGet.Config sources, CPM (`Directory.Packages.props`), basic hygiene signals

L0 mutate remains in `AIGuiders.Cdp.ScriptableIde` (`PackageOps`). Offline/air-gap feeds: `agent-nuget-pm` (ANPM).
