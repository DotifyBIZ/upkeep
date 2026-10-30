# Design tokens

Upkeep uses the [Dotify One Design System](https://repo.dotify.biz/one/design-system/) for colour, type, radius, the accent and the primary button. UI structure follows native WinUI 3 / Fluent patterns (`NavigationView`, `ContentDialog`, native controls), not a web app shell, as in Pulsemap.

## Where the values live

`App.xaml` holds the design system's `xaml/Dotify.inline.xaml`, pasted inside `MergedDictionaries` after `XamlControlsResources`. It is not a separate merged file (see `CLAUDE.md` for the XAML compiler reason). To update, paste the current file over the inner `ResourceDictionary`. Never edit the values in place, and never write a hex value in a view.

Montserrat ships as a variable font in `Assets/Fonts/Montserrat-Variable.ttf` (SIL Open Font License, `Assets/Fonts/OFL.txt`). It is the face for all text, controls included; `FontWeight` picks the weight.

## Which resource to use

| Use | Resource |
|---|---|
| Page title and subtitle | `DotifyPageTitleTextStyle`, `DotifyPageSubtitleTextStyle` |
| Section label | `DotifySectionLabelTextStyle` |
| Body text | `DotifyBodySmTextStyle` |
| Hints, timestamps, explanations | `DotifyHintTextStyle` |
| Card | `Style="{StaticResource DotifyCardStyle}"` on a `Border` |
| Page background | `{ThemeResource DotifyColorSurfaceBrush}` |
| Panel and bottom bar background | `{ThemeResource DotifyColorSurfaceRaisedBrush}` |
| Table header band, pills, bar tracks | `{ThemeResource DotifyColorSurfaceInsetBrush}` |
| Borders and dividers | `{ThemeResource DotifyColorBorderBrush}` |
| Headings and emphasised text | `{ThemeResource DotifyColorOnSurfaceBrush}` |
| Secondary text | `{ThemeResource DotifyColorOnSurfaceMutedBrush}` |
| Success and warning panels | `DotifyColorSuccessLightBrush` with `DotifyColorSuccessTextBrush`; `DotifyColorWarningLightBrush` with `DotifyColorWarningTextBrush` |
| Data: the current or reached item (usage bars, the latest boot, a reached milestone) | `{ThemeResource DotifyColorSecondaryBrush}`, with `DotifyColorSecondaryFgBrush` for a glyph on it |
| Data: everything else in the same chart | `{ThemeResource DotifyColorBorderStrongBrush}` |
| Font sizes | `{StaticResource DotifyTextXs}` … `DotifyText3xl` (12, 14, 16, 18, 20, 24, 30) |
| The one primary action on a page | `Style="{StaticResource DotifyPrimaryButtonStyle}"` |

- **Themes:** content follows the Windows light or dark setting. Every colour above is a theme brush, so use `{ThemeResource}` for brushes and `{StaticResource}` for sizes and styles.
- **Accent:** the design system sets the Windows accent to Dotify Red, so native selection (`NavigationView`'s marker, `CheckBox`, `ToggleSwitch`, `RadioButton`, `SelectorBar`) is red rather than the system blue. Upkeep is a selection-heavy app; on the system accent every screen was dominated by a colour that isn't Dotify's.
- **Primary button:** `DotifyPrimaryButtonStyle` has its own hover and pressed states. Don't add `Button.Resources` overrides or set the content's `Foreground`.
- **Status:** pair every status colour with an icon or text, never colour alone. Brand red is for primary actions and selection, never an error.

## Mica backdrop

Standard Mica (`MicaKind.Base`), single main window. Backdrop-only: it shows through the title bar and the `NavigationView` pane, never through content surfaces, which are solid Dotify surfaces.

## Icons

Segoe Fluent Icons glyphs for native shell affordances (nav items, buttons), matching the platform. Sizes 16px in lists and buttons, 20px in headers. No emoji, except the heart in the "Crafted with ❤️ by Dotify" attribution line.

A shield glyph always and only means "this needs administrator approval". A lock glyph always and only means "Upkeep won't change this".
