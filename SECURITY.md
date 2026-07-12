# Security Policy

## Reporting a vulnerability

Please do not open a public issue for vulnerabilities or suspected token exposure. Use GitHub's **Report a vulnerability** flow in the repository Security tab.

Include the affected platform, reproduction steps, impact, and the smallest safe proof of concept. Do not include real Codex access tokens, `auth.json`, or private usage data.

## Project boundaries

Codex Pet HUD reads the local Codex pet state and uses the existing Codex access token in memory to request usage data. It must never persist or log that token. Cache maintenance is restricted to paths owned by Codex Pet HUD.
