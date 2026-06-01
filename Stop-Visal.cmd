@echo off
REM Detiene Visal con doble click.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0stop-visal.ps1"
pause
