#!/bin/bash
# Starts the OpenRA AI stack: the Gemma vision model server and the coalition brain server.
#
# Usage:
#   ai/run.sh                     # default: mlx-vlm (Gemma 4 E4B 4-bit) + brain server (--llm --vision)
#   AI_MODEL_NAME=<model-id> ai/run.sh
#
# Monitoring while you play:
#   tail -f ai/brain.log
#   tail -f ~/Library/Application\ Support/OpenRA/ai-telemetry.log
# Stop with:
#   pkill -f mlx_vlm.server; pkill -f model_server.py

set -eo pipefail

PYTHON="${PYTHON:-/opt/homebrew/bin/python3}"
MODEL="${AI_MODEL_NAME:-mlx-community/gemma-4-e4b-it-4bit}"
VLM_PORT="${AI_VLM_PORT:-11435}"
BRAIN_PORT="${AI_BRAIN_PORT:-8765}"
HERE="$(cd "$(dirname "$0")" && pwd)"

echo "Starting Gemma vision model server (${MODEL}) on :${VLM_PORT}..."
"${PYTHON}" -m mlx_vlm.server --model "${MODEL}" --port "${VLM_PORT}" > /tmp/mlx_vlm.log 2>&1 &

echo "Starting coalition brain server on :${BRAIN_PORT}..."
sleep 2
AI_MODEL_ENDPOINT="http://127.0.0.1:${VLM_PORT}/v1/chat/completions" \
AI_MODEL_NAME="${MODEL}" \
"${PYTHON}" "${HERE}/model_server.py" --llm --vision > /tmp/ai_brain.log 2>&1 &

echo
echo "AI stack starting (model load can take a minute on first run)."
echo "Health checks:"
echo "  curl http://127.0.0.1:${VLM_PORT}/v1/models"
echo "  curl http://127.0.0.1:${BRAIN_PORT}/health"
echo "Watch:"
echo "  tail -f ${HERE}/brain.log"
echo "  tail -f \"$HOME/Library/Application Support/OpenRA/ai-telemetry.log\""
