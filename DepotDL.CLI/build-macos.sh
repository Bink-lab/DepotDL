#!/bin/bash
exec "$(dirname "${BASH_SOURCE[0]}")/build.sh" "${1:-osx-arm64}"
