# Assets and provenance

## Included

- Editor/Data/algorithms.json: compatibility metadata derived from the official editor XML. Included at the maintainer's explicit direction; provenance remains in catalog-provenance.json. This is not a claim that the manufacturer's original material has been relicensed under GPL.
- Editor/Converter/nam_input_wav.wav: conversion stimulus, included at the maintainer's explicit direction. It is not automatically played through the pedalboard.
- Editor/Data/clear-default-gp200-1.8.2.prst: the maintainer's empty-preset template used for clear/move operations.
- Project-created illustrations in `Editor/Data/OfficialImages/effects_bg/`: 87 AMP, 60 CAB, 40 DST, 15 EQ, 4 NR and 2 WAH files (S-Wah and B-Wah). They are original project artwork without third-party logos and are supplied under GPL-3.0-only. AMP/EQ/CAB/DST came from the project's generated proposal folders and their hashes were checked against the current application assets; NR and WAH have per-image provenance records.
- Catalogs and configuration files are data required by the editor. No firmware image is included.

## Excluded

Original editor PNGs, official chain icons, generated WAH illustrations with visible commercial marks, the official PDF manual, complete firmware BINs, user NAM/CLO/IR libraries, private backups, logs, SDKs and original application binaries are not part of this repository. The two included native binaries are documented source-built dependencies, not binaries extracted from the official editor.

## Optional local artwork

The loader expects Data/OfficialImages/effects_bg/{module}/{resource-name}.png, and chain icons in Data/OfficialImages/Background/chain_icon_{module}_{state}.png, relative to the published executable. Keep catalog resource names exactly. Large images use 1148×580 RGBA PNG with transparent background; chain icons use 180×180 RGBA. Restart the editor after replacing artwork.

Users may supply local assets for which they have the appropriate rights. Keep third-party assets out of the public repository and release ZIP. A copy from an installed program is not itself a redistribution license. This repository does not automatically extract files from the official editor.

The included artwork was selected from project-generated proposals by matching the current editor's filenames and the recorded modified dates/hashes. Upstream originals and backup folders were not copied. New public artwork should be original and unbranded; add its provenance here when adding it.
