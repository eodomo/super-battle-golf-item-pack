@echo off
rem Runs a copy of the dev MCP server from %%TEMP%%, so rebuilding it never fails on a locked file.
set "SRC=%~dp0bin\out"
set "DST=%TEMP%\sbg-dev-mcp\%RANDOM%%RANDOM%"
xcopy /q /y /i "%SRC%" "%DST%" >nul
"%DST%\DevMcp.exe"
