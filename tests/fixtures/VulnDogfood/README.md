# VulnDogfood fixture

Intentional vulnerable package for habitat `cdp_pkg_*` dogfood.

| Step | Tool |
|------|------|
| 1 | `cdp_pkg_audit path=.../VulnDogfood.csproj` → `vulnerable:1` (GHSA-5crp-9r3c-p9vr) |
| 2 | `cdp_pkg_upgrade_plan` → SDK `apply_command` |
| 3 | `cdp_pkg_fix_vuln` → `dotnet package update --vulnerable` |
| 4 | Re-audit → `clean`; reset csproj to `11.0.2` for next run |

Pinned: **Newtonsoft.Json 11.0.2** (CVE-2024-21907, High).
