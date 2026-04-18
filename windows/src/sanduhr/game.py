"""Vector snake game overlay for deep cooldown periods."""

import random
from typing import List, Tuple

from PySide6.QtCore import Qt, QTimer, Signal, QRectF
from PySide6.QtGui import QPainter, QColor, QPen, QBrush

class SnakeOverlay(QWidget):
    finished = Signal()
    highScoreReached = Signal(int)

    def __init__(self, theme: dict, high_score: int = 0, parent=None):
        super().__init__(parent)
        self._theme = theme
        self._high_score = high_score
        self._score = 0
        self.setAttribute(Qt.WA_TransparentForMouseEvents, False)
        self.setFocusPolicy(Qt.StrongFocus)
        self.hide()

        self._timer = QTimer(self)
        self._timer.setInterval(120)  # slightly forgiving initially
        self._timer.timeout.connect(self._game_loop)

        self._grid_size = 20
        self._reset_game()

    def apply_theme(self, theme: dict) -> None:
        self._theme = theme
        self.update()

    def _reset_game(self):
        # Coordinates in grid units
        self._snake: List[Tuple[int, int]] = [(10, 10), (10, 11), (10, 12)]
        self._dir = (0, -1)
        self._next_dir = (0, -1)
        self._food = self._spawn_food()
        self._game_over = False
        self._score = 0

    def _spawn_food(self) -> Tuple[int, int]:
        while True:
            f = (random.randint(0, self._grid_size - 1), random.randint(0, self._grid_size - 1))
            if f not in self._snake:
                return f

    def start_game(self):
        self._reset_game()
        self.show()
        self.setFocus()
        self._timer.start()

    def stop_game(self):
        self._timer.stop()
        self.hide()
        self.finished.emit()

    def keyPressEvent(self, event):
        key = event.key()
        if key == Qt.Key_Escape:
            self.stop_game()
            return
            
        if self._game_over:
            if key == Qt.Key_Space or key == Qt.Key_Return:
                self.start_game()
            return

        # Prevent 180 reverses
        if key == Qt.Key_Up and self._dir != (0, 1):
            self._next_dir = (0, -1)
        elif key == Qt.Key_Down and self._dir != (0, -1):
            self._next_dir = (0, 1)
        elif key == Qt.Key_Left and self._dir != (1, 0):
            self._next_dir = (-1, 0)
        elif key == Qt.Key_Right and self._dir != (-1, 0):
            self._next_dir = (1, 0)

    def _game_loop(self):
        if self._game_over:
            return

        self._dir = self._next_dir
        head_x, head_y = self._snake[0]
        dx, dy = self._dir
        
        new_head = (head_x + dx, head_y + dy)

        # Wall collisions
        if (new_head[0] < 0 or new_head[0] >= self._grid_size or
            new_head[1] < 0 or new_head[1] >= self._grid_size):
            self._game_over = True
            self.update()
            return

        # Self collisions
        if new_head in self._snake:
            self._game_over = True
            self.update()
            return

        self._snake.insert(0, new_head)

        # Eat food
        if new_head == self._food:
            self._food = self._spawn_food()
            self._score += 10
            if self._score > self._high_score:
                self._high_score = self._score
                self.highScoreReached.emit(self._high_score)
            # Increase speed slightly to a max
            new_interval = max(60, int(self._timer.interval() * 0.95))
            self._timer.setInterval(new_interval)
        else:
            self._snake.pop()

        self.update()

    def paintEvent(self, event):
        super().paintEvent(event)
        
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing)
        
        # Dim background
        bg = QColor(self._theme.get("bg", "#1a0a1f"))
        bg.setAlpha(230)
        painter.fillRect(self.rect(), bg)

        # Grid bounds logic
        # Center a square grid in whatever rect we have
        w, h = self.width(), self.height()
        box_px = min(w, h) - 20
        cell_px = box_px / self._grid_size
        
        off_x = (w - box_px) / 2
        off_y = (h - box_px) / 2

        # Draw grid border
        pen = QPen(QColor(self._theme.get("border", "#333333")))
        pen.setWidth(2)
        painter.setPen(pen)
        painter.drawRect(int(off_x), int(off_y), int(box_px), int(box_px))
        
        # Draw Score
        painter.setPen(QColor(self._theme.get("text_dim", "#aaaaaa")))
        font = painter.font()
        font.setPointSize(9)
        font.setBold(True)
        painter.setFont(font)
        painter.drawText(int(off_x), int(off_y - 20), int(box_px), 20, Qt.AlignLeft | Qt.AlignBottom, f"Score: {self._score}")
        painter.drawText(int(off_x), int(off_y - 20), int(box_px), 20, Qt.AlignRight | Qt.AlignBottom, f"Best: {self._high_score}")

        # Snake logic
        snake_color = QColor(self._theme.get("accent", "#60a5fa"))
        painter.setBrush(QBrush(snake_color))
        painter.setPen(Qt.NoPen)
        for i, (sx, sy) in enumerate(self._snake):
            # Alpha fade the tail slightly out
            alpha = max(100, 255 - (i * 255 // len(self._snake)))
            snake_color.setAlpha(alpha)
            painter.setBrush(QBrush(snake_color))
            
            px = off_x + sx * cell_px
            py = off_y + sy * cell_px
            painter.drawRoundedRect(QRectF(px + 1, py + 1, cell_px - 2, cell_px - 2), 2, 2)

        # Food logic
        food_color = QColor(self._theme.get("text", "#ffffff"))
        painter.setBrush(QBrush(food_color))
        fx = off_x + self._food[0] * cell_px
        fy = off_y + self._food[1] * cell_px
        painter.drawEllipse(QRectF(fx + 2, fy + 2, cell_px - 4, cell_px - 4))

        if self._game_over:
            painter.setPen(QColor(self._theme.get("text_dim", "#aaaaaa")))
            font = painter.font()
            font.setPointSize(16)
            font.setBold(True)
            painter.setFont(font)
            painter.drawText(self.rect(), Qt.AlignCenter, "GAME OVER\nPress Space to Restart\nEsc to Exit")
            
        painter.end()
