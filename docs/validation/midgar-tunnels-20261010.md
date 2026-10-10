# Late Midgar tunnel navigation: native evidence

The tester's Steam x64 `(13)` log contains the exact 538,327,632-byte prefix of
`(11)`. This investigation uses its 32,621,743 appended bytes. The full log's
SHA-256 is `282565c7420581c634dc6bfdb54fe2ba20894142f82fb40c21c6c2dfcebcd0b6`.
During chapter 1601, Story was empty in `sbwy4_22` (735), `tunnel_4` (736)
and `tunnel_5` (737). The prior catalog omitted 735 and limited the latter
screens to chapter 1602 and two section values.

## Native route

The installed legacy and Steam field archives agree on the relevant script
bytes. Read-only Ghidra decompilation of the existing native engine project
confirmed the field opcode table, byte-bank mapping, INC/DEC and LADER operands.
The [FF7 tools source](https://github.com/cebix/ff7tools/blob/master/ff7/field.py)
provides the opcode format reference; installed archive bytes govern the rows.

- 733's catwalk jump loads 735 on triangle 9. Cloud's Main starts LADER itself;
  its ordinary Up input completes the descent and loads 778 with section 4.
  Story gives manual ladder guidance and starts no walking route here.
- Bank 15 byte 129 selects the reused tunnel section. Upward passages decrement
  it and downward passages increment it. Section 4 is 778, other even sections
  use 736 and odd sections use 737. Section 1's upper-left passage reaches
  section 0, which uses the downward return. Section 3's upper-left passage loads bridge
  field 738. Story now covers both chapter 1601 and 1602 visits.
- Before the encounter, Story returns from optional sections toward section 4.
  Afterwards it returns toward section 3 and the bridge. Invalid section values,
  the wrong screen parity and disabled native lines provide no guessed target.
- 778's encounter bit (bank 15 byte 128, bit 2) is set after either fighting or
  declining. The upward Story row requires this bit and section 4. The mod does
  not choose an encounter response or write the section or progression state.

The regenerated catalog has 1,449 rows, four more than its predecessor. Only
735, 736, 737 and 778 changed; all other rows and their cycling order match.
The generator and shipped catalog contain the same reviewed rows.

## Objects audit

All eleven pickups and both save points in 733-737/778 were already catalogued
correctly. No object production change was needed. The native scripts place
W-Item and its save point in section 18, and four tunnel chests in sections 13,
15 and 17. Objects uses current native model visibility, Talk state and collected
flags. It does not expose pickups in other sections. 735 and 778 grant no items.
Empty Objects reads alone do not establish an exact native section value.

`MidgarTunnelObjectTests` pins every grant and collected flag to installed
scripts, verifies native visibility and opened-chest filtering, and covers
the absence of invented pickups. `MidgarTunnelStoryTests` reproduces the old
empty Story lists and covers every valid section, encounter state, manual
ladder input, disabled/invalid state and real routes from native MAPJUMP arrivals.
Both test hosts run this coverage with `--midgar-tunnels-only` and in their
full suites. Focused checks pass on both installed archives. Review identified
the reachable section 0; its added regression failed before the return row was
expanded to include it.

## Release verification boundaries

The release workflow runs dual-runtime Research gates, installed game-data
integration, native parity and four full suites against exact packaged DLLs.
Results, independent review, package digests and protected-data manifests are
retained under `C:\Users\buu42\Documents\FFVII-ActiveBuild\release-0.9.0-20261010`.
Native investigation evidence and focused logs are under the adjacent
`midgar-tunnels-20261010` directory.

These are automated and static checks. Live traversal, physical controller
delivery and listening tests have not been performed for this repair. The
previously documented Wonder Square route-search timeout is outside this scope.
