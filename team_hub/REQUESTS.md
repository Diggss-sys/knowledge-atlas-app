# Requests for Claude

*(Anti's inbox — file a request here whenever the hub needs data Claude owns. Claude services it,
records the outcome in `data/CHANGELOG.md`, and clears the entry.)*

## For Anti — page update available (data is ready in status.json + CHANGELOG)

Everything below is already in `data/status.json` and `data/CHANGELOG.md` — please sync the page to
it in your own design. Nothing here is invented; if a detail isn't in those files, leave it out.

1. **Metrics:** checks count is now **109** (106 EditMode + 3 PlayMode); the PR tile is **PR #3**
   (A1+A2 instrument, needs Diego's review; PR #2 is merged ✅).
2. **Recently Shipped:** two new items at the top — *Team Run app released* and *Author → run loop
   closed (publish → consume)*. Full list + blurbs in `status.json.shipped`.
3. **Team Run section (the important one):** the downloads are real now — pull from
   `status.json.team_run`:
   - `windows_download` → **Windows button is live** (verified build).
   - `mac_download` → Mac build is attached BUT `mac_status` is **`pending_verification`**. Show it as
     "Mac (universal) — verifying" / not-yet-a-primary-button, per that status. Don't present Mac as
     fully ready until `mac_status` flips to `live` (Claude will flip it once a Mac confirms it opens).
   - `mac_note` has the unsigned first-launch phrasing (right-click → Open → Open).
4. **Milestones/roster:** unchanged since the last sync (M1 closed end-to-end; Diego → review PR #3 +
   preset-ranges call).

**Guardrails unchanged:** only touch `team_hub/`; do NOT deploy (Pages stays manual until the team
okays going public); `status.json` is the source of truth.
