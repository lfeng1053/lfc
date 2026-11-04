lfc - quick view and quick reply for Facebook Messenger
=======================================================

START
  Double-click lfc.exe (keep the .dll files next to it).
  If Windows shows "Windows protected your PC": click "More info" -> "Run anyway".
  First run: a sign-in window opens. Sign in to Facebook; the window closes
  by itself and lfc keeps running in the tray (next to the clock).
  The sign-in window only comes back if you are signed out
  (Settings -> Facebook account -> Sign in / Log out).
  lfc has NO window: it only appears when a message arrives. When you start it,
  a notice "lfc is running" appears. On Windows 11 the tray icon may be hidden
  behind the ^ arrow next to the clock.

USE
  - A message arrives -> the sender's avatar, name and message appear
    for 5 seconds.
  - Ctrl + Alt + R          ON / OFF switch for the reply panel.
                            ON starts at the newest person waiting (or the
                            person you talked to last).
  - Ctrl + Alt + PageUp/Dn  switch person: everyone waiting + people you
                            talked to recently (inside the box: PageUp/Dn).
  - Enter                   send; the panel stays on that person so you can
                            send more or switch to someone else.
  - Esc                     close the panel (unsent text is kept).
  - The popups ignore the mouse (it goes through them): keyboard only.
  - Only a failed send shows a red error; your text is kept, press Enter to retry.

SETTINGS  (right-click the tray icon -> Settings...)
  Opacity of the popups, hotkeys, start with Windows,
  Facebook account: Sign in / Log out.
  More options in %APPDATA%\lfc\settings.json (edit with Notepad, then restart lfc):
  "HidePagerDots": true                       hide the small position dots
  "BubbleX", "BubbleY"                        position of the message box (pixels)
  "MyAvatarPath": "D:\\Mes\\lfc\\me.png"  your avatar in the reply box
                                              (empty = taken from Facebook)

TRAY MENU (right-click the tray icon; left-click does nothing)
  Settings... - Exit
  (plus "Sign in to Facebook" while you are signed out)

TROUBLESHOOTING
  Log file: %LOCALAPPDATA%\lfc\log.txt (never contains message text).

UNINSTALL
  Delete this folder, and also %APPDATA%\lfc and %LOCALAPPDATA%\lfc
  to remove settings and the Facebook login.
