# Instrument sample export (with metadata)

This document is the **canonical** guide for exporting Heartopia instrument performance samples as WAV files **together with machine-readable metadata**. Follow it whenever you regenerate samples after a game patch, fix a mislabeled bank, or add a new instrument map.

Related code:

| Path | Role |
|------|------|
| `tools/extract_instrument_samples.py` | Decode banks → WAVs + `manifest.json` |
| `tools/fix_instrument_subsongs.py` | Rebuild `subsongByNoteId` / `variantSubsong` from Wwise |
| `tools/reseed_instrument_maps.py` | Rebuild `noteIds` / `keyByNoteId` / `midiByNoteId` from `Instrumenttype` |
| `tools/audit_instrument_subsongs.py` | Verify maps against Wwise **and** `Instrumenttype` (must be zero failures) |
| `tools/wwise_bnk.py` | Event FNV → Play Action → DIDX → vgmstream subsong |
| `tools/instrument_banks.json` | `instrumentType` → bank filename, or a list for one-bank-per-note types |
| `tools/instrument_bank_index.py` | Resolve which bank holds a given `noteId` |
| `tools/*_map.json`, `tools/maps/*.json` | Per-instrument note/key/MIDI/subsong maps |
| `tools/HeartopiaTables/cn_tables.db` | `Musicaudio`, `Instrumenttype` design tables |
| `tools/parse_record.py` | In-game `.bin` calibration (key → noteId only) |

---

## 1. Goal

Produce a folder of WAV files where:

1. Each file is the **exact PCM** the game plays for a given `noteId` (via the matching Wwise Event).
2. The filename encodes `noteId` (and optional variant index).
3. A sidecar **`manifest.json`** lists every file with full metadata: instrument type, bank, map, subsong, noteId, variant, layout key, target MIDI, label, relative path.

Consumers (Auto MIDI player sample packs, listening QA, re-import tools) must trust **`noteId` + `manifest.json`**, not “file order” or “subsong number in the name.”

---

## 2. Hard rules (read before exporting)

### 2.1 Never invent `noteId ↔ stream` from arithmetic

**Wrong:**

```text
subsong = noteId - 11200          # harp myth
subsong = bankNoteIds.index(id)+1 # “table order = bank order” myth
subsong = stream_count // n_notes block layout
```

Wwise packs media into `DIDX` in an **arbitrary** order. `vgmstream-cli -s N` indexes that order. It is **not** Musicaudio id order and **not** Instrumenttype array order.

**Worked counter-example (harp, type 6):**

| In-game | Value |
|---------|--------|
| Key | `q` |
| `Instrumenttype.notes22[0]` | `11208` |
| `Musicaudio.playEeventName` | `Play_music_harp_08c` |
| Correct vgmstream subsong | **20** |
| Incorrect identity map | 8 |

If you export with the identity map, the WAV named `noteId_11220.wav` may contain the audio that actually belongs to noteId `11208`. Ear tests then “confirm” the wrong labels and poison later maps.

### 2.2 Separate three bindings

| Binding | Authoritative source | What it is **not** |
|---------|----------------------|--------------------|
| **key ↔ noteId** | `Instrumenttype` (`notes15a` / `notes15b` / `notes22` + `halfnotes15`) + KeyMode / `pianoSemitone`; optional `.bin` calibration | Pitch of a WAV |
| **noteId ↔ Wwise event** | `Musicaudio.playEeventName` | Guessed event name from noteId |
| **noteId ↔ vgmstream subsong** | Bank HIRC: Event (FNV-32 of event name) → Play Action(s) → Sound → DIDX media id → stream index (`tools/wwise_bnk.py`) | `noteId` arithmetic, Hungarian pitch assign |

Pitch / autocorrelation may help choose **target MIDI numbers for keys** (layout). It must **never** decide which stream is which `noteId`.

### 2.3 Calibration `.bin` files do not fix export

Path pattern:

```text
%LocalLow%\xd\Heartopia\record\<uid>\*_*.bin
```

A recording stores **noteIds the game sent** on keydown/keyup. Use it to verify **key → noteId**. It does **not** tell you which DIDX stream is which. Stream binding is Wwise-only.

