# Chattyn

A small always-on-top overlay that helps you write World of Warcraft roleplay posts in natural English.

Type your post, press **Ctrl+Enter**, and Chattyn (using Claude) shows:

- **Natural**: how a native RP writer would phrase it, in your character's voice
- **Corrected**: only the spelling, grammar and punctuation fixed
- **Notes**: the most important mistakes, explained in your language
- **Translations**: what your text and the natural version say in your own language (Czech by default)

You can also write in your own language:

- **One word you don't know**: leave it in your language inside the English sentence. Chattyn fills in the English word in the right form and lists alternatives with their differences.
- **A whole post, or a single word**: you get a natural English version, a more literal translation, and notes on the key vocabulary.

**Characters (optional).** Describe who you play in Settings: name, gender, race and class, how they speak, and free notes. The natural version is then written in that character's voice: a formal elf gets "We must speak of the war", a gruff dwarf gets "Aye, we need to talk about the war, lad". The corrected version never restyles your text. You can keep several characters and switch between them in the writing panel.

Because Chattyn knows your language, it watches for the mistakes its speakers typically make in English (false friends, articles, word order, prepositions) and explains them against your language.

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

2. Run Chattyn. On first start the Settings window opens. **Your language** and **Explain mistakes in** are both set to Czech; change them if you want another language (several languages: separate them with commas).

Chattyn looks for `.env` next to `Chattyn.exe` and in its parent folders, so the file in the repository root works for development builds.

## Using it

| Key | Action |
|---|---|
| `Ctrl+Alt+Space` (global) | Open the writing panel / go back to the game |
| `Ctrl+Enter` | Check the post |
| `Enter` | Copy the chosen version and return to WoW. In the Natural or Corrected box, copies that box (you can edit it first). |
| `Alt+1` / `Alt+2` / `Alt+3` | Choose Natural / Corrected (or Literal translation) / Mine |
| `Alt+C` | Switch to the next character (or pick one in the list next to "Send") |
| `Esc` | Back to the game without copying |
| `Ctrl+Alt+PageDown` (global) | Copy the next part of a long post. Active only while parts are waiting. |

Tips:

- A chat command at the start (`/e`, `/s`, `/y`, `/p`, `/w Name`, `/1`) is kept, and repeated on every part of a long post.
- When your post was not in English, "Mine" is switched to "Natural" for that post, so you don't paste the wrong language into chat.
- On keyboards where AltGr types characters (Czech, German, ...), AltGr counts as Ctrl+Alt. Avoid `Ctrl+Alt+letter` hotkeys, or that AltGr character stops working while the hotkey is active.
- "How they speak" has the biggest effect on tone. The character's gender decides she / he in emotes, also when you write in your own language.
- After you switch characters, a result already on screen is marked as written for another character; `Ctrl+Enter` checks again.
- Character names are never marked as typos. Add other lore words in **Settings**.
- The tray icon has Show, Settings and Quit. The ✕ button only hides the overlay.

Settings are stored in `%APPDATA%\Chattyn\settings.json` (no secrets there).

## Build from source

```powershell
dotnet build
dotnet test
dotnet run --project src/Chattyn
```

`dotnet test` skips the tests that call the real Claude API. To run them too (five short requests, a fraction of a cent, key taken from `.env`):

```powershell
$env:CHATTYN_LIVE_TEST = '1'; dotnet test
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
- **"No API key"** or **key rejected**: check that `.env` contains `ANTHROPIC_API_KEY=...`. The file is read again on every check, so a corrected key works right away.
- **No red squiggles**: add English under Windows Settings > Time & language > Language & region.
- **Everything is underlined red when I write in my language**: the spell check is English only. The check itself still works.
- Errors are logged to `%APPDATA%\Chattyn\error.log`.

## License

GPL-3.0, see [LICENSE](LICENSE).
