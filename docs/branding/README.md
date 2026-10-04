# Branding

The Dogeometric mark: a boxy wolfdog head on a navy rounded square, with the red/green/blue
move axes and an orange rotate ring of a 3D transform gizmo.

| File | Use |
|---|---|
| `icon-{16..1024}.png` | Square app icon, transparent outside the rounded square |
| `icon.ico` | Windows icon, 16 to 256 px |
| `logo-horizontal-light.png` | Icon + wordmark, for light backgrounds |
| `logo-horizontal-dark.png` | Icon + wordmark, for dark backgrounds |

`app/icon.png` is the same 1024 px icon, used by Godot as `config/icon`.

The wordmark is Ubuntu Sans ExtraBold; "Dog" in tan (`#C48846` light, `#E6B880` dark), the rest in
navy `#161C2F` or off-white `#F5F5F5`.

To pick the variant by theme in Markdown:

```html
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/branding/logo-horizontal-dark.png">
  <img alt="Dogeometric" src="docs/branding/logo-horizontal-light.png" width="480">
</picture>
```
