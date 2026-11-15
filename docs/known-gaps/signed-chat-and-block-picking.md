# Signed chat


**Status:** 26.3 chat and signed-command packets are decoded completely, including acknowledgement bits and checksum,
and player-chat/deletion payloads have vanilla captures. The server still delivers ordinary chat through its existing
system-message path; signature validation, acknowledgement validation and a per-connection signature cache are not
implemented.
