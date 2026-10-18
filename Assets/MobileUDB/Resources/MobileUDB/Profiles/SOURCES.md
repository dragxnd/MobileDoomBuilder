# Original UDB thing definitions

First-release additions (2026-10-06): Boom.txt is the unchanged
Assets/Common/Configurations/Includes/Boom_things.cfg. MBF.txt contains only the
MBF dog (888), with DOGSA / radius 11 / height 28 from MBF's editing specification
and UDB Dehacked_Doom.cfg Thing 140. MBFDog.png is the unchanged
Assets/Common/Sprites/Dog.png, used as a picker fallback when resources omit DOGS.
Actual resource sprites take priority. GPL-3.0; MBF/MBF21 action provenance is in
../Actions/SOURCES.md.

The `.txt` assets are byte-for-byte copies from the legacy Ultimate Doom Builder
repository, renamed so Unity imports them as TextAssets:

| Asset | Original |
| --- | --- |
| Doom.txt | Assets/Common/Configurations/Includes/Doom_things.cfg |
| Doom2.txt | Assets/Common/Configurations/Includes/Doom2_things.cfg |
| Heretic.txt | Assets/Common/Configurations/Includes/Heretic_things.cfg |
| Hexen.txt | Assets/Common/Configurations/Includes/Hexen_things.cfg |
| Strife.txt | Assets/Common/Configurations/Includes/Strife_things.cfg |
| DehackedDoom.txt | Assets/Common/Configurations/Includes/Dehacked_Doom.cfg |

License: GPL-3.0, see `UltimateDoomBuilder-master/LICENSE.txt`.
Doom II merges Doom definitions first, then Doom2 overrides/additions. The small
core parser reads bundled text; includes resolve only explicitly supplied text
resources and never execute filesystem instructions.
Category defaults, sprite frames, dimensions and placement follow `ThingCategory`,
`ThingTypeInfo`, `VisualThing` and `BaseVisualThing` in the original source.
IWAD sprites remain in user-supplied resource archives; none are bundled here.

DehackedCatalog.cs uses the original DehackedParser/DehackedThing/DehackedFrame
editor metadata and the unchanged Dehacked_Doom config (including Boom/MBF and
extended slots). Patch dimensions use 16.16; configuration dimensions are map
units. Later patch properties override earlier supplied properties. All DEHACKED
lumps and root PK3 DEHACKED* files are read, with root WADs last as in
PK3StructuredReader.GetDehackedData. Frame-only patches and Text/[SPRITES] renames
also update native actors. Changed DoomEdNums remove obsolete catalog mappings.
This is editor metadata, not an implementation of patched engine behavior.
Standalone DEH/BEX attachments share the ordered patch list and are saved inside
MobileUDB projects with an explicit attachment kind. Container signatures retain
priority for older projects with a misleading display extension. INCLUDE and
editor color/arrow overrides are pending.