### 2.4 Do not re-run unsafe map builders before export

`python build_instrument_maps.py --legacy-pitch` remaps via stream order / pitch matching and **breaks** `noteId ↔ media`. Default `build_instrument_maps.py` (no flag) only refreshes Wwise bindings (same as `fix_instrument_subsongs.py`).

A legacy run damages **two** bindings, and repairing one does not repair the other:

| Damaged by `--legacy-pitch` | Repaired by |
|---|---|
| `subsongByNoteId` / `variantSubsong` | `fix_instrument_subsongs.py` |
| `noteIds` / `keyByNoteId` / `midiByNoteId` / `notes22` / `halfnotes15` | `reseed_instrument_maps.py` |

**After any legacy rebuild run both**, then audit.

This was a real, long-lived failure: `piano37_map.json`, `piano2row_map.json` and the conga/cajon maps sat in the tree with a scrambled key space while the subsong audit reported `bad=0 miss=0`, because the audit only knew about the stream binding. Every piano key resolved to the wrong sample in downstream packs. Section 5 Step B now covers the key space too.

---

## 3. End-to-end play pipeline (game → WAV)

```text
Player key (e.g. q)
    │
    ▼
Instrument UI / KeyMode / pianoSemitone
    │  picks slot in notes15a | notes15b | notes22+halfnotes15
    ▼
noteId  (int, e.g. 11208)
    │
    ▼
TableData.GetMusicaudio(noteId).playEeventName
    │  e.g. Play_music_harp_08c
    ▼
AudioManager.PlaySound(eventName)
    │
    ▼
Wwise Event short ID = FNV-32(lowercase event name)
    │
    ▼
HIRC Event → one or more Play Actions (atype 4)
    │  each action targets a Sound / Random / Actor-Mixer tree
    ▼
Leaf Sound references a DIDX media id
    │
    ▼
DATA chunk PCM  ←→  vgmstream stream index N (1-based, DIDX order)
    │
    ▼
export: noteId_<noteId>.wav   (+ optional _vK for later Play actions)
manifest row: { noteId, subsong: N, key, midi, … }
```

Stop events (`stopEventName`) are ignored for sample export.

---

## 4. Prerequisites

1. **Heartopia install** with banks under:

   ```text
   <HeartopiaDir>\xdt_Data\StreamingAssets\Audio\GeneratedSoundBanks\Windows\
   ```

2. **`vgmstream-cli` on PATH**  
   Example: `winget install vgmstream.vgmstream`

3. **Python 3.10+** with repo tools on disk (`tools/`).

4. **Design tables DB** (for fix/audit):

   ```text
   tools/HeartopiaTables/cn_tables.db
   ```

   Must contain current `Musicaudio` and `Instrumenttype` rows. After a content patch, regenerate tables before fixing maps (see project table-export docs / `export-tables` skill).

5. **Maps present** under `tools/maps/*.json` and `tools/*_map.json`, with correct `noteIds` / `keyByNoteId` from design tables (not only subsongs).

---

## 5. Correct export workflow (checklist)

Run from the repo (PowerShell or cmd). Prefer an absolute `--game-dir`.

### Step A — Refresh Wwise bindings on every map

```bat
cd tools
python fix_instrument_subsongs.py
```

This walks every map that has an entry in `instrument_banks.json`, loads `playEeventName` for each `noteId` from `cn_tables.db`, parses the bank HIRC, and writes:

- `subsongByNoteId` — primary stream (first Play action)
- `variantSubsong` — all Play-action streams when count &gt; 1
- `subsongSource` — provenance string
- clears obsolete ear-only `listeningMapSource` overrides that disagree with HIRC

### Step B — Audit (mandatory gate)

```bat
python audit_instrument_subsongs.py
```

**Required result:**

```text
TOTAL ok=<N> bad=0 miss=0 keybad=0
```

Two independent checks run per map:

| Counter | Compares | Against |
|---------|----------|---------|
| `bad` / `miss` | `subsongByNoteId`, `variantSubsong` | Wwise HIRC Event → Play Action → DIDX |
| `keybad` | `noteIds`, `keyByNoteId`, `notes22`, `halfnotes15` | `Instrumenttype` |

