#!/usr/bin/env python
"""pvu: helpers del Unity CLI para Primer Volante.

Envuelve `unity command ...` para que compilar, correr verificaciones, la suite NUnit y escenarios de Play Mode
sea una sola llamada que espera sola hasta que termina (sondeando el estado del Editor) y devuelve un resumen corto.
La idea es no usar nunca `sleep N` a ciegas ni reescribir estos scripts en cada tarea.

Uso (desde cualquier carpeta del repo):
    python tools/unity/pvu.py status
    python tools/unity/pvu.py wait
    python tools/unity/pvu.py compile
    python tools/unity/pvu.py eval archivo.cs          |  eval --code 'return 1+1;'
    python tools/unity/pvu.py console [--since N] [--level warn] [--grep RE] [--tail N]
    python tools/unity/pvu.py cursor
    python tools/unity/pvu.py menu "Tools/Primer Volante/Verify X"
    python tools/unity/pvu.py tests [WrongWay TurnSignalInfraction ...]   (sin args: suite completa)
    python tools/unity/pvu.py play start|stop
    python tools/unity/pvu.py scenario escenario.cs [--timeout 180]

Código de salida: 0 = OK, 1 = falló (compilación, prueba, timeout), 2 = Editor no disponible.
"""
import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
import time

PROJECT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'primerVolante'))
SUITE_CLASS = 'PrimerVolante.Testing.Editor.VerificationSuiteTests'
SUMMARY_RE = r'\d+\s*/\s*\d+'
READY_STATES = ('ready', 'playing', 'paused')
TRANSIENT = ('timed out', 'ECONNREFUSED', 'ECONNRESET', 'socket hang up', 'not connected', 'No running',
             'domain reload', 'Bad Gateway', 'Service Unavailable', 'connect')

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')


class UnityError(Exception):
    pass


def log(msg):
    print(msg, flush=True)


def unity(cmd, *args, timeout=60, retries=20, retry_delay=1.5):
    """Corre `unity command <cmd> ... --result-only` y devuelve el JSON. Reintenta errores transitorios
    (Editor ocupado, recarga de dominio, reconexión)."""
    full = ['unity', 'command', cmd, *[str(a) for a in args], '--result-only']
    env = dict(os.environ, UNITY_NO_BANNER='1', UNITY_NO_PAGER='1', NO_COLOR='1', UNITY_NON_INTERACTIVE='1')
    last = ''
    for _ in range(max(1, retries)):
        try:
            p = subprocess.run(full, cwd=PROJECT, capture_output=True, text=True, encoding='utf-8',
                               errors='replace', timeout=timeout, env=env)
        except subprocess.TimeoutExpired:
            last = f'timeout de {timeout}s esperando `unity command {cmd}`'
            time.sleep(retry_delay)
            continue
        out = (p.stdout or '').strip()
        try:
            data = json.loads(out) if out else None
        except json.JSONDecodeError:
            data = None
        if isinstance(data, dict) and data.get('success') is False and 'errors' in data and 'data' in data:
            last = '; '.join(e.get('message', '') for e in data.get('errors') or []) or out
            if any(t.lower() in last.lower() for t in TRANSIENT):
                time.sleep(retry_delay)
                continue
            raise UnityError(last)
        if data is None:
            last = (out + '\n' + (p.stderr or '')).strip()
            if p.returncode != 0 and any(t.lower() in last.lower() for t in TRANSIENT):
                time.sleep(retry_delay)
                continue
            if p.returncode != 0:
                raise UnityError(last or f'`unity command {cmd}` salió con código {p.returncode}')
            return out
        return data
    raise UnityError(f'`unity command {cmd}` no respondió: {last}')


def editor_status():
    return unity('editor_status', retries=3)


def console_cursor():
    s = unity('console_status')
    return s.get('cursor', -1), s.get('session')


