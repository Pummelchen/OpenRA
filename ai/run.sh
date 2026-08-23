#!/bin/bash
# Starts the OpenRA AI stack: the Qwen3.5 vision model server and the coalition brain server.
#
# Usage:
#   ai/run.sh                     # default: Qwen3.5 4B MLX 8-bit + brain server (--llm --vision)
#   AI_MODEL_NAME=<model-id> ai/run.sh
#
# Monitoring while you play:
#   tail -f ai/brain.log
#   tail -f ~/Library/Application\ Support/OpenRA/ai-telemetry.log
# Stop with:
#   pkill -f mlx_vlm.server; pkill -f model_server.py

set -eo pipefail

PYTHON="${PYTHON:-/opt/homebrew/bin/python3.13}"
MODEL="${AI_MODEL_NAME:-mlx-community/Qwen3.5-4B-MLX-8bit}"
VLM_PORT="${AI_VLM_PORT:-11435}"
BRAIN_PORT="${AI_BRAIN_PORT:-8765}"
HERE="$(cd "$(dirname "$0")" && pwd)"
VLM_LOG="${AI_VLM_LOG:-${HERE}/mlx-vlm.log}"
BRAIN_SERVER_LOG="${AI_BRAIN_SERVER_LOG:-${HERE}/brain-server.log}"

if ! command -v "${PYTHON}" >/dev/null 2>&1; then
	echo "Python runtime not found: ${PYTHON}" >&2
	exit 1
fi

if ! "${PYTHON}" -c 'import mlx_vlm' >/dev/null 2>&1; then
	echo "mlx-vlm is not installed for ${PYTHON}. Run: ${PYTHON} -m pip install --upgrade mlx-vlm" >&2
	exit 1
fi

echo "Starting Qwen3.5 vision model server (${MODEL}) on :${VLM_PORT}..."
"${PYTHON}" -m mlx_vlm.server --model "${MODEL}" --port "${VLM_PORT}" > "${VLM_LOG}" 2>&1 &

echo "Starting coalition brain server on :${BRAIN_PORT}..."
sleep 2
AI_MODEL_ENDPOINT="http://127.0.0.1:${VLM_PORT}/v1/chat/completions" \
AI_MODEL_NAME="${MODEL}" \
"${PYTHON}" "${HERE}/model_server.py" --llm --vision > "${BRAIN_SERVER_LOG}" 2>&1 &

echo
echo "AI stack starting (model load can take a minute on first run)."
echo "Health checks:"
echo "  curl http://127.0.0.1:${VLM_PORT}/v1/models"
echo "  curl http://127.0.0.1:${BRAIN_PORT}/health"
echo "Watch:"
echo "  tail -f ${HERE}/brain.log"
echo "  tail -f ${VLM_LOG} ${BRAIN_SERVER_LOG}"
echo "  tail -f \"$HOME/Library/Application Support/OpenRA/ai-telemetry.log\""
