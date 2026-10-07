"""Build a compressed, seekable update archive and a file hash/range manifest.

Clients request only each changed file's raw deflate range, never the whole ZIP.
The manifest's GitHub asset digest authenticates file hashes and byte ranges.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import zipfile


def build(root, output, version):
    root, output = Path(root).resolve(), Path(output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    # Installer-owned Uninstall files and user-local data are never managed here.
    files = [root / 'MapleDay.exe'] + sorted(
        p for p in (root / 'App').rglob('*') if p.is_file()
        and not any(part.startswith('.') or part.lower() == 'uninstall'
                    for part in p.relative_to(root).parts)
        and p.name != 'update-files.txt')
    index = root / 'App' / 'update-files.txt'
    index.write_text('\n'.join(p.relative_to(root).as_posix() for p in files)
                     + '\nApp/update-files.txt\n', encoding='utf-8')
    files.append(index)
    archive_name = f'MapleDay-Update-{version}-x64.zip'
    archive = output / archive_name
    entries = []
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as zipped:
        for file in files:
            name = file.relative_to(root).as_posix()
            zipped.write(file, name)
    with archive.open('rb') as raw, zipfile.ZipFile(archive) as zipped:
        for item in zipped.infolist():
            raw.seek(item.header_offset)
            header = raw.read(30)
            assert header[:4] == b'PK\x03\x04' and item.compress_type == zipfile.ZIP_DEFLATED
            name_size, extra_size = struct.unpack_from('<HH', header, 26)
            file = root / item.filename
            with file.open('rb') as original:
                digest = hashlib.file_digest(original, 'sha256').hexdigest()
            entries.append(dict(path=item.filename, size=item.file_size,
                                sha256=digest,
                                offset=item.header_offset + 30 + name_size + extra_size,
                                compressed_size=item.compress_size))
    manifest = dict(format=1, version=version, archive=archive_name,
                    archive_size=archive.stat().st_size, files=entries)
    target = output / f'MapleDay-Update-{version}-x64.json'
    target.write_text(json.dumps(manifest, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    print(f'Update archive: {archive}; {len(entries)} individually verified files')
    print(target)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('root')
    parser.add_argument('output')
    parser.add_argument('version')
    args = parser.parse_args()
    build(args.root, args.output, args.version)
