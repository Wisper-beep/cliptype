import importlib.util
import json
import os
from pathlib import Path
import queue
import socket
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('cliptype', Path(__file__).resolve().parents[1] / 'cliptype.py')
cliptype = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cliptype)

class Guard:
    def __init__(self): self.changed = False
    def ensure(self):
        if self.changed: raise cliptype.Stopped('Changed')

class CoreTests(unittest.TestCase):
    def test_unicode_and_normalization(self):
        self.assertEqual(cliptype.normalize('中\r\n文\r🙂\tX\0'), '中\n文\n🙂    X')
        self.assertEqual(cliptype.normalize('A\nB', True), 'A B')
        self.assertEqual(list(cliptype.elements('中e\u0301👩‍💻🇨🇳👍🏽')), ['中','e\u0301','👩‍💻','🇨🇳','👍🏽'])

    def test_rhythm(self):
        self.assertEqual(cliptype.delay_after('字','字',100,20,sample=0), .08)
        self.assertEqual(cliptype.delay_after('字','字',100,20,sample=1), .12)
        self.assertEqual(cliptype.delay_after('，','字',100,punctuation=200,sample=.5), .3)
        self.assertEqual(cliptype.delay_after('。','字',100,sentence=400,sample=.5), .5)
        self.assertEqual(cliptype.delay_after('\n','\n',100,paragraph=800,sample=.5), .9)

    def test_pause_and_resume_validate_target(self):
        guard=Guard(); session=cliptype.Session(guard); session.state='running'
        self.assertEqual(session.command('pause')['state'],'paused')
        guard.changed=True
        with self.assertRaises(cliptype.Stopped): session.command('resume')
        self.assertEqual(session.state,'paused')

    def test_cancel_while_paused(self):
        session=cliptype.Session(Guard()); session.state='paused'; session.command('stop')
        with self.assertRaises(cliptype.Stopped): session.wait(10)

    def test_stop_during_focus_check_is_observed_before_returning(self):
        guard=Guard(); session=cliptype.Session(guard)
        guard.ensure=lambda: session.command('stop')
        with self.assertRaises(cliptype.Stopped): session.check()

    def test_focus_event_during_query_is_latched(self):
        with patch.object(cliptype.NiriGuard,'focused_id',return_value=12):
            guard=cliptype.NiriGuard('unused')
        def focus_query():
            guard.lost.set()
            return 12
        with patch.object(guard,'focused_id',side_effect=focus_query):
            with self.assertRaises(cliptype.Stopped): guard.ensure()

    def test_focus_query_failure_permanently_stops_guard(self):
        with patch.object(cliptype.NiriGuard,'focused_id',return_value=12):
            guard=cliptype.NiriGuard('unused')
        with patch.object(guard,'focused_id',side_effect=OSError('disconnected')):
            with self.assertRaises(cliptype.Stopped): guard.ensure()
        self.assertTrue(guard.lost.is_set())
        with patch.object(guard,'focused_id',return_value=12) as query:
            with self.assertRaises(cliptype.Stopped): guard.ensure()
            query.assert_not_called()

    def test_pause_survives_countdown_to_running_transition(self):
        session=cliptype.Session(Guard()); session.set_phase('countdown')
        session.command('pause'); session.set_phase('running')
        self.assertEqual(session.command('status')['state'],'paused')
        self.assertEqual(session.command('resume')['state'],'running')

    def test_paused_wait_freezes_active_delay(self):
        session=cliptype.Session(Guard()); session.state='running'
        finished=threading.Event()
        thread=threading.Thread(target=lambda:(session.wait(.20),finished.set()))
        thread.start(); time.sleep(.03); session.command('pause'); time.sleep(.25)
        self.assertFalse(finished.is_set()); session.command('resume'); self.assertTrue(finished.wait(1)); thread.join()

    def test_plain_text_never_enters_shell_or_argv(self):
        session=cliptype.Session(Guard())
        with patch.object(cliptype.subprocess, 'Popen') as spawn:
            process=spawn.return_value; process.returncode=0; process.poll.return_value=0
            cliptype.Wtype(session,'space').emit('中;$(bad)🙂')
            self.assertEqual(spawn.call_args.args[0],['wtype','-'])
            self.assertEqual(process.communicate.call_args.args[0], '中;$(bad)🙂'.encode())
            self.assertNotIn('shell',spawn.call_args.kwargs)

    def test_newlines_are_explicit_chords(self):
        with patch.object(cliptype.subprocess,'Popen') as spawn:
            process=spawn.return_value; process.returncode=0; process.poll.return_value=0
            cliptype.Wtype(cliptype.Session(Guard()),'shift-enter').emit('\n')
            self.assertEqual(spawn.call_args.args[0],['wtype','-M','shift','-k','Return','-m','shift'])

    def test_blank_lines_keep_paragraph_rhythm_when_rendered_as_spaces(self):
        clipboard=subprocess.CompletedProcess(['wl-paste'],0,b'A\n\nB',b'')
        with patch.object(cliptype.shutil,'which',return_value='/usr/bin/tool'), \
             patch.object(cliptype,'NiriGuard'), patch.object(cliptype,'ControlServer'), \
             patch.object(cliptype,'runtime_path',return_value=Path('unused')), \
             patch.object(cliptype.signal,'signal'), \
             patch.object(cliptype.subprocess,'run',return_value=clipboard), \
             patch.object(cliptype.Session,'wait') as wait, \
             patch.object(cliptype.subprocess,'Popen') as spawn, \
             patch.object(cliptype,'print',create=True):
            process=spawn.return_value; process.returncode=0; process.poll.return_value=0
            self.assertEqual(cliptype.main(['run','--delay','300','--interval','0','--paragraph','800']),0)
            self.assertEqual([call.args[0] for call in process.communicate.call_args_list],[b'A',b' ',b' ',b'B'])
            self.assertEqual([call.args[0] for call in wait.call_args_list].count(.8),1)

    def test_pause_between_wait_and_writer_launch_blocks_launch(self):
        session=cliptype.Session(Guard()); session.state='running'
        paused=threading.Event(); errors=[]; original_wait=session.wait
        def wait(seconds):
            original_wait(seconds)
            if not paused.is_set():
                session.command('pause'); paused.set()
        def emit():
            try: cliptype.Wtype(session,'space').emit('x')
            except Exception as error: errors.append(error)
        with patch.object(session,'wait',side_effect=wait), patch.object(cliptype.subprocess,'Popen') as spawn:
            process=spawn.return_value; process.returncode=0; process.poll.return_value=0
            thread=threading.Thread(target=emit,daemon=True); thread.start()
            try:
                self.assertTrue(paused.wait(1)); spawn.assert_not_called()
                session.command('resume'); thread.join(timeout=1)
                self.assertFalse(thread.is_alive()); self.assertEqual(errors,[])
                spawn.assert_called_once()
            finally:
                session.command('stop'); thread.join(timeout=1)

    def test_hung_writer_is_killed_on_cancel(self):
        class Process:
            returncode=None
            killed=False
            def communicate(self,*args,**kwargs):
                if not self.killed: raise subprocess.TimeoutExpired('wtype',.02)
                return b'',b''
            def poll(self): return 0 if self.killed else None
            def kill(self): self.killed=True
        process=Process(); guard=Guard(); session=cliptype.Session(guard)
        def spawn(*args,**kwargs): session.cancelled.set(); return process
        with patch.object(cliptype.subprocess,'Popen',side_effect=spawn):
            with self.assertRaises(cliptype.Stopped): cliptype.Wtype(session,'space').emit('x')
        self.assertTrue(process.killed)

    def test_cli_rejects_out_of_range_and_non_linux_is_explicit(self):
        with self.assertRaises(SystemExit): cliptype.main(['run','--interval','-1'])
        with patch.object(cliptype.sys,'platform','win32'):
            with self.assertRaises(RuntimeError): cliptype.runtime_path()