If any counter is non-zero, **do not export**. A `keybad` hit prints the exact disagreement per note:

```text
KEY  BAD  white noteId=10008 table_key='q' map_key='p'
```

Fix with `python reseed_instrument_maps.py` (rebuilds the key space from `Instrumenttype`), then re-run Step A and Step B.

### Step C — Extract WAVs + metadata manifest

**Full set (recommended for shipping sample packs):**

```bat
python extract_instrument_samples.py ^
  --game-dir "C:\Program Files (x86)\Steam\steamapps\common\Heartopia" ^
  --long-only ^
  -o "%USERPROFILE%\Downloads\heartopia\samples"
```

**Single instrument:**

```bat
python extract_instrument_samples.py ^
  --game-dir "C:\...\Heartopia" ^
  --map harp37_map.json ^
  --long-only ^
  -o "%USERPROFILE%\Downloads\heartopia\samples"
```

```bat
python extract_instrument_samples.py ^
  --game-dir "C:\...\Heartopia" ^
  --map maps/violin_15.json ^
  --long-only ^
  -o "%USERPROFILE%\Downloads\heartopia\samples"
```

### Step D — Spot-check (strongly recommended)

1. Pick a known key (e.g. harp `q` → noteId `11208`).
2. Read `subsongByNoteId["11208"]` from the map (e.g. `20`).
3. Decode independently:

   ```bat
   vgmstream-cli -s 20 -o _probe.wav "<HeartopiaDir>\...\Musictheme_harp.bnk"
   ```

4. Compare MD5/SHA of `_probe.wav` to `noteId_11208.wav` in the export folder — must match.
5. Optional: record a `.bin` while pressing that key; confirm the recording’s noteId equals the filename’s noteId (key binding), independent of the WAV hash check (stream binding).

---

## 6. CLI reference (`extract_instrument_samples.py`)

| Argument | Required | Meaning |
|----------|----------|---------|
| `--game-dir` | one of game/banks | Heartopia install root (contains `xdt_Data`) |
| `--banks-dir` | one of game/banks | Override `.../GeneratedSoundBanks/Windows` |
| `-o` / `--output` | yes | Output root directory |
| `--map` | no (repeatable) | One map path; default = all `maps/*.json` + `*_map.json` |
| `--long-only` | no | Export only variant **0** per noteId when `variantSubsong` exists |
| `--all-musictheme` | no | Also dump every `Musictheme_*.bnk` as raw `subsong_NNN.wav` (no noteId metadata) |
| `--include-perform` | no | Also dump `music_perform_*.bnk` raw |

### What `--long-only` means

Despite the historical name, **`--long-only` does not pick the longest WAV**.

When `variantSubsong` is present, it exports **variant index 0** only (first Play action on the Event). That is the primary performance sample used for metadata packs.

Without `--long-only`, every variant is written:

- `noteId_11251.wav` — variant 0  
- `noteId_11251_v1.wav` — variant 1  
- …

---

## 7. Output layout

```text
<output>/
  manifest.json
  type01_piano37_map/
    noteId_10005.wav
    noteId_10008.wav
    ...
  type06_harp37_map/
    noteId_11201.wav
    noteId_11208.wav
    ...
  type21_saxophone_15/
    noteId_11183.wav
    ...
  type22_angaria_delphinus_8/
    noteId_11251.wav
    ...
```

### Folder naming

```text
type{instrumentType:02d}_{map_stem}/
```

Examples: `type06_harp37_map`, `type20_violin_15`.

### WAV naming

| Case | Filename |
|------|----------|
| Primary sample for a noteId | `noteId_<id>.wav` |
| Additional variant K ≥ 1 | `noteId_<id>_vK.wav` |
| Unmapped raw stream (rare / raw dump) | `typeTT_subsongSSS.wav` or `subsong_SSS.wav` |

The **noteId in the filename is the game id**, not the subsong index.

---

## 8. Metadata: `manifest.json`

Written next to the sample folders. One JSON array; each element is one exported file.

### 8.1 Field schema

