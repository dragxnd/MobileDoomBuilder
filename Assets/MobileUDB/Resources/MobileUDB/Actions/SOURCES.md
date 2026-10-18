# Original UDB action definitions

## First release scope (2026-10-06)

Binary Doom / Hexen, Boom, MBF and MBF21. UDMF is deferred and excluded from the UI
and TEXTMAP parser/writer compilation. The older notes below describe provenance,
including code that is currently disabled.

MBF21_misc.txt, MBF21_linedefs.txt and MBF21_generalized.txt are unchanged copies
of UDB's matching Includes/*.cfg files. MBF_misc.txt contains the MBF friendly
map-thing flag (128). Profile composition follows UDB MBF21_Doom2Doom.cfg and
includes Doom/Boom first, then MBF21 overrides. The existing Boom_linedefs.cfg
already contains MBF sky-transfer actions 271/272.

Primary references:

- [MBF editing features](https://github.com/fragglet/mbf/blob/master/mbfedit.txt)
- [MBF21 specification v1.4](https://github.com/kraflab/mbf21/blob/master/docs/spec.md)

MBF.txt dog type 888 uses DOGSA, radius 11 and height 28 from the MBF documentation
and UDB Dehacked_Doom.cfg Thing 140. Boom.txt is the unchanged Boom_things.cfg.
MBFDog.png is the unchanged Assets/Common/Sprites/Dog.png for picker fallback;
actual DOGS resource sprites take priority. DEH/BEX editor metadata and standalone
attachments are implemented; see ../Profiles/SOURCES.md. No action simulation is
supplied by the 3D preview.

MapMetadataChecks follows native CheckUnknownActions/CheckUnknownThings and uses
configured argument tag types/classes plus HexenLineTags for IDs encoded on load.
Output TIDs for Thing_Spawn/NoFog/Facing, ProjectileIntercept/Aimed and ChangeTID
are definitions, not input references. Teleport_Line arg1 remains a destination
reference despite the native Hexen override marking it plain type 0. MBF21's
nonzero scroller tag associations look for another wall, as specified by the
linked MBF21 spec. Boom generalized push triggers use the local back sector,
so they are exempt from remote-sector tag diagnostics. Zero/special enum values
and unknown/dynamic actions do not infer required targets. These are static,
read-only remarks, not proof of runtime behavior or full script validation.

Sky-transfer preview follows [MBF p_spec.c](https://github.com/fragglet/mbf/blob/master/p_spec.c)
for tag matching and last-linedef precedence, and [dsda-doom r_plane.c](https://github.com/kraflab/dsda-doom/blob/master/prboom2/src/r_plane.c)
for the first-side upper texture, 271/272 column direction, fixed-point angle
offset and rowoffset minus 28. The preview retains its angular vertical projection;
it does not simulate engine scrolling or animated skies.

All 17 `.txt` files are unchanged copies of the corresponding `.cfg` files in
`UltimateDoomBuilder-master/Assets/Common/Configurations/Includes`:

| Prefix | Files |
| --- | --- |
| Doom, Heretic, Hexen, Strife, ZDoom | *_linedefs.cfg, *_sectors.cfg, *_misc.cfg |
| Boom | Boom_linedefs.cfg, Boom_misc.cfg |

The extension change lets Unity import them as TextAssets. License: GPL-3.0,
see the legacy repository's `LICENSE.txt`.

Composition follows the corresponding `*_common.cfg`, native game config files,
`LinedefActionInfo` and `ArgumentInfo`. Hexen's native configuration also includes
the standard ZDoom enumerations and game-specific enum section. UDMF `NULL`
overrides remove Line_SetIdentification; actions outside byte range are omitted
for Hexen's binary format. Doom-family UDMF namespaces use their classic codes.

`GameConfigText` handles text declarations, inherited blocks, NULL removal, bare
flags and includes. Runtime includes resolve only these bundled TextAssets,
never user filesystem paths. Generalized Boom action builders are still pending.
