from __future__ import annotations

import io
import re
from pathlib import Path

import PIL.Image
from plyer import notification  # type: ignore

from SRACore.notification.channels.base import NotificationChannel
from SRACore.notification.models import NotificationContext
from SRACore.util.const import AppRootDir


class SystemChannel(NotificationChannel):
    name = "系统"
    enabled_attr = "isSystemEnabled"

    def send(self, context: NotificationContext) -> bool:
        fn = getattr(notification, "notify", None)
        if callable(fn):
            fn(title=context.title, message=context.message, app_name="SRA", timeout=5)
        if context.screenshot_bytes is not None:
            # 保存截图到 AppRootDir/log/screenshot/ 目录
            filename = re.sub(r'[<>:"/\\|?*\x00-\x1f]', "_", context.message).replace(" ", "_")
            filename = "notification_" + filename[:100].rstrip(". ")
            directory = Path(AppRootDir) / "log" / "screenshot"
            directory.mkdir(parents=True, exist_ok=True)
            with PIL.Image.open(io.BytesIO(context.screenshot_bytes)) as image:
                image.save(directory / f"{filename}.png")
        return True

