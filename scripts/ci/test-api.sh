#!/usr/bin/env bash
#
# The one entry point for the API tests, locally and in CI: checks that Docker is reachable,
# then runs dotnet test with the arguments it is given.
#
#   scripts/ci/test-api.sh --solution SaasBtp.slnx -c Release --no-build
#
# The integration tests start PostgreSQL through Testcontainers. Without Docker each of them
# fails on its own, deep in a stack trace; this script reports the cause once instead, before
# any test runs.

set -euo pipefail

if ! docker info > /dev/null 2>&1; then
  echo "Docker is unavailable: the API tests start PostgreSQL through Testcontainers, so no test was run. Start Docker, or run 'docker info' to see why it cannot be reached, then run this command again." >&2
  exit 1
fi

exec dotnet test "$@"