def console_since(cursor, session=None, level='log', tail=1000):
    args = ['--since', cursor, '--tail', tail, '--level', level]
    if session:
        args += ['--since_session', session]
    return unity('console', *args).get('entries', [])


def first_line(msg, limit=400):
    m = re.sub(r'</?color[^>]*>', '', (msg or '').strip().splitlines()[0] if msg else '')
    return m if len(m) <= limit else m[:limit] + '…'


def wait_ready(timeout=300, quiet=False):
    """Espera a que el Editor esté listo: sin compilar ni recargar dominio."""
    t0 = time.time()
    last = ''
    while time.time() - t0 < timeout:
        try:
            s = editor_status()
            if s.get('status') in READY_STATES and not s.get('compiling') and not s.get('domainReloadInProgress'):
                if not quiet:
                    log(f'Editor listo ({time.time() - t0:.0f}s, playMode={s.get("playMode")})')
                return s
            last = f'status={s.get("status")} compiling={s.get("compiling")}'
        except UnityError as e:
            last = str(e)
            if 'Invalid arguments' in last or 'unknown' in last.lower():
                raise
        time.sleep(1)
    raise UnityError(f'el Editor no quedó listo en {timeout}s ({last})')


# ---------------------------------------------------------------- comandos

def cmd_status(a):
    s = editor_status()
    c = unity('console_status').get('groundTruth', {})
    log(f'status={s.get("status")} playMode={s.get("playMode")} compiling={s.get("compiling")} '
        f'domainReload={s.get("domainReloadInProgress")} compilationFailed={c.get("compilationFailed")} '
        f'consola: {c.get("consoleErrors")} errores, {c.get("consoleWarnings")} warnings')
    return 0


def cmd_wait(a):
    wait_ready(a.timeout)
    return 0


def cmd_compile(a):
    """Importa los scripts cambiados, recompila, espera y reporta errores y warnings de C# nuevos."""
    wait_ready(a.timeout, quiet=True)
    cursor, session = console_cursor()
    t0 = time.time()
    run_eval_code('UnityEditor.AssetDatabase.Refresh(); return 0;', quiet=True)
    unity('recompile')
    while time.time() - t0 < a.timeout:
        try:
            st = unity('recompile_status', retries=3)
            if st.get('status') in ('completed', 'up_to_date', 'idle'):
                break
        except UnityError:
            pass
        time.sleep(1)
    else:
        log(f'TIMEOUT: la compilación no terminó en {a.timeout}s')
        return 1
    wait_ready(a.timeout, quiet=True)
    st = unity('recompile_status')
    failed = bool(st.get('failed') or st.get('compilationFailed'))
    entries = console_since(cursor, session, level='warn')
    errors = [e for e in entries if re.search(r'error CS\d+', e.get('message', ''))]
    warns = [e for e in entries if re.search(r'warning CS\d+', e.get('message', ''))]
    printed = set()

    def once(prefix, msg):
        line = first_line(msg)
        if line not in printed:
            printed.add(line)
            log(prefix + line)

    for e in st.get('errors') or []:
        once('ERROR ', e if isinstance(e, str) else json.dumps(e, ensure_ascii=False))
    for e in errors:
        once('ERROR ', e['message'])
    own = [w for w in warns if re.match(r'\s*Assets[\\/]', w.get('message', ''))]
    for w in own:
        once('WARNING ', w['message'])
    own = [m for m in printed if m.startswith('Assets') and 'warning CS' in m]
    if failed or errors:
        log(f'COMPILACIÓN FALLÓ ({time.time() - t0:.0f}s)')
        return 1
    log(f'Compilación OK en {time.time() - t0:.0f}s ({st.get("status")}), {len(own)} warnings de C# en Assets/')
    return 0


def run_eval_code(code, timeout_ms=30000, quiet=False):
    fd, path = tempfile.mkstemp(suffix='.cs', prefix='pvu_')
    with os.fdopen(fd, 'w', encoding='utf-8') as f:
        f.write(code)
    try:
        return run_eval_file(path, timeout_ms, quiet)
    finally:
        os.remove(path)


