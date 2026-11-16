# Recording the demo GIF

The README has room for a short demo at `assets/demo.gif`. This page explains how to record it on Windows.

## Target

| | |
|---|---|
| Length | **15–25 seconds** |
| Size | **at most ~5 MB** |
| Width | 1000–1280 px (the README shows it at 800 px) |
| Frame rate | 10–15 fps is enough for a desktop app |
| Data | **only the synthetic files in [samples/](../samples/)**: never a real configuration |

## Before recording

1. Use a clean Windows account or close everything that shows personal data (taskbar notifications, other windows,
   file paths with your name). Record the Shiftwall window only, not the whole screen.
2. Copy the files from `samples/` to a short folder such as `C:\demo` so the paths in the dialogs show no user name.
3. Start Shiftwall, choose the language (English for the main README) and the light theme, and maximize or size the
   window to about 1100 × 720.

## What to show (storyboard)

| Seconds | Action |
|---|---|
| 0–4 | Migration page: drag `branch-60f.conf` to the left and `factory-default-70g.conf` to the right; select **FG-70G**. |
| 4–7 | **Next** → pre-checks list. |
| 7–12 | **Next** → mapping: **Load template…** → `branch-60f-to-70g.swmap.json`; the rows fill in. |
| 12–18 | **Next** → transform: show the change list and scroll the before/after diff a little. |
| 18–22 | **Next** → report: show the checklist. Do not save files into personal folders. |
| optional 22–25 | **Security audit** page with `hq-60f.conf` loaded: score and findings. |

Move the mouse slowly, pause about one second on each step, and do not type anything private.

## Tools

**ScreenToGif** (free, open source, Windows): records a region and exports GIF directly. Official site:
<https://www.screentogif.com>, source: <https://github.com/NickeManarin/ScreenToGif>.

1. *Recorder* → place the frame over the Shiftwall window → set 12–15 fps → record → stop.
2. In the editor, delete dead frames at the start and end, and reduce the frame count if the file is too large.
3. *Save as* → GIF. If the file is over ~5 MB: lower the fps, crop the window or shorten the clip.

Alternative: record an MP4 with the Snipping Tool or the Xbox Game Bar (Win + Alt + R) and convert it with
[FFmpeg](https://ffmpeg.org/):

```bash
ffmpeg -i demo.mp4 -vf "fps=12,scale=1100:-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse" -loop 0 demo.gif
```

## Publishing it

1. Save the file as `assets/demo.gif` and check its size.
2. In `README.md` and `README.it.md`, replace the screenshot under the badges with the `<img src="assets/demo.gif" …>`
   line that is already written in the HTML comment just above it.
3. Commit with `docs: add demo GIF`.
