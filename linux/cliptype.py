#!/usr/bin/env python3
"""ClipType: unprivileged Niri/Wayland prototype. No clipboard history or shell execution."""
import argparse
import errno
import json
import os
from pathlib import Path
import random
import shutil
import signal
import socket
import stat
import struct
import subprocess
import sys
import threading
import time
import unicodedata

MAX_UNITS = 100000

class Stopped(Exception):
    pass

def normalize(text, single_line=False):
    text = text.replace('\r\n', '\n').replace('\r', '\n').replace('\u2028', '\n').replace('\u2029', '\n').replace('\t', '    ')
    text = ''.join(c for c in text if c == '\n' or unicodedata.category(c) != 'Cc')
    return text.replace('\n', ' ') if single_line else text

def elements(text):
    """Common combining marks, emoji modifiers, ZWJ, tags and RI pairs; not full UAX29."""
    part, previous, regional_count = '', '', 0
    for char in text:
        point = ord(char)
        regional = 0x1F1E6 <= point <= 0x1F1FF
        extend = unicodedata.category(char).startswith('M') or char == '\u200d' or previous == '\u200d' or 0x1F3FB <= point <= 0x1F3FF or 0xE0020 <= point <= 0xE007F or regional and regional_count % 2 == 1
        if part and (not extend or char == '\n' or previous == '\n'):
            yield part
            part = ''
        part += char
        regional_count = regional_count + 1 if regional else 0
        previous = char
    if part:
        yield part

def delay_after(text, following, interval, variation=0, punctuation=0, sentence=0, paragraph=0, sample=None):
    sample = random.random() if sample is None else sample
    delay = round(interval * (1 + (max(0, min(1, sample)) * 2 - 1) * variation / 100))
    if text == '\n' and following == '\n':
        delay += paragraph
    elif text and text[0] in '.!?。！？':
        delay += sentence
    elif text and text[0] in ',;:，；：、':
        delay += punctuation
    return max(0, delay) / 1000

def read_line(connection, limit=1024 * 1024):
    data = bytearray()
    while len(data) <= limit:
        char = connection.recv(1)
        if not char:
            raise RuntimeError('IPC connection closed')
        if char == b'\n':
            return json.loads(data)
        data.extend(char)
    raise RuntimeError('IPC response too large')

class NiriGuard:
    def __init__(self, address):
        if not address:
            raise RuntimeError('NIRI_SOCKET is missing; this prototype requires a Niri session.')
        self.address = address
        self.lost = threading.Event()
        self.ready = threading.Event()
        self.closing = threading.Event()
        self.connection = None
        self.target = self.focused_id()
        if self.target is None:
            raise RuntimeError('Niri has no focused window.')

    def focused_id(self):
        with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as connection:
            connection.settimeout(1)
            connection.connect(self.address)
            connection.sendall(b'"FocusedWindow"\n')
            reply = read_line(connection)
        if not isinstance(reply, dict) or 'Ok' not in reply or not isinstance(reply['Ok'], dict) or 'FocusedWindow' not in reply['Ok']:
            raise RuntimeError('Unexpected Niri focused-window response.')
        window = reply['Ok']['FocusedWindow']
        return window.get('id') if window else None

    def start(self):
        self.connection = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.connection.settimeout(1)
        self.connection.connect(self.address)
        self.connection.sendall(b'"EventStream"\n')
        self.thread = threading.Thread(target=self._watch, daemon=True)
        self.thread.start()
        if not self.ready.wait(2):
            self.close()
            raise RuntimeError('Niri event stream did not supply initial window state.')
        self.ensure()
        return self

    def _watch(self):
        try:
            # A persistent file reader preserves partial JSON across socket reads.
            self.connection.settimeout(None)
            with self.connection.makefile('rb') as stream:
                for line in stream:
                    if self.closing.is_set():
                        return
                    if len(line) > 1024 * 1024:
                        raise RuntimeError('Event too large')
                    event = json.loads(line)
                    if not isinstance(event, dict):
                        raise ValueError('Unexpected Niri event')
                    if 'WindowsChanged' in event:
                        focused = [w['id'] for w in event['WindowsChanged']['windows'] if w.get('is_focused')]
                        if focused != [self.target]:
                            self.lost.set()
                        self.ready.set()
                    elif 'WindowFocusChanged' in event and event['WindowFocusChanged']['id'] != self.target:
                        self.lost.set()
                    elif 'WindowOpenedOrChanged' in event:
                        window = event['WindowOpenedOrChanged']['window']
                        if window.get('is_focused') and window['id'] != self.target or window['id'] == self.target and not window.get('is_focused'):
                            self.lost.set()
                    elif event.get('WindowClosed', {}).get('id') == self.target or 'Err' in event:
                        self.lost.set()
        except (OSError, ValueError, KeyError, TypeError):
            self.lost.set()
        finally:
            if not self.closing.is_set():
                self.lost.set()

    def ensure(self):
        if self.lost.is_set():
            raise Stopped('Niri target changed or its IPC monitor failed; stopped.')
        try:
            focused = self.focused_id()
        except (OSError, RuntimeError, ValueError, KeyError, TypeError, AttributeError) as error:
            self.lost.set()
            raise Stopped('Niri target changed or its IPC monitor failed; stopped.') from error
        if self.lost.is_set() or focused != self.target:
            self.lost.set()
            raise Stopped('Niri target changed or its IPC monitor failed; stopped.')

    def close(self):
        self.closing.set()
        if self.connection:
            try:
                self.connection.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
            self.connection.close()
        if hasattr(self, 'thread'):
            self.thread.join(timeout=1)

