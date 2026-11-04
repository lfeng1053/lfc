# lfc installer - downloads the latest lfc from GitHub and installs it for the current user.
#
#   Install / update:  irm https://raw.githubusercontent.com/lfeng1053/lfc/main/install.ps1 | iex
#   Uninstall:         & ([scriptblock]::Create((irm https://raw.githubusercontent.com/lfeng1053/lfc/main/install.ps1))) -Uninstall
#
# What it does:
#   1. Closes lfc if it is running.
#   2. Downloads the repository (main branch) and copies the app files from its "lfc" folder to
#      %LOCALAPPDATA%\Programs\lfc  (no admin rights needed).
#   3. Checks that Microsoft Edge WebView2 Runtime is installed (needed by lfc).
#   4. Creates a Start Menu shortcut, then starts lfc.
# Settings and the Facebook login (%APPDATA%\lfc, %LOCALAPPDATA%\lfc) are kept on update.

param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$Repo       = 'lfeng1053/lfc'
$Branch     = 'main'
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\lfc'
$Shortcut   = Join-Path ([Environment]::GetFolderPath('Programs')) 'lfc.lnk'
$AppFiles   = @('lfc.exe', 'lfc.exe.config', 'Microsoft.Web.WebView2.Core.dll',
                'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll', 'README.txt')

function Stop-Lfc {
    $p = Get-Process -Name 'lfc' -ErrorAction SilentlyContinue
    if ($p) {
        Write-Host 'Closing lfc...'
        $p | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
}

# ---------------- Uninstall ----------------
if ($Uninstall) {
    Stop-Lfc
    if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
    if (Test-Path $Shortcut)   { Remove-Item $Shortcut -Force }
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'lfc' -ErrorAction SilentlyContinue
    Write-Host 'lfc has been removed.'
    Write-Host "Settings and the Facebook login are still in $env:APPDATA\lfc and $env:LOCALAPPDATA\lfc - delete them too if you want."
    return
}

# ---------------- Install / update ----------------
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$tmp = Join-Path $env:TEMP ('lfc-install-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    Write-Host "Downloading lfc from github.com/$Repo ..."
    $zip = Join-Path $tmp 'lfc.zip'
    Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/$Repo/archive/refs/heads/$Branch.zip" -OutFile $zip

    Write-Host 'Extracting...'
    Expand-Archive -Path $zip -DestinationPath $tmp -Force
    $src = Get-ChildItem -Path $tmp -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'lfc\lfc.exe') } |
           Select-Object -First 1
    if (-not $src) { throw "lfc\lfc.exe was not found in the downloaded repository." }
    $src = Join-Path $src.FullName 'lfc'

    Stop-Lfc
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    foreach ($f in $AppFiles) {
        $from = Join-Path $src $f
        if (Test-Path $from) { Copy-Item $from -Destination $InstallDir -Force }
    }
    Get-ChildItem $InstallDir | Unblock-File   # downloaded files: remove the "from the internet" mark
    Write-Host "Installed to $InstallDir"
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# WebView2 Runtime (built into Windows 11 and most Windows 10 PCs)
$wv2 = @(
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
    'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
    'HKCU:\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
) | Where-Object { Test-Path $_ }
if (-not $wv2) {
    Write-Host 'Microsoft Edge WebView2 Runtime is missing; opening its download page...'
    Start-Process 'https://go.microsoft.com/fwlink/p/?LinkId=2124703'
}

# Start Menu shortcut
$exe = Join-Path $InstallDir 'lfc.exe'
$wsh = New-Object -ComObject WScript.Shell
$lnk = $wsh.CreateShortcut($Shortcut)
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $InstallDir
$lnk.Description = 'lfc - quick view and quick reply for Messenger'
$lnk.Save()

# If "Start with Windows" was on for an older location, point it to the new one
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty -Path $run -Name 'lfc' -ErrorAction SilentlyContinue) {
    Set-ItemProperty -Path $run -Name 'lfc' -Value ('"' + $exe + '" --autostart')
}

Write-Host 'Starting lfc...'
Start-Process $exe
Write-Host 'Done. lfc runs in the tray (next to the clock). Right-click its icon for Settings.'
