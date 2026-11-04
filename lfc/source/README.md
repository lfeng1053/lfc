# lfc

Quick view and quick reply for Facebook Messenger on Windows.
One flow only: **a message arrives → avatar + name + text appear → press a hotkey → type → Enter sends.**
No news feed, no badges, no "unread" or "sending" labels. Only a failed send shows an error.

## Build
1. Open `lfc.csproj` in Visual Studio 2022 (workload **.NET desktop development**).
2. Visual Studio restores the NuGet package `Microsoft.Web.WebView2`.
3. Press **F5**.

Targets **.NET Framework 4.8**, which ships with Windows 10/11.

## Keys
| Key | Action |
|---|---|
| `Ctrl+Alt+R` | Open the reply box for the person shown (or the newest sender) |
| `Enter` | Send |
| `Ctrl+Alt+PageUp` / `Ctrl+Alt+PageDown` | Several people waiting: look at each one **before** replying, then pick with `Ctrl+Alt+R` |
| `PageUp` / `PageDown` | Same, while the reply box is open |
| `Esc` | Dismiss this message |

The popups (message box and reply box) are **keyboard-only**: the mouse goes straight through them to the
windows underneath. The box sits at `BubbleX` / `BubbleY` from settings (default: right edge, 1/3 down).
The tray icon only reacts to a right-click (settings menu); left-click does nothing.

When two or more people are waiting, small dots under the box show which one you are looking at.
Hide them with `"HidePagerDots": true` in `settings.json`.

## Sign-in
There is no Messenger window. The hidden Messenger page shows itself **only as a sign-in window,
automatically, when you are signed out of Facebook** (first run, or if Facebook signs you out), and
closes itself as soon as you are signed in. While signed out, the tray menu also has "Sign in to Facebook".

Tray menu: Start with Windows · Exit (plus "Sign in to Facebook" while signed out).

## Settings
`%APPDATA%\lfc\settings.json` (restart the app after editing):

| Entry | Meaning |
|---|---|
| `ReplyHotkey`, `PrevHotkey`, `NextHotkey` | Hotkeys, e.g. `"Alt+Q"`, `"Ctrl+Shift+Space"` |
| `HidePagerDots` | `true` hides the position dots |
| `BubbleX`, `BubbleY` | Screen position (pixels) of the message box's top-left corner |
| `MyAvatarPath` | Your avatar in the reply box, e.g. `"D:\\Mes\\lfc\\me.png"`. Empty = taken from your Facebook account |

## Code
| File | Purpose |
|---|---|
| `Program.cs` | Entry point, single instance, `AppInfo` (name, data folders, migration from the old "BongBongChat" folders). |
| `BubbleApp.cs` | Runs the app: incoming message → show; hotkey → reply box; switching people; sending; errors. The queue of waiting people lives here. |
| `HeadForm.cs` | Message box (design Asset 2, 3): grey `#D3D2CD` box, 16px corners, 298px wide, 38px avatar inside the top corner on the screen-edge side, bold name + text. Layout values at the top of the file. |
| `ReplyBox.cs` | Reply box (design Asset 1): same box style, your avatar opposite the sender's. Made of `ReplyBackground` (box + avatar) and the text field placed inside it. |
| `Person.cs` | One person waiting: avatar, latest message, unsent draft, send error. |
| `ChatForm.cs` | Hidden Messenger page in WebView2: reads incoming messages (chat list + notifications), sends replies, blocks the news feed, detects the sign-in state (`SignedInChanged`). Shown only as the sign-in window while signed out. |
| `HotkeyWindow.cs` | Global hotkeys. |
| `Settings.cs`, `Log.cs` | Settings and log (`%LOCALAPPDATA%\lfc\log.txt`, never contains message text). |
| `LayeredForm.cs`, `Gfx.cs`, `NativeMethods.cs` | Window and drawing helpers. |
| `icon.png` (= design Asset 4), `icon.ico` | App icon and default avatar. `icon.ico` is generated from `icon.png`. |

## How messages are received and sent
- **Receive:** a script injected into the page (`HookScript`) reads the conversation list. A row counts as "just messaged" only when it is **unread** (bold), within the **first 6 rows** (`TOP_ROWS`), the text is a **real message** (not typing, a reaction, your own message or a status), **not seen recently** for that row, and **stable for 0.5 s** (`CONFIRM_MS`). The conversation open in the hidden page is exempt from the unread check. Page notifications are a second source; both are merged by name and duplicates within 15 s are dropped.
- **Send:** opens the conversation in the hidden page, **checks the recipient's name is on the page** (otherwise nothing is sent), inserts the text and presses Enter through the DevTools Protocol, then waits for the composer to be empty to confirm.

The patterns in `HookScript` contain Vietnamese words on purpose: they match Facebook's Vietnamese UI (e.g. "đang hoạt động" = active now). Keep them, and add English equivalents if you switch Facebook's language.

## Limits
- Only messages that arrive while the app is running are shown, and only the newest one per person.
- Photos and stickers appear as text (e.g. "sent a photo").
- The app reads Facebook's page layout; if Facebook changes it, `HookScript` or `ComposerJs` in `ChatForm.cs` may need updating.
