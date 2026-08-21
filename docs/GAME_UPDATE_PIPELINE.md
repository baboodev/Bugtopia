# Post-Update Pipeline — decrypt, decompile, diff

What to run after Heartopia updates, and how to read the result. Two independent
pipelines, one per artifact kind:

| | Pipeline A — **code** | Pipeline B — **data** |
|---|---|---|
| Input | `%LocalLow%/xd/Heartopia/DotnetAssemblies/*.dll` (XDENCODE) | `%LocalLow%/xd/Heartopia/AssetBundle/*cn.ab` |
| Output | `ilspy-dumps/<Module>/**.cs` | `tools/HeartopiaTables/cn_tables.db` |
| Driver | `tools/gameupdate/hcode.py` | `tools/gameupdate/htablediff.py` |
| Skill | `decompile-assemblies` | `export-tables` |

**Order matters.** Row schemas are parsed live from `ilspy-dumps/EcsClient/Table*.cs`,
so a code update must be decompiled and promoted *before* the tables are re-decoded,
or the decode desyncs and dies mid-stream.

```bash
python tools/gameupdate/hcode.py run --write-events
```
```bash
python tools/gameupdate/htablediff.py run
```

Both write to a scratch dir (`%TEMP%/heartopia-update`, override with
`HEARTOPIA_UPDATE_WORK`) and print the path. **Do not delete it until you are done**
— it holds the only copy of the previous dump and the previous `cn_tables.db`.

---

## 0. Which kind of update is this?

Resource hotfixes and code updates arrive separately, and the answer changes what
you run.

```bash
python tools/gameupdate/hcode.py status
```

Modules sharing the newest mtime are the changed set; the rest are **controls**.

- **Module mtimes unchanged** → resource-only hotfix. Skip Pipeline A entirely and
  run `htablediff.py run --res-only` (keeps `table_code_map.json`, skips the icon index).
- **Some modules changed** → code update. Run both pipelines.
- **Every module changed** (the 2026-08-06 and 2026-08-20 case) → mtimes do not
  discriminate. Decompile everything (`--all`) and let the type-set diff find the
  real changes. `status` says so explicitly and names the smallest module as a soft
  control.

Also check `designTable.db`'s mtime in `%LocalLow%/xd/Heartopia/Others/db/` — if it
did not move, no localization work is needed at all.

---

## Pipeline A — assemblies

The gameplay code is **embedded Mono**, not IL2CPP. `GameAssembly.dll` is a shell;
the real logic lives in 15 XDENCODE-encrypted modules that decode offline — no game
launch, no runtime dump.

`hcode.py run` chains the steps below in the only safe order. Each is also a
standalone subcommand, so a failed step can be re-run without redoing the rest.

### A1. `decode` — XDENCODE → plaintext PE

Wraps `tools/XdUnpack`. Success is `SUMMARY: 15 XDENCODE decoded … 0 error(s)`;
the script fails the run on anything else.

### A2. `control` — prove ILSpy is byte-stable **before** trusting any diff

Decompiles a module that did *not* change and diffs it against the existing dump.

- **Empty diff** → your `ilspycmd` produces byte-identical output to the one that
  built `ilspy-dumps`, so every difference reported later is real signal.
- **Non-empty** → your ILSpy version differs. Formatter noise will swamp the diff.
  Regenerate **all 15** modules (`--all`), not just the changed set.

This step is why the reported diffs can be trusted at all. `run` aborts if it is
dirty unless you pass `--force`.

### A3. `decompile` — one `ilspycmd -p` per module

**Never point several DLLs at one `-o`.** That merges them into a single flat
namespace tree and breaks every grep workflow the docs assume. The script enforces
one output tree per module and runs 3 in parallel (`--jobs`); the module list is
ordered biggest-first so `XDTGameUI` (~8 MB) starts immediately instead of last.

ILSpy project mode writes **one `.cs` per top-level type** into dot-separated
namespace folders, so the file set *is* the type set — which is what makes the diff
below meaningful.

### A4. `diff` — type-set diff, taken against the OLD tree

Added/removed files = added/removed types; changed content = changed type.

