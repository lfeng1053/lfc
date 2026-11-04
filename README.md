# lfc

Quick view and quick reply for Facebook Messenger on Windows 10/11.
A message arrives, its avatar, name and text appear; `Ctrl+Alt+R` opens the reply panel.

## Install / update
Open **PowerShell** and run:

```powershell
irm https://raw.githubusercontent.com/lfeng1053/lfc/main/install.ps1 | iex
```

or download [`install.cmd`](install.cmd) and double-click it.

The app is installed to `%LOCALAPPDATA%\Programs\lfc` (no admin rights), a Start Menu shortcut is created and lfc starts.
Running it again updates to the latest version; settings and the Facebook login are kept.

## Uninstall
```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/lfeng1053/lfc/main/install.ps1))) -Uninstall
```

## More
- How to use: [`lfc/README.txt`](lfc/README.txt)
- Source code and build: [`lfc/source`](lfc/source)
