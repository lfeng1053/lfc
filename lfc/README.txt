lfc - quick view and quick reply for Facebook Messenger
=======================================================

START
  Double-click lfc.exe (keep the .dll files next to it).
  If Windows shows "Windows protected your PC": click "More info" -> "Run anyway".
  First run: a sign-in window opens. Sign in to Facebook; the window closes
  by itself and lfc keeps running in the tray (next to the clock).
  The sign-in window only comes back if Facebook signs you out.
  lfc has NO window: it only appears when a message arrives. When you start it,
  a notice "lfc is running" appears. On Windows 11 the tray icon may be hidden
  behind the ^ arrow next to the clock.

USE
  - A message arrives -> the sender's avatar, name and message appear.
  - Ctrl + Alt + R          open the reply box. Type, press Enter to send.
  - Ctrl + Alt + PageUp/Dn  several people waiting: look at each one, then
                            press Ctrl + Alt + R to reply to that person.
                            (Inside the reply box: just PageUp / PageDown.)
  - Esc                     dismiss this message.
  - The popups ignore the mouse (it goes through them): keyboard only.
  - Only a failed send shows a red error; your text is kept, press Enter to retry.

SETTINGS  (%APPDATA%\lfc\settings.json - edit with Notepad, then restart lfc)
  "ReplyHotkey", "PrevHotkey", "NextHotkey"   e.g. "Alt+Q"
  "HidePagerDots": true                       hide the small position dots
  "BubbleX", "BubbleY"                        position of the message box (pixels)
  "MyAvatarPath": "D:\\Mes\\lfc\\me.png"  your avatar in the reply box
                                              (empty = taken from Facebook)

TRAY MENU (right-click the tray icon; left-click does nothing)
  Start with Windows - Exit
  (plus "Sign in to Facebook" while you are signed out)

TROUBLESHOOTING
  Log file: %LOCALAPPDATA%\lfc\log.txt (never contains message text).

UNINSTALL
  Delete this folder, and also %APPDATA%\lfc and %LOCALAPPDATA%\lfc
  to remove settings and the Facebook login.