if sys.platform.startswith('linux'):
    import socketserver
    class FakeNiri:
        def __enter__(self):
            self.folder=tempfile.TemporaryDirectory(); self.path=str(Path(self.folder.name)/'niri.sock')
            self.focus=12; self.queues=[]; self.closed=threading.Event(); fake=self
            class Handler(socketserver.StreamRequestHandler):
                def handle(self):
                    request=json.loads(self.rfile.readline())
                    if request=='FocusedWindow':
                        window={'id':fake.focus} if fake.focus is not None else None
                        self.wfile.write((json.dumps({'Ok':{'FocusedWindow':window}})+'\n').encode())
                    elif request=='EventStream':
                        updates=queue.Queue(); fake.queues.append(updates)
                        self.wfile.write((json.dumps({'Ok':'Handled'})+'\n').encode())
                        self.wfile.write((json.dumps({'WindowsChanged':{'windows':[{'id':fake.focus,'is_focused':True}]}})+'\n').encode());self.wfile.flush()
                        while not fake.closed.is_set():
                            try: event=updates.get(timeout=.05)
                            except queue.Empty: continue
                            try: self.wfile.write((json.dumps(event)+'\n').encode());self.wfile.flush()
                            except OSError: return
            class Server(socketserver.ThreadingUnixStreamServer): daemon_threads=True
            self.server=Server(self.path,Handler); self.thread=threading.Thread(target=self.server.serve_forever,daemon=True);self.thread.start();return self
        def set_focus(self,value):
            self.focus=value
            for updates in self.queues: updates.put({'WindowFocusChanged':{'id':value}})
        def open_focused(self,value):
            self.focus=value
            for updates in self.queues: updates.put({'WindowOpenedOrChanged':{'window':{'id':value,'is_focused':True}}})
        def __exit__(self,*_):
            self.closed.set();self.server.shutdown();self.server.server_close();self.thread.join();self.folder.cleanup()

    class PosixTests(unittest.TestCase):
        def test_niri_initial_snapshot_and_focus_change(self):
            with FakeNiri() as niri:
                guard=cliptype.NiriGuard(niri.path).start()
                try:
                    guard.ensure();niri.set_focus(99)
                    self.assertTrue(guard.lost.wait(1))
                    with self.assertRaises(cliptype.Stopped): guard.ensure()
                finally: guard.close()

        def test_focus_away_and_back_still_aborts(self):
            with FakeNiri() as niri:
                guard=cliptype.NiriGuard(niri.path).start()
                try:
                    niri.set_focus(99);niri.set_focus(12);self.assertTrue(guard.lost.wait(1))
                    with self.assertRaises(cliptype.Stopped): guard.ensure()
                finally: guard.close()

        def test_new_focused_window_then_return_still_aborts(self):
            with FakeNiri() as niri:
                guard=cliptype.NiriGuard(niri.path).start()
                try:
                    niri.open_focused(99); niri.set_focus(12)
                    self.assertTrue(guard.lost.wait(1))
                    with self.assertRaises(cliptype.Stopped): guard.ensure()
                finally: guard.close()

        def test_control_socket_modes_and_duplicate_instance(self):
            with tempfile.TemporaryDirectory() as folder, patch.dict(os.environ,{'XDG_RUNTIME_DIR':folder}):
                runtime=cliptype.runtime_path();session=cliptype.Session(Guard());session.state='running'
                with cliptype.ControlServer(runtime,session):
                    self.assertEqual((runtime/'control.sock').stat().st_mode & 0o777,0o600)
                    self.assertEqual(runtime.stat().st_mode & 0o777,0o700)
                    self.assertEqual(cliptype.control('pause')['state'],'paused')
                    self.assertEqual(cliptype.control('resume')['state'],'running')
                    with self.assertRaises(BlockingIOError):
                        with cliptype.ControlServer(runtime,session): pass
                    self.assertTrue(cliptype.control('stop')['cancelled'])
                    self.assertEqual(set(cliptype.control('status')),{'ok','state','completed','total','cancelled'})
                self.assertFalse((runtime/'control.sock').exists())

        def test_unsafe_runtime_refused(self):
            with tempfile.TemporaryDirectory() as folder, patch.dict(os.environ,{'XDG_RUNTIME_DIR':folder}):
                target=Path(folder)/'cliptype';target.mkdir(mode=0o755)
                with self.assertRaises(RuntimeError): cliptype.runtime_path()

if __name__=='__main__': unittest.main()
