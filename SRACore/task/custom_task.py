import importlib.util
import json
import sys
from pathlib import Path

from SRACore.task import BaseTask
from SRACore.util.const import AppDataDir
from SRACore.util.logger import logger


CUSTOM_TASK_PREFIX = "CustomTask_"


def is_custom_task_name(task_name: str) -> bool:
    return task_name.startswith(CUSTOM_TASK_PREFIX)


def get_custom_tasks(config: dict, enabled_only: bool = False) -> dict[str, dict]:
    custom_tasks = config.get("customTasks", config.get("CustomTasks", []))
    result: dict[str, dict] = {}
    for task in custom_tasks:
        if not isinstance(task, dict):
            continue
        task_id = task.get("Id")
        if not task_id:
            continue
        if enabled_only and not task.get("IsEnabled", False):
            continue
        result[f"{CUSTOM_TASK_PREFIX}{task_id}"] = task
    return result


def load_custom_task(task_config_entry: dict, operator, config: dict) -> BaseTask | None:
    script_id = task_config_entry.get("ScriptId", "")
    task_entry = task_config_entry.get("TaskEntry", "main.py")
    task_class_name = task_config_entry.get("TaskClassName", "")
    script_dir = AppDataDir / "scripts" / script_id
    entry_path = script_dir / task_entry
    if not entry_path.exists():
        logger.error(f"Custom task script not found: {entry_path}")
        return None

    task_class = _load_task_class(script_id, task_entry, task_class_name, script_dir, entry_path)
    if task_class is None:
        return None

    task_config = dict(config)
    task_config["_task_params"] = load_custom_task_params(task_config_entry, script_dir)
    task_config["_task_name"] = task_config_entry.get("Name", script_id)
    task_instance = task_class(operator, task_config)
    task_instance._sra_task_key = f"{CUSTOM_TASK_PREFIX}{task_config_entry.get('Id', '')}"
    return task_instance


def load_custom_task_params(task_config_entry: dict, script_dir: Path) -> dict:
    params = {}
    config_path = script_dir / "config.json"
    if config_path.exists():
        try:
            with open(config_path, "r", encoding="utf-8") as f:
                data = json.load(f)
            if isinstance(data, dict):
                params.update(data)
        except Exception as e:
            logger.warning(f"Failed to read custom task config: {config_path}, {e}")

    task_params = task_config_entry.get("Params", {})
    if isinstance(task_params, dict):
        params.update(task_params)
    return params


def _load_task_class(script_id: str, task_entry: str, task_class_name: str, script_dir: Path, entry_path: Path):
    scripts_dir = AppDataDir / "scripts"
    module_name = f"_sra_script_{script_id}_{task_entry.replace('.py', '')}"
    if str(script_dir) not in sys.path:
        sys.path.insert(0, str(script_dir))
    if str(scripts_dir) not in sys.path:
        sys.path.insert(0, str(scripts_dir))

    try:
        spec = importlib.util.spec_from_file_location(module_name, entry_path)
        if spec is None or spec.loader is None:
            logger.error(f"Failed to load custom task module: {entry_path}")
            return None
        module = importlib.util.module_from_spec(spec)
        sys.modules[module_name] = module
        spec.loader.exec_module(module)
        return getattr(module, task_class_name)
    except Exception as e:
        sys.modules.pop(module_name, None)
        logger.error(f"Failed to load custom task {task_class_name}: {e}")
        return None
