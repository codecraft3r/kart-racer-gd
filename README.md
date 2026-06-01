# Kart Racer GD

Quick onboarding for this Godot C# project.

## Requirements

- **Godot**: v4.6.3-mono (project is authored for Godot .NET 4.6.3+ workflow)
- **.NET SDK**: 9.0.303 (pinned in `global.json`)
- **Git LFS**: optional for older history or future large binary assets
- **CodeGraph cache**: optional local tool cache generated in `.codegraph/`

## Setup

No extra asset hydration step is required for the current checkout. The active
Kenney color map is tracked as a normal PNG so the project can open without
Git LFS installed.

## Build and run (CLI)

```bash
dotnet build kart_racer.sln
```

For a local game run, open `project.godot` in Godot and run the project from the editor.

## Build and run (Godot)

- Install Godot 4.6.3 (Mono).
- Open `project.godot`.
- Press **F5 / Run Project** (main scene: `default_3d.tscn`).
- On first build, restore .NET packages if prompted.

## Project entry points

- Main scene: `default_3d.tscn`
- C# entry assembly: `kart_racer.csproj`

## Notes for contributors

- Keep `.codegraph/` and Godot editor/cache folders in local-only state; they are ignored by git.
- If your tooling generates additional local artifacts (IDE folders, cache, etc.), avoid committing them.
