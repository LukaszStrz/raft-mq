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

## Antigravity IDE / CLI setup

[`LukaszStrz/agent-skills`](https://github.com/LukaszStrz/agent-skills) also includes a native
Antigravity plugin manifest (`plugin.json`) that works across both **Antigravity IDE** and the
**Antigravity CLI (`agy`)** via their shared global plugin directory (`~/.gemini/config/plugins/`):

1. Install the plugin once per machine using the Antigravity CLI:
   ```bash
   agy plugin install https://github.com/LukaszStrz/agent-skills.git
   ```
2. Restart or reload the Antigravity IDE window so it discovers the newly installed plugin
   directory (`~/.gemini/config/plugins/agent-skills/`).
3. Verify: `agy plugin list` should show `agent-skills (enabled, 0.6.13)`, and the plugin's 25
   skills and 4 specialist subagent personas will appear in the IDE's Customizations panel.
   * **Note:** The short `/spec`, `/build`, `/test`, `/review` wrappers are Claude Code-specific
     commands. In Antigravity IDE & CLI, skills activate automatically based on task intent, or
     can be invoked directly by their namespaced skill name (e.g.
     `/agent-skills:spec-driven-development`, `/agent-skills:planning-and-task-breakdown`,
     `/agent-skills:incremental-implementation`, `/agent-skills:test-driven-development`,
     `/agent-skills:code-review-and-quality`). See
     [`docs/antigravity-setup.md`](https://github.com/LukaszStrz/agent-skills/blob/main/docs/antigravity-setup.md)
     for the full mapping table.

To pick up new or updated skills from `LukaszStrz/agent-skills`: push to the fork and re-run
`agy plugin install https://github.com/LukaszStrz/agent-skills.git`.