```bash
python tools/gameupdate/hcode.py diff --old <work>/old --detail
```

Output has three parts:

- **Per-module `+N / -N / ~N`** with old→new type counts.
- **Removed types, in full** — removals are what break bindings, so they are never
  truncated. Each removed type name is then grepped against `buddy/`; anything that
  matches is listed with the files, for manual triage (a hit may be a comment, or
  the mod's own same-named type — check before panicking).
- **Added types grouped by namespace**, which is what names the update's feature areas.

### A5. `promote` — move the new trees in, archive the old ones

`ilspy-dumps*` is **gitignored**, so the previous tree is not recoverable from git.
Steam's `xdt_Data/StreamingAssets/DotnetAssemblies/*.dll.bytes` is *usually an older
build than the one being replaced* — it is a fallback, not a baseline. The archive
`promote` writes is the real baseline for every later per-type diff.

It also copies the **unchanged** modules into the archive, so the baseline is a
complete 15-module tree. Without that, the differential audit in A6 indexes a partial
tree and invents phantom breakages.

### A6. `bindings` — the differential mod-binding audit

`tools/audit_bindings.py` indexes every class/method/field in a dump and checks every
resolution site in `buddy/`. Run alone, its report is **~90 % pre-existing noise**:
the mod probes several candidate names per symbol on purpose, and the misses that
never resolved still look like failures.

The fix is to run it against **both** dumps and diff:

```bash
python tools/gameupdate/hcode.py bindings --old <work>/old
```

Only entries that fail against the new dump *and* passed against the old one are
regressions. On 2026-08-20 that reduced 143 raw failures to exactly **1**.

Each survivor is either a real removal or a **namespace move**. Check how the mod
resolves that symbol before calling it broken:

- resolved by **full name** (`FindAuraMonoClassByFullName("A.B.C")`) → broken, fix the string;
- resolved **off a live object** (`auraMonoObjectGetClass(obj)`) → unaffected, the
  name never mattered.

### A7. `uipaths` — the check the symbol audit cannot do

UI node paths are **string literals**. No symbol audit sees them, and the 2026-08-06
update broke 16 call sites exactly this way.

The check is **differential**, and it has to be:

- Many nodes the mod addresses are instantiated at runtime (list cells like
  `tracking_chop@list`) and appear in **no** dump. "Absent" is their normal state, so
  absence alone is not a signal — the script reports these separately as `dynamic`.
- It compares both **segments** (`root_visible@go`) and **parent/child pairs**
  (`root_visible@go/cells@t`). Pairs are the load-bearing half: the 08-06 break
  renamed a *parent* (`root_visible@go` → `root_visible@go@group`) while the segment
  itself kept existing under a different widget, so segment-only checking passes it.
  Verified: the renamed pair occurs in 0 files of the new dump, the new pair in 1.
- Pairs spanning a `(Clone)` boundary are skipped — a child widget's `_Auto` declares
  paths relative to its own root, so those pairs never appear literally.

The regression signal is: **present in the old dump, gone from the new one.**

### A8. `events` — regenerate `docs/GAME_EVENTS_LIST.md`

Scans every `struct X : … IEvent` and diffs against the committed list.

```bash
python tools/gameupdate/hcode.py events --write
```

Removed events matter more than added ones: a hook on a deleted event fails silently
(it simply never fires). The script calls this out.

**Not every "…Event" type is an `IEvent`.** The Tarot module added ~20 types named
`*Event` that are `[NetworkEvent] struct : IMessageBase` — a different channel from
the `EventCenter` dispatch the mod detours. They correctly do not appear in the list.

---

## Pipeline B — design tables

The numeric config (items, drops, fish, recipes, stores, tasks) is a custom binary
`cn.bytes` inside the AssetBundle `cn.ab`, read at runtime by `EcsClient.TableData.Init`.
`tools/HeartopiaTables/htables.py` decodes it to SQLite; `htablediff.py` wraps that
with the parts that are easy to get wrong.

### B1. `snapshot` — **before** anything overwrites the DB

Row counts alone miss edited rows, and once `cn_tables.db` is overwritten the old
content is gone. `snapshot` copies `cn_tables.db`, `table_code_map.json`,
`icon_index.tsv` and `conditional_spawns.tsv` aside first. `run` does it automatically.

### B2. `decode`

Always decodes the **LocalLow** `cn.ab`: a hotfix drops a new one there and it
**overrides** the Steam copy at runtime.

`table_code_map.json` is cached and only rebuilt when missing. It follows the table
**code**, not the resources:

- code update → the script deletes it first (codes can move — they did on 2026-08-20);
- resource-only hotfix → it is stable; pass `--res-only` to keep it.

**Three success criteria, all checked, all load-bearing:**

1. `leftover=0` — the decoder consumed the file **exactly** to EOF. This is the one
   that matters: a mis-decode can still pass the trailing-code sanity test.
2. A plausible table count and row total.
3. Zero row-read failures in the trace.

### B3. When the decode fails

Two distinct failure shapes:

**Schema skew** — a ctor reads a field the shipped binary does not have (the `cn.ab`
is an earlier sub-build than the decompiled code). Add an entry to `SCHEMA_OVERRIDES`
in `cn_bytes_decode.py`. Only `TableCooker` (`_cookerType` is a `Byte`, not
`ReadUInt16`) is permanently overridden.

**Unknown opcode** — `ValueError: unknown read 'X' in: data.ReadX()`. The update
introduced a `BinaryReader` op the schema parser has never seen. This happened on
2026-08-20 with **`ReadUInt64`** (the Hot-Air-Balloon tables store `wayId` as an
8-byte unsigned). Fix, permanently, in two files:

```python
# tools/HeartopiaTables/cn_schema.py
PRIM = {..., "UInt64"}
PRIM_BYTES = {..., "UInt64": 8}
```
```python
# tools/HeartopiaTables/cn_bytes_decode.py
def u64(self): return struct.unpack_from("<Q", self.b, self._adv(8))[0]
SCALAR = {..., "UInt64": lambda r: r.u64()}
```

Find which tables need it with
`grep -rl 'ReadUInt64()' ilspy-dumps/EcsClient/Table*.cs`.

**Diagnosing either:** the failing table named in the trace may be the one **after**
the real culprit — step back one `block@0xHEX` and decode row 0 op-by-op against the
ctor. Console prints of Chinese names crash on cp1252, so start such scripts with
`sys.stdout.reconfigure(encoding="utf-8", errors="replace")` (`_common.py` does it).

### B4. `diff` — table-level

```bash
python tools/gameupdate/htablediff.py diff --old <work>/cn_tables_old.db
```

Reports added / removed / changed tables with row deltas, split into **grew**,
**SHRANK** (always inspect — a table dropping to 0 rows is either a content removal
or a decode problem) and **edited in place at the same row count** (68 tables on
2026-08-20 — pure row counting would have missed every one).

A `Table*.cs` **rename** shows up as one removal + one addition with identical row
counts, e.g. `AnimalBehaviacConf` → `AnimalBehaviourConf` (7 rows both sides).

It also reports how many "changed" tables differ **only by expression-pool
renumbering** — see below.

### B5. `rows` — row-level, for one table

```bash
python tools/gameupdate/htablediff.py rows --old <old.db> --table StoreGroup
```

Prints added/removed keys, per-column before→after for edited rows, and a
`columns edited most` line that usually names the intent of the patch in one glance.

**Expression-pool churn.** Condition columns embed the row's slot in a shared pool as
`{"idx": N, "expr": "…"}`. Inserting one new expression renumbers every slot after
it, so thousands of rows "change" with no condition actually differing. The script
blanks `idx` before comparing by default. On 2026-08-20 this took `StoreGroup` from
**3029 "edited" rows down to 14 real ones** — which turned out to be 5 price changes
rolling back the increases from 2026-08-06. Pass `--raw` to see the unfiltered diff.

If the table's schema changed, the script says which columns appeared/disappeared and
compares only the shared ones.

### B6. `names` — resolve ids to something human

```bash
python tools/gameupdate/htablediff.py names --old <old.db> --table Fish,Insect,Bird
```

`cn_tables.db` stores the Chinese string inline; `designTable.db` carries the
translations under the same `zhHans` key (XOR'd with `SecureStorage.Key`, rotated by
the row's primary key — implemented in `_common.loc_decrypt`). Two resolution paths:

1. **The table's own text column** (`name`, `description`, …) — always preferred.
2. **`Entity._name`** for tables whose key is an item id.

Order matters: a small key like `1` or `2` will happily collide with a real `Entity`
id from a completely different id space, and that cross-table match is a lie. When a
table has neither path the script says so instead of printing a wrong answer.

Row growth without new ids is normal and worth knowing: on 2026-08-20 `Fish` grew
+221 rows but only **5 new ids** — the rest were extra variant rows on existing ones.

### B7. `downstream`

| Artifact | When |
|---|---|
| `icon_index.tsv` | after a **code** update (md5 moved on 2026-08-20; byte-identical across three prior resource hotfixes, so `--res-only` skips it) |
| `heartopia_index.db` | whenever `cn_tables.db` or a Layer-A DB changed (see the `search-gamedata` skill) |
| `conditional_spawns.tsv` | after any table regen |
| `.research-record/heartopia-tables/` | the durable snapshot — md5-verified against live |

---

## Reading the results

**Real vs cosmetic.** A framework refactor (`DispatchEvent<T>(ref x)` → `(in x)`)
touches hundreds of files and means nothing. State plainly which findings are proven
public-signature changes and which are decompiler noise.

**Zero removals is the good case.** Removals break bindings; additions never do.

**Same name ≠ same type.** On 2026-08-20 `EventCenter` "changed" — but the one that
changed was the ECS-internal `XD.GameGerm.Ecs.Boost.Services.EventCenter`, while the
one the mod actually detours (`XDTGame.Core.EventCenter` in `XDTBaseService`) was
byte-identical. Always confirm *which* type by full namespace.

**Three checks, three blind spots.** The type diff sees removals but not string
literals; the binding audit sees symbols but not UI paths; `uipaths` sees UI paths but
not table schemas. Run all of them.

---

## File locations

| What | Where | Tracked? |
|---|---|---|
| Encrypted modules (live) | `%LocalLow%/xd/Heartopia/DotnetAssemblies/*.dll` | — |
| Older build fallback | `<Steam>/Heartopia/xdt_Data/StreamingAssets/DotnetAssemblies/*.dll.bytes` | — |
| Decompiled C# | `ilspy-dumps/<Module>/` | **gitignored** |
| Design tables | `tools/HeartopiaTables/cn_tables.db` | **gitignored** |
| Localization | `%LocalLow%/xd/Heartopia/Others/db/designTable.db` | — |
| Durable snapshot | `.research-record/heartopia-tables/` | **gitignored** |
| These scripts | `tools/gameupdate/` | **gitignored** — local tooling, like the rest of the game-data pipeline |

`XdUnpack` globs `*.dll` only, so staged `.dll.bytes` files must be renamed to `.dll`
first or it reports `0 file(s)`. Verify a staged file starts with `XDENCODE0001`.

Because both the dumps and the tables are gitignored, **the scratch dir is the only
baseline that exists.** Note its path when the pipeline prints it.

---

## Update history

| Date | Code | Tables | The one thing that mattered |
|---|---|---|---|
| 2026-07-09 | yes | yes | established the binding-audit recipe |
| 2026-07-23 | yes | yes | purely additive |
| 2026-08-06 | +0 / −0 / ~22 | 911 tables, no row change | `IconsBarWidget` renamed one node → 16 broken `GameObject.Find` paths |
| 2026-08-20 | +774 / −92 / ~1078 | 911 → 948 tables, 337 746 → 376 657 rows | `ReadUInt64` opcode added to the decoder; `AreaPriorityManager` moved namespace (diagnostic-only break) |

See also: [GAME_ASSEMBLIES_AND_TOOLS.md](GAME_ASSEMBLIES_AND_TOOLS.md) (runtime access,
IL2CPP tree), [GAME_EVENTS.md](GAME_EVENTS.md) (the event engine),
[DECOMPILED_SOURCE_MAP.md](DECOMPILED_SOURCE_MAP.md) (where things live in the dump).
