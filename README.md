# raft-mq

## Claude Code setup

This repo uses [`LukaszStrz/agent-skills`](https://github.com/LukaszStrz/agent-skills) (a fork
of [`addyosmani/agent-skills`](https://github.com/addyosmani/agent-skills)) as a Claude Code
plugin — not a git submodule. The marketplace registration lives in the committed
[`.claude/settings.json`](.claude/settings.json), but Claude Code doesn't auto-fetch plugins
from an external `github` source just because they're listed there, so there's one manual,
one-time step per machine:

1. Clone the repo and open it in Claude Code. Accept the workspace-trust dialog the first time
   it prompts — until you do, `.claude/settings.json` is ignored entirely (silently, no error).
2. Install the plugin once:
   ```bash
   claude plugin install agent-skills@addy-agent-skills
   ```
   (`enabledPlugins` in `.claude/settings.json` only toggles an *already-installed* plugin on
   or off — it doesn't trigger the install. This step does.)
3. Verify: `claude plugin list` should show `agent-skills@addy-agent-skills`, and commands like
   `/spec`, `/plan`, `/build`, `/test`, `/review` should be available.

To add a personal skill on top of the fork: add `skills/<name>/SKILL.md` to
`LukaszStrz/agent-skills`, push, then re-run `claude plugin install agent-skills@addy-agent-skills`
(or wait for its background auto-update) to pick it up — no changes needed in `raft-mq` itself.
