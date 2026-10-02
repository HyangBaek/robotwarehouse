"""WebSocket 메시지 type 목록. docs/api.md, Unity Messages.cs 와 일치해야 한다 (TC-API-09)."""
WS_TYPES = {
    "hello", "transcript", "map_ready", "question",
    "sim_ready", "compare", "analysis", "error",
}

ERROR_CODES = {
    "STT_EMPTY", "LLM_TIMEOUT", "LLM_FORMAT", "MAP_NOT_CONFIRMED",
    "SIM_COLLISION", "NOT_FOUND", "BUSY",
}
