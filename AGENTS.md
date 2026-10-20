# Development standards

Time Tracker is a personal, offline Windows utility. Keep runtime dependencies free to use; prefer MIT/Apache/BSD licensed packages. No accounts, telemetry, remote fonts, web services, or subscription requirements.

Use C#/.NET 10 and WPF. Keep timer rules and persistence in TimeTracker.Core; keep Windows APIs and presentation in TimeTracker.Desktop. Use descriptive names, small focused files, shared XAML styles/templates, and C# XML documentation on types and methods. Comments should explain intent and edge cases. Avoid speculative abstractions and duplicated behavior.

Every behavior change needs relevant tests. Bug fixes need regression tests. Run core tests and the desktop cross-build before handing off; run FlaUI tests on Windows with an unlocked interactive desktop. Never claim Windows validation from a Linux build. See README.md and docs/testing.md for exact commands.

Use the existing checkout; cloud tasks are already isolated. Do not create worktrees unless requested. Preserve user changes. Do not commit runtime data, build outputs, or secrets.
