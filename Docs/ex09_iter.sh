#!/bin/bash
# usage: ex09_iter.sh <pow> <tag>   builds the rig (debug) and renders pose tests
cd "C:/Users/trist/Desktop/Real-time-computer-graphics"
U="C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe"
"$U" -batchmode -quit -projectPath "$PWD" -executeMethod RtcgEx09.Build -pow $1 -debug 1 -logFile Captures/_logs/ex09_build.log
grep -n "error CS\|EX09 built\|Exception" Captures/_logs/ex09_build.log | head -5
"$U" -batchmode -projectPath "$PWD" -executeMethod RtcgEx09.OpenAndPlay -ex09pose Captures/Ex09/test_$2 -logFile Captures/_logs/ex09_play.log
grep -n "error CS\|Exception" Captures/_logs/ex09_play.log | head -5
python Docs/Ex09_montage.py Captures/Ex09/test_$2 pose_t0.00,pose_t1.45,pose_t3.10,extreme,extreme_b,rest_heat C:/Users/trist/AppData/Local/Temp/ex09_t.png
