@echo off
rem Builds OpenSees 3.8.0 with `system Mumps` (multi-core) for Windows x64, the way the Multi-core
rem OpenSees workflow does, into <out>\bin\OpenSees.exe + libiomp5md.dll and <out>\lib\tcl8.6\init.tcl.
rem
rem   Tools\OpenSees\build-windows.bat [work dir] [out dir]
rem
rem OPENSEES_REF picks another tag, branch or commit of OpenSees to patch - a commit by its full hash:
rem   set OPENSEES_REF=d0ac447eb93cbb9f8c8f63e75261dcc10e5c0b61
rem
rem Run it from the "x64 Native Tools Command Prompt for VS 2022", with Intel oneAPI installed - oneMKL
rem and the Fortran compiler (ifx) - and Git, Python 3 and curl on PATH. It mirrors the workflow's
rem steps, but has not been run on a Windows machine itself: the workflow is the tested road.
setlocal

set "HERE=%~dp0"
set "WORK=%~1"
if "%WORK%"=="" set "WORK=%USERPROFILE%\opensees-mumps"
set "OUT=%~2"
if "%OUT%"=="" set "OUT=%WORK%\dist"
rem forward slashes for CMake
set "W=%WORK:\=/%"

if "%OPENSEES_REF%"=="" set OPENSEES_REF=v3.8.0
set MUMPS_CMAKE_COMMIT=ec5f340acabb29dc71c6ffde93b58bdcd04af083
set MUMPS_URL=https://mumps-solver.org/MUMPS_5.5.1.tar.gz
set MUMPS_SHA256=1abff294fa47ee4cfd50dfd5c595942b72ebfcedce08142a75a99ab35014fa15
set CMAKE_POLICY_VERSION_MINIMUM=3.5
set "CONAN_HOME=%WORK%\conan"
set "ONEAPI=C:\Program Files (x86)\Intel\oneAPI"

if not exist "%WORK%" mkdir "%WORK%"

if not exist "%WORK%\venv\Scripts\conan.exe" (
  python -m venv "%WORK%\venv" || exit /b 1
  "%WORK%\venv\Scripts\pip" install -q conan || exit /b 1
)
set "CONAN=%WORK%\venv\Scripts\conan.exe"
"%CONAN%" profile detect --force >nul

rem --- OpenSees, patched ---------------------------------------------------------------------
if not exist "%WORK%\opensees" (
  rem Fetched rather than cloned, so that a commit will do as well as a tag or a branch.
  git -c core.autocrlf=false init -q "%WORK%\opensees" || exit /b 1
  git -C "%WORK%\opensees" -c core.autocrlf=false fetch -q --depth 1 https://github.com/OpenSees/OpenSees.git %OPENSEES_REF% || exit /b 1
  git -C "%WORK%\opensees" -c core.autocrlf=false checkout -q FETCH_HEAD || exit /b 1
  git -C "%WORK%\opensees" apply "%HERE%opensees-v3.8.0-mumps.patch" || exit /b 1
)

rem --- MUMPS 5.5.1: sequential (no MPI), OpenMP, static --------------------------------------
if not exist "%WORK%\mumps-install\lib\dmumps.lib" (
  if exist "%WORK%\mumps" rmdir /s /q "%WORK%\mumps"
  git -c core.autocrlf=false clone https://github.com/OpenSees/mumps.git "%WORK%\mumps" || exit /b 1
  git -C "%WORK%\mumps" checkout -q %MUMPS_CMAKE_COMMIT% || exit /b 1
  git -C "%WORK%\mumps" apply "%HERE%mumps-openmp.patch" || exit /b 1
  if not exist "%WORK%\mumps-archive" mkdir "%WORK%\mumps-archive"
  curl.exe -L --fail --retry 5 -o "%WORK%\mumps-archive\mumps_src.tar.gz" %MUMPS_URL% || exit /b 1
  powershell -NoProfile -Command "if ((Get-FileHash '%WORK%\mumps-archive\mumps_src.tar.gz' -Algorithm SHA256).Hash -ne '%MUMPS_SHA256%') { exit 1 }" || exit /b 1
  call "%ONEAPI%\setvars.bat" intel64 mod
  cmake -S "%W%/mumps" -B "%W%/mumps-build" -G Ninja -Dlocal="%W%/mumps-archive" -Dparallel=OFF -Dopenmp=ON -Darith=d -DBUILD_SHARED_LIBS=OFF -DBUILD_TESTING=OFF -DCMAKE_BUILD_TYPE=Release -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded -DCMAKE_Fortran_COMPILER=ifx -DCMAKE_INSTALL_PREFIX="%W%/mumps-install" || exit /b 1
  cmake --build "%W%/mumps-build" || exit /b 1
  cmake --install "%W%/mumps-build" || exit /b 1
)

rem --- OpenSees with system Mumps ------------------------------------------------------------
pushd "%WORK%\opensees"
"%CONAN%" install . -s arch=x86_64 -s compiler.runtime=static --build=missing -c tools.cmake.cmaketoolchain:generator=Ninja || (popd & exit /b 1)
call "%ONEAPI%\setvars.bat" intel64 mod
cmake -S . -B build/Release -G Ninja -DCMAKE_TOOLCHAIN_FILE=build/Release/generators/conan_toolchain.cmake -DCMAKE_BUILD_TYPE=Release -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded -DCMAKE_Fortran_COMPILER=ifx -DBLA_STATIC=ON -DMKL_LINK=static -DMKL_INTERFACE_FULL=intel_lp64 -DCMAKE_DISABLE_FIND_PACKAGE_MPI=ON -DMUMPS_SEQ_DIR="%W%/mumps-install" -DCMAKE_EXE_LINKER_FLAGS=/FORCE:MULTIPLE -DCMAKE_NINJA_FORCE_RESPONSE_FILE=ON || (popd & exit /b 1)
cmake --build build/Release --target OpenSees || (popd & exit /b 1)
popd

if not exist "%OUT%\bin" mkdir "%OUT%\bin"
if not exist "%OUT%\lib\tcl8.6" mkdir "%OUT%\lib\tcl8.6"
copy /y "%WORK%\opensees\build\Release\OpenSees.exe" "%OUT%\bin\" >nul || exit /b 1
for /f "delims=" %%f in ('dir /s /b "%ONEAPI%\compiler\libiomp5md.dll" ^| findstr /i "\\bin\\libiomp5md.dll"') do copy /y "%%f" "%OUT%\bin\" >nul
for /f "delims=" %%f in ('dir /s /b "%CONAN_HOME%\init.tcl" ^| findstr /i "tcl8.6"') do copy /y "%%f" "%OUT%\lib\tcl8.6\" >nul

rem It has to run with nothing beside it but libiomp5md.dll: no MKL, Fortran or MPI DLL.
dumpbin /dependents "%OUT%\bin\OpenSees.exe"
echo Built %OUT%\bin\OpenSees.exe
endlocal