class Session:
    def __init__(self, guard):
        self.guard = guard
        self.lock = threading.RLock()
        self.cancelled = threading.Event()
        self.state, self.previous = 'preparing', 'running'
        self.completed = self.total = 0

    def command(self, action):
        with self.lock:
            if action == 'stop':
                self.cancelled.set()
            elif action == 'pause' and self.state in ('countdown', 'running'):
                self.previous, self.state = self.state, 'paused'
            elif action == 'resume' and self.state == 'paused':
                self.guard.ensure()
                self.state = self.previous
            elif action not in ('pause', 'resume', 'status'):
                raise ValueError('Unknown control command')
            return dict(state=self.state, completed=self.completed, total=self.total, cancelled=self.cancelled.is_set())

    def check(self):
        if self.cancelled.is_set():
            raise Stopped('Stopped by user.')
        self.guard.ensure()
        if self.cancelled.is_set():
            raise Stopped('Stopped by user.')

    def set_phase(self, state):
        with self.lock:
            if self.state == 'paused':
                self.previous = state
            else:
                self.state = state

    def wait(self, seconds):
        remaining = max(0, seconds)
        while True:
            self.check()
            while self.state == 'paused':
                self.cancelled.wait(.02)
                self.check()
            if remaining <= 0:
                return
            before = time.monotonic()
            self.cancelled.wait(min(.02, remaining))
            remaining -= time.monotonic() - before

def runtime_path():
    if not sys.platform.startswith('linux'):
        raise RuntimeError('The Wayland backend runs on Linux only.')
    base = Path(os.environ.get('XDG_RUNTIME_DIR', ''))
    if not base.is_absolute() or not base.is_dir() or base.is_symlink() or base.stat().st_uid != os.getuid():
        raise RuntimeError('An owned, absolute XDG_RUNTIME_DIR is required.')
    target = base / 'cliptype'
    target.mkdir(mode=0o700, exist_ok=True)
    info = target.lstat()
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid() or stat.S_IMODE(info.st_mode) != 0o700:
        raise RuntimeError('Unsafe ClipType runtime directory; expected an owned mode-0700 directory.')
    return target

class ControlServer:
    def __init__(self, folder, session):
        self.path = folder / 'control.sock'
        self.session = session
        self.closed = threading.Event()
        self.listener = None
        self.lockfile = None

    def __enter__(self):
        import fcntl
        descriptor = os.open(self.path.parent / 'session.lock', os.O_CREAT | os.O_WRONLY | os.O_NOFOLLOW, 0o600)
        self.lockfile = os.fdopen(descriptor, 'w')
        try:
            fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
            if self.path.exists():
                info = self.path.lstat()
                if not stat.S_ISSOCK(info.st_mode) or info.st_uid != os.getuid():
                    raise RuntimeError('Unsafe stale control socket.')
                self.path.unlink()
            self.listener = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
            self.listener.bind(str(self.path))
            os.chmod(self.path, 0o600)
            self.listener.listen(4)
            self.listener.settimeout(.2)
        except Exception:
            self.lockfile.close()
            if self.listener:
                self.listener.close()
            raise
        self.thread = threading.Thread(target=self._serve, daemon=True)
        self.thread.start()
        return self

    def _serve(self):
        while not self.closed.is_set():
            try:
                connection, _ = self.listener.accept()
            except socket.timeout:
                continue
            except OSError:
                return
            with connection:
                connection.settimeout(.5)
                try:
                    credentials = connection.getsockopt(socket.SOL_SOCKET, socket.SO_PEERCRED, struct.calcsize('3i'))
                    if struct.unpack('3i', credentials)[1] != os.getuid():
                        continue
                    request = read_line(connection, 1024)
                    if not isinstance(request, dict) or not isinstance(request.get('action'), str):
                        raise ValueError('Invalid control request')
                    result = self.session.command(request['action'])
                    connection.sendall((json.dumps({'ok': True, **result}) + '\n').encode())
                except (OSError, ValueError, KeyError, TypeError, RuntimeError, Stopped) as error:
                    try:
                        connection.sendall((json.dumps({'ok': False, 'error': str(error)}) + '\n').encode())
                    except OSError:
                        pass

    def __exit__(self, *_):
        self.closed.set()
        self.listener.close()
        self.thread.join(timeout=1)
        self.path.unlink(missing_ok=True)
        self.lockfile.close()

def control(action):
    with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as connection:
        connection.settimeout(2)
        connection.connect(str(runtime_path() / 'control.sock'))
        connection.sendall((json.dumps({'action': action}) + '\n').encode())
        result = read_line(connection, 4096)
    if not result.get('ok'):
        raise RuntimeError(result.get('error', 'Control request failed'))
    return result

