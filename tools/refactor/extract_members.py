#!/usr/bin/env python3
"""Move named members of the `HeartopiaComplete` partial class out of a source
file into a new partial-class file.

Heuristics tuned for this codebase's formatting:
  * Class members sit at exactly 8 spaces of indentation.
  * A method's closing brace is the first line equal to '        }' (8 spaces + }).
  * Expression-bodied / single-line members end on their own line (`;`).
  * Leading attribute lines ('        [...'), XML doc ('        ///') and the
    decompiler '// Token:' comment directly above a member are carried along.

The compiler is the real validator: if a span is mis-cut the build fails loudly.
Run with --dry-run first to review the plan.
"""
import argparse
import re
import sys

INDENT = "        "  # 8 spaces: member level


def is_member_decl(line, name):
    """True if `line` declares a member called `name` (method or field)."""
    if not line.startswith(INDENT) or line[8:9] in (" ", "\t"):
        return False
    body = line.strip()
    # must start with a modifier or a type word, not a statement
    if not re.match(r"^(public|private|internal|protected|static|unsafe|override|sealed|virtual|async|new|extern|partial|readonly|const|\[)", body):
        return False
    # type decl: `... class|struct|enum|interface Name` (brace may be on next line)
    if re.search(r"\b(class|struct|enum|interface)\s+" + re.escape(name) + r"\b", body):
        return True
    # method (incl. generic `Name<T>(`): `... Name(` or `... Name<`
    # field/prop: `... Name ` then = or ; or {
    return re.search(r"\b" + re.escape(name) + r"\s*[\(<]", body) is not None \
        or re.search(r"\b" + re.escape(name) + r"\s*[;={]", body) is not None


def find_member_span(lines, idx):
    """Given the index of a declaration line, return (start, end) inclusive,
    where start absorbs leading attribute/doc/Token lines."""
    # absorb leading attribute / xmldoc / Token comment lines
    start = idx
    while start > 0:
        prev = lines[start - 1]
        s = prev.strip()
        if prev.startswith(INDENT) and (s.startswith("[") or s.startswith("///") or s.startswith("// Token:")):
            start -= 1
        else:
            break
    # find body
    i = idx
    # advance to either an opening brace line, or a line ending the decl with ;  / =>...;
    # collect the signature until we hit '{' (own line or trailing) or a ';'
    depth = 0
    # Walk forward to locate the opening brace of the member body.
    j = idx
    opened = False
    while j < len(lines):
        line = lines[j]
        # expression-bodied or field: terminates with ';' before any '{'
        if not opened and "{" not in line and line.rstrip().endswith(";"):
            return start, j
        if "{" in line:
            opened = True
            break
        j += 1
    if not opened:
        return start, idx  # fallback: single line
    # single-line balanced member (e.g. one-liner class/prop): braces close on the same line
    if lines[j].count("{") > 0 and lines[j].count("{") == lines[j].count("}"):
        return start, j
    # now brace-match: closing brace is first line == '        }' possibly with trailing
    k = j
    while k < len(lines):
        if lines[k].rstrip("\n") == INDENT + "}":
            return start, k
        # also handle '        } // comment' style
        if re.match(r"^" + INDENT + r"\}\s*(//.*)?$", lines[k].rstrip("\n")):
            return start, k
        k += 1
    raise RuntimeError(f"unterminated member starting at line {idx+1}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--names", required=True, help="file with one member name per line")
    ap.add_argument("--header", help="file with usings+namespace+class open (new file mode)")
    ap.add_argument("--append", action="store_true",
                    help="append moved members into an existing --out partial file")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    with open(args.src, encoding="utf-8-sig") as f:
        text = f.read()
    had_bom = True
    lines = text.splitlines(keepends=True)

    with open(args.names, encoding="utf-8") as f:
        names = [n.strip() for n in f if n.strip() and not n.startswith("#")]

    taken = [False] * len(lines)
    moved_spans = []  # (start, end, name)
    not_found = []
    multi = []
    for name in names:
        hits = [i for i, ln in enumerate(lines) if is_member_decl(ln, name)]
        # skip already-taken
        hits = [i for i in hits if not taken[i]]
        if not hits:
            not_found.append(name)
            continue
        if len(hits) > 1:
            multi.append((name, [h + 1 for h in hits]))
        for idx in hits:
            if taken[idx]:
                continue
            s, e = find_member_span(lines, idx)
            for t in range(s, e + 1):
                taken[t] = True
            moved_spans.append((s, e, name))

    moved_spans.sort()
    total_lines = sum(e - s + 1 for s, e, _ in moved_spans)
    print(f"members requested : {len(names)}")
    print(f"members moved      : {len(moved_spans)}")
    print(f"lines moved        : {total_lines}")
    if not_found:
        print(f"NOT FOUND ({len(not_found)}): {', '.join(not_found)}")
    if multi:
        print("MULTIPLE matches (all moved):")
        for n, ls in multi:
            print(f"   {n}: lines {ls}")

    if args.dry_run:
        print("\n--- sample of first 3 spans ---")
        for s, e, name in moved_spans[:3]:
            print(f"[{name}] lines {s+1}-{e+1}:")
            print("".join(lines[s:min(e + 1, s + 6)]))
            print("   ...")
        return

    # build moved body
    moved_text = []
    for s, e, _ in moved_spans:
        moved_text.append("".join(lines[s:e + 1]))
        if not moved_text[-1].endswith("\n"):
            moved_text[-1] += "\n"
    body = "\n".join(moved_text)

    if args.append:
        # insert before the final class+namespace closing braces of an existing file
        with open(args.out, encoding="utf-8-sig") as f:
            existing = f.read()
        marker = existing.rfind("\n    }\n}")
        if marker == -1:
            raise RuntimeError("could not find class/namespace close in " + args.out)
        out_content = existing[:marker] + "\n" + body + existing[marker:]
    else:
        with open(args.header, encoding="utf-8") as f:
            header = f.read()
        out_content = header.replace("__BODY__", body)
    with open(args.out, "w", encoding="utf-8-sig", newline="") as f:
        f.write(out_content)

    # rewrite source without taken lines
    kept = [ln for i, ln in enumerate(lines) if not taken[i]]
    with open(args.src, "w", encoding="utf-8-sig", newline="") as f:
        f.write("".join(kept))
    print(f"\nwrote {args.out} and rewrote {args.src}")


if __name__ == "__main__":
    main()
