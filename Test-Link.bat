@echo off
title Teeth - test the link
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0App Files\Tools\Test-Link.ps1"
echo.
pause
