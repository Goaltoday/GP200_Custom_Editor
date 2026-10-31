# Third-party notices

GPL-3.0-only applies to the project's own code. It does not replace licenses in third-party files or imply ownership of external resources.

| Component | Use | License / location |
|---|---|---|
| NamToClo converter sources | Native/Converter/upstream/src | MIT; upstream/LICENSE |
| NeuralAmpModelerCore | Native/vendor/namcore/NAM | MIT; namcore/LICENSE |
| AudioDSPTools | NAMCore headers | MIT; Dependencies/AudioDSPTools/LICENSE |
| nlohmann JSON | JSON headers | MIT; Dependencies/nlohmann/LICENSE |
| Eigen | NAMCore matrix operations | Primarily MPL-2.0 with per-file exceptions; COPYING files retained |
| r8brain | Conversion and IR resampling | MIT; separate source snapshots and notices retained |
| Ooura FFT4G | FFT source from SoXR | Permissive Ooura notice in fft4g.c and OOURA-NOTICE.txt |
| SoXR FFT support headers | fft4g.h and math-wrap.h | LGPL-2.1; COPYING.LGPL included |

The worker uses the FFT source and two support headers; it does not link the entire SoXR library. The support headers do carry LGPL notices, which must not be omitted merely because the main FFT source is permissive. Complete used source, build definitions and notices are provided under Native; recipients can rebuild and replace the binaries. Eigen's relevant source is likewise included.

The runtime notices are preserved under Editor/Converter/Licenses and Editor/Data/r8brain-LICENSE.txt. Do not remove existing copyright notices or replace them with the project copyright.

Sample rate converter designed by Aleksey Vaneev of Voxengo.

See docs/ASSETS.md for the separate provenance and publication decision concerning catalogs, stimulus WAV and artwork. Product names describe compatibility and are not a claim of endorsement.
