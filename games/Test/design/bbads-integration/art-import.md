# BBads texture import rules

In the Test Unity project, use **Tools → Test → BBads → Import → Preview Texture Rules** to generate a JSON report under the project's `Library` directory. **Apply Texture Rules** writes only importer settings whose planned values differ and generates its own report. Run preview or apply again to check that `changes` is zero. Batch entry points are `BBads.Editor.BBadsTextureImport.Preview` and `BBads.Editor.BBadsTextureImport.Apply`. Pass `-bbads-import-report <path>` to direct JSON outside `Assets`.

| Group | Identification | Standalone | Android |
| --- | --- | --- | --- |
| UI | `Art/CF` HUD sprites and Resources `Items` or `UI` | BC7 on dimensions divisible by four, RGBA32 otherwise | ASTC 4×4 |
| Background | `Art/CF/BG.png` | BC7 | ASTC 6×6 |
| World | Resources `projectile`, `KM/projectile` and `HealthBar` | BC7 on dimensions divisible by four, RGBA32 otherwise | ASTC 4×4 |
| Spine | Page filenames in a Spine `.atlas.txt` or `.atlas` | RGBA32 | RGBA32 |
| Fonts and digits | Font extensions and font, digit and number directories | Source settings | Source settings |

The Resources rules recognize both the original directory names and the equivalent `Resources/BBads` prefix. Other texture folders are reported as unmanaged. UI and backgrounds use bilinear filtering without mipmaps. World sprites use bilinear filtering and retain their original mipmap choice. Sprites already marked readable and artwork with a mismatched texture type retain their source settings. Spine pages retain alpha interpretation, color space, filtering, and readability, with a platform size cap that accommodates the atlas source dimensions. Per-asset exceptions live in [TextureExceptions.json](../../client/Unity/Assets/BBads/ImportSettings/TextureExceptions.json).

The report records original dimensions and alpha, GUIDs, imported sprite file IDs and geometry, direct dependencies, and reverse references from scanned BBads assets. It includes the effective rule, exceptions, and before/expected importer values. Applying the rules preserves original file bytes, sprite modes, slices, pivots, borders, and pixels per unit. Unity's dependency scan covers serialized dependencies. Callers that load by string use the keys documented with the quality resolver. [Quality diagnostics](art-quality.md)

## Validation

Unity **2022.3.16f1** ran in batch mode against copies of the source `Art`, `Resources/BBads` and `Fonts` groups in an isolated project. Two previews produced byte-identical JSON. The report contained 40 entries, including eight Spine pages and two wave-bar exceptions. The first apply changed 37 importers and the second changed zero. Compared before and after, source file bytes, sprite IDs, imported geometry and dependency lists matched. A fixture scene retained its reference to the `BG_0` sliced sprite using its original GUID and local file ID.

The repository Test Unity batch import and compilation also passed with the new tooling. Raw reports and Unity logs are under `artifacts/bbads-art-validation` in the implementation worktree. This run covered texture import and serialization. The integrated battle scene provides the remaining visual check of the wave-bar edges, background, and Spine materials.
