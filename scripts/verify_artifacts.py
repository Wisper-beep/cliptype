"""Verify the actual local deliverables, their resources, source freshness and SHA256."""
import hashlib
import json
from pathlib import Path, PurePosixPath
import tarfile
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[1]
DIST = ROOT / 'dist'
VERSION = json.loads((ROOT / 'package.json').read_text())['version']

def verify():
    for line in (DIST / 'SHA256SUMS.txt').read_text().splitlines():
        digest, name = line.split('  ', 1)
        assert Path(name).name == name
        assert hashlib.sha256((DIST / name).read_bytes()).hexdigest() == digest, name
    for browser in ('Chromium', 'Firefox'):
        with ZipFile(DIST / f'ClipType-{browser}.zip') as archive:
            assert archive.testzip() is None
            names = set(archive.namelist())
            manifest = json.loads(archive.read('manifest.json'))
            assert manifest['version'] == VERSION
            assert manifest['manifest_version'] == 3
            assert not manifest.get('host_permissions') and not manifest.get('content_scripts')
            for file in ('LICENSE', 'core.js', 'content.js', 'popup.js', 'background.js', 'test.js', 'test.html', manifest['action']['default_popup'], *manifest['icons'].values()):
                assert file in names, (browser, file)
            for name in ('core.js', 'content.js', 'popup.js', 'background.js'):
                assert archive.read(name) == (ROOT / 'extension' / name).read_bytes(), name
            if browser == 'Firefox':
                assert manifest['background'] == {'scripts': ['background.js']}
                assert manifest['browser_specific_settings']['gecko']['data_collection_permissions']['required'] == ['none']
                assert not any(name.startswith('META-INF/') for name in names), 'Development package must not pretend to be signed'
            else:
                assert manifest['background'] == {'service_worker': 'background.js'}
    with ZipFile(DIST / 'ClipType-Windows.zip') as archive:
        assert archive.testzip() is None
        assert archive.read('ClipType.exe') == (DIST / 'ClipType.exe').read_bytes()
        assert archive.read('ClipType.exe').startswith(b'MZ')
        assert not any('Tests' in name or 'Target' in name for name in archive.namelist())
        assert archive.read('README.md') == (ROOT / 'docs/windows.md').read_bytes()
    with ZipFile(DIST / 'cliptype.pyz') as archive:
        assert archive.read('cliptype.py') == (ROOT / 'linux/cliptype.py').read_bytes()
        assert '__main__.py' in archive.namelist()
    with tarfile.open(DIST / 'ClipType-Linux.tar.gz') as archive:
        for member in archive.getmembers():
            name = PurePosixPath(member.name)
            assert not name.is_absolute() and '..' not in name.parts
        member = archive.getmember('cliptype-linux/cliptype.pyz')
        assert member.mode & 0o111
        assert archive.extractfile(member).read() == (DIST / 'cliptype.pyz').read_bytes()
    assert (DIST / 'ClipType-Extension.zip').read_bytes() == (DIST / 'ClipType-Chromium.zip').read_bytes()
    print('PASS: all development archives, source versions, resources, executable entries and SHA256 match.')

if __name__ == '__main__':
    verify()
