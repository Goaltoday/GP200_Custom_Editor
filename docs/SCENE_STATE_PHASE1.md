# Scene and preset state: phase 1

## Transitions observed before this change

| Event | Active PRST | Editing baseline and history | Scene pending flag |
| --- | --- | --- | --- |
| Connect or select preset | Read from device | Set to active snapshot; clear history | Initially clear |
| Edit a parameter | Write and read back | Keep previous baseline; add undo entry | Unchanged |
| Capture Scene in editor | Read back active PRST | Keep baseline and history | Set |
| Recall Scene in editor | Read back active PRST | Replaced baseline with recalled sound; cleared history | Unchanged |
| Physical Scene change detected by polling | Read back active PRST | Replaced baseline with recalled sound; cleared history | Unchanged |
| STORE in editor | Read stored PRST | Replace baseline | Clear after service confirmation |
| Physical STORE detected by polling | Read stored PRST | Accept only if Scene flag clear | Could not clear Scene flag |

The recall and physical Scene transitions could erase the editor's evidence of an
unsaved PRST edit. A stored PRST read cannot prove that the persisted SC10 bank
matches a captured Scene.

## Phase 1 behavior

Scene recall and physical Scene changes update the active document but retain the
editing baseline and history. This preserves the pending indicator when the
active sound still differs from the previously confirmed baseline. A physical
STORE can confirm the PRST when no Scene capture is pending. When a capture is
pending and the stored PRST matches, the UI reports that only PRST matches and
keeps the Scene pending flag. The editor does not claim persistent SC10
verification from a volatile Scene snapshot.

The firmware's current USB Scene snapshot describes the active bank in RAM.
Verifying persisted SC10 would require an explicit reload and comparison that
temporarily changes the active preset. This phase does not perform that operation
automatically. Physical Scene capture performed outside the editor, without a
status change, is not detected as a pending capture by this state model.
