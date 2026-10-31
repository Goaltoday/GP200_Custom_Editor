# Architecture

Editor/Source contains WPF UI, PRST model/codecs, device session, WinMM transport and application services. Native contains the optional native conversion worker and IR resampler sources plus their runtime binaries.

UI → LiveEditCoordinator/LiveEditing or BankService/Mx42PresetTransactions → DeviceSession/DeviceMidi → WinMM. PresetDocument preserves unknown words and padding. Stock and Custom share only mechanisms with equivalent behavior. Custom's visible profile name maps to MX42 internally.

Preserve internal slot, visual order, effective family and algorithm as separate concepts. CHAIN uses words 13/14 of internal slot10; word12 can belong to an effect. Preserve empty parallel branches and the existing BLEND curve. One selected NAM, even bypassed, counts against the per-preset limit; stored unused models do not.

Scene capture dirties the Scene bank independently of PRST. Recall, BASE, STORE and capture are distinct operations. Keep OperationGate for complete operations, driver serialization for exchanges, preflight, backups, readback and no automatic retry after an uncertain write.

The public copy keeps product behavior. Three HelpWindow default paths have been made portable, original manufacturer artwork and branded images have been excluded, and the repository includes project-created illustrations for AMP, CAB, DST, EQ, NR and WAH. Project metadata and runtime license documents identify GPL-3.0-only and Goaltoday. No firmware, DSP, protocol bytes or audio algorithms have been modified for publication.
