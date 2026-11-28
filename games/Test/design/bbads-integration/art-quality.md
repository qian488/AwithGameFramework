# BBads art quality tiers

Reference the `BBads.Quality` assembly from the presentation assembly. Call `BBads.Quality.BBadsArtQuality.LoadSprite(key, fallback)` before assigning the sprite to the existing `Image` or `SpriteRenderer`. The overload with `out string resolvedKey` exposes the actual Resources key. A caller-supplied fallback returns a null resolution key. Use the migrated scene sprite as the background fallback and let atlas or original Resources loading supply fallbacks for existing icons.

| Logical key | High source pixels | Low source pixels |
| --- | ---: | ---: |
| `BBads/Art/CF/BG` | 1080×1920 | 540×960 |
| `BBads/Items/Bag_Slot` | 82×82 | 41×41 |
| `BBads/Items/Cat_Gangster1` | 170×170 | 85×85 |

The separate PNGs live in `Assets/BBads/Quality/Resources/BBads/Quality/{High,Low}` with their own GUIDs. High uses the source pixels. Low uses half-size Lanczos resampling and 50 pixels per unit, compared with 100 for High. Both tiers retain the same logical sprite size, normalized center pivot and zero border for these source sprites. The background stays standalone. These tier resources are available for presentation callers that opt into replacement. Coordinate any replacement of packed icons with the atlas grouping so packed and standalone textures are measured correctly.

Selection defaults to High on desktop and Low on Android. The last valid `-bbads-art-quality high|low` argument overrides the default. Call `SetOverride(BBadsArtTier.High)` or `SetOverride(BBadsArtTier.Low)` before loading sprites to force a tier through an in-game diagnostics entry. `SetOverride(null)` restores the launch choice. A lookup tries its chosen tier, the other tier, the supplied sprite, then the original Resources key. Reload the affected presentation to apply a changed setting.

Use **Tools → Test → BBads → Quality → Validate Variants** or batch entry `BBads.Editor.BBadsQualityAudit.Validate` on a project containing both tiers. It checks matched geometry and per-target import settings and logs source and imported dimensions and formats. For a single-tier release, copy the integrated Unity project into a build staging directory, omitting the other tier folder and its adjacent `.meta` before starting the editor. Build the staged project with the normal Player entry. Keep both folders in the project when the Player needs both tiers at runtime.

## Validation

Unity **2022.3.16f1** built two separate Windows x64 Players using copies of the same fixture scene. Each build contained only its staged tier. Background execution passed four combinations of packaged and forced tier. A third Windows build packaged both tiers and returned the matching asset for either forced tier. The tests also checked the caller-supplied fallback, the original `BBads/Items/Fish1` resource and a completely missing resource key.

| Texture | Windows Player format | Android-target editor format |
| --- | --- | --- |
| Background | BC7 | ASTC 6×6 |
| Slot and item icon | RGBA32 | ASTC 4×4 |

Windows BC7 requires dimensions aligned to four pixels, so the original icon geometry uses RGBA32 on that target. The two Windows fixture builds totalled 80,035,931 bytes for High and 78,373,803 bytes for Low. The three loaded textures account for an estimated 2,216,096 bytes of texture payload in High and 554,024 bytes in Low, calculated from the actual dimensions and formats with no mipmaps. The headless Player did not report GPU residency.

Android-target editor import verified both tiers at the listed sizes and ASTC formats. A later isolated Android build supplied Unity 2022.3.16f1's OpenJDK, SDK, and NDK r23b, passed prerequisite detection, and reached Gradle assembly. The Gradle step failed before an APK was produced. The migrated battle scene, device GPU format and on-screen fidelity remain integration checks. The isolated fixture and logs are in `artifacts/bbads-art-validation`.
