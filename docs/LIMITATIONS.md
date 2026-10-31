# Known limits

- Custom requires the matching negotiated USB capabilities. The capability mask does not prove the complete hash or identity of installed firmware.
- BASE and LIVE can differ because of expression processing; strict restoration may reject after a persistent preset replacement. This publication does not change that behavior.
- Save in the active position uses native STORE, but the host does not fully verify persistent SC10 by reloading A–D after every in-place save.
- Multi-position writes are not atomic; errors do not imply automatic rollback. Retain the reported backup and inspect the actual device before retrying.
- Native NAM loading requires recovery protection and a free/unselected destination. One selected NAM block maximum, including bypassed blocks; 32 stored models.
- Local BIN/manifest NAM inventory was removed. Installed names are read over USB.
- Original manufacturer illustrations, official chain icons and generated WAH images with visible brand marks are omitted. Other effect illustrations are included where project-created artwork exists; text fallbacks remain functional for missing images.

Firmware update and factory reset are outside the application scope. This publication does not include hardware diagnostic commands or claim audio-equivalence certification.
