# Instrument sample export

How to export Heartopia instrument WAVs with **correct noteId ↔ audio** binding.

## Hard rule

**Never assume `vgmstream` subsong index equals `noteId` offset or table order.**

`Musictheme_*.bnk` packs media in an arbitrary DIDX order. The game plays audio via:

```
key → Instrumenttype.notes* / halfnotes*
    → noteId
    → Musicaudio.playEeventName
    → Wwise Event (FNV-32 of event name)
    → Play Action(s)
    → Sound / container
    → DIDX media id
    → PCM stream  (= vgmstream -s N)
```

Only the last hop is what `vgmstream-cli -s` indexes. Identity maps like `subsong = noteId - 11200` (harp) or `subsong = bankNoteIds.index(noteId) + 1` are **wrong** for almost every bank.

Example (harp): key `q` → noteId `11208` → `Play_music_harp_08c` → **subsong 20**, not 8. A mislabeled export made `noteId_11220.wav` contain the real `11208` sample.

## Authoritative sources

| Binding | Source |
|---------|--------|
| `noteId` ↔ key | `Instrumenttype` (`notes15a` / `notes15b` / `notes22` + `halfnotes15`) + KeyMode / pianoSemitone layout; optional `.bin` calibration for press order |
| `noteId` ↔ Wwise event | `Musicaudio.playEeventName` (`tools/HeartopiaTables/cn_tables.db`) |
| `noteId` ↔ subsong | Parse bank HIRC: Event → Play Action → media → DIDX index (`tools/wwise_bnk.py`) |
| bank file | `tools/instrument_banks.json` → `byInstrumentType` |

Pitch / Hungarian matching may help assign **MIDI targets to keys**, but must **not** invent `noteId ↔ stream` links.

## Tools

| Script | Role |
|--------|------|
| `tools/wwise_bnk.py` | FNV Event name → ordered Play-action subsongs |
| `tools/fix_instrument_subsongs.py` | Rewrite `subsongByNoteId` / `variantSubsong` on all maps |
| `tools/audit_instrument_subsongs.py` | Compare maps vs Wwise (must be 0 bad) |
| `tools/extract_instrument_samples.py` | Decode WAVs named `noteId_NNNNN.wav` using map `subsongByNoteId` |
| `tools/build_instrument_maps.py` | Default = same as fix script; `--legacy-pitch` is unsafe |
| `tools/parse_record.py` | Calibration `.bin` → keydown noteId sequence |

## Correct export workflow

```bat
cd tools

REM 1) After game audio / table patch: refresh Wwise bindings
python fix_instrument_subsongs.py

REM 2) Verify
python audit_instrument_subsongs.py
REM expect: TOTAL ok=… bad=0 miss=0

REM 3) Export
python extract_instrument_samples.py ^
  --game-dir "C:\Program Files (x86)\Steam\steamapps\common\Heartopia" ^
  --long-only ^
  -o "%USERPROFILE%\Downloads\heartopia\samples"
```

`--long-only` exports **variant 0** from `variantSubsong` when present (first Play action), not “longest WAV”.

Single instrument:

```bat
python extract_instrument_samples.py --game-dir "..." --map harp37_map.json -o ...
python extract_instrument_samples.py --game-dir "..." --map maps/violin_15.json -o ...
```

## Map JSON fields

| Field | Meaning |
|-------|---------|
| `instrumentType` | Game `InstrumentType` enum |
| `noteIds` | Game noteIds for this layout (KeyMode order) |
| `keyByNoteId` | noteId → layout key char |
| `midiByNoteId` | noteId → target MIDI (layout), not measured pitch |
| `subsongByNoteId` | noteId → primary vgmstream index (**from Wwise**) |
| `variantSubsong` | noteId → `[sub0, sub1, …]` when Event has multiple Play actions (conch, sax, xiao, lute, …) |
| `subsongSource` | Provenance string; should mention Wwise HIRC |
| `notes22` / `halfnotes15` / `notes15a` | Copied from `Instrumenttype` when applicable |

## Variants

Some Events fire **multiple Play actions** (alternate articulations / A–B samples):

- Conch (type 22): 8 notes × 2 → 16 streams  
- Sax (type 21): 15 notes × 2 Play actions (bank also has unused streams)  
- Xiao / lute: some notes have 2 variants  

Export names:

- primary: `noteId_11251.wav` (variant 0)  
- extra: `noteId_11251_v1.wav` (unless `--long-only`)

Do not treat “stream count ÷ note count” block layout as gospel — always read HIRC.

## Calibration recordings

`.bin` under `%LocalLow%/xd/Heartopia/record/` records **noteIds the game sent**, not WAV paths.

Use them to verify **key → noteId** (press order vs `PIANO_37_KEYS` / 15a keys).  
They do **not** tell you which DIDX stream is which — that is Wwise-only.

## Anti-patterns

| Do not | Why |
|--------|-----|
| `subsong = noteId - base` | DIDX order ≠ Musicaudio id order |
| `subsong = index(noteId in sorted ids) + 1` | Same |
| Pitch-match streams to keys, then set `noteId = bankNoteIds[subsong-1]` | Assigns wrong media to noteIds |
| Keep a “listening” `variantSubsong` that disagrees with HIRC | Ear tests against mislabeled WAVs reinforce the bug |
| Re-run `build_instrument_maps.py --legacy-pitch` then export without fix | Regenerates wrong maps |

## After a game patch

1. Rebuild / refresh `cn_tables.db` if `Musicaudio` / `Instrumenttype` changed.  
2. `python fix_instrument_subsongs.py`  
3. `python audit_instrument_subsongs.py`  
4. Re-export samples.  
5. Spot-check: in-game key → noteId (record `.bin`) vs exported `noteId_*.wav` content (hash vs `vgmstream -s` of `subsongByNoteId`).
