# QuietShelf working agreement

## Current product mode

- QuietShelf is currently a personal, local-first Windows application under active iteration.
- Optimize the local development and update loop before adding public distribution infrastructure.
- Do not push, open or merge pull requests, create tags, or publish GitHub releases unless the user explicitly asks in the current conversation.
- Keep accounts, cloud sync, telemetry, social features, recommendations, and online dependencies outside the product unless explicitly requested.

## Workspace and installed application

- Treat the repository root as the source workspace.
- Resolve the installed application directory from the current environment or user instruction.
- Keep machine-specific absolute paths out of tracked files; use repository-relative paths, environment variables, or local-only notes.
- Treat source, installed program files, and user data as three separate concerns.
- Before replacing installed program files, confirm that `QuietShelf.App` is not running.

## User data

- The default data root is `%LOCALAPPDATA%\QuietShelf`.
- The SQLite library is `%LOCALAPPDATA%\QuietShelf\records.db`; covers live below the same data root.
- Builds, installer runs, and local updates must preserve that directory.
- Never move the live database or covers into the repository, publish output, or installation directory.
- Back up data before any schema migration or destructive data operation, and verify the migration against existing local data.

## Local build and deployment

- Use a framework-dependent, multi-file `win-x64` publish for the current local installation.
- Keep `PublishSingleFile=false` and `SelfContained=false` in the local installer build. This machine already has the required .NET 10 Desktop Runtime.
- The Inno Setup `[Files]` entry must include the complete publish directory, including subdirectories.
- Build with `scripts\build-installer.ps1`; publish output belongs in `artifacts\win-x64`.
- For a local update, copy the complete publish output to the configured installation directory, not only `QuietShelf.App.exe`.
- Do not judge completion from a successful compile alone. Launch the exact installed executable and verify that it stays running with Windows Smart App Control enabled.
- After launch verification, check the Code Integrity operational log for a new QuietShelf block when startup behavior is ambiguous.
- Public distribution is a separate milestone. Revisit trusted code signing, packaging, runtime inclusion, and update delivery before the next public release.

## Verification baseline

- Run `dotnet test .\QuietShelf.slnx -c Release --filter "Category!=Manual&Category!=LocalData"` for focused automated coverage.
- Verify the generated publish directory contains `QuietShelf.App.exe`, `QuietShelf.App.dll`, its dependency manifest, runtime configuration, and required application dependencies.
- Verify the installed application opens the current dashboard and reads the existing local library.
- Report only checks that actually ran, including exact failures or environmental blocks.

## Editing discipline

- Preserve unrelated local changes and inspect `git status` before and after edits.
- Prefer narrow changes tied to the requested behavior.
- Keep the application centered on works, reading/viewing sessions, progress notes, completion records, covers, and local reflection.
