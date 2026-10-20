# Dependencies and licensing

Time Tracker's source is MIT licensed; see the root LICENSE. Its runtime uses C#/.NET and WPF, which are open-source Microsoft projects distributed under their applicable MIT and third-party notices. The self-contained release retains the runtime's supplied license/notice files. Windows itself is proprietary and is a platform prerequisite, not bundled by this project.

Development/test dependencies are downloaded through NuGet:

| Dependency | Purpose | License |
| --- | --- | --- |
| Microsoft.NET.Test.Sdk | Test discovery/execution | MIT |
| xUnit.net 2 | Unit/UI test framework | Apache-2.0 |
| xunit.runner.visualstudio | Visual Studio and CLI test integration | Apache-2.0 |
| FlaUI.UIA3 / FlaUI.Core | Native Windows UI automation | MIT |

Their transitive dependencies retain their respective upstream licenses. Inspect `dotnet list TimeTracker.slnx package --include-transitive` and the package metadata when changing dependencies. Test packages are not shipped as part of the application.

No paid testing platform, Avalonia commercial tooling, browser automation service, cloud database, or runtime subscription is required. Visual Studio Community is an optional free personal-use IDE under Microsoft's proprietary terms, not an open-source dependency. GitHub hosting/Actions is optional and subject to GitHub's own terms and quotas; all build/test commands can run locally.
