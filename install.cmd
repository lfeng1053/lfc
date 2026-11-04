@echo off
rem Installs or updates lfc from github.com/lfeng1053/lfc (no admin rights needed).
powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/lfeng1053/lfc/main/install.ps1 | iex"
echo.
pause
