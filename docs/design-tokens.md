# Design tokens

Upkeep adopts Dotify's design-system palette and type scale; UI structure follows native WinUI 3 / Fluent patterns (`NavigationView`, `ContentDialog`, native controls), not a web app shell. This mirrors Pulsemap's approach, with two deliberate differences noted below.

Implement these as XAML `Color`/`SolidColorBrush`/`FontFamily` resources in `App.xaml`'s `ResourceDictionary` — not as hand-picked hex values inline in views, and not in a separate merged dictionary file (see `CLAUDE.md` for the XAML compiler reason).

## Color

**Primary (brand red)** — primary actions, selection, and the app accent. Never used to signal an error.

| Token | Hex |
|---|---|
| primary-50 | `#fef2f2` |
| primary-100 | `#fee2e2` |
| primary-400 | `#e45a5c` |
| primary-500 | `#d42a2c` |
| primary-600 | `#b41618` — canonical brand red |
| primary-700 | `#971214` |
| primary-800 | `#7d1012` |
| primary-900 | `#6b1113` |

**Secondary (navy)** — data visualization (treemap, usage bars), informational surfaces, links.

| Token | Hex |
|---|---|
| secondary-50 | `#eeeef8` |
| secondary-100 | `#d9d9ef` |
| secondary-200 | `#b8b7e0` |
| secondary-300 | `#9695cf` |
| secondary-400 | `#6e6cb8` |
| secondary-600 | `#211f60` — canonical navy |

**Neutrals** — Tailwind gray scale. Page background `gray-50` (`#f9fafb`), card surfaces white, borders `gray-200` (`#e5e7eb`), body text `gray-700` (`#374151`), headings `gray-900` (`#111827`), captions `gray-500` (`#6b7280`).

**Semantic** (state, never the brand accent): success `#16a34a` (on `#dcfce7` with `#166534` text), warning `#f59e0b` (on `#fef3c7` with `#92400e` text), danger `#ef4444`. Always paired with an icon or text — never color alone.

### The accent override

**Difference from Pulsemap:** Upkeep overrides the Windows accent color with brand red, so native selection — `NavigationView`'s selection marker, `CheckBox`, `ToggleSwitch`, `RadioButton`, `SelectorBar` — is red rather than the system blue. Upkeep is a selection-heavy app; leaving it on the system accent meant every screen was dominated by a color that isn't Dotify's.

Set in `App.xaml`: `SystemAccentColor` and its Light1-3/Dark1-3 siblings from the primary scale above, plus `AccentFillColorDefaultBrush`, `AccentFillColorSecondaryBrush` (90% alpha), `AccentFillColorTertiaryBrush` (80% alpha), and `NavigationViewSelectionIndicatorForeground`.

## Typography

**Difference from Pulsemap:** headings and the wordmark use **Montserrat**, Dotify's brand typeface, shipped with the app (`Assets/Fonts/Montserrat.ttf`, SIL Open Font License — `Assets/Fonts/OFL.txt`). Everything else uses Segoe UI Variable so the app reads as native Windows.

| Role | Family | Weight |
|---|---|---|
| Wordmark (title bar, About) | Montserrat | 700 |
| Page titles, dialog titles, the results figure | Montserrat | 600 |
| Section labels, body, captions, all controls | `Segoe UI Variable Text`, fallback `Segoe UI` | 400 body, 600 labels |

Scale (px size / line-height): xs 12/16, sm 14/20, base 16/24, lg 18/28, xl 20/28, 2xl 24/32, 3xl 30/36. Default is sm (14px) for dense application UI.

Reference the bundled face as `ms-appx:///Assets/Fonts/Montserrat.ttf#Montserrat` and set `FontWeight` explicitly; it is a variable font, so weights come from the axis rather than separate files.

## Radius

| Element | Value |
|---|---|
| Inputs, buttons (native default) | 4px |
| Brand primary buttons | 6px |
| Dialogs, popovers, inline panels | 8px |
| Cards | 16px |
| Pills, badges, chips | full |

## Mica backdrop

Standard Mica (`MicaKind.Base`), single main window. Backdrop-only: it shows through the title bar and the `NavigationView` pane, never through content surfaces. Content pages are solid `gray-50` with white cards, which keeps native control foregrounds legible regardless of the user's system theme.

## Icons

Segoe Fluent Icons glyphs for native shell affordances (nav items, buttons), matching the platform. Sizes 16px in lists and buttons, 20px in headers. No emoji, except the heart in the "Crafted with ❤️ by Dotify" attribution line.

A shield glyph always and only means "this needs administrator approval". A lock glyph always and only means "Upkeep won't change this".