def run_eval_file(path, timeout_ms=30000, quiet=False):
    r = unity('eval_file', '--file', os.path.abspath(path), '--timeout', timeout_ms,
              timeout=timeout_ms / 1000 + 30)
    if not isinstance(r, dict):
        raise UnityError(f'respuesta inesperada de eval_file: {r}')
    ok = r.get('success', False)
    if not quiet or not ok:
        for d in r.get('diagnostics') or []:
            if d.get('severity') == 'error':
                log(f'  {d.get("id")} línea {d.get("line")}: {d.get("message")}')
        if r.get('output'):
            log(str(r['output']).rstrip())
        if ok:
            log(f'result: {json.dumps(r.get("result"), ensure_ascii=False)}')
        else:
            log(f'EVAL FALLÓ: {r.get("error")} {r.get("errorDetails") or ""}'.rstrip())
    if not ok and quiet:
        raise UnityError(f'eval falló: {r.get("error")}')
    return r


def cmd_eval(a):
    wait_ready(120, quiet=True)
    if a.code is not None:
        r = run_eval_code(a.code, a.timeout_ms)
    elif a.file:
        r = run_eval_file(a.file, a.timeout_ms)
    else:
        log('pasá un archivo .cs o --code')
        return 1
    return 0 if r.get('success') else 1


def print_entries(entries, grep=None, stack=False):
    for e in entries:
        msg = e.get('message', '')
        if grep and not re.search(grep, msg):
            continue
        log(f'[{e.get("seq")}] {e.get("level", "log")[:5]:5s} {first_line(msg)}')
        if stack and e.get('stackTrace'):
            log('      ' + e['stackTrace'].strip().replace('\n', '\n      '))


def cmd_console(a):
    if a.since is not None:
        entries = console_since(a.since, a.session, a.level, a.tail)
    else:
        entries = unity('console', '--tail', a.tail, '--level', a.level).get('entries', [])
    print_entries(entries, a.grep, a.stack)
    cursor, session = console_cursor()
    log(f'cursor={cursor} session={session}')
    return 0


def cmd_cursor(a):
    cursor, session = console_cursor()
    log(f'{cursor} {session}')
    return 0


def poll_console(cursor, session, done_re, timeout, fail_re=None):
    """Junta entradas de consola nuevas hasta que una coincide con done_re (o timeout)."""
    seen, t0 = [], time.time()
    while time.time() - t0 < timeout:
        new = console_since(cursor, session)
        if new:
            seen.extend(new)
            cursor = new[-1].get('seq', cursor)
            if any(re.search(done_re, e.get('message', '')) for e in new):
                return seen, True
            if fail_re and any(re.search(fail_re, e.get('message', '')) for e in new):
                return seen, True
        time.sleep(1)
    return seen, False


def summarize_run(entries, done, show_all):
    errors = [e for e in entries if e.get('level') == 'error']
    for e in entries:
        msg = e.get('message', '')
        interesting = (e.get('level') in ('error', 'warn') or re.search(SUMMARY_RE, msg)
                       or re.search(r'\[TEST\]|FAIL|❌', msg))
        if show_all or interesting:
            log(f'{e.get("level", "log")[:5]:5s} {first_line(msg)}')
    ok = done and not errors
    log(f'{"OK" if ok else "FALLÓ"}: {len(entries)} líneas de consola, {len(errors)} errores'
        + ('' if done else ', TIMEOUT sin línea de resumen'))
    return 0 if ok else 1


def cmd_menu(a):
    wait_ready(120, quiet=True)
    cursor, session = console_cursor()
    unity('menu', '--path', a.path, timeout=a.timeout + 30, retries=1)
    entries, done = poll_console(cursor, session, a.done, a.timeout)
    return summarize_run(entries, done, a.all)


