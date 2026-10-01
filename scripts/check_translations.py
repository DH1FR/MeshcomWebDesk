#!/usr/bin/env python3
"""
Translation coverage check for MeshCom WebDesk.

Scans the sources for every T(de, en) / TF(de, en, ...) call, collects the English texts and
compares them with the language files in MeshcomWebDesk/Languages/*.json.

  python scripts/check_translations.py                 # report for all languages
  python scripts/check_translations.py fr              # report for one language
  python scripts/check_translations.py --write fr      # add missing keys (empty value) to Languages/fr.json,
                                                     # creates the file if the language is new
  python scripts/check_translations.py --strict        # exit code 1 if anything is missing (CI / release check)

An empty value means "not translated yet" – the app then shows the English text.
Keys that are no longer used by any T()/TF() call are reported as unused (often a reworded English text:
rename the key in the JSON to the new wording to keep the translation).
"""
import glob
import json
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECT = os.path.join(REPO, "MeshcomWebDesk")
LANG_DIR = os.path.join(PROJECT, "Languages")

ESC = {"n": "\n", "t": "\t", "r": "\r", "\\": "\\", '"': '"', "'": "'", "0": "\0"}
CALL = re.compile(r"(?<![A-Za-z0-9_])(?:[A-Za-z_]+\.)?(?:T|TF)\s*\(")


def parse_string(s, i):
    """Parse a C# string literal at s[i]. Returns (value, end, interpolated) or None."""
    interp = False
    if s.startswith('$@"', i) or s.startswith('@$"', i):
        interp, verbatim, i = True, True, i + 3
    elif s.startswith('@"', i):
        verbatim, i = True, i + 2
    elif s.startswith('$"', i):
        interp, verbatim, i = True, False, i + 2
    elif s.startswith('"', i):
        verbatim, i = False, i + 1
    else:
        return None
    out = []
    while i < len(s):
        c = s[i]
        if verbatim:
            if c == '"':
                if s[i + 1:i + 2] == '"':
                    out.append('"')
                    i += 2
                    continue
                return "".join(out), i + 1, interp
            out.append(c)
            i += 1
            continue
        if c == "\\":
            n = s[i + 1]
            if n == "u":
                out.append(chr(int(s[i + 2:i + 6], 16)))
                i += 6
            elif n == "U":
                out.append(chr(int(s[i + 2:i + 10], 16)))
                i += 10
            else:
                out.append(ESC.get(n, n))
                i += 2
            continue
        if c == '"':
            return "".join(out), i + 1, interp
        out.append(c)
        i += 1
    return None


def skip_ws(s, i):
    while i < len(s) and s[i].isspace():
        i += 1
    return i


def parse_concat(s, i):
    """A string literal, optionally concatenated with '+'. Returns (value, end, interpolated) or None."""
    r = parse_string(s, skip_ws(s, i))
    if not r:
        return None
    val, i, interp = r
    while True:
        k = skip_ws(s, i)
        if s[k:k + 1] != "+":
            return val, i, interp
        r = parse_string(s, skip_ws(s, k + 1))
        if not r:
            return val, i, True
        val, i, interp = val + r[0], r[1], interp or r[2]


def scan_sources():
    """Returns ({en_text: 'file:line'}, [(file, line, snippet) of non-literal / interpolated calls])."""
    used, dynamic = {}, []
    sep = os.sep
    for path in glob.glob(PROJECT + "/**/*", recursive=True):
        if not path.endswith((".razor", ".cs")):
            continue
        if (sep + "obj" + sep) in path or (sep + "bin" + sep) in path or "LanguageService.cs" in path:
            continue
        s = open(path, encoding="utf-8-sig").read()
        rel = os.path.relpath(path, PROJECT)
        for m in CALL.finditer(s):
            line = s.count("\n", 0, m.start()) + 1
            a = parse_concat(s, m.end())
            if not a:
                continue
            k = skip_ws(s, a[1])
            if s[k:k + 1] != ",":
                continue
            b = parse_concat(s, k + 1)
            if not b:
                dynamic.append((rel, line, s[m.start():m.start() + 70].replace("\n", " ")))
                continue
            if a[2] or b[2]:
                dynamic.append((rel, line, "interpolated: " + b[0][:60]))
                continue
            used.setdefault(b[0], f"{rel}:{line}")
    return used, dynamic


def load_lang(code):
    path = os.path.join(LANG_DIR, code + ".json")
    if not os.path.exists(path):
        return None
    return json.load(open(path, encoding="utf-8"))


def write_lang(code, data):
    with open(os.path.join(LANG_DIR, code + ".json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")


def main():
    args = sys.argv[1:]
    strict = "--strict" in args
    write = "--write" in args
    codes = [a for a in args if not a.startswith("--")]
    if not codes:
        codes = sorted(os.path.splitext(os.path.basename(p))[0] for p in glob.glob(os.path.join(LANG_DIR, "*.json")))
    used, dynamic = scan_sources()
    print(f"{len(used)} distinct English texts in T()/TF() calls; "
          f"{len(dynamic)} calls with interpolated/non-literal text (cannot be translated, use TF).")
    bad = False
    for code in codes:
        data = load_lang(code)
        if data is None:
            if not write:
                print(f"[{code}] no file Languages/{code}.json – use --write to create it")
                bad = True
                continue
            data = {"code": code, "name": code, "flag": "", "strings": {}}
        strings = data["strings"]
        missing = [k for k in used if not strings.get(k)]
        unused = [k for k in strings if k not in used]
        print(f"[{code}] {len(strings) - len([v for v in strings.values() if not v])} translated, "
              f"{len(missing)} missing, {len(unused)} unused keys")
        if write:
            for k in missing:
                strings.setdefault(k, "")
            data["strings"] = dict(sorted(strings.items(), key=lambda kv: kv[0].lower()))
            write_lang(code, data)
            print(f"[{code}] written Languages/{code}.json (missing keys added with empty value)")
        else:
            for k in missing[:15]:
                print(f"   missing  {used[k]:45s} {k[:80]!r}")
            if len(missing) > 15:
                print(f"   … and {len(missing) - 15} more (run with --write {code} and fill in the empty values)")
            for k in unused[:10]:
                print(f"   unused   {k[:90]!r}")
        bad = bad or bool(missing)
    if strict and bad:
        sys.exit(1)


if __name__ == "__main__":
    main()
