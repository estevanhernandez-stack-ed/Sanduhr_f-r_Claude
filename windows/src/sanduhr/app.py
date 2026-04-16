"""Entry point -- bootstraps QApplication, sets Windows AppUserModelID, runs the widget."""

import logging
import logging.handlers
import os
import sys

from PySide6.QtGui import QIcon
from PySide6.QtWidgets import QApplication

from sanduhr import paths, themes
from sanduhr.widget import SanduhrWidget


def _set_app_user_model_id() -> None:
    """Group the widget under a stable taskbar identity (icon grouping + jump lists)."""
    if sys.platform != "win32":
        return
    import ctypes

    try:
        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(
            "com.626labs.sanduhr"
        )
    except Exception:
        pass


def _configure_logging() -> None:
    log_path = paths.log_file()
    handler = logging.handlers.RotatingFileHandler(
        log_path, maxBytes=1_000_000, backupCount=3, encoding="utf-8"
    )
    handler.setFormatter(
        logging.Formatter("%(asctime)s %(levelname)s %(name)s: %(message)s")
    )
    root = logging.getLogger()
    root.setLevel(logging.DEBUG if "--debug" in sys.argv else logging.INFO)
    root.addHandler(handler)

    def _excepthook(exc_type, exc, tb):
        root.exception("Unhandled exception", exc_info=(exc_type, exc, tb))
        sys.__excepthook__(exc_type, exc, tb)

    sys.excepthook = _excepthook


def _locate_icon() -> str:
    """Find Sanduhr.ico whether running from source or PyInstaller bundle."""
    meipass = getattr(sys, "_MEIPASS", None)
    if meipass:
        return os.path.join(meipass, "icon", "Sanduhr.ico")
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.join(here, "..", "..", "icon", "Sanduhr.ico")


def main() -> int:
    _configure_logging()
    _set_app_user_model_id()

    # Merge user-dropped themes from %APPDATA%\Sanduhr\themes\*.json into THEMES
    # before widget construction so the theme strip picks them up automatically.
    themes.load_user_themes()

    app = QApplication(sys.argv)
    app.setApplicationName("Sanduhr für Claude")
    app.setOrganizationName("626Labs")

    icon_path = _locate_icon()
    if os.path.exists(icon_path):
        app.setWindowIcon(QIcon(icon_path))

    widget = SanduhrWidget()
    widget.show()
    # Qt legacy alias exec_() still works on PySide6
    return app.exec_()


if __name__ == "__main__":
    raise SystemExit(main())
