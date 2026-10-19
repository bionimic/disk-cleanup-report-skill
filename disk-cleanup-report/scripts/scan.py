"""Read-only disk scan: folder sizes, totals per file type, biggest files.

Usage: py -3 scan.py <root> <out.json> [--depth N] [--min-gb N]

Never modifies anything. Skips junctions, symlinks and other reparse points
so no folder is counted twice.
"""
import argparse
import json
import os
import sys
from collections import defaultdict

SEP = os.sep
REPARSE_POINT = 0x400


def scan(root, depth, min_bytes):
    root = root.rstrip(SEP)
    dir_size = defaultdict(int)
    ext = defaultdict(lambda: [0, 0])
    big = []
    stack = [root + SEP]
    while stack:
        d = stack.pop()
        try:
            it = os.scandir(d)
        except OSError:
            continue
        with it:
            for e in it:
                try:
                    st = e.stat(follow_symlinks=False)
                    if getattr(st, 'st_file_attributes', 0) & REPARSE_POINT:
                        continue
                    if e.is_dir(follow_symlinks=False):
                        stack.append(e.path)
                        continue
                    size = st.st_size
                    rel = e.path[len(root):].strip(SEP).split(SEP)
                    for k in range(1, min(depth, len(rel) - 1) + 1):
                        dir_size[root + SEP + SEP.join(rel[:k])] += size
                    x = os.path.splitext(e.name)[1].lower() or '(none)'
                    ext[x][0] += size
                    ext[x][1] += 1
                    big.append((size, e.path, st.st_mtime))
                    if len(big) > 2000:
                        big.sort(reverse=True)
                        del big[100:]
                except OSError:
                    pass
    big.sort(reverse=True)
    return {
        'root': root,
        'dirs': sorted([(v, k) for k, v in dir_size.items() if v >= min_bytes], reverse=True),
        'ext': sorted([(v[0], k, v[1]) for k, v in ext.items()], reverse=True)[:50],
        'big': big[:100],
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('root')
    ap.add_argument('out')
    ap.add_argument('--depth', type=int, default=4)
    ap.add_argument('--min-gb', type=float, default=1.0)
    a = ap.parse_args()
    result = scan(a.root, a.depth, a.min_gb * 1e9)
    with open(a.out, 'w', encoding='utf-8') as f:
        json.dump(result, f, indent=0)
    print('Top folders:')
    for size, path in result['dirs'][:25]:
        print('%8.1f GB  %s' % (size / 1e9, path))
    print('Top file types:')
    for size, x, n in result['ext'][:15]:
        print('%8.1f GB  %-10s %d files' % (size / 1e9, x, n))
    print('Biggest files:')
    for size, path, _ in result['big'][:15]:
        print('%8.1f GB  %s' % (size / 1e9, path))


if __name__ == '__main__':
    sys.exit(main())
