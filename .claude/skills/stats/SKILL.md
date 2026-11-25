---
name: stats
description: Show GitHub statistics (release downloads, traffic, stars, forks) for MeshcomWebDesk with trend since the last run.
---

# GitHub statistics on demand

Run `pwsh scripts/github-stats.ps1` (add `-Html` if the user wants the report page opened in the browser,
`-NoSave` to look without writing to the history). It needs the `gh` CLI logged in as the repository owner
(traffic data is owner-only).

Each run appends a snapshot to `scripts/stats-history/` (git-ignored), so the output also shows the change
since the previous run and, over time, trends beyond GitHub's 14-day traffic window.

Summarise the output for the user in German: stars/forks, download change since the last run, the newest
releases, traffic. Always point out that downloads/clones/views are NOT the number of running installations,
and that Docker pull counts are only visible on the GitHub package page.
