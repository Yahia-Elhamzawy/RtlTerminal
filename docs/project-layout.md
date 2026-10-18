# Repository layout

Open `RtlTerminal.sln` from the repository root in Visual Studio.

| Directory | Contents |
| --- | --- |
| `src/RtlTerminal/` | WPF project, application startup and assembly metadata |
| `src/RtlTerminal/Views/` | Windows and their matching XAML code-behind files |
| `src/RtlTerminal/Controls/` | Terminal view, scrollbar resources and menu styling |
| `src/RtlTerminal/Terminal/` | ConPTY sessions, buffer, RTL shaping and wrapping |
| `src/RtlTerminal/Themes/` | Theme definitions, palettes and tab colors |
| `src/RtlTerminal/Services/` | Settings, shell paths, WSL, updates and Windows integration |
| `src/RtlTerminal/Assets/` | Application icon |
| `tests/` | Buffer and WPF regression projects |
| `scripts/` | Version reader and local release build |
| `installer/` | Inno Setup installer definition |
| `docs/` | Website and developer documentation |
| `screenshots/` | Images used by the README and website |
| `artifacts/` | Ignored generated output and archived local builds |

From the repository root:

```powershell
dotnet build RtlTerminal.sln -c Release
dotnet run --project src/RtlTerminal/RtlTerminal.csproj
dotnet run --project tests/RtlTerminal.BufferTests -c Release
dotnet run --project tests/RtlTerminal.RenderTests -c Release
.\scripts\build-release.ps1
```

The release script resolves paths from its own location, so it can also be run
from another working directory. It writes the app to `artifacts/publish/win-x64`
and the installer to `artifacts/release`. Render tests write their output to
`artifacts/render-checks`. Normal SDK `bin` and `obj` folders stay inside each
project and are ignored by Git.

Existing root-level build output was moved intact into `artifacts/legacy/`.
The root `RtlTerminal.png` retains its public URL used by the README and website.
Local `.vs` and `.mimocode` folders retain their original locations because they
belong to development tools rather than application source.
