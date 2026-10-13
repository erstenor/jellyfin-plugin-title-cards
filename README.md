# Title Cards for Jellyfin

![Title Cards](src/Jellyfin.Plugin.TitleCards/logo.png)

A Jellyfin plugin that turns every home-screen library image into a collage of that library's
own artwork, with the library name drawn exactly the way Jellyfin draws it. Every library gets
one, including Live TV (for which Jellyfin doesn't automatically generate an image), recordings, grouped
views and libraries added later. Images containing logos and lettering in the artwork that would obscure the library title are excluded.

Each user chooses whether they see title cards or Jellyfin's own library images, and whether
album and book covers are cropped to fill their tiles; the choices apply in all their apps,
including TV apps (tested with Android TV / Fire TV). Jellyfin's own images are left untouched and
keep refreshing as usual.

**Status:** 0.3, an early release for Jellyfin 12.2. It runs on one server so far, so
[feedback and issues](https://github.com/erstenor/jellyfin-plugin-title-cards/issues) are very
welcome.

## Screenshots

Cards for Live TV, shows, movies, collections, recordings (DVR) and a grouped "TV Shows" view:

![Title cards for Live TV, Shows, Movies, Collections, DVR and a grouped TV Shows view](docs/images/gallery.jpg)

What Jellyfin shows by default (top) and Title Cards (bottom). Jellyfin generates a single-backdrop
image for most libraries and none for Live TV, which gets a plain colored card with an icon. The
plugin's title uses the same font, size and position as Jellyfin's; logos in guide art stay in the
outer corners, clear of it:

![Before and after: Jellyfin's default library images and Title Cards collages](docs/images/before-after.jpg)

Every library type: Live TV, TV shows, movies, collections, audiobooks, books, music, music
videos, photos and playlists. The Playlists card is built from what's in the playlists (here
albums and music videos) rather than Jellyfin's playlist mosaics.
With **Crop covers to fill their tiles** off (the default), covers whose shape is far from their
tile's are shown whole on a blurred copy of themselves, as on the highlighted Books card:

![Title cards for every library type, with the crop setting off](docs/images/libraries-crop-off.jpg)

With the setting on, covers are cropped to fill their tiles edge to edge, as on the highlighted
Books card. Each user picks one or the other; the plugin draws both versions of a card from the
same covers (these two screenshots were taken on different days, so their artwork differs):

![Title cards for every library type, with the crop setting on](docs/images/libraries-crop-on.jpg)

The number of tiles varies with the artwork. Book and audiobook covers usually print their title
and author at the top or bottom, right where the library name crosses a quarter tile, so the text
filter often rules them out of the four-tile layout and the plugin falls back to fewer tiles. In
the two-tile layout the library name crosses the middle of each cover instead, which is usually
artwork. A tile whose
shape is close to its cover's (within 1.25×, such as a square cover in a two-tile half) is always
cropped slightly, whatever the setting.

The Live TV, TV Shows, Movies and Collections cards come from the maintainer's server. The other
six come from a test server holding only public-domain, CC0 and CC BY sample media (see
[Image credits](#image-credits)); more testing on servers with large libraries of those types is
welcome.

## Install

Requires **Jellyfin 12.2**.

1. In Jellyfin, go to **Dashboard > Plugins > Repositories** (on some versions **Catalog >
   Settings**) and add a repository:
   - Name: `Title Cards`
   - URL: `https://raw.githubusercontent.com/erstenor/jellyfin-plugin-title-cards/main/manifest.json`
2. Open the **Catalog**, find **Title Cards** and install it.
3. Restart Jellyfin. On startup the plugin builds a card for every library; the log shows
   `Title Cards: ... card saved` for each, and `Title Cards: text detection ready` where the text
   filter is available.

**Manual install:** download `title-cards_<version>.zip` from
[Releases](https://github.com/erstenor/jellyfin-plugin-title-cards/releases), unzip it into a
folder named `Title Cards_<version>` in Jellyfin's `plugins` folder (Docker: `/config/data/plugins/`),
owned by the user Jellyfin runs as, and restart. Manually installed plugins show "An error
occurred while getting the plugin details from the repository" in the dashboard until the
repository above is added; the plugin works either way. A manually installed copy may also have
automatic updates turned off, so *Update Plugins* skips it. Once the repository is added, install
the latest version from the Catalog (and restart) to switch to a normal, auto-updating install.

New versions can take a few minutes to appear in the Catalog after a release, while GitHub's
cache of the repository file refreshes.

**Upgrading:** the first start of 0.3 rebuilds every card once, so cards with square or portrait
covers also get their cropped version (it takes about as long as a nightly refresh).

**Upgrading from 0.1:** versions before 0.2 saved cards into each library's own image, where
Jellyfin then stopped refreshing its image. On its first start, 0.2 removes those cards (only
where the library image is still the card the plugin saved, so images you uploaded since are
left alone) and Jellyfin rebuilds its own. Live TV has no Jellyfin-made image, so users who turn
cards off see the app's plain Live TV card unless you upload an image for it.

**Uninstall:** remove the plugin from Dashboard > Plugins and restart. Everyone then sees
Jellyfin's own library images, which the plugin never changed. Its cards and settings stay in
the plugin's data folder (`plugins/Jellyfin.Plugin.TitleCards/` under Jellyfin's data folder)
until you delete it.

## What it does

For each view any enabled user sees on the home screen (once each):

1. **Gathers artwork** from a random sample of the library's items. Backdrops and landscape
   images come first; square album art and portrait covers are shown whole on a blurred,
   darkened copy of themselves, or cropped to fill the tile for users who turn on **Crop covers
   to fill their tiles**. Each library type has its own item types (series, movies,
   albums, photos, books...); unknown types use a broad default. Collections take one image per
   collection in turn, and a series only appears once. Playlists use the art behind what's in
   them (a track's artist backdrop and album cover, an episode's series, a movie's own art),
   one playlist at a time in turn, rather than the mosaic image Jellyfin makes for each playlist;
   a playlist with none of these falls back to its own image. Audiobooks stored as one file per
   chapter (which Jellyfin lists as one item per chapter) use their book folder's cover, so each
   book counts once, whichever chapter is sampled. No image is used twice on a card.
2. **Live TV** uses programs airing now (sports optional; their team-logo graphics clash with
   the title). Its artwork is each program's image from your guide data (the XMLTV `<icon>`
   element, or Schedules Direct's program images). Jellyfin doesn't look guide artwork up
   elsewhere, so if your EPG provider supplies no program images, or only portrait or text-heavy
   ones, there is nothing to build a Live TV card from and it keeps its current card, if it has
   one. The log shows `Title Cards: Live TV: no images clear the title (0 candidates); its
   current card (or Jellyfin's image) is kept`. Libraries
   that contain a Live TV recording folder (e.g. DVR) use their recordings first, topped up from
   what's airing.
3. **Keeps text clear of the title**: an image is skipped for a tile if text is detected where
   the title covers that tile (with a 24px margin). Text elsewhere in the image is fine. A cover
   that is drawn differently when cropped must clear the title both ways, so the two versions of
   a card show the same covers.
4. **Lays out** the largest arrangement it can fill: 4 tiles, else 3, 2 or 1. If nothing usable
   clears the title, the library keeps its current card, or Jellyfin's image if it has none.
5. **Draws** with Jellyfin's own recipe: black layer at alpha 0x78, the library name in the
   server's bold sans-serif at 112px, centered, baseline at half height plus half the x-height
   (see [Fonts](#fonts)).
6. **Keeps** the card in the plugin's data folder, plus a cropped version where cropping changes
   it, and shows each user the one they chose (see [Who sees title cards](#who-sees-title-cards)).
   The library's own image stays Jellyfin's. Libraries of backdrops (movies, shows, Live TV) look
   the same either way and have one card.

### When cards are built

| Trigger | What runs |
|---|---|
| Nightly, 02:30 (Scheduled Tasks > Title Cards > *Refresh title cards*) | Every card, with fresh artwork |
| A user's home settings change, or a library is added | Missing cards only, about 20 seconds later |
| Server start, and every 30 minutes (*Create missing title cards*) | Missing cards only; a safety net |

"Missing" means a view the plugin hasn't handled, one whose card is no longer stored, or one
that was empty when last checked (a new library is usually seen before its first scan fills it).
A library whose images were all rejected by the text filter waits for the nightly refresh, so
the text detector isn't rerun on it every half hour. Changing **Home > Library Folders**
(grouped folders) gives that user a new combined view for the new combination of libraries, so
it gets a card shortly after. With nothing missing, these checks finish without loading the
text detector.

### Who sees title cards

Each user sees either title cards or Jellyfin's own library images, and chooses whether covers in
their cards are cropped:

- **Users choose for themselves** in the web client under **Settings > Display**: **Show title
  cards**, and **Crop covers to fill their tiles** (album, book and audiobook covers fill their
  tiles edge to edge, losing their top and bottom; off, each cover is shown whole on a blurred
  copy of itself). The choices are saved on the server, so they apply in every app the user signs
  in to, including TV apps, which have no settings of their own: set them once in a browser.
  Tested with the web client and Android TV / Fire TV; other apps get the same server replies,
  and reports from them are welcome. The settings need the
  [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation)
  plugin to appear in the web client; without it, everything else works the same.
- **Admins can set them for users** under **Per-user choices** on the plugin's settings page,
  with or without File Transformation.
- **Everyone else** follows **Show title cards to users who haven't chosen** (on by default) and
  **Crop covers to fill their tiles for users who haven't chosen** (off by default).

![The Show title cards and Crop covers settings in Settings > Display](docs/images/display-setting.png)

How it works: a library's own image stays Jellyfin's, generated and refreshed by Jellyfin as
usual. When the server lists libraries for a user who sees title cards (the home screen's
library row in every app, and other home-screen plugins that list libraries), the plugin points
each library at its card (or its cropped card) instead. A user's choices apply the next time their
app loads the home screen; an app that has the images cached may need to be restarted.

Users who turn cards off see exactly what Jellyfin shows without the plugin: one backdrop with
the library name for most libraries, a strip of posters for Collections and Playlists (whose
items have posters rather than backdrops), and for Live TV the app's plain icon card, or an image
you upload for Live TV under its Edit Images.

### Settings

Dashboard > Plugins > Title Cards (also listed under Plugins in the dashboard's side menu):

- **Show title cards to users who haven't chosen** (on)
- **Crop covers to fill their tiles for users who haven't chosen** (off). Cropped covers keep
  their middle: a square cover the middle 56% of its height, a portrait book cover about 37%.
- **Per-user choices**: each user's *Title cards* and *Crop covers*, each *Server default*, *On*
  or *Off*, with a summary, a search box and a filter to show only users with their own setting,
  for servers with many users
- **Exclude text from behind the title** (on)
- **Leave sports out of Live TV cards** (on)
- **Build cards as soon as home settings or libraries change** (on)

![Title Cards settings page](docs/images/settings.png)

Schedules are changed under Scheduled Tasks like any other task. Users' choices take effect
straight away, since both versions of each card are already built. After changing the text or
sports setting, run *Refresh title cards* to redraw existing cards straight away.

### Fonts

No font is bundled. The title uses whichever installed font the system's font configuration
picks for bold `sans-serif`, the same lookup Jellyfin uses for its own library images, so the
cards follow the server's fonts. In Jellyfin's Docker images that's DejaVu Sans Bold.

Characters that font doesn't have are drawn in another installed font that does, character by
character. For example, a Japanese library name such as `映画 Movies` keeps "Movies" in the title font and
draws 映画 in a Japanese font, if one is installed. Jellyfin's Docker images include no Japanese,
Chinese or Korean fonts, so those characters come out as boxes until one is added, and the log
names them:

```
Title Cards: 映画 Movies: no installed font has 映 画; they will show as boxes until a font with them is installed
```

Left: only DejaVu installed. Right: with Noto Sans CJK installed.

![A Japanese library title drawn as boxes without a Japanese font, and correctly with one](docs/images/fonts.jpg)

To add fonts in Docker, mount a folder of `.ttf`/`.otf`/`.ttc` files into the container's
`/usr/local/share/fonts/` (for example `- ./fonts:/usr/local/share/fonts/custom:ro` in Compose)
and restart Jellyfin. [Noto Sans CJK](https://github.com/notofonts/noto-cjk) covers Japanese,
Chinese and Korean. Fonts installed inside a running container are lost when it is recreated, so
mount them rather than installing them there. Outside Docker, install the font with your system's
package manager (for example `fonts-noto-cjk` on Debian and Ubuntu) and restart Jellyfin. Cards
made after the restart use it; run *Refresh title cards* to redraw existing cards right away.

## Platform support

| Platform | Cards | Text filter |
|---|---|---|
| Linux x64 (including Docker images such as linuxserver/jellyfin and jellyfin/jellyfin) | Yes | Yes |
| Linux ARM64 (Raspberry Pi 4/5, ARM NAS) | Yes | Yes |
| Windows, macOS, other Linux architectures | Yes | No, cards are built without it |

The text filter runs a small PP-OCR text detection model with ONNX Runtime, whose native
library is platform-specific. To keep the plugin at about 28 MB, only the Linux x64 and ARM64
runtimes are packaged. On other platforms the plugin logs a warning once and builds cards
without the check, so a logo can occasionally end up behind a title (mostly on Live TV, whose
guide art often carries show logos; library backdrops rarely have text).

**Windows and macOS in the future.** Supporting them is a packaging change rather than a code change:
the detector already uses whatever ONNX Runtime library it can load. Options:

1. **One package with every runtime.** Add `win-x64/onnxruntime.dll` (16.5 MB, about 6 MB
   compressed) and `osx-arm64/libonnxruntime.dylib` (42.8 MB, about 12 MB compressed), plus a
   loader entry for each in `TextDetector` like the Linux ones. One install for everyone, at
   about 46 MB. Simplest for users.
2. **Per-platform packages.** One zip per platform, each with only its runtime. Smallest
   downloads, but Jellyfin's repository manifest has one download per version, so each platform
   needs its own repository URL and users must pick the right one.
3. **Download on first use.** Ship without native runtimes and fetch the right one from the ONNX
   Runtime NuGet package when the filter first runs. Smallest install, but it needs internet
   access from the server and has more ways to fail.

If you run Jellyfin on Windows or macOS and would use the text filter, please say so in an
issue; it helps decide between these.

## Development

Needs the .NET 10 SDK; packaging also needs Python 3.

```sh
dotnet build Jellyfin.Plugin.TitleCards.sln
dotnet test Jellyfin.Plugin.TitleCards.sln
python build/package.py --version 0.3.0.0      # dist/title-cards_0.3.0.0.zip
```

Releasing: `python build/package.py --version X --changelog "..." --manifest` builds the zip and
adds the version to `manifest.json`; attach the zip to a GitHub release tagged `vX` and commit
the manifest.

Tests cover tile layout and selection, the renderer (including the per-character font fallback;
its Japanese test checks the drawing when a Japanese font is installed and the missing-character
report when not), the detector's post-processing, the card store and image tags, per-user
choices, the missing-cards check, and the web client script injection (which must change
`index.html` and nothing else).
`DetectorParityTests` also compares the plugin's text detection with
[RapidOCR](https://github.com/RapidAI/RapidOCR) (detection and recognition) on real artwork, when
`TITLECARDS_FIXTURES` points at fixtures made by `build/make_parity_fixtures.py` from your own
server (the artwork isn't committed). On 65 Live TV and library images the plugin agreed on 58
and was stricter on 7: without RapidOCR's recognition step to drop weak matches, it errs toward
skipping an image. It never let through text that RapidOCR caught.

| Path | Contents |
|---|---|
| `src/Jellyfin.Plugin.TitleCards/Cards/` | Artwork selection, tile layout, rendering, text detection, saved-card state |
| `src/Jellyfin.Plugin.TitleCards/Variants/` | Card store, and the filter that shows cards to the users who see them |
| `src/Jellyfin.Plugin.TitleCards/Preferences/`, `Api/` | Per-user choices and their API (`/TitleCards/Preference`, `/TitleCards/Preferences`) |
| `src/Jellyfin.Plugin.TitleCards/Web/` | The Settings > Display script, added to the web client through File Transformation |
| `src/Jellyfin.Plugin.TitleCards/ScheduledTasks/` | *Refresh title cards* and *Create missing title cards* |
| `src/Jellyfin.Plugin.TitleCards/ChangeMonitor.cs` | Reacts to home-settings changes and new libraries |
| `tests/` | xUnit tests |
| `build/package.py` | Builds the zip and updates `manifest.json` |
| `build/make_parity_fixtures.py` | Makes fixtures for the detector parity test |
| `build/make_logo.py` | Draws the logo (abstract tiles, no third-party artwork) |
| `build/fetch_example_art.py` | Downloads openly licensed artwork for rendering example cards, with credits |
| `build/render_examples.py` | Renders example cards from that artwork, and Jellyfin's default no-image card (used in the before-and-after image) |

## AI assistance

This plugin was written with substantial help from AI. The code, tests, packaging scripts and
documentation were largely written by [Claude](https://www.anthropic.com/claude) (Anthropic),
working as a coding agent in Claude Code. The project's direction and design decisions, testing on
a live Jellyfin server, and review of the results were done by the maintainer
([@erstenor](https://github.com/erstenor)). Commits written with Claude carry a
`Co-Authored-By: Claude` trailer.

Please judge the code on its merits and report anything that looks wrong; issues and pull requests
are welcome whether or not you use AI tools yourself.

## Image credits

The Audiobooks, Books, Music, Music Videos, Photos and Playlists cards in the library screenshots
are cropped and combined from the works below. Audiobook and book covers are public-domain
[LibriVox](https://librivox.org/) recordings and [Project Gutenberg](https://www.gutenberg.org/)
ebooks.

- **Music videos** (frames, from [Wikimedia Commons](https://commons.wikimedia.org/)):
  - [Jasmine Rae - Heartbeat](https://commons.wikimedia.org/wiki/File:Jasmine_Rae_-_Heartbeat_(Official_Music_Video).webm), Jasmine Rae, [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)
  - [Katy Adelson - Carmen Habanera (Violin Version)](https://commons.wikimedia.org/wiki/File:Carmen_-_Habanera_~_Violin_Version!.webm), Katy Adelson, [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)
  - [PIXA X BYEALEX - APÁTOK](https://commons.wikimedia.org/wiki/File:PIXA_X_BYEALEX_-_AP%C3%81TOK_(OFFICIAL_MUSIC_VIDEO)-_7PaQ-yF4vc.webm), pixamusic, [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)
  - [Tempalay - Sonatine](https://commons.wikimedia.org/wiki/File:Tempalay_-_Sonatine_(Official_Music_Video)%E2%81%A3%E2%81%A3.webm), PERIMETRON, [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)
  - [ロッカジャポニカ - Spring Tour 2018 Live Digest](https://commons.wikimedia.org/wiki/File:%E3%80%90LIVE%E3%80%91_%E3%80%8C%E3%83%AD%E3%83%83%E3%82%AB%E3%82%B8%E3%83%A3%E3%83%9D%E3%83%8B%E3%82%AB_Spring_Tour_2018_%EF%BD%9ERe-view_ROCK_A_JAPONICA%E3%80%9C_FINAL_%E6%B8%8B%E8%B0%B7_CLUB_QUATTRO%E3%80%8D_LIVE_DIGEST.webm), ROCK A JAPONICA Official Channel, [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)
- **Album covers** (Internet Archive netlabel releases):
  - [Chenard Walcker - Sweet Kali Breathe (2004)](https://archive.org/details/chenard_walcker-sweet_kali_breathe), Chenard Walcker, [CC BY 2.0](https://creativecommons.org/licenses/by/2.0/)
  - [Deadman & Digi Hartatak & Electric Stiletto - Letter B](https://archive.org/details/LetterB), Deadman & Digi Hartatak & Electric Stiletto, [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)
  - [Digi Hartatak - Dull Duco Dances (2009)](https://archive.org/details/dullducodances), Digi Hartatak, [CC BY 3.0 US](https://creativecommons.org/licenses/by/3.0/us/)
  - [Vlad Shegal - SPUTNIKRECORDS007 (2012)](https://archive.org/details/SPUTNIKRECORDS007), Vlad Shegal, [CC0](https://creativecommons.org/publicdomain/zero/1.0/)
  - [Команданто Чё Vlad Shegal - Пол-литра бабушкиных сказок (Split) (2016)](https://archive.org/details/SPUTNIKRECORDS038), Команданто Чё Vlad Shegal, [CC0](https://creativecommons.org/publicdomain/zero/1.0/)
- **Photographs and paintings** (Wikimedia Commons; the paintings are [Art Institute of Chicago](https://www.artic.edu/open-access) open-access images):
  - [International Commerce Centre on Victoria Harbour](https://commons.wikimedia.org/wiki/File:International_Commerce_Centre_on_Victoria_Harbour.jpg), Wilfredor, [CC0](https://creativecommons.org/publicdomain/zero/1.0/)
  - [Messier 24 (infrared, red, green)](https://commons.wikimedia.org/wiki/File:Messier_24_(infrared,_red,_green).jpg), Brainandforce, [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)
  - [Messier83 - Heic1403a](https://commons.wikimedia.org/wiki/File:Messier83_-_Heic1403a.jpg), NASA, ESA, and the Hubble Heritage Team (STScI/AURA) Acknowledgement: William Blair (Johns Hopkins University), Public domain
  - [NGC 3372a-full](https://commons.wikimedia.org/wiki/File:NGC_3372a-full.jpg), NASA, ESA, N. Smith (University of California, Berkeley), and The Hubble Heritage Team (STScI/AURA); credit for CTIO Image: N. Smith (University of California, Berkeley) and NOAO/AURA/NSF, Public domain
  - [Painting with Troika - Vasily Kandinsky](https://www.artic.edu/artworks/8983), Vasily Kandinsky, January 18, 1911, [CC0](https://creativecommons.org/publicdomain/zero/1.0/)
  - [Recife Favela Detran street](https://commons.wikimedia.org/wiki/File:Recife_Favela_Detran_street.jpg), Wilfredor, [CC0](https://creativecommons.org/publicdomain/zero/1.0/)
  - [Sigmaringen August 2015](https://commons.wikimedia.org/wiki/File:Sigmaringen_August_2015.jpg), Felix König, [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)
  - [View to Moscow river from Bolshoy Moskvoretsky Bridge](https://commons.wikimedia.org/wiki/File:View_to_Moscow_river_from_Bolshoy_Moskvoretsky_Bridge.jpg), Anton Zelenov, [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)

## License

[GPL-3.0](LICENSE), like the Jellyfin plugin template. Bundled third-party components (the text
detection model and ONNX Runtime) keep their own licenses; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Screenshots show artwork from a personal
library for illustration; it belongs to its respective owners.