| Field | Type | Description |
|-------|------|-------------|
| `instrumentType` | int | Game `InstrumentType` enum (see §10) |
| `map` | string | Source map filename (e.g. `harp37_map.json`) |
| `bank` | string | Wwise bank filename (e.g. `Musictheme_harp.bnk`) |
| `subsong` | int | 1-based vgmstream stream index used for decode |
| `noteId` | int \| null | Game Musicaudio / Instrumenttype note id; null for raw dumps |
| `variant` | int | 0 = primary; 1+ = later Play-action variants |
| `midi` | int \| null | **Layout target MIDI** from the map (`midiByNoteId` / `midi[]`), not measured pitch |
| `key` | string \| null | Layout key character (`q`, `2`, `,`, …) from `keyByNoteId` / `keys[]` |
| `label` | string | Human label from the map (`label`) |
| `wav` | string | Path relative to output root, forward slashes |

### 8.2 Example rows

Harp, key `z`, noteId `11201`:

```json
{
  "instrumentType": 6,
  "map": "harp37_map.json",
  "bank": "Musictheme_harp.bnk",
  "subsong": 3,
  "noteId": 11201,
  "variant": 0,
  "midi": 60,
  "key": "z",
  "label": "Heartopia:Harp (37 Key)",
  "wav": "type06_harp37_map/noteId_11201.wav"
}
```

Saxophone, key `q`, noteId `11183` (primary variant only under `--long-only`):

```json
{
  "instrumentType": 21,
  "map": "saxophone_15.json",
  "bank": "Musictheme_sax.bnk",
  "subsong": 14,
  "noteId": 11183,
  "variant": 0,
  "midi": 72,
  "key": "q",
  "label": "Saxophone (15-key)",
  "wav": "type21_saxophone_15/noteId_11183.wav"
}
```

### 8.3 How consumers should use metadata

1. Prefer **`noteId`** as the stable join key to game tables and recordings.  
2. Use **`key` + `midi`** for keyboard / piano-roll UIs (layout space).  
3. Use **`subsong` + `bank`** only to re-decode or verify hashes against the live install.  
4. Treat **`midi` as nominal layout pitch**. Actual acoustic pitch of the WAV can differ; do not “correct” filenames by re-pitching into noteId space.  
5. When multiple maps share a bank (e.g. `piano37_map.json` and `piano2row_map.json`, both `Musictheme_piano.bnk`), the same noteId appears once per map — filter by `map` or `instrumentType` as needed. Two maps of the *same* key mode on one bank are pure duplication; see §10.

### 8.4 Joining manifest → design tables

```text
manifest.noteId  →  Musicaudio.id
                 →  playEeventName / stopEventName

manifest.instrumentType + key layout
                 →  Instrumenttype row (notes15a / notes22 / …)
```

---

## 9. Map JSON metadata (input to the exporter)

Maps live in `tools/maps/` and as top-level `tools/*_map.json`. The exporter reads them; fix/audit rewrite only the Wwise-derived fields.

### 9.1 Core fields

| Field | Required | Meaning |
|-------|----------|---------|
| `label` | recommended | Display name |
| `instrumentType` | **yes** | Selects bank via `instrument_banks.json` |
| `noteIds` | **yes** (or `bankNoteIds` for piano/harp) | Ordered list of noteIds for this layout |
| `bankNoteIds` | optional | Full bank noteId set when the map is a subset |
| `keyByNoteId` | strongly recommended | `"11208": "q"` — drives manifest `key` |
| `midiByNoteId` | strongly recommended | `"11208": 72` — drives manifest `midi` |
| `keys` / `midi` | alt for simple 15-key maps | Parallel arrays in KeyMode15a order |
| `subsongByNoteId` | **yes for correct export** | `"11208": 20` — from Wwise, not arithmetic |
| `variantSubsong` | when Event has ≥2 Play actions | `"11251": [14, 11]` |
| `variantsPerNote` | optional | Max variant count hint |
| `bankByNoteId` | one-bank-per-note types only | `"11268": "instrument_ocarina_08c.bnk"` — set by fix script from HIRC |
| `subsongSource` | set by fix script | Provenance (must mention Wwise HIRC) |

### 9.2 Layout-specific fields

