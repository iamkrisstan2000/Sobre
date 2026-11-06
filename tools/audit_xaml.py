#!/usr/bin/env python3
"""
Static gate for Sobre's XAML. Catches the failures that only surface at runtime on a
user's machine, which is exactly how 1.0.4 shipped a crash:

  * Color="{DynamicResource ...}"      -> SolidColorBrush.Color is a struct, not a
                                          dependency property that accepts a resource.
                                          It throws "Set property ... threw an exception"
                                          the moment the view loads.
  * Color="{StaticResource ...}"       -> same failure.
  * <SolidColorBrush Color="{...}"/>   -> same failure.
  * unresolvable {StaticResource}/{DynamicResource} keys
  * unknown Fluent icon names
  * malformed XML

Exit code 1 on any BLOCKER. Run before every publish.
"""
import glob
import os
import re
import sys
import xml.etree.ElementTree as ET

ROOT = "/home/user/sobre/src/Sobre"
blockers, notes = [], []

xaml = sorted(set(
    glob.glob(f"{ROOT}/Themes/*.xaml")
    + glob.glob(f"{ROOT}/Views/*.xaml")
    + [f"{ROOT}/App.xaml"]
))
xaml = [f for f in xaml if os.path.exists(f)]

if not xaml:
    print("no XAML found under", ROOT)
    sys.exit(1)

# ---------- 1. well-formed XML ----------
for f in xaml:
    try:
        ET.parse(f)
    except ET.ParseError as e:
        blockers.append(f"{os.path.relpath(f, ROOT)}: malformed XML — {e}")

blob = ""
for f in xaml:
    blob += open(f, encoding="utf-8").read()

# ---------- 2. the 1.0.4 crash pattern ----------
# Any attribute whose type is a *struct* cannot take a resource reference: the XAML parser
# has to produce a MarkupExtension value where a value type is expected, and throws at load.
# SolidColorBrush.Color is the one that bit us in 1.0.4; the rest are the same trap.
STRUCT_ATTRS = ("Color", "Thickness", "CornerRadius", "FontWeight", "FontStyle", "FontStretch",
                "GridLength", "Point", "Rect", "Visibility", "HorizontalAlignment",
                "VerticalAlignment", "TextAlignment", "Double", "Boolean", "Int32", "Length")
BAD_COLOR = re.compile(
    r'\b(' + "|".join(STRUCT_ATTRS) + r')\s*=\s*"\{(Dynamic|Static)Resource\s+([^}]+)\}"')
for f in xaml:
    for i, line in enumerate(open(f, encoding="utf-8"), 1):
        for m in BAD_COLOR.finditer(line):
            blockers.append(
                f"{os.path.relpath(f, ROOT)}:{i}: {m.group(1)}={{...Resource {m.group(3)}}} — "
                f"{m.group(1)} is a value type and cannot accept a resource reference; "
                "use a brush-valued property (Background/Foreground) or a literal"
            )

# ---------- 3. resource keys ----------
defined = set(re.findall(r'x:Key="([^"]+)"', blob))
defined |= set(re.findall(r'\bx:Name="([^"]+)"', blob))
used = set(re.findall(r'\{(?:Static|Dynamic)Resource\s+([^\}]+)\}', blob))
theme_cs = open(f"{ROOT}/Services/ThemeService.cs", encoding="utf-8").read()
theme_tokens = set(re.findall(r'Set(?:Brush)?\("([A-Za-z0-9]+)"', theme_cs))

for key in sorted(used):
    key = key.strip()
    if key.startswith("{"):          # nested markup extension, not a key
        continue
    if key in defined or key in theme_tokens:
        continue
    blockers.append(f"unresolved resource key: {key}")

# ---------- 4. icons ----------
icons_cs = open(f"{ROOT}/Themes/Icons.xaml", encoding="utf-8").read()
have_icons = set(re.findall(r'x:Key="Icon\.([A-Za-z0-9]+)"', icons_cs))
cs_files = glob.glob(f"{ROOT}/**/*.cs", recursive=True)
cs_blob = "".join(open(f, encoding="utf-8").read() for f in cs_files)
wanted = set(re.findall(r'Icon="([A-Za-z0-9]+)"', blob))
for m in re.finditer(r'\bIcon\s*=\s*"([A-Za-z0-9]+)"', cs_blob):
    wanted.add(m.group(1))
for name in sorted(wanted):
    if name not in have_icons:
        blockers.append(f"icon not found in Icons.xaml: {name}")

# ---------- 5. advisory: tokens nobody consumes ----------
unused = sorted(t for t in theme_tokens if t not in used)
if unused:
    notes.append("theme tokens defined but unused: " + ", ".join(unused))

print(f"XAML files audited: {len(xaml)}")
print(f"resource keys defined: {len(defined)}  ·  theme tokens: {len(theme_tokens)}")
if notes:
    print("\nnotes")
    for n in notes:
        print("  ·", n)
if blockers:
    print(f"\n{len(blockers)} BLOCKER(S)")
    for b in blockers:
        print("  ✗", b)
    sys.exit(1)
print("\nno blockers — safe to publish")