class Wtype:
    def __init__(self, session, newlines):
        self.session, self.newlines = session, newlines

    def emit(self, text):
        args = ['wtype', '-']
        data = b' ' if text == '\n' and self.newlines == 'space' else text.encode('utf-8')
        if text == '\n' and self.newlines != 'space':
            args = ['wtype', '-k', 'Return'] if self.newlines == 'enter' else ['wtype', '-M', 'shift', '-k', 'Return', '-m', 'shift']
            data = b''
        # Clipboard text never appears in argv, logs, settings or shell commands.
        while True:
            self.session.wait(0)
            with self.session.lock:
                if self.session.state == 'paused':
                    continue
                if self.session.cancelled.is_set():
                    raise Stopped('Stopped by user.')
                process = subprocess.Popen(args, stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
                break
        first = True
        deadline = time.monotonic() + 3
        try:
            while True:
                try:
                    process.communicate(data if first else None, timeout=.02)
                    break
                except subprocess.TimeoutExpired:
                    first = False
                    self.session.check()
                    if time.monotonic() >= deadline:
                        raise RuntimeError('wtype did not finish; stopped.')
            if process.returncode:
                raise RuntimeError('wtype failed. cliptype doctor checks dependencies and environment only; verify virtual-keyboard support in the Niri session.')
        finally:
            if process.poll() is None:
                process.kill()
                process.communicate()

def run(args):
    for executable in ('wl-paste', 'wtype'):
        if not shutil.which(executable):
            raise RuntimeError(f'{executable} is missing. Install wtype and wl-clipboard.')
    guard = NiriGuard(os.environ.get('NIRI_SOCKET'))
    session = Session(guard)
    previous_handlers = {}
    try:
        with ControlServer(runtime_path(), session):
            for sig in (signal.SIGINT, signal.SIGTERM):
                previous_handlers[sig] = signal.signal(sig, lambda *_: session.cancelled.set())
            guard.start()
            result = subprocess.run(['wl-paste', '--no-newline', '--type', 'text'], stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=3)
            if result.returncode:
                raise RuntimeError('Could not read clipboard text.')
            text = result.stdout.decode('utf-8', errors='strict')
            if len(text.encode('utf-16-le')) // 2 > MAX_UNITS:
                raise RuntimeError('Clipboard exceeds 100,000 UTF-16 units; split it into smaller parts.')
            # Keep logical line breaks for paragraph rhythm; render them only when sending.
            parts = list(elements(normalize(text)))
            if not parts:
                raise RuntimeError('Clipboard has no text to type.')
            session.total = len(parts)
            session.set_phase('countdown')
            session.wait(args.delay / 1000)
            session.set_phase('running')
            writer = Wtype(session, args.newlines)
            for index, part in enumerate(parts):
                session.wait(0)
                writer.emit(part)
                session.completed = index + 1
                if index + 1 < len(parts):
                    session.wait(delay_after(part, parts[index + 1], args.interval, args.variation, args.punctuation, args.sentence, args.paragraph))
            session.state = 'completed'
            print(json.dumps(session.command('status')))
    except Stopped as error:
        session.state = 'cancelled'
        print(str(error), file=sys.stderr)
        return 2
    except Exception:
        session.state = 'failed'
        raise
    finally:
        guard.close()
        for sig, handler in previous_handlers.items():
            signal.signal(sig, handler)
    return 0

def bounded(low, high):
    def parse(value):
        value = int(value)
        if not low <= value <= high:
            raise argparse.ArgumentTypeError(f'Expected {low}..{high}')
        return value
    return parse

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    start = commands.add_parser('run', help='Type clipboard text into the already focused Niri window')
    for name, default, low, high in (('interval',30,0,500),('delay',800,300,5000),('variation',0,0,90),('punctuation',0,0,5000),('sentence',0,0,5000),('paragraph',0,0,10000)):
        start.add_argument('--' + name, default=default, type=bounded(low, high))
    start.add_argument('--newlines', choices=('space','shift-enter','enter'), default='space')
    for action in ('pause','resume','stop','status'):
        commands.add_parser(action)
    commands.add_parser('doctor', help='Check dependencies and environment; does not test the Wayland virtual-keyboard protocol')
    args = parser.parse_args(argv)
    try:
        if args.command == 'doctor':
            report = dict(platform=sys.platform, wayland=bool(os.environ.get('WAYLAND_DISPLAY')), niri_socket=bool(os.environ.get('NIRI_SOCKET')), wtype=bool(shutil.which('wtype')), wl_paste=bool(shutil.which('wl-paste')))
            print(json.dumps(report))
            return 0 if all(report[key] for key in ('wayland','niri_socket','wtype','wl_paste')) and sys.platform.startswith('linux') else 1
        if args.command == 'run':
            return run(args)
        print(json.dumps(control(args.command)))
        return 0
    except (OSError, RuntimeError, ValueError, subprocess.SubprocessError) as error:
        print('ClipType: ' + str(error), file=sys.stderr)
        return 1

if __name__ == '__main__':
    raise SystemExit(main())