| Layout | Extra fields | Typical instruments |
|--------|--------------|---------------------|
| KeyMode15a (15 keys) | `keys`, `midi`; noteIds = `Instrumenttype.notes15a` | lute, violin, sax, xiao, … |
| KeyMode22 + pianoSemitone (37 keys) | `whiteKeys`, `blackKeys`, `notes22`, `halfnotes15`, `pianoSemitone: true` | piano, harp |
| 8-key | `gameKeys` / `keys`; often `calibrationNoteIds` | conga, cajon, boomwhackers, conch |

### 9.3 Standard 15-key layout (KeyMode15a)

Keys (top then bottom row):

```text
q w e r t y u i
a s d f g h j
```

Nominal MIDI (white-key major layout used by maps):

```text
72 74 76 77 79 81 83 84
60 62 64 65 67 69 71
```

`noteIds` for a 15-key instrument **must** be `Instrumenttype.notes15a` for that type (not a pitch-sorted permutation). Broken maps (duplicate noteIds, fewer than 15 unique ids) produce wrong filenames even if Wwise subsongs are fixed — restore from `cn_tables.db` first.

### 9.4 Piano / harp 37-key layout (KeyMode22 + pianoSemitone)

Chromatic QWERTY order used by calibration (`PIANO_37_KEYS` in `parse_record.py`):

```text
q 2 w 3 e r 5 t 6 y 7 u i
z s x d c v g b h n j m
, l . ; / o 0 p - [ = ]
```

- White keys → `notes22` (22 ids)  
- Black keys → `halfnotes15` (15 ids)  
- `keyByNoteId` / `midiByNoteId` cover all 37  

Harp noteIds are `11201`–`11237`. Piano uses `10001`–`10022` and sharp ids `10201`–`10215` (see map / Musicaudio).

---

## 10. Instrument types and banks

From `tools/instrument_banks.json` (banks relative to `GeneratedSoundBanks/Windows`):

| Type | Name (game) | Bank | Typical map |
|------|-------------|------|-------------|
| 1 | Piano | `Musictheme_piano.bnk` | `piano37_map.json`, `piano2row_map.json` |
| 2 | Conga | `Musictheme_congaBongos.bnk` | `maps/conga_8.json` |
| 3 | Cajon | `Musictheme_cajon.bnk` | `maps/cajon_8.json` |
| 4 | BaYinTong / BoomWhackers | `Music_BoomWhackers.bnk` | `maps/bayintong_8.json` |
| 5 | Ethereal drum (hang) | `Musictheme_hang.bnk` | `maps/ethereal_drum_15.json` |
| 6 | Harp | `Musictheme_harp.bnk` | `harp37_map.json` |
| 11 | Lute | `Musictheme_lunghe.bnk` | `maps/lute_15.json` |
| 12 | Wooden bass | `Musictheme_acousticBass.bnk` | `maps/wooden_bass_15.json` |
| 13 | Recorder | `Musictheme_sopranoRecorder.bnk` | `maps/recorder_15.json` |
| 14 | Concertina | `Musictheme_concertina.bnk` | `maps/concertina_15.json` |
| 15 | Bamboo xiao | `Musictheme_xiao.bnk` | `maps/bamboo_xiao_15.json` |
| 16 | Mbira / Kalimba | `Musictheme_Kalimba.bnk` | `maps/mbira_15.json` |
| 17 | Lyre | `Musictheme_lyre.bnk` | `maps/lyre_15.json` |
| 18 | Bagpipe | `Musictheme_bagpipes.bnk` | `maps/bagpipe_15.json` |
| 19 | Cello | `Musictheme_cello.bnk` | `maps/cello_15.json` |
| 20 | Violin | `Musictheme_violin.bnk` | `maps/violin_15.json` |
| 21 | Saxophone | `Musictheme_sax.bnk` | `maps/saxophone_15.json` |
| 22 | Angaria delphinus / conch | `Musictheme_conchShells.bnk` | `maps/angaria_delphinus_8.json` |
| 23 | Ocarina | `instrument_ocarina_01c.bnk` … `15c.bnk` (**15 banks**) | `maps/ocarina_15.json` |

