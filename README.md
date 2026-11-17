# P4V-HistroyPlugin

Perforce line-history plugins for code editors. Both implementations resolve the
P4 server and workspace from the file being edited; no repository is hard-coded.

- [`VisualStudio/`](VisualStudio/README.md): Visual Studio 2022 and Visual Studio 2026.
- [`VSCode/`](VSCode/README.md): Visual Studio Code.

Installable packages are committed under each editor's `Installer/` directory.

Both extensions share the user-level workspace cache at
`%LOCALAPPDATA%\P4HistroyPlugin\workspaces.json`. A successfully resolved
workspace is captured automatically; users can later override or clear it from
either editor without storing a P4 password.
