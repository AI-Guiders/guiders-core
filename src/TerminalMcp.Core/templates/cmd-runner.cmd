@echo off
setlocal EnableDelayedExpansion
chcp __CDP_TOKEN_CODEPAGE__ >nul
call "__CDP_TOKEN_USER_SCRIPT__"
set __CDP_EC=!ERRORLEVEL!
echo __CDP_TOKEN_CWD_MARK__!CD!
exit /b !__CDP_EC!
