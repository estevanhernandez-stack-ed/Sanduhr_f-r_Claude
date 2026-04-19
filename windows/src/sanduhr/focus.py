"""Sanduhr Focus Mode -- a minimalist Pomodoro-style timer overlay.

Replaces the tier cards with a single glowing circle tracking a distraction-free
work block.
"""

from PySide6.QtCore import Qt, QTimer, Signal, QRectF
from PySide6.QtGui import QPainter, QPen, QColor, QFont, QBrush
from PySide6.QtWidgets import QWidget, QVBoxLayout, QLabel, QSpinBox, QHBoxLayout, QPushButton
import random

class FocusTimerWidget(QWidget):
    finished = Signal()

    def __init__(self, theme: dict, parent=None):
        super().__init__(parent)
        self._theme = theme
        self._duration_secs = 25 * 60
        self._remaining = self._duration_secs
        
        self.setMinimumHeight(240)
        self._setup_ui()
        self._init_physics()
        
        self._timer = QTimer(self)
        self._timer.setInterval(1000)
        self._timer.timeout.connect(self._tick)
        
        self._physics_timer = QTimer(self)
        self._physics_timer.setInterval(33)  # ~30fps physics update
        self._physics_timer.timeout.connect(self._physics_tick)

    def _init_physics(self):
        self._gw, self._gh = 31, 31
        self._cx, self._cy = self._gw // 2, self._gh // 2
        
        self._mask = [[False]*self._gw for _ in range(self._gh)]
        self._grid = [[False]*self._gw for _ in range(self._gh)]
        self._total_sand = 0
        
        # Build hourglass mask and spawn sand in top half
        for y in range(self._gh):
            for x in range(self._gw):
                dy = abs(y - self._cy)
                dx = abs(x - self._cx)
                if dx <= dy + 1:
                    self._mask[y][x] = True
                    if y < self._cy:
                        self._grid[y][x] = True
                        self._total_sand += 1
                        
        self._sand_passed = 0

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

        # Duration spinner row
        self._setup_row = QWidget()
        self._setup_row.setAttribute(Qt.WA_TranslucentBackground, True)
        sh_layout = QHBoxLayout(self._setup_row)
        sh_layout.setContentsMargins(0, 0, 0, 0)
        sh_layout.addStretch()
        
        lbl = QLabel("Minutes:")
        lbl.setStyleSheet(f"color: {self._theme.get('text', '#ffffff')}; font-size: 10pt;")
        sh_layout.addWidget(lbl)
        
        self._spin_dur = QSpinBox()
        self._spin_dur.setRange(1, 120)
        self._spin_dur.setValue(25)
        self._spin_dur.setFixedWidth(50)
        self._spin_dur.setStyleSheet(f"background-color: {self._theme.get('bg', '#1a0a1f')}; color: {self._theme.get('text', '#ffffff')};")
        sh_layout.addWidget(self._spin_dur)
        
        self._btn_apply = QPushButton("Start Cooldown")
        self._btn_apply.setCursor(Qt.PointingHandCursor)
        self._btn_apply.setStyleSheet(f"background-color: {self._theme.get('glass', '#333333')}; color: {self._theme.get('text', '#ffffff')}; border-radius: 4px; padding: 4px 10px;")
        self._btn_apply.clicked.connect(lambda: self.start(self._spin_dur.value()))
        sh_layout.addWidget(self._btn_apply)
        sh_layout.addStretch()

        layout.addStretch()
        layout.addWidget(self._lbl_time)
        layout.addSpacing(4)
        layout.addWidget(self._lbl_desc)
        layout.addWidget(self._setup_row)
        layout.addStretch()

    def apply_theme(self, theme: dict) -> None:
        self._theme = theme
        self._lbl_time.setStyleSheet(f"color: {theme.get('text', '#ffffff')};")
        self._lbl_desc.setStyleSheet(f"color: {theme.get('text_dim', '#aaaaaa')};")
        self.update()

    def start(self, minutes: int) -> None:
        self._duration_secs = minutes * 60
        self._remaining = self._duration_secs
        self._setup_row.hide()
        self._init_physics()
        self._update_label()
        self._timer.start()
        self._physics_timer.start()
        self.update()

    def stop(self) -> None:
        self._timer.stop()
        self._physics_timer.stop()
        self._setup_row.show()
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

    def _physics_tick(self) -> None:
        if self._remaining <= 0:
            return

        elapsed = self._duration_secs - self._remaining
        expected_passed = int((elapsed / self._duration_secs) * self._total_sand)

        moved_any = False
        # Sweep bottom up so particles at the bottom fall first making room
        for y in reversed(range(self._gh - 1)):
            for x in range(self._gw):
                if not self._grid[y][x]:
                    continue

                # Throttle the bottleneck
                if y == self._cy - 1 and x == self._cx:
                    if self._sand_passed >= expected_passed:
                        continue
                    
                # 1. Try directly below
                if self._mask[y+1][x] and not self._grid[y+1][x]:
                    self._grid[y][x] = False
                    self._grid[y+1][x] = True
                    moved_any = True
                    if y == self._cy - 1 and x == self._cx:
                        self._sand_passed += 1
                else:
                    # 2. Try falling down-diagonal
                    dirs = [-1, 1] if random.random() > 0.5 else [1, -1]
                    for dx in dirs:
                        nx = x + dx
                        if 0 <= nx < self._gw and self._mask[y+1][nx] and not self._grid[y+1][nx]:
                            self._grid[y][x] = False
                            self._grid[y+1][nx] = True
                            moved_any = True
                            if y == self._cy - 1 and x == self._cx:
                                self._sand_passed += 1
                            break

        if moved_any:
            self.update()

    def paintEvent(self, event) -> None:
        super().paintEvent(event)
        if self._duration_secs <= 0 or not self._timer.isActive():
            return
            
        painter = QPainter(self)
        
        # Calculate cell bounds
        size = min(self.width(), self.height()) - 40
        cell_w = size / self._gw
        cell_h = size / self._gh
        
        x_off = (self.width() - size) / 2
        y_off = (self.height() - size) / 2
        
        bg_col = QColor(self._theme.get("bar_bg", "#2a2a2a"))
        fg_col = QColor(self._theme.get("accent", "#60a5fa"))
        
        for y in range(self._gh):
            for x in range(self._gw):
                if not self._mask[y][x]:
                    continue
                px = x_off + x * cell_w
                py = y_off + y * cell_h
                r = QRectF(px, py, cell_w - 0.5, cell_h - 0.5)
                
                if self._grid[y][x]:
                    painter.fillRect(r, fg_col)
                else:
                    # Draw subtle hourglass backing outline
                    if x == self._cx and y == self._cy:
                        pass # empty throat
                    else:
                        painter.fillRect(r, QColor(bg_col.red(), bg_col.green(), bg_col.blue(), 60))
                        
        painter.end()
