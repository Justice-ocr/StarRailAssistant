from __future__ import annotations

import json
import os

from SRACore.runtime import shared_runtime


def _use_runtime_dir(monkeypatch, tmp_path) -> None:
    runtime_dir = tmp_path / "runtime"
    monkeypatch.setattr(shared_runtime, "RuntimeDir", runtime_dir)
    monkeypatch.setattr(shared_runtime, "SessionFile", runtime_dir / "sra-session.json")
    monkeypatch.setattr(shared_runtime, "StopRequestFile", runtime_dir / "stop.request")
    monkeypatch.setattr(shared_runtime, "LockFile", runtime_dir / "task.lock")


def test_runtime_session_is_exclusive_and_receives_stop_requests(monkeypatch, tmp_path) -> None:
    _use_runtime_dir(monkeypatch, tmp_path)
    owner = shared_runtime.RuntimeSession("test", "run", ["Default"])
    contender = shared_runtime.RuntimeSession("test", "single", ["Default"], "StartGameTask")

    assert owner.start()
    assert not contender.start()

    shared_runtime.request_stop("pytest")
    assert owner.stop_requested()

    owner.finish()
    assert contender.start()
    contender.finish()


def test_runtime_session_releases_lock_when_initial_publish_fails(monkeypatch, tmp_path) -> None:
    _use_runtime_dir(monkeypatch, tmp_path)
    session = shared_runtime.RuntimeSession("test", "run")
    monkeypatch.setattr(session, "_write", lambda: (_ for _ in ()).throw(OSError("write failed")))

    assert not session.start()
    assert not shared_runtime.LockFile.exists()


def test_live_lock_owner_is_not_replaced_before_session_publish(monkeypatch, tmp_path) -> None:
    _use_runtime_dir(monkeypatch, tmp_path)
    shared_runtime.RuntimeDir.mkdir(parents=True)
    lock_data = {"sessionId": "starting", "pid": os.getpid(), "createdAtUnix": 0}
    shared_runtime.LockFile.write_text(json.dumps(lock_data), encoding="utf-8")

    contender = shared_runtime.RuntimeSession("test", "run")

    assert not contender.start()
    assert json.loads(shared_runtime.LockFile.read_text(encoding="utf-8")) == lock_data