def cmd_tests(a):
    """Corre suites de VerificationSuiteTests (o un filtro libre) y espera el resultado sin sleeps fijos."""
    filters = a.names or ['']
    total_rc = 0
    for name in filters:
        flt = a.filter or (f'{SUITE_CLASS}.{name}' if name else SUITE_CLASS)
        wait_ready(300, quiet=True)
        t0 = time.time()
        unity('run_tests', '--mode', 'editor', '--filter', flt, '--async_tests', 'true', '--timeout', a.timeout)
        st, started = {}, False
        while time.time() - t0 < a.timeout + 60:
            time.sleep(2)
            try:
                st = unity('test_status', retries=3)
            except UnityError:
                continue
            status = st.get('status')
            if status in ('running', 'queued', 'starting', 'pending'):
                started = True
                continue
            # un 'completed' anterior a nuestro run puede aparecer antes de que arranque
            names = [r.get('FullName', '') for r in st.get('results') or []]
            ours = bool(names) and all(flt.lower() in n.lower() for n in names)
            if not ours and time.time() - t0 < 30:
                continue
            if not started and not ours and time.time() - t0 < 8:
                continue
            break
        s = st.get('summary') or {}
        failed = [r for r in st.get('results') or [] if r.get('Status') not in ('Passed', 'Skipped')]
        for r in failed:
            log(f'FAIL {r.get("FullName", "").replace(SUITE_CLASS + ".", "")}: '
                f'{first_line(r.get("Message") or "", 600)}')
        label = name or a.filter or 'suite completa'
        log(f'{label}: {st.get("status")} {s.get("passed", 0)}/{s.get("total", 0)} pasaron, '
            f'{s.get("failed", 0)} fallaron ({time.time() - t0:.0f}s)')
        if st.get('status') != 'completed' or s.get('failed') or not s.get('total'):
            total_rc = 1
    # Alguna verificación existente deja Time.timeScale != 1 y Unity lo guarda en ProjectSettings/TimeManager.asset.
    try:
        r = run_eval_code('var ts = UnityEngine.Time.timeScale; UnityEngine.Time.timeScale = 1f; return ts;', quiet=True)
        if r.get('result') not in (1, 1.0):
            log(f'AVISO: los tests dejaron Time.timeScale={r.get("result")}; se restauró a 1. Revisá que '
                'ProjectSettings/TimeManager.asset no quede modificado (git checkout) y no lo commitees.')
    except UnityError:
        pass
    return total_rc


def set_play(on, timeout=90):
    target = 'playing' if on else 'stopped'
    s = wait_ready(120, quiet=True)
    if s.get('playMode') != target:
        unity('editor_play' if on else 'editor_stop', retries=3)
    t0 = time.time()
    while time.time() - t0 < timeout:
        try:
            s = editor_status()
            if s.get('playMode') == target and s.get('status') in READY_STATES and not s.get('domainReloadInProgress'):
                return True
        except UnityError:
            pass
        time.sleep(1)
    raise UnityError(f'Play Mode no llegó a "{target}" en {timeout}s')


REMOVE_TEST_DEVICES = '''
var devices = new System.Collections.Generic.List<UnityEngine.InputSystem.InputDevice>(UnityEngine.InputSystem.InputSystem.devices);
var removed = 0;
foreach (var d in devices)
    if (!d.native && d is UnityEngine.InputSystem.Gamepad) { UnityEngine.InputSystem.InputSystem.RemoveDevice(d); removed++; }
return removed;
'''


def cmd_play(a):
    set_play(a.state == 'start')
    if a.state == 'stop':
        run_eval_code(REMOVE_TEST_DEVICES, quiet=True)
    log(f'playMode={"playing" if a.state == "start" else "stopped"}')
    return 0


