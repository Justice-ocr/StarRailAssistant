import io
import json
import os
import subprocess
import sys
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock

import cmd2
import pytest

from SRACore.cli2 import SRACli
from SRACore.extension import ExtensionConfigManager, load_extensions
from SRACore.models.app_settings import AppSettings
from SRACore.task import TaskEntry
from SRACore.thread.runner import RuntimeInfo
from tasks.taskcli import CurrencyWarsCommands, TrailblazePowerCommands


@pytest.fixture
def cli(monkeypatch, tmp_path):
    load_extensions()
    monkeypatch.setattr(ExtensionConfigManager, "DEFAULT_PATH", tmp_path / "extensions.json")
    app = SRACli.__new__(SRACli)
    cmd2.Cmd.__init__(
        app,
        stdout=io.StringIO(),
        allow_cli_args=False,
        auto_load_commands=False,
        command_sets=[CurrencyWarsCommands(), TrailblazePowerCommands()],
    )
    app._use_json = False
    app.settings_service = SimpleNamespace(settings=AppSettings())
    app.task_manager = Mock(info=RuntimeInfo())
    app.trigger_manager = Mock()
    app.extension_runner = Mock()
    app.event_listener = Mock()
    app.extension_config_manager = ExtensionConfigManager()
    return app


def run_json(cli, command):
    cli.stdout.seek(0)
    cli.stdout.truncate(0)
    assert not cli.onecmd_plus_hooks(command)
    lines = cli.stdout.getvalue().splitlines()
    assert len(lines) == 1
    result = json.loads(lines[0])
    assert isinstance(result["success"], bool)
    assert isinstance(result["message"], str)
    return result


def test_onecmd_preserves_parsed_statement(cli, monkeypatch):
    statement = cli.statement_parser.parse('task single "my task" --config "my config"')
    dispatch = Mock(return_value=False)
    monkeypatch.setattr(cmd2.Cmd, "onecmd", dispatch)

    assert not cli.onecmd(statement, add_to_history=False)

    assert dispatch.call_args.args[0] is statement
    assert dispatch.call_args.kwargs == {"add_to_history": False}


@pytest.mark.parametrize("prefix", ["\ufeff", "\ufffe", "\u00ef\u00bb\u00bf"])
def test_onecmd_keeps_bom_compatibility(cli, prefix):
    assert not cli.onecmd(prefix + "version")
    assert cli.stdout.getvalue().strip()


def test_task_list_is_sorted_and_loads_tasks(cli, monkeypatch):
    from SRACore import task

    class ExampleTask:
        """Example description."""

    entries = [
        TaskEntry(ExampleTask, "second", 2),
        TaskEntry(ExampleTask, "first-b", 1),
        TaskEntry(ExampleTask, "first-a", 1),
    ]
    load = Mock()
    monkeypatch.setattr(task, "get_task_classes", load)
    monkeypatch.setattr(task.task_registry, "get_entries", lambda: entries)

    result = run_json(cli, "task list --json")

    load.assert_called_once_with()
    assert result["success"]
    assert [item["id"] for item in result["data"]] == ["first-a", "first-b", "second"]
    assert result["data"][0]["class"] == "ExampleTask"
    assert result["data"][0]["doc"] == "Example description."


def test_empty_task_list_still_returns_json(cli, monkeypatch):
    from SRACore import task

    monkeypatch.setattr(task, "get_task_classes", lambda: None)
    monkeypatch.setattr(task.task_registry, "get_entries", lambda: [])
    assert run_json(cli, "task list --json")["data"] == []


@pytest.mark.parametrize(
    ("command", "data_type"),
    [
        ("task status --json", dict),
        ("extension list --json", list),
        ("extension schema AutoPlot --json", dict),
        ("extension config get AutoPlot --json", dict),
        ("strategy list --json", list),
        ("tpconfig --json", dict),
    ],
)
def test_frontend_queries_return_response_envelope(cli, command, data_type):
    result = run_json(cli, command)
    assert result["success"]
    assert isinstance(result["data"], data_type)


@pytest.mark.parametrize(
    "command",
    [
        "extension schema missing-extension --json",
        "extension config get missing-extension --json",
    ],
)
def test_unknown_extension_returns_json_error(cli, command):
    assert not run_json(cli, command)["success"]


def test_json_mode_resets_and_exit_dispatches(cli):
    assert run_json(cli, "task status --json")["success"]
    cli.stdout.seek(0)
    cli.stdout.truncate(0)

    assert not cli.onecmd_plus_hooks("task status")
    assert "Session ID:" in cli.stdout.getvalue()
    assert not cli._use_json
    assert cli.onecmd_plus_hooks("exit")
    cli.task_manager.stop_thread.assert_called_once()
    cli.trigger_manager.stop_thread.assert_called_once()
    cli.event_listener.stop.assert_called_once()


@pytest.mark.parametrize("startup", [False, True])
def test_readonly_commands_in_real_process(tmp_path, startup):
    root = Path(__file__).resolve().parents[1]
    env = {
        **os.environ,
        "APPDATA": str(tmp_path),
        "HOME": str(tmp_path),
        "PYTHONUTF8": "1",
        "PYTHONIOENCODING": "utf-8",
    }
    app_data = tmp_path / "SRA"
    app_data.mkdir(parents=True, exist_ok=True)
    (app_data / "settings.json").write_text("{}", encoding="utf-8")
    commands = ["task list --json", "extension list --json", "exit"]
    args = [sys.executable, "main.py", "--inline", "--no-admin"]
    if startup:
        args.extend(["--command", " + ".join(commands)])
    result = subprocess.run(
        args,
        cwd=root,
        env=env,
        input="" if startup else "\n".join(commands) + "\n",
        capture_output=True,
        text=True,
        encoding="utf-8",
        timeout=30,
        check=False,
    )

    assert result.returncode == 0, result.stderr
    responses = [json.loads(line) for line in result.stdout.splitlines() if line.startswith("{")]
    assert len(responses) == 2, result.stdout
    assert all(response["success"] for response in responses)
    assert "StartGameTask" in {entry["id"] for entry in responses[0]["data"]}
    assert {"AutoPlot", "WarpForecast"} <= {entry["id"] for entry in responses[1]["data"]}
