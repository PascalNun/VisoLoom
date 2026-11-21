# VisoLoom — agent instructions

## Commit and push policy

The user works alone and syncs machines through Git. These defaults apply
unless the user says otherwise for a task.

- Commits: one imperative subject line (aim for <= 72 characters). Add a body
  only when the subject cannot carry the information, and keep it to a few
  factual lines. No prose, no narratives.
- Never add agent attribution of any kind: no "Co-Authored-By", no "Generated
  by/with <tool>", no agent mentions. The author of every commit is the user.
- When a piece of work is complete and verified, land it on main — linear
  history if possible — and push to origin main right away. Do not hold work
  back on side branches.
- Never force-push main and never rewrite published history.
- Never commit secrets, API keys, local state (`.codex/`, `.env`,
  credentials) or absolute local paths. Check what is staged before
  committing.

## Working in this repository

- Never publish source, assets or fixtures: the repository is private and its
  licensing model is undecided. [CONTRIBUTING.md](CONTRIBUTING.md) has the
  full conventions.
- Build must stay at zero warnings: `dotnet build VisoLoom.sln`
- Tests: `dotnet test tests/VisoLoom.Protocol.Tests`, plus the Core tests on
  the isolated SDK:
  `~/.local/share/visoloom-dev/dotnet/dotnet test tests/VisoLoom.Core.Tests`
  and the Python engine suite: `python3 -m unittest discover -s tests/engine`
  (one skip is pre-existing).
- Every increment records itself: an ADR in `docs/adr/` plus a row in
  `docs/adr/README.md`, and a dated section in
  `docs/development/phase-4-review.md`. Provider-facing increments also
  update `docs/providers/current-capabilities.md`.
- Rhino-side acceptance (registered panel, Settings dialogs, Windows) can
  only be run by the user; record what is still owed as native checks.