def cmd_scenario(a):
    """Entra a Play Mode, registra el escenario (eval_file) y lee líneas [TEST] hasta '[TEST] DONE'."""
    if a.scene:
        set_play(False)
        run_eval_code(f'UnityEditor.SceneManagement.EditorSceneManager.OpenScene("{a.scene}"); return 0;', quiet=True)
    set_play(True)
    try:
        time.sleep(a.settle)
        cursor, session = console_cursor()
        run_eval_file(a.file, 30000, quiet=True)
        entries, done = poll_console(cursor, session, r'\[TEST\] DONE', a.timeout)
        rc = summarize_run(entries, done, a.all)
        if any(re.search(r'\[TEST\].*FAIL', e.get('message', '')) for e in entries):
            rc = 1
    finally:
        if not a.keep_playing:
            set_play(False)
            run_eval_code(REMOVE_TEST_DEVICES, quiet=True)
    return rc


def main():
    p = argparse.ArgumentParser(prog='pvu', description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest='cmd', required=True)

    sub.add_parser('status', help='estado del Editor y de la consola').set_defaults(fn=cmd_status)

    s = sub.add_parser('wait', help='esperar a que el Editor esté listo')
    s.add_argument('--timeout', type=int, default=300)
    s.set_defaults(fn=cmd_wait)

    s = sub.add_parser('compile', help='refresh + recompilar + esperar + reportar errores/warnings CS')
    s.add_argument('--timeout', type=int, default=600)
    s.set_defaults(fn=cmd_compile)

    s = sub.add_parser('eval', help='ejecutar C# en el Editor (archivo o --code)')
    s.add_argument('file', nargs='?')
    s.add_argument('--code')
    s.add_argument('--timeout-ms', type=int, default=30000)
    s.set_defaults(fn=cmd_eval)

    s = sub.add_parser('console', help='leer la consola (una línea por entrada, sin stack)')
    s.add_argument('--since', type=int)
    s.add_argument('--session')
    s.add_argument('--level', default='log', choices=['log', 'warn', 'error'])
    s.add_argument('--grep')
    s.add_argument('--tail', type=int, default=50)
    s.add_argument('--stack', action='store_true')
    s.set_defaults(fn=cmd_console)

    sub.add_parser('cursor', help='cursor actual de la consola').set_defaults(fn=cmd_cursor)

    s = sub.add_parser('menu', help='ejecutar un MenuItem de verificación y esperar su línea "N/M"')
    s.add_argument('path')
    s.add_argument('--timeout', type=int, default=180)
    s.add_argument('--done', default=SUMMARY_RE, help='regex que marca el fin (default: "N/M")')
    s.add_argument('--all', action='store_true', help='mostrar todas las líneas, no solo errores/resumen')
    s.set_defaults(fn=cmd_menu)

    s = sub.add_parser('tests', help='correr suites de VerificationSuiteTests por nombre (sin args: todas)')
    s.add_argument('names', nargs='*', help='nombres de suite, ej. WrongWay TurnSignalInfraction')
    s.add_argument('--filter', help='filtro libre de run_tests en vez de nombres de suite')
    s.add_argument('--timeout', type=int, default=900)
    s.set_defaults(fn=cmd_tests)

    s = sub.add_parser('play', help='entrar/salir de Play Mode y esperar el cambio')
    s.add_argument('state', choices=['start', 'stop'])
    s.set_defaults(fn=cmd_play)

    s = sub.add_parser('scenario', help='escenario de Play Mode (ver tools/unity/templates/DrivingScenario.cs)')
    s.add_argument('file')
    s.add_argument('--scene', help='escena a abrir antes de entrar a Play Mode (ruta Assets/...)')
    s.add_argument('--timeout', type=int, default=180)
    s.add_argument('--settle', type=float, default=2.0, help='segundos a esperar tras entrar a Play Mode')
    s.add_argument('--keep-playing', action='store_true')
    s.add_argument('--all', action='store_true')
    s.set_defaults(fn=cmd_scenario)

    a = p.parse_args()
    try:
        return a.fn(a)
    except UnityError as e:
        log(f'ERROR Unity CLI: {e}')
        return 2


if __name__ == '__main__':
    sys.exit(main())
