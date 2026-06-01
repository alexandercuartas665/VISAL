@echo off
REM Arranca Visal con doble click. No requiere ajustar ExecutionPolicy.
REM El script .ps1 deja a Visal corriendo detached y se cierra solo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-visal.ps1" %*
