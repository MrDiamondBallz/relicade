"""Local-only preview for the Relicade brand and interaction prototype.

Run `python tools/preview.py`. No installer, game launch, telemetry, or writes.
The research directory and executable evidence are never served.
"""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote, urlsplit
from typing import cast
import argparse


class PreviewHandler(SimpleHTTPRequestHandler):
    def __init__(self, *args, root: Path, **kwargs):
        self.root = root.resolve()
        super().__init__(*args, directory=str(self.root), **kwargs)

    def log_message(self, format, *args):
        # Keep browsing history out of persistent logs.
        pass

    def _allowed_host(self):
        port = cast(ThreadingHTTPServer, self.server).server_port
        return self.headers.get('Host', '').lower() in {
            f'127.0.0.1:{port}', f'localhost:{port}',
        }

    def _resolve_asset(self):
        path = unquote(urlsplit(self.path).path)
        if '\\' in path or '\x00' in path:
            return None
        parts = Path(path.lstrip('/')).parts
        if not parts or parts[0] not in {'prototype', 'brand'}:
            return None
        if any(p in {'.', '..'} or p.startswith('.') for p in parts):
            return None
        target = (self.root / Path(*parts)).resolve()
        allowed = self.root / parts[0]
        if allowed.resolve() != allowed or not target.is_relative_to(allowed):
            return None
        if target.is_dir():
            for index in ('index.html', 'index.htm'):
                candidate = target / index
                if candidate.is_file() and not candidate.resolve().is_relative_to(allowed):
                    return None
        return target

    def send_head(self):
        if not self._allowed_host():
            self.send_error(403, 'Local preview only')
            return None
        if self.path == '/':
            self.send_response(302)
            self.send_header('Location', '/prototype/')
            self.send_header('Content-Length', '0')
            self.end_headers()
            return None
        if self._resolve_asset() is None:
            self.send_error(404, 'Not found')
            return None
        return super().send_head()

    def translate_path(self, path):
        target = self._resolve_asset()
        return str(target) if target is not None else str(self.root / '.not-served')

    def list_directory(self, path):
        self.send_error(404, 'Not found')
        return None

    def end_headers(self):
        self.send_header('X-Content-Type-Options', 'nosniff')
        self.send_header('Referrer-Policy', 'no-referrer')
        self.send_header('Cache-Control', 'no-store')
        if urlsplit(self.path).path == '/prototype/art-worker.js':
            self.send_header('Content-Security-Policy',
                             "default-src 'none'; script-src 'self'; connect-src https://cdn2.steamgriddb.com")
        else:
            self.send_header('Content-Security-Policy',
                         "default-src 'self'; script-src 'self'; "
                         "style-src 'self' 'unsafe-inline'; font-src 'self'; "
                         "img-src 'self' https://cdn2.steamgriddb.com; connect-src 'self'; worker-src 'self'; "
                         "object-src 'none'; frame-ancestors 'none'; "
                         "base-uri 'none'; form-action 'none'")
        super().end_headers()

    def do_POST(self):
        self.send_error(405, 'Read-only preview')

    do_PUT = do_POST
    do_PATCH = do_POST
    do_DELETE = do_POST


def make_server(root: Path, port: int = 8766):
    return ThreadingHTTPServer(('127.0.0.1', port), partial(PreviewHandler, root=root))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port', type=int, default=8766)
    args = parser.parse_args()
    if not 0 <= args.port <= 65535:
        parser.error('Port must be between 0 and 65535')
    root = Path(__file__).resolve().parents[1]
    with make_server(root, args.port) as server:
        print(f'Relicade prototype: http://127.0.0.1:{server.server_port}/prototype/', flush=True)
        print(f'Brand board: http://127.0.0.1:{server.server_port}/brand/', flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == '__main__':
    main()
