#!/bin/bash
# Launch the local Mac Catalyst build.
set -euo pipefail
cd "$(dirname "$0")/.."
open Zaffre/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/Zaffre.app
