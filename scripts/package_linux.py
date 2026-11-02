"""Build the Python zipapp and portable Linux source archive without native compilation."""
from pathlib import Path
import io
import tarfile
import tempfile
import zipapp

ROOT = Path(__file__).resolve().parents[1]
DIST = ROOT / 'dist'

def build():
    DIST.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='cliptype-linux-', dir=DIST) as folder:
        stage=Path(folder)
        assert stage.resolve().parent == DIST.resolve()
        (stage/'cliptype.py').write_bytes((ROOT/'linux/cliptype.py').read_bytes())
        (stage/'__main__.py').write_text('from cliptype import main\nraise SystemExit(main())\n',encoding='utf-8')
        zipapp.create_archive(stage, DIST/'cliptype.pyz', interpreter='/usr/bin/env python3', compressed=True)
    files={'cliptype.pyz':DIST/'cliptype.pyz','README.md':ROOT/'linux/README.md','niri-bindings.kdl':ROOT/'linux/niri-bindings.kdl','LICENSE':ROOT/'LICENSE','PKGBUILD':ROOT/'linux/arch/PKGBUILD'}
    with tarfile.open(DIST/'ClipType-Linux.tar.gz','w:gz') as archive:
        for name,file in files.items():
            data=file.read_bytes();info=tarfile.TarInfo('cliptype-linux/'+name);info.size=len(data)
            info.mode=0o755 if name=='cliptype.pyz' else 0o644;info.mtime=0
            archive.addfile(info,io.BytesIO(data))
    print('Built cliptype.pyz and ClipType-Linux.tar.gz (requires Python / wtype / wl-clipboard / Niri)')

if __name__=='__main__': build()
