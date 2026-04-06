@echo off
setlocal

call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" || exit /b 1
set "CHERE_INVOKING=1"
set "MSYS2_PATH_TYPE=inherit"
set "MSYSTEM=MSYS"

"C:\msys64\usr\bin\bash.exe" -lc "cd /c/Users/daniel.NB080/source/repos/SVQNext/external/ffmpeg-official && make -j4 && make install"
exit /b %ERRORLEVEL%
