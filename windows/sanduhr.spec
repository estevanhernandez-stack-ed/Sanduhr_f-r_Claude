# PyInstaller spec for Sanduhr für Claude.
# Run:  pyinstaller sanduhr.spec --clean
#
# One-folder mode: dist/Sanduhr/ contains Sanduhr.exe + Qt DLLs + runtime.

# -*- mode: python ; coding: utf-8 -*-

block_cipher = None

a = Analysis(
    ['src/sanduhr/__main__.py'],
    pathex=['src'],
    binaries=[],
    datas=[
        ('icon/Sanduhr.ico', 'icon'),
    ],
    hiddenimports=[
        'cloudscraper',
        'keyring.backends.Windows',
        'PySide6.QtSvg',
    ],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[
        'PySide6.QtWebEngineCore',
        'PySide6.QtWebEngineWidgets',
        'PySide6.QtMultimedia',
        'PySide6.QtQuick3D',
        'PySide6.QtNetworkAuth',
        'PySide6.QtPdf',
        'PySide6.QtBluetooth',
        'PySide6.QtSerialPort',
    ],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name='Sanduhr',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    console=False,
    disable_windowed_traceback=False,
    icon='icon/Sanduhr.ico',
    version='version_info.txt',
)

coll = COLLECT(
    exe,
    a.binaries,
    a.zipfiles,
    a.datas,
    strip=False,
    upx=False,
    name='Sanduhr',
)
