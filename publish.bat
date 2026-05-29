@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0scripts\publish-win.ps1" %*