### One bank per note (type 23)

The ocarina is the one instrument that does **not** pack its notes into a single
`Musictheme_*.bnk`. It ships fifteen banks, one per note, each holding a single stream, and
its events are named `Play_instrument_ocarina_NNc` rather than `Play_music_*`.

Its `instrument_banks.json` entry is therefore a **list** of candidate banks, and its map
carries `bankByNoteId`. `fix_instrument_subsongs.py` fills that in by resolving each Event
through HIRC in every candidate and recording the bank that answered — never by matching
the number in the filename to the noteId. The numbering does line up today, but relying on
that is the same assumption §2.1 forbids.

Everything downstream is unchanged: `subsongByNoteId` is `1` for every note, the key layout
is an ordinary KeyMode15a, and the manifest's `bank` field names that note's own bank.

`music_perform_*.bnk` files are start/end stingers, not per-note kits. Export them only with `--include-perform` as raw streams (no noteId metadata).

### Manifest labels

The `label` a map carries is what consumers join on, so it must name the key mode the instrument really has. Conga (2) and Cajon (3) fill `notes8` and leave `notes15a` empty — they are 8-key instruments and their labels say `(8 Key)`. They were labelled `(2 Row)` until 2026-09, which made Auto MIDI Player look up a 15-key layout against 8 exported keys.

`conga2row_map.json` and `cajon2row_map.json` were leftovers of that era — same `instrumentType`, `noteIds`, label and subsongs as `maps/conga_8.json` / `maps/cajon_8.json`, exporting a second folder of byte-identical audio. Both were deleted in 2026-09 along with their `type02_conga2row_map` / `type03_cajon2row_map` folders. A bank should carry one map per key mode; a second map of the same mode only duplicates manifest rows.

---

## 11. Variants (multiple Play actions)

Some Events register **two (or more) Play actions**, each pointing at different media. The fix script stores them in order as `variantSubsong[noteId] = [sub0, sub1, …]`.

Known multi-variant kits (may change after patches — trust audit/HIRC):

| Instrument | Notes | Variants per note (typical) |
|------------|-------|------------------------------|
| Conch (22) | 8 | 2 |
| Sax (21) | 15 | 2 |
| Xiao (15) | some notes | 1–2 |
| Lute (11) | some notes | 1–2 |

**Metadata rules:**

- Variant 0 → `noteId_N.wav`, `variant: 0` in manifest  
- Variant K → `noteId_N_vK.wav`, `variant: K`  
- `--long-only` → only variant 0  

Do not assume `stream_count == notes * variants` with contiguous blocks. Sax’s bank may contain unused streams beyond HIRC Play targets.

---

## 12. How Wwise resolution works (`wwise_bnk.py`)

1. Read bank chunks: `BKHD`, `DIDX`, `DATA`, `HIRC`.  
2. Build `mediaId → subsong` from DIDX order (1-based).  
3. Parse HIRC objects (Sound=2, Action=3, Event=4, …).  
4. Compute `wwise_fnv32(playEeventName.lower())` → Event object id.  
5. For each **Play** action (action type 4) on that Event, walk the target graph to leaf Sounds, collect unique media ids, map to subsongs.  
6. Preserve Play-action order → variant list.

If an Event cannot be resolved, audit reports `MISS` — usually stale `Musicaudio` names or a wrong bank file.

---

## 13. After a game patch

1. Update / regenerate `tools/HeartopiaTables/cn_tables.db` (`Musicaudio`, `Instrumenttype`).  
2. Confirm banks exist under `GeneratedSoundBanks/Windows` (names in `instrument_banks.json`).  
3. If `Instrumenttype.notes*` arrays changed: `python reseed_instrument_maps.py` (never hand-edit from pitch).  
4. `python fix_instrument_subsongs.py`  
5. `python audit_instrument_subsongs.py` → `bad=0 miss=0 keybad=0`  
6. Re-run `extract_instrument_samples.py --long-only -o …`  
7. Spot-check one key per changed bank (hash vs `vgmstream -s`).  
8. Optionally re-record a short `.bin` calibration if key layouts changed (see §16).

---

## 14. Anti-patterns

