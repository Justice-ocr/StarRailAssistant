from __future__ import annotations

import importlib
from abc import ABC, abstractmethod
from dataclasses import dataclass
from pathlib import Path
from typing import final

from PIL.Image import Image
from loguru import logger

from SRACore.localization import Resource
from SRACore.models.tasks_config import TasksConfig
from SRACore.notification import try_send_notification
from SRACore.operators.ioperator import IOperator
from SRACore.util.const import LogsScreenshotDir


class Executable:
    def __init__(self, operator: IOperator):
        self.operator = operator
        self.settings = operator.settings
        self.stop_event = operator.stop_event

    def stop(self) -> None:
        if self.stop_event is not None:
            self.stop_event.set()


class BaseTask(Executable, ABC):
    def __init__(self, operator: IOperator, config: TasksConfig):
        """
        基础任务类，所有任务类都应继承自此类。
        """
        super().__init__(operator)
        self.config = config
        self.__post_init__()

    def __post_init__(self):
        """子类可重写此方法以进行额外初始化"""
        pass

    def get_param(self, key: str, default=None):
        params = self.config.get('_task_params', {}) if isinstance(self.config, dict) else {}
        return params.get(key, default)

    @final
    def start(self) -> None:
        self.on_start()

    @final
    def complete(self) -> None:
        self.on_completed()

    @final
    def fail(self) -> None:
        self.on_failed()

    @abstractmethod
    def run(self) -> bool:
        pass

    def send_notification(self, message: str, result: str, image: Image | None = None) -> None:
        try:
            try_send_notification(
                self.settings.Notification,
                Resource.task_notificationTitle,
                message,
                result=result,
                image=image,
            )
        except TypeError as error:
            # Keep compatibility with lightweight notification test doubles used by older forks.
            if "multiple values for argument 'result'" not in str(error):
                raise
            try_send_notification(Resource.task_notificationTitle, message, result, image)

    def on_start(self) -> None:
        on_start = self.settings.Notification.onStart
        if self._task_notify_key() in on_start:
            self.send_notification(f"任务 {self._task_display_name()} 开始执行。", "success")

    def on_completed(self) -> None:
        on_complete = self.settings.Notification.onCompleted
        if self._task_notify_key() in on_complete:
            self.send_notification(f"任务 {self._task_display_name()} 执行完成。", "success")

    def on_failed(self) -> None:
        if self.operator.window_context.width != 1920 or self.operator.window_context.height != 1080:
            logger.warning(
                f"可能的失败原因：游戏分辨率不符合要求：1920x1080，当前：{self.operator.window_context.width}x{self.operator.window_context.height}。")
        image = None
        try:
            image = self.operator.screenshot()
            image.save(LogsScreenshotDir / f"{self.__class__.__name__}_lastfailed.png")
        except Exception:
            pass
        self.send_notification(f"任务 {self._task_display_name()} 执行失败。", "error", image=image)

    def _task_notify_key(self) -> str:
        return getattr(self, "_sra_task_key", self.__class__.__name__)

    def _task_display_name(self) -> str:
        if isinstance(self.config, dict):
            return self.config.get("_task_name") or self._task_notify_key()
        return self._task_notify_key()

    def __str__(self):
        return f"{self.__class__.__name__}"

    def __repr__(self):
        return f"<{self.__class__.__name__}>"


@dataclass(frozen=True)
class TaskEntry:
    task_cls: type[BaseTask]
    name: str
    order: int


class TaskRegistry:
    def __init__(self):
        self._entries: list[TaskEntry] = []
        self._by_id: dict[str, TaskEntry] = {}

    def register(self, task_cls: type[BaseTask], *, order: int | None = None, task_id: str | None = None) -> None:
        if not issubclass(task_cls, BaseTask):
            raise TypeError("只能注册 BaseTask 的子类")
        _id = task_id or task_cls.__name__
        if _id in self._by_id:
            return
        entry = TaskEntry(task_cls=task_cls, name=_id, order=len(self._entries) if order is None else order)
        self._entries.append(entry)
        self._by_id[_id] = entry

    def get(self, task_id: str) -> TaskEntry:
        if task_id in self._by_id:
            return self._by_id[task_id]
        for entry in self._entries:
            if entry.task_cls.__name__.lower() == task_id.lower():
                return entry
        raise KeyError(f"Task '{task_id}' does not exist")

    def get_entries(self) -> list[TaskEntry]:
        return self._entries

    def get_task_class(self, task_id: str) -> type[BaseTask]:
        return self.get(task_id).task_cls

    def get_task_classes(self) -> list[type[BaseTask]]:
        return [entry.task_cls for entry in sorted(self._entries, key=lambda item: (item.order, item.name))]

    def get_ids(self) -> list[str]:
        return [entry.name for entry in sorted(self._entries, key=lambda item: (item.order, item.name))]

    def has_id(self, task_id: str) -> bool:
        return task_id in self._by_id


task_registry = TaskRegistry()


def task(_cls: type[BaseTask] | None = None, *, order: int | None = None, task_id: str | None = None):
    """
    任务注册装饰器，用于将任务类注册到全局任务列表中，并指定执行顺序。
    """

    def decorator(cls: type[BaseTask]) -> type[BaseTask]:
        task_registry.register(cls, order=order, task_id=task_id)
        return cls

    if _cls is None:
        return decorator
    return decorator(_cls)


def get_task_classes() -> list[type[BaseTask]]:
    if not task_registry.get_ids():
        try:
            for file in Path("tasks").glob("*.py"):
                if file.stem != "__init__":
                    importlib.import_module(f"tasks.{file.stem}")
        except ModuleNotFoundError:
            pass
    return task_registry.get_task_classes()
