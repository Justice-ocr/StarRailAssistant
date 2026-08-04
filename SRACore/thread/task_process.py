import dataclasses
import importlib
import os
import threading
import uuid
from typing import Any

from SRACore.localization import Resource
from SRACore.models.app_settings import AppSettings
from SRACore.notification import try_send_notification
from SRACore.operators.factory import OperatorFactory, OperatorType
from SRACore.runtime.shared_runtime import RuntimeSession
from SRACore.task import BaseTask, get_task_classes
from SRACore.task.custom_task import get_custom_tasks, is_custom_task_name, load_custom_task
from SRACore.util import encryption, sys_util  # NOQA
from SRACore.util.data_persister import load_cache, load_config
from SRACore.util.errors import ThreadStoppedError
from SRACore.util.logger import logger
from SRACore.util.task_recovery import TaskRecovery


@dataclasses.dataclass
class TaskInfo:
    sessionId: str = dataclasses.field(default_factory=lambda: uuid.uuid4().hex)
    pid: int = dataclasses.field(default_factory=os.getpid)
    mode: str = "unknown"
    configs: tuple[str, ...] = dataclasses.field(default_factory=tuple)
    task: str = "unknown"
    status: str = "stop"

class TaskManager:
    """
    任务管理器线程，负责按顺序执行多个任务（如启动游戏、体力刷取等）。
    支持通过配置动态加载任务列表，并处理任务的中断和错误。
    """

    def __init__(self, settings: AppSettings):
        """
        初始化任务管理器。
        """
        self.log_queue = None
        self._stop_event = threading.Event()
        self._runtime_watcher_stop = threading.Event()
        self._runtime_watcher_thread: threading.Thread | None = None
        self._thread: threading.Thread | None = None
        self.info = TaskInfo()
        self.task_list: list[type[BaseTask]] = get_task_classes()
        self.settings: AppSettings = settings
        self._recovery = TaskRecovery()
        logger.debug(f"Successfully load task: {self.task_list}")

    def request_stop(self) -> None:
        """请求停止当前任务执行。"""
        self._stop_event.set()

    def get_operator(self):
        optype = OperatorType.Browser if self.settings.General.isCloudGameEnabled else OperatorType.Local
        return OperatorFactory.get_operator(optype, self._stop_event)

    def _start_runtime_watcher(self, session: RuntimeSession) -> None:
        self._runtime_watcher_stop.clear()

        def watch() -> None:
            while not self._runtime_watcher_stop.wait(1.0):
                session.heartbeat()
                if session.stop_requested():
                    logger.warning(Resource.cli_task_requestStop)
                    session.mark_stopping()
                    self.request_stop()

        self._runtime_watcher_thread = threading.Thread(target=watch, daemon=True)
        self._runtime_watcher_thread.start()

    def _stop_runtime_watcher(self) -> None:
        self._runtime_watcher_stop.set()
        if self._runtime_watcher_thread is not None:
            self._runtime_watcher_thread.join(timeout=2.0)
            self._runtime_watcher_thread = None

    def is_thread_running(self) -> bool:
        """检查任务线程是否正在运行"""
        return self._thread is not None and self._thread.is_alive()

    def _run_target(self, target, *args):
        """线程执行目标函数的包装器"""
        try:
            target(*args)
        except KeyboardInterrupt:
            self.request_stop()

    def start_thread(self, target, *args):
        """启动任务线程"""
        if self.is_thread_running():
            logger.warning("Task thread is already running")
            return False
        self._thread = threading.Thread(
            target=self._run_target,
            daemon=True,
            args=(target, *args)
        )
        self._thread.start()
        logger.info("Task thread started")
        return True

    def stop_thread(self, timeout: float = 30.0):
        """停止任务线程"""
        if not self.is_thread_running():
            return
        logger.warning(Resource.cli_task_requestStop)
        self.request_stop()
        self._thread.join(timeout=timeout)
        if self._thread.is_alive():
            logger.warning(Resource.cli_task_timeout)
        else:
            logger.info(Resource.cli_task_stopped)
        self._thread = None

    def run_in_thread(self, *args: Any) -> bool:
        """在线程中运行任务（非阻塞）"""
        if self.is_thread_running():
            logger.warning(Resource.cli_task_taskAlreadyRunning)
            return False
        return self.start_thread(self.run, *args)

    def run_task_in_thread(self, task: int | str, config_name: str | None = None) -> bool:
        """在线程中运行单个任务（非阻塞）"""
        if self.is_thread_running():
            logger.warning(Resource.cli_task_taskAlreadyRunning)
            return False
        return self.start_thread(self.run_task, task, config_name)

    def run(self, *args: Any) -> None:
        """
        进程主循环：
        1. 读取配置列表（单配置或多配置）
        2. 对每个配置加载任务列表并执行
        3. 处理任务中断或失败的情况
        4. 任务失败时支持自动重试（重启游戏后从当前配置重新开始）
        """
        self._stop_event.clear()
        self._recovery.reset()
        if len(args) == 0:
            config_list = load_cache().get("ConfigNames", [])
        else:
            config_list = args

        config_names = [str(config_name) for config_name in config_list]
        session = RuntimeSession(owner="sra-cli", mode="run", config_names=config_names)
        if not session.start():
            logger.warning(Resource.cli_task_taskAlreadyRunning)
            return
        self._start_runtime_watcher(session)
        logger.debug('[Start]')
        self.info.sessionId = session.session_id
        self.info.pid = os.getpid()
        self.info.mode = "run"
        self.info.configs = tuple(config_names)
        self.info.task = "unknown"
        self.info.status = "running"
        terminal_state = "completed"
        try:
            last_operator = None
            # 支持重试的配置索引，从这里继续执行
            config_start_index = 0
            while config_start_index < len(config_list):
                if self._stop_event.is_set():
                    session.mark_stopping()
                    return
                retry_triggered = False
                for ci in range(config_start_index, len(config_list)):
                    config_name = config_list[ci]
                    if self._stop_event.is_set():
                        return
                    logger.info(Resource.task_currentConfig(config_name))

                    # 获取当前配置需要执行的任务列表
                    tasks_to_run = self.get_tasks(config_name)
                    if tasks_to_run:
                        last_operator = tasks_to_run[0].operator
                    logger.debug(f'tasks_to_run: {tasks_to_run}')
                    if not tasks_to_run:
                        logger.warning(Resource.task_noSelectedTasks(config_name))
                        continue

                    # 依次执行任务
                    task_failed = False
                    for task in tasks_to_run:
                        if self._stop_event.is_set():
                            session.mark_stopping()
                            terminal_state = "stopped"
                            return
                        try:
                            # 运行任务，如果返回 False 表示任务失败
                            logger.debug('running task: ' + str(task))
                            self.info.task = str(task)
                            session.task_name = str(task)
                            session.heartbeat()
                            # 任务开始
                            task.start()
                            if not task.run():
                                # 如果是用户主动停止，直接返回，不触发重试
                                if self._stop_event.is_set():
                                    session.mark_stopping()
                                    terminal_state = "stopped"
                                    return
                                logger.error(Resource.task_taskFailed(str(task)))
                                task.fail()
                                # 尝试重试
                                if self._recovery.should_retry():
                                    task_failed = True
                                    break
                                else:
                                    terminal_state = "failed"
                                    return
                            # 任务完成
                            task.complete()
                        except ThreadStoppedError as e:
                            logger.error(e)
                            terminal_state = "stopped"
                            return
                        except Exception as e:
                            # 如果是用户主动停止，直接返回，不触发重试
                            if self._stop_event.is_set():
                                session.mark_stopping()
                                terminal_state = "stopped"
                                return
                            # 捕获任务执行中的异常（如未处理的错误）
                            logger.exception(Resource.task_taskCrashed(str(task), str(e)))
                            task.fail()
                            # 尝试重试
                            if self._recovery.should_retry():
                                task_failed = True
                                break
                            else:
                                terminal_state = "failed"
                                return

                    if task_failed:
                        # 准备重试：杀死游戏进程并等待
                        if self._recovery.prepare_retry():
                            # 如果在等待期间用户停止了任务，直接返回
                            if self._stop_event.is_set():
                                session.mark_stopping()
                                terminal_state = "stopped"
                                return
                            # 重试时需要确保游戏已启动
                            # 如果任务列表中没有 StartGameTask，则先执行它
                            if not any(t.__class__.__name__ == 'StartGameTask' for t in tasks_to_run):
                                logger.info("重试时需要启动游戏，自动执行启动游戏任务")
                                start_game_task = self._create_start_game_task(config_name)
                                if start_game_task:
                                    try:
                                        start_game_task.start()
                                        if not start_game_task.run():
                                            logger.error("重试时启动游戏失败")
                                            terminal_state = "failed"
                                            return
                                        start_game_task.complete()
                                    except Exception as e:
                                        logger.error(f"重试时启动游戏异常: {e}")
                                        terminal_state = "failed"
                                        return
                            logger.info(Resource.task_retryFromConfig(config_name))
                            config_start_index = ci
                            retry_triggered = True
                            break  # 跳出 tasks 循环，重新开始当前配置
                        else:
                            terminal_state = "failed"
                            return

                    logger.info(Resource.task_configCompleted(config_name))
                    logger.info("=" * 50)

                if not retry_triggered:
                    break  # 所有配置执行完毕，退出重试循环
            logger.info("All tasks completed.")
            try_send_notification(
                Resource.task_notificationTitle,
                Resource.task_notificationMessage,
                image=last_operator.screenshot() if last_operator else None
            )
        except Exception as e:
            # 捕获线程主循环中的异常（如配置加载失败）
            logger.exception(Resource.task_managerCrashed(str(e)))
            terminal_state = "failed"
        finally:
            final_state = "stopped" if self._stop_event.is_set() else terminal_state
            self._stop_runtime_watcher()
            session.finish(final_state)
            self.info.status = final_state
            logger.debug("[Done]")

    def _create_start_game_task(self, config_name: str) -> BaseTask | None:
        """创建 StartGameTask 实例（用于重试时启动游戏）"""
        config = load_config(config_name)
        if config is None:
            return None
        try:
            optype = OperatorType.Browser if self.settings.General.isCloudGameEnabled else OperatorType.Local
            operator = OperatorFactory.get_operator(optype, self._stop_event)
            # StartGameTask 是第一个任务（index=0）
            if len(self.task_list) > 0:
                return self.task_list[0](operator, config)
        except Exception as e:
            logger.error(f"创建 StartGameTask 失败: {e}")
        return None

    def get_tasks(self, config_name: str) -> list[BaseTask]:
        """
        根据配置名称加载配置，并返回需要执行的任务实例列表。

        Args:
            config_name (str): 配置名称

        Returns:
            List[Executable]: 可执行任务实例列表（已过滤未选中的任务）

        Raises:
            Exception: 如果配置加载或任务实例化失败（异常会被上层捕获）
        """
        # 加载指定配置
        config = load_config(config_name)
        if config is None:
            return []
        print_config = config.to_dict()
        print_config["startGame"]["password"] = "******"
        print_config["startGame"]["username"] = "******"
        logger.debug('config: ' + str(print_config))
        # 从配置中读取任务选择列表（如 [True, False, True]）
        task_select = [config.StartGame.isEnabled,
                       config.TrailblazePower.isEnabled,
                       config.ReceiveRewards.isEnabled,
                       config.CosmicStrife.isEnabled,
                       config.MissionAccomplished.isEnabled]
        logger.debug('task_select: ' + str(task_select))
        if not task_select:
            return []
        tasks = []
        operator = self.get_operator()

        raw_config = config.to_dict() if hasattr(config, "to_dict") else {}
        task_order = raw_config.get("taskOrder", raw_config.get("TaskOrder", []))
        if task_order:
            return self._build_ordered_tasks(task_order, task_select, operator, config, raw_config)

        # 遍历 task_select，根据选择状态实例化对应任务
        for index, is_select in enumerate(task_select):
            # 检查：1. 任务被选中 2. 索引在 task_list 范围内
            if is_select and index < len(self.task_list):
                try:
                    # 实例化任务类
                    tasks.append(self.task_list[index](operator, config))
                except Exception as e:
                    logger.exception(Resource.task_instantiateFailed(index, str(e)))
        return tasks

    def _build_ordered_tasks(self, task_order, task_select, operator, config, raw_config) -> list[BaseTask]:
        tasks = []
        builtin_name_to_index = {cls.__name__: index for index, cls in enumerate(self.task_list)}
        custom_tasks_map = get_custom_tasks(raw_config, enabled_only=True)

        for task_name in task_order:
            if task_name in custom_tasks_map:
                try:
                    task_instance = load_custom_task(custom_tasks_map[task_name], operator, raw_config)
                    if task_instance:
                        tasks.append(task_instance)
                except Exception as e:
                    logger.exception(Resource.task_instantiateFailed(task_name, str(e)))
                continue

            index = builtin_name_to_index.get(task_name)
            if index is None or index >= len(task_select) or not task_select[index]:
                continue
            try:
                tasks.append(self.task_list[index](operator, config))
            except Exception as e:
                logger.exception(Resource.task_instantiateFailed(index, str(e)))
        return tasks

    def run_task(self, task: int | str, config: str | None = None) -> bool:
        """
        根据配置名称和任务索引或名称执行单个任务。

        Args:
            task (int | str): 任务索引（int）或任务类名称（str）
            config (str): 配置名称

        Returns:
            bool: 任务执行结果（成功返回 True，失败返回 False）

        Raises:
            ValueError: 如果任务未找到或配置加载失败
        """
        logger.debug('[Start]')
        if config is None:
            # 不指定配置时，使用缓存中的当前配置名称
            config = load_cache().get("CurrentConfigName")
        if config is None:
            return False
        task_name = str(task)
        logger.debug(f"run single task: config={config}, task={task}")
        self._stop_event.clear()
        session = RuntimeSession(
            owner="sra-cli", mode="single", config_names=[str(config)], task_name=task_name
        )
        if not session.start():
            logger.warning(Resource.cli_task_taskAlreadyRunning)
            return False
        self._start_runtime_watcher(session)
        self.info.sessionId = session.session_id
        self.info.pid = os.getpid()
        self.info.mode = "single"
        self.info.configs = (str(config),)
        self.info.task = task_name
        self.info.status = "running"
        terminal_state = "completed"
        task_instance = None
        try:
            # 获取任务实例
            task_instance = self.get_task(config, task_name)
            if task_instance is None:
                terminal_state = "failed"
                logger.error(Resource.task_noSuchTask(config))
                return False
            logger.debug('running task: ' + str(task_instance.__class__.__name__))
            # 单次运行：开始通知
            task_instance.start()
            # 运行任务
            result = task_instance.run()
            if not result:
                logger.error(Resource.task_taskFailed(str(task_instance)))
                task_instance.fail()
                terminal_state = "failed"
            else:
                logger.info(Resource.task_taskCompleted(str(task_instance)))
                # 单次运行：完成
                task_instance.complete()
            return result
        except ThreadStoppedError as e:
            logger.error(e)
            terminal_state = "stopped"
            return False
        except Exception as e:
            logger.exception(Resource.task_taskCrashed(task, str(e)))
            if task_instance is not None:
                task_instance.fail()
            terminal_state = "failed"
            return False
        finally:
            final_state = "stopped" if self._stop_event.is_set() else terminal_state
            self._stop_runtime_watcher()
            session.finish(final_state)
            self.info.status = final_state
            logger.debug("[Done]")

    def get_task(self, config_name: str, task: str) -> BaseTask | None:
        """
        根据配置名称和任务索引或名称获取单个任务实例。

        Args:
            config_name (str): 配置名称
            task ( str): 任务索引或任务类名称（str）

        Returns:
            BaseTask: 任务实例

        Raises:
            ValueError: 如果任务未找到或配置加载失败
        """
        config = load_config(config_name)
        if config is None:
            return None
        raw_config = config.to_dict() if hasattr(config, "to_dict") else {}
        print_config = config.to_dict()
        print_config["startGame"]["password"] = "******"
        print_config["startGame"]["username"] = "******"
        logger.debug('config: ' + str(print_config))
        operator = self.get_operator()

        if is_custom_task_name(task):
            custom_task = get_custom_tasks(raw_config).get(task)
            if custom_task is None:
                logger.error(f"Custom task not found: {task}")
                return None
            return load_custom_task(custom_task, operator, raw_config)

        # 根据参数类型获取任务类
        task_class = None
        if task.isdecimal():
            index = int(task)
            if 0 <= index < len(self.task_list):
                task_class = self.task_list[index]
        else:
            for cls in self.task_list:
                if cls.__name__.lower() == task.lower():
                    task_class = cls
                    break
            else:
                task_class = importlib.import_module(f"tasks.{task}").__getattribute__(task)
        if task_class is None:
            return None
        try:
            return task_class(operator, config)
        except Exception as e:
            logger.error(Resource.task_instantiateFailed(task, f'{e.__class__.__name__}: {e}'))
            return None
