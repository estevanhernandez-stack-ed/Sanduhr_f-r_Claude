"""Sanduhr Focus Mode -- a minimalist Pomodoro-style timer overlay.

Replaces the tier cards with a single glowing circle tracking a distraction-free
work block.
"""

from PySide6.QtCore import Qt, QTimer, Signal
from PySide6.QtGui import QPainter, QPen, QColor, QFont
from PySide6.QtWidgets import QWidget, QVBoxLayout, QLabel

class FocusTimerWidget(QWidget):
    finished = Signal()

    def __init__(self, theme: dict, parent=None):
        super().__init__(parent)
        self._theme = theme
        self._duration_secs = 25 * 60
        self._remaining = self._duration_secs
        
        self.setMinimumHeight(180)
        self._setup_ui()
        
        self._timer = QTimer(self)
        self._timer.setInterval(1000)
        self._timer.timeout.connect(self._tick)

    def _setup_ui(self) -> None:
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setAlignment(Qt.AlignCenter)
        
        self._lbl_time = QLabel()
        self._lbl_time.setAlignment(Qt.AlignCenter)
        self._lbl_time.setWordWrap(True)
        self._lbl_time.setAttribute(Qt.WA_TranslucentBackground, True)
        self._lbl_time.setStyleSheet(f"color: {self._theme.get('text', '#ffffff')};")
        
        font = self._lbl_time.font()
        font.setPointSize(24)
        font.setWeight(QFont.Bold)
        self._lbl_time.setFont(font)
        
        self._lbl_desc = QLabel("Deep Work Active")
        self._lbl_desc.setAlignment(Qt.AlignCenter)
        self._lbl_desc.setAttribute(Qt.WA_TranslucentBackground, True)
        self._lbl_desc.setStyleSheet(f"color: {self._theme.get('text_dim', '#aaaaaa')};")
        
        layout.addStretch()
        layout.addWidget(self._lbl_time)
        layout.addSpacing(4)
        layout.addWidget(self._lbl_desc)
        layout.addStretch()

    def apply_theme(self, theme: dict) -> None:
        self._theme = theme
        self._lbl_time.setStyleSheet(f"color: {theme.get('text', '#ffffff')};")
        self._lbl_desc.setStyleSheet(f"color: {theme.get('text_dim', '#aaaaaa')};")
        self.update()

    def start(self, minutes: int) -> None:
        self._duration_secs = minutes * 60
        self._remaining = self._duration_secs
        self._update_label()
        self._timer.start()
        self.update()

    def stop(self) -> None:
        self._timer.stop()
        self._remaining = 0
        self.update()
        
    def is_active(self) -> bool:
        return self._timer.isActive()

    def _tick(self) -> None:
        if self._remaining > 0:
            self._remaining -= 1
            self._update_label()
            self.update()
        else:
            self.stop()
            self.finished.emit()

    def _update_label(self) -> None:
        m, s = divmod(self._remaining, 60)
        self._lbl_time.setText(f"{m:02d}:{s:02d}")

    def paintEvent(self, event) -> None:
        super().paintEvent(event)
        if self._duration_secs <= 0 or not self._timer.isActive():
            return
            
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing)
        
        # Draw background ring
        bg_pen = QPen(QColor(self._theme.get("bar_bg", "#2a2a2a")))
        bg_pen.setWidth(6)
        painter.setPen(bg_pen)
        
        # Center the ring
        size = min(self.width(), self.height()) - 40
        x = (self.width() - size) / 2
        y = (self.height() - size) / 2
        
        painter.drawArc(int(x), int(y), int(size), int(size), 0, 360 * 16)
        
        # Draw progress ring
        prog_pen = QPen(QColor(self._theme.get("accent", "#60a5fa")))
        prog_pen.setWidth(6)
        prog_pen.setCapStyle(Qt.RoundCap)
        painter.setPen(prog_pen)
        
        progress = self._remaining / self._duration_secs
        # Start at top (90 degrees = 90 * 16), go clockwise (negative angle)
        start_angle = 90 * 16
        span_angle = int(-360 * progress * 16)
        
        painter.drawArc(int(x), int(y), int(size), int(size), start_angle, span_angle)
        painter.end()
