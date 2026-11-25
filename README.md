# Chattyn

A small always-on-top overlay that helps you write World of Warcraft roleplay posts in natural English.

Type your post, press **Ctrl+Enter**, and Chattyn (using Claude) shows:

- **Natural**: how a native RP writer would phrase it, in your character's voice
- **Corrected**: only the spelling, grammar and punctuation fixed
- **Notes**: the most important mistakes, explained in your language
- **Translations**: what your text and the natural version say in the languages you pick

Press **Enter** to copy the version you chose, and Chattyn hands focus back to WoW. Paste it with **Ctrl+V**. Long posts are split into 255-byte chat messages.

It is a separate Windows app, not a WoW addon: addons cannot reach the internet.

## Requirements

- Windows 10 or 11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (the SDK, if you build it yourself)
- An Anthropic API key with some prepaid credit, from the [Claude Console](https://platform.claude.com/settings/keys). API usage is billed separately from a Claude.ai subscription. With the default model (`claude-haiku-5-5`), 100 checked posts cost roughly a few cents.
- WoW in **Windowed** or **Windowed (Fullscreen)** mode. Exclusive fullscreen hides overlays.

## Setup

1. Copy `.env.example` to `.env` and paste your key after the `=`:

   ```
   ANTHROPIC_API_KEY=sk-ant-...
   ```

   You can also start Chattyn, open **Settings > Create / open .env**, paste the key, save the file and click **Reload key**.

   `.env` is git-ignored. The key is stored as plain text, so use a dedicated key with a low monthly spend limit and never share the folder with `.env` inside. The `ANTHROPIC_API_KEY` environment variable, if set, takes precedence.

2. Run Chattyn. On first start the Settings window opens: choose the languages to translate into and the language for explanations.

Chattyn looks for `.env` next to `Chattyn.exe` and in its parent folders, so the file in the repository root works for development builds.

## Using it

| Key | Action |
|---|---|
| `Ctrl+Alt+Space` (global) | Open the writing panel / go back to the game |
| `Ctrl+Enter` | Check the post |
| `Enter` | Copy the chosen version and return to WoW. In the Natural or Corrected box, copies that box (you can edit it first). |
| `Alt+1` / `Alt+2` / `Alt+3` | Choose Natural / Corrected / Mine |
| `Esc` | Back to the game without copying |
| `Ctrl+Alt+N` (global) | Copy the next part of a long post |

Tips:

- A chat command at the start (`/e`, `/s`, `/y`, `/p`, `/w Name`, `/1`) is kept, and repeated on every part of a long post.
- If you don't know an English word, write it in your own language. Claude fills in the English and tells you in Notes.
- Add your character's name and lore words in **Settings**, so they are kept as written and not marked as typos.
- The tray icon has Show, Settings and Quit. The ✕ button only hides the overlay.

Settings are stored in `%APPDATA%\Chattyn\settings.json` (no secrets there).

## Build from source

```powershell
dotnet build
dotnet test
dotnet run --project src/Chattyn
```

Publish a single-file exe (needs the .NET 10 Desktop Runtime on the target PC):

```powershell
dotnet publish src/Chattyn -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Then put your `.env` next to `publish\Chattyn.exe`.

## Project layout

```
src/Chattyn/              WPF overlay (window, hotkeys, tray, settings UI)
src/Chattyn.Core/         Logic without UI: Claude client, prompt, chat splitting, .env, settings
tests/Chattyn.Core.Tests/ xUnit tests for Chattyn.Core
```

## Privacy

The text you check is sent to the Anthropic API. Nothing else leaves your PC.

## Troubleshooting

- **Hotkey does nothing in game**: if WoW runs as administrator, run Chattyn as administrator too. If another app already uses the hotkey, pick another one in Settings.
- **"No API key"**: check that `.env` contains `ANTHROPIC_API_KEY=...` and click **Reload key** in Settings.
- **No red squiggles**: add English under Windows Settings > Time & language > Language & region.
- Errors are logged to `%APPDATA%\Chattyn\error.log`.

## License

GPL-3.0, see [LICENSE](LICENSE).
