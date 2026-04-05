# Audio Switcher
A Cities: Skylines II mod that lets you swap siren, vehicle engine, ambient, and public transport announcement audio with custom `.wav` and `.ogg` files.

## Local Audio
Local audio replacement can be done from:
- `Custom Sirens` - For sirens.
- `Custom Engines` - For engine sounds.
- `Custom Ambient` - For other ambient sounds.
- `Custom Announcements` - For public transport arrival/departure announcements.

These are found in the directory where the mod is installed.

## Live UI Editing
You can live-edit the Gameface UI while the game is running.

1. Build/deploy the mod once so the target folder exists.
2. Install UI tooling once in this repository:
- `npm install`
3. Run watch mode:
- `npm run dev`
4. Start Cities: Skylines II with:
- `--uiDeveloperMode`
5. Edit:
- `SirenChanger.Guidance.mjs` (current UI source)
- files in `Images/`
6. Use Chrome DevTools at:
- `http://localhost:9444`

Notes:
- `npm run dev` uses the official-style webpack watch pipeline with an `index` entry at `ui/src/index.js`.
- Webpack output is written to `%CSII_USERDATAPATH%\\Mods\\SirenChanger`.
- If `CSII_USERDATAPATH` is not set, fallback path is `%USERPROFILE%\\AppData\\LocalLow\\Colossal Order\\Cities Skylines II`.
- C# changes still require a normal rebuild.

## Public Transport Announcements
Audio Switcher uses a per-station, per-line announcement system. No TTS is required.

### Supported services
- Train
- Bus
- Metro
- Tram
- Ferry

### Setup in Options
1. Open `Options > Audio Switcher > Public Transport Audio`.
2. Enable `Enable Transit Station Announcements`.
3. Click `Rescan Custom Announcement Files`.
4. Load into a city and click `Scan Transit Lines`.
5. Select `Line Service`, `Transit Station`, and `Transit Line`.
6. Set `Station-Line Arrival Override` and `Station-Line Departure Override`.
7. Check `Station-Line Override Status` and `Transit Line Scan Status`.

`Default` means no announcement for that station-line event.

### Trigger behavior
- Arrival announcements trigger when a vehicle enters the `Arriving` state.
- Departure announcements trigger when a vehicle exits the `Boarding` state.
- A minimum 1.5 second interval is applied per vehicle for arrivals and departures.
- Newly observed vehicles do not fire fake arrival/departure events on the first observed frame.

### Multiple arrivals/departures
- Events are queued per slot + station + line while clips are still loading.
- Queue order is FIFO.
- Queue limit is 16 pending events per slot.
- Pending events time out after 12 seconds.

### Maintenance tools
- `Scan Transit Lines` discovers active lines, stations, and station-line pairs in a loaded city.
- `Prune Stale Lines` removes old discovered entries that are no longer observed and are not used by overrides.
- `Custom Announcement File Scan Status` shows scan output and the resolved announcements folder path.

## Module Packs
Audio Switcher supports module packs delivered as separate mods via PDX.

A module pack is discovered when `AudioSwitcherModule.json` exists in either:
- the mod root, or
- the mod `content/` folder.

### How To Make A Module (In-Game)
Open `Options > Audio Switcher > Developer > Module Creation & Upload`.

1. Set module metadata:
- `Module Display Name` (spaces supported)
- `Module ID` (letters, numbers, periods, dashes, underscores)
- `Module Version` (numbers and periods only)
- `Export Directory`
- `Package Folder Name`

2. Select content to include:
- Sirens: `Local Siren File` + `Add Selected Siren`
- Vehicle engines: `Local Engine File` + `Add Selected Engine`
- Ambient: `Local Ambient File` + `Add Selected Ambient`
- Transit: `Local Line Announcement File` + `Add Selected Line Announcement`
- Optional: include sound set profiles with `Sound Set Profile` + `Add Selected Sound Set`
- Optional shortcuts: `Select All Local Audio` and `Select All Sound Sets`

3. Build the package:
- `Build Local`: creates a local module package.
- `Build + Upload`: creates an upload-ready asset package and immediately uploads it to PDX Mods.

4. Review status fields:
- `Audio Selection Summary`
- `Sound Set Profile Summary`
- `Build Status`
- `Upload Status`
- `Pipeline Status`

Module builder rules:
- At least one local audio file must be included.
- Profile-only modules are currently disabled.
- Module-sourced selections are skipped during export; only local files are exported.
- A unique folder name is generated automatically when the target folder already exists.

### Build + Upload Settings
When using `Build + Upload`, configure:
- `Visibility`: Public, Private, or Unlisted
- `Publish Mode`: Create New or Update Existing
- `Existing Mod ID`: required for Update Existing
- `PDX Page Description`: optional long description
- `Additional Dependency IDs`: optional comma/semicolon/newline-separated IDs; optional `@version` suffix (example `123456@1.0.0`)
- `Thumbnail Directory` and `Thumbnail`

Notes:
- Audio Switcher dependency is always added automatically during upload.
- `Refresh Thumbnails` rescans the latest generated package and thumbnail directory.
- Supported thumbnail formats are `.png`, `.jpg`, `.jpeg` (`.png` is not recommended).
- If no thumbnail is selected, `thumbnail.png` is used (generated automatically when missing).

### Package Layouts
`Build Local` output:
- `AudioSwitcherModule.json`
- `Audio/`
- `Profiles/`
- `README.txt`

`Build + Upload` output:
- `content/AudioSwitcherModule.json`
- `content/Audio/`
- `content/Profiles/`
- `thumbnail.png`
- `README.txt`

### Module Manifest
`AudioSwitcherModule.json` is auto-generated by the module builder.

The file looks like this:

```json
{
  "schemaVersion": 1,
  "moduleId": "example.audio.pack",
  "displayName": "Example Audio Pack",
  "version": "1.0.0",
  "sirens": [
    {
      "key": "police/na/wail",
      "displayName": "Police Wail NA",
      "file": "Audio/Sirens/police_na_wail.wav",
      "profile": {
        "Volume": 1.0,
        "Pitch": 1.0,
        "SpatialBlend": 1.0,
        "Doppler": 1.0,
        "Spread": 0.0,
        "MinDistance": 1.0,
        "MaxDistance": 200.0,
        "Loop": true,
        "RolloffMode": 1,
        "FadeInSeconds": 0.0,
        "FadeOutSeconds": 0.0,
        "RandomStartTime": false
      }
    }
  ],
  "vehicleEngines": [
    {
      "key": "cars/sport_a",
      "displayName": "Sport Engine A",
      "file": "Audio/Engines/sport_a.ogg"
    }
  ],
  "ambient": [
    {
      "key": "city/night_loop",
      "displayName": "City Night Loop",
      "file": "Audio/Ambient/city_night_loop.ogg"
    }
  ],
  "soundSetProfiles": [
    {
      "setId": "default",
      "displayName": "Default",
      "folder": "Profiles/default",
      "files": [
        "SirenChangerSettings.json",
        "VehicleEngineSettings.json",
        "AmbientSettings.json",
        "TransitAnnouncementSettings.json"
      ]
    }
  ],
  "transitAnnouncements": [
    {
      "key": "station/line/train_arrival_01",
      "displayName": "Train Arrival 01",
      "file": "Audio/Transit/train_arrival_01.ogg"
    }
  ]
}
```

Notes:
- `file` is relative to the directory containing `AudioSwitcherModule.json`.
- `profile` is optional but recommended. If omitted, Audio Switcher uses a default template for that audio domain.
- Entries appear in dropdowns with `[Module: <displayName>]` labels.
