# Anime Filler Marker

Automatically marks anime filler episodes in your Jellyfin library using data from [animefillerlist.com](https://www.animefillerlist.com).

- Pure filler episodes get a `[F]` prefix in the title
- Mixed canon/filler episodes get a `[C/F]` prefix (optional)
- Disabling both removes all existing markers on the next run

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
| Filler prefix | `[F]` | Customizable prefix |
| Mark mixed episodes | on | Prepends `[C/F]` to mixed canon/filler titles |
| Mixed prefix | `[C/F]` | Customizable prefix |

The task runs daily at 03:00 and can also be triggered manually under **Dashboard → Scheduled Tasks**.
