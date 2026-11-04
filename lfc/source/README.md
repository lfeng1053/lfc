# lfc

Quick view and quick reply for Facebook Messenger on Windows.
**A message arrives → avatar + name + text appear for 5 s. `Ctrl+Alt+R` switches the reply panel on/off;
while it is on, switch between people and reply with Enter.**
No news feed, no badges, no "unread" or "sending" labels. Only a failed send shows an error.

## Build
1. Open `lfc.csproj` in Visual Studio 2022 (workload **.NET desktop development**).
2. Visual Studio restores the NuGet package `Microsoft.Web.WebView2`.
3. Press **F5**.

Targets **.NET Framework 4.8**, which ships with Windows 10/11.

## Keys
| Key | Action |
|---|---|
| `Ctrl+Alt+R` | **Switch the reply panel ON / OFF.** ON starts at the newest person waiting (or the person you talked to last) |
| `Ctrl+Alt+PageUp` / `Ctrl+Alt+PageDown` | Previous / next person: everyone waiting **and** the people you talked to recently. When the panel is off, the first press turns it on |
| `PageUp` / `PageDown` | Same, inside the reply box |
| `Enter` | Send. The panel stays on the same person: send more, or switch to someone else |
| `Esc` | Close the panel (same as `Ctrl+Alt+R`). Clicking another window also closes it. Unsent text is kept |

While the panel is on, new messages never make it jump away from the person you are replying to; the new
sender is added to the list (the dots update). While it is off, a new message shows as a 5-second notification.

When two or more people are waiting, small dots under the box show which one you are looking at.
Hide them with `"HidePagerDots": true` in `settings.json`.

## Sign-in
There is no Messenger window. The hidden Messenger page shows itself **only as a sign-in window,
automatically, when you are signed out of Facebook** (first run, or if Facebook signs you out), and
closes itself as soon as you are signed in. Settings → Facebook account has **Sign in** / **Log out**.

Tray menu (right-click): Settings... · Exit. The message box hides after **5 s** if you don't reply
(it stays in the queue: press the reply hotkey to bring it back).

## Settings
Tray icon → right-click → **Settings...**:
- **Opacity** of the popups (30–100 %), with a preview; applied live while dragging.
- **Hotkeys**: click a box and press the new combination (needs Ctrl, Alt, Shift or Win). "Default" restores it.
  Global hotkeys are paused while the window is open so they can be recorded.
- **Start with Windows**.
- **Facebook account**: shows whether you are signed in, with **Sign in** or **Log out** (asks first).

Everything is stored in `%APPDATA%\lfc\settings.json` (other entries can be edited there; restart the app after editing):

| Entry | Meaning |
|---|---|
| `ReplyHotkey`, `PrevHotkey`, `NextHotkey` | Hotkeys, e.g. `"Alt+Q"`, `"Ctrl+Shift+Space"` |
| `Opacity` | Popup opacity in percent (30–100) |
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
| `HotkeyWindow.cs` | Global hotkeys (register, clear, record from a key press). |
| `SettingsForm.cs` | Settings window: opacity, hotkeys, start with Windows, sign in / log out. |
| `Settings.cs`, `Log.cs` | Settings and log (`%LOCALAPPDATA%\lfc\log.txt`, never contains message text). |
| `LayeredForm.cs`, `Gfx.cs`, `NativeMethods.cs` | Window and drawing helpers. |
| `icon.png` (= design Asset 4), `icon.ico` | App icon and default avatar. `icon.ico` is generated from `icon.png`. |

## How messages are received and sent
- **Receive:** a script injected into the page (`HookScript`) reads the conversation list. For the first **8 s** after the list appears (`SETTLE_MS`) everything is only remembered, because Facebook loads the list in batches: messages that were unread before the app started are never shown. After that, a conversation not seen before only counts when it appears in the **top 2 rows**. A row counts as "just messaged" only when it is **unread** (bold), within the **first 6 rows** (`TOP_ROWS`), the text is a **real message** (not typing, a reaction, your own message or a status), **not seen recently** for that row, and **stable for 0.5 s** (`CONFIRM_MS`). The conversation open in the hidden page is exempt from the unread check. Page notifications are a second source; both are merged by name and duplicates within 15 s are dropped.
- **Send:** opens the conversation in the hidden page, **checks the recipient's name is on the page** (otherwise nothing is sent), inserts the text and presses Enter through the DevTools Protocol, then waits for the composer to be empty to confirm.

The patterns in `HookScript` contain Vietnamese words on purpose: they match Facebook's Vietnamese UI (e.g. "đang hoạt động" = active now). Keep them, and add English equivalents if you switch Facebook's language.

## Light mode (lower memory)
The hidden Messenger page is kept as small as possible:
- **No images, video, audio or web fonts** are loaded while you are signed in and the page is hidden
  (`WebResourceRequested` returns 404). The app only needs text and avatar links; avatars are downloaded
  separately. The sign-in window loads everything (captchas, pictures).
- **Browser switches** (`LightBrowserArgs`): no GPU, one renderer process, no extensions, no background
  updates or Edge extras.
- **Memory target Low** (`MemoryUsageTargetLevel`) while hidden, so WebView2 frees caches.

Compare in Task Manager: `lfc.exe` + its `msedgewebview2.exe` processes.

## Limits
- Only messages that arrive while the app is running are shown, and only the newest one per person.
- Photos and stickers appear as text (e.g. "sent a photo").
- The app reads Facebook's page layout; if Facebook changes it, `HookScript` or `ComposerJs` in `ChatForm.cs` may need updating.
