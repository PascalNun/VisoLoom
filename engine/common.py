"""Small platform primitives shared by the coordinator and isolated worker."""

import os


class EngineError(Exception):
    def __init__(self, code, message):
        self.code, self.message = code, message
        super().__init__(message)


class WorkerLock:
    """OS lock survives a crashed owner without deleting another process's lock."""
    def __init__(self, root, name=".worker.lock"):
        self.path = root / name
        self.stream = None

    def __enter__(self):
        if self.path.is_symlink():
            raise EngineError("invalidStorage", "Invalid worker lock.")
        self.stream = os.fdopen(os.open(self.path, os.O_RDWR | os.O_CREAT, 0o600), "r+b")
        try:
            if os.name == "nt":
                import msvcrt
                if self.path.stat().st_size == 0:
                    self.stream.write(b"0")
                    self.stream.flush()
                self.stream.seek(0)
                msvcrt.locking(self.stream.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(self.stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            self.stream.close()
            raise EngineError("engineBusy", "This engine is already processing a job.") from None
        return self

    def __exit__(self, *args):
        self.stream.close()
