#!/usr/bin/env python3
"""
XML 주석(///) 이스케이프 검증 스크립트.

각 .cs 파일에서 연속된 `///` 줄을 하나의 블록으로 묶어 `<doc>...</doc>`으로 감싼 뒤
실제 XML 파서로 파싱한다. 같은 줄에 `<`/`>` 쌍이 있는 경우만 잡아내는 정규식 검사와
달리, "온도 > 80"처럼 짝이 없는 단독 `<`/`>`도 전부 잡아낸다.

사용법(리포지토리 루트에서 실행):
    python3 tools/check-xmldoc.py

종료 코드: 깨진 블록이 있으면 1, 없으면 0.
"""
import glob
import sys
import xml.etree.ElementTree as ET


def find_broken_blocks(root: str = "."):
    files = sorted(glob.glob(f"{root}/src/**/*.cs", recursive=True)) + \
        sorted(glob.glob(f"{root}/test/**/*.cs", recursive=True))
    files = [f for f in files if "/obj/" not in f and "\\obj\\" not in f]

    broken = []
    for path in files:
        lines = open(path, encoding="utf-8").read().split("\n")
        i, n = 0, len(lines)
        while i < n:
            if lines[i].strip().startswith("///"):
                start = i
                block = []
                while i < n and lines[i].strip().startswith("///"):
                    content = lines[i].strip()[3:]
                    if content.startswith(" "):
                        content = content[1:]
                    block.append(content)
                    i += 1
                xml_text = "<doc>" + "\n".join(block) + "</doc>"
                try:
                    ET.fromstring(xml_text)
                except ET.ParseError as e:
                    broken.append((path, start + 1, i, str(e)))
            else:
                i += 1
    return broken


if __name__ == "__main__":
    broken = find_broken_blocks()
    for path, s, e, err in broken:
        print(f"{path}:{s}-{e}  {err}")
    print(f"\nTotal broken doc-comment blocks: {len(broken)}")
    sys.exit(1 if broken else 0)
