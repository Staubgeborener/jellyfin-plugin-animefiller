# Anime Filler Marker

Automatically marks anime filler episodes in your Jellyfin library using data from [animefillerlist.com](https://www.animefillerlist.com).

- Pure filler episodes get a `[F]` prefix in the title
- Mixed canon/filler episodes get a `[C/F]` prefix (optional)
- Disabling both removes all existing markers on the next run

There is already a project called [Ronin](https://github.com/ahza-code/jellyfin-plugin-ronin) that automatically detects and marks fillers and mixed/fillers with badges. And indeed it does a great job. Unfortunately, these badges are only displayed in the Jellyfin web UI (i.e. Browser), but not in the [Jellyfin apk](https://github.com/jellyfin/jellyfin-android) which i like to use for my SmartTV. This project helps here: We don't have badges, but we can see at a glance from the episode name whether we can skip it or not. 

## Installation

Add the repository to Jellyfin under **Dashboard → Plugins → Repositories**:

```
https://raw.githubusercontent.com/Staubgeborener/jellyfin-plugin-animefiller/main/manifest.json
```

Then install *Anime Filler Marker* from the catalog and restart Jellyfin.

## Settings

**Dashboard → Plugins → Anime Filler Marker → Settings**

| Setting | Default | Description |
|---|---|---|
| Mark filler episodes | on | Prepends `[F]` to filler episode titles |
| Mark mixed episodes | on | Prepends `[C/F]` to mixed canon/filler titles |

The task runs daily at 03:00 and can also be triggered manually under **Dashboard → Scheduled Tasks**.