| Do not | Why |
|--------|-----|
| Name WAVs by subsong and pretend the number is noteId | Consumers will join the wrong Musicaudio row |
| Set `subsong = noteId - base` | Fails on every current theme bank |
| Pitch-match streams → keys, then set `noteId = bankNoteIds[subsong-1]` | Scrambles media under the wrong ids |
| Trust ear tests against a previously mislabeled export | Confirms the bug |
| Keep `listeningMapSource` / hand `variantSubsong` that disagree with HIRC | Same |
| Export without audit after editing maps | Silent wrong packs |
| Use `--legacy-pitch` then ship samples | Regenerates broken bindings |
| Treat `--long-only` as “longest duration wins” | It selects variant 0 only |
| Use measured WAV pitch to rewrite `midiByNoteId` into noteId space | MIDI in maps is **layout** metadata |
| Treat a clean subsong audit as proof the map is correct | It says nothing about the key space — check `keybad` too |

---

## 15. Quick command card

```bat
cd tools

python reseed_instrument_maps.py
python fix_instrument_subsongs.py
python audit_instrument_subsongs.py

python extract_instrument_samples.py ^
  --game-dir "C:\Program Files (x86)\Steam\steamapps\common\Heartopia" ^
  --long-only ^
  -o "%USERPROFILE%\Downloads\heartopia\samples"
```

Success criteria:

- Audit: `bad=0 miss=0 keybad=0`  
- Output: per-instrument folders of `noteId_*.wav`  
- `manifest.json` present with `noteId`, `key`, `midi`, `subsong`, `bank`, `map`, `variant`, `wav` on every performance sample  
- Spot-check: `noteId_X.wav` MD5 == `vgmstream-cli -s <subsongByNoteId[X]>` from the live bank  

---

## 16. Verifying the key ↔ noteId binding

The audit compares maps to `Instrumenttype`, which is authoritative — but if you ever need to confirm the table itself against the running game, only one method involves no inference.

### Gold standard: in-game calibration recording

1. Sit at the instrument in game, in the key mode you are checking.
2. Start recording, press the keys **in layout order with pauses**, stop.
3. Find the `.bin` under `%LocalLow%\xd\Heartopia\record\<uid>\`.

`parse_record.py` takes a subcommand — the bare `parse_record.py <file>` form is rejected.

**Full sweep** — press *every* key of the layout in order. `calibrate` then prints the whole slot → key → MIDI → noteId table and can save it as JSON:

```bat
python parse_record.py calibrate -i "<path to .bin>"
python parse_record.py calibrate -i "<path to .bin>" -m mymap.json
```

It only accepts a complete layout (37 keydowns for piano, or exactly the preset length for the instrument's key mode) and errors out otherwise — a partial recording is not a calibration.

**Partial spot-check** — a few keys only. `info` gives the summary; use `--events` for the per-press noteIds:

```bat
python parse_record.py info --events "<path to .bin>"
```

Compare the keydown sequence with `Instrumenttype.notes22` (or `notes15a` / `notes8`) sliced to the keys you pressed.

### Cheap cross-check: measured pitch of the exported WAVs

For melodic instruments, decoding the WAVs in table order must produce an ascending scale. A **uniform** offset is fine and common — the soprano recorder, cello (−24), wooden bass (−24), lyre / sax / lute (−12) all sound away from their nominal layout MIDI, and §8.3 rule 4 allows it. What indicates damage is a **non-uniform spread**: a scrambled map shows offsets scattered across two octaves.

Do not run this on percussion (conga, cajon, BaYinTong) — pitch estimation on drums is meaningless and will report false scrambles.

### What each source can and cannot settle

| Question | Settled by |
|---|---|
| Which stream is this noteId? | Wwise HIRC only |
| Which noteId does this key send? | `Instrumenttype`, confirmed by `.bin` recording |
| Does this WAV sound like its layout MIDI? | Pitch measurement — **evidence, never an assignment input** |

---

*Last aligned with Wwise HIRC-based `subsongByNoteId` repair and `extract_instrument_samples.py` manifest schema. After major audio or table patches, re-run fix + audit before publishing sample packs.*
