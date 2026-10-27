# Contributing to raft-mq

Thank you for your interest in contributing to `raft-mq`!

## Before You Start

- **Bug reports and feature requests:** Please open an issue using the repository's [issue forms](https://github.com/LukaszStrz/raft-mq/issues/new/choose) before starting work on a non-trivial change so we can align on scope and design.
- **Security vulnerabilities:** Do **not** open a public issue. Follow [SECURITY.md](SECURITY.md) to submit a private vulnerability report.

## Development Setup

- **Line endings & formatting:** This repository enforces `LF` line endings across platforms via [`.gitattributes`](.gitattributes) and [`.editorconfig`](.editorconfig) (with `CRLF` reserved for `.sln`, `.cmd`, and `.bat`). Ensure your editor respects `.editorconfig`.
- **.NET SDK:** Once the solution skeleton lands, the required .NET SDK version will be pinned in `global.json`.

## Pull Request Workflow

1. **Branch from `main`:** Create a short-lived topic branch (`feat/<topic>`, `fix/<topic>`, `docs/<topic>`, `chore/<topic>`).
2. **Keep changes focused:** One logical change per pull request.
3. **Pull request titles:** The repository uses **squash merges** with the pull request title as the final commit subject and the pull request body as the commit message. Use [Conventional Commits](https://www.conventionalcommits.org/) format for PR titles:
   - `feat: ...`
   - `fix: ...`
   - `perf: ...`
   - `docs: ...`
   - `refactor: ...`
   - `test: ...`
   - `chore: ...`
4. **Quality gates:** All automated checks and review conversations must be resolved before merging.

## License

By contributing to `raft-mq`, you agree that your contributions will be licensed under the [MIT License](LICENSE).
