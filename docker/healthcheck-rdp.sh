#!/bin/bash
set -euo pipefail

# RDP daemon is up and accepting connections when the port is listening.
# systemctl --user -M requires machinectl which is not available inside the container.
ss -tln | grep -q ":${GM_RDP_DOCKER_PORT} "
