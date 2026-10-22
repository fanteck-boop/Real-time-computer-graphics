#!/bin/bash
M=$1; L=$2; shift 2
LOG="C:/Users/trist/Desktop/Real-time-computer-graphics/Captures/_logs/${L}.log"
Q="-quit"; [[ "$M" == *PlayCapture* ]] && Q=""
"/c/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe" -batchmode $Q -projectPath "C:/Users/trist/Desktop/Real-time-computer-graphics" -executeMethod $M -logFile "$LOG" "$@"
echo exit=$?
