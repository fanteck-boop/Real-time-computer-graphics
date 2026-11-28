@echo off
rem Double-click to start the voxel Nisse in the rtcg_ex9 conda environment.
rem Arguments are passed on, e.g.  run_ex9.bat --capture
cd /d "%~dp0"
set "CONDA_ROOT=%USERPROFILE%\anaconda3"
if not exist "%CONDA_ROOT%\Scripts\activate.bat" set "CONDA_ROOT=%USERPROFILE%\miniconda3"
if not exist "%CONDA_ROOT%\envs\rtcg_ex9\python.exe" (
    echo Could not find the rtcg_ex9 environment. Create it with:
    echo   conda env create -f environment.yml
    pause
    exit /b 1
)
call "%CONDA_ROOT%\Scripts\activate.bat" rtcg_ex9
python nisse_voxels.py %*
if errorlevel 1 pause
