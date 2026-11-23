#!/usr/bin/env bash
#
# The settings check of ADR-ARCH-038: reads a Supabase project's Auth configuration and signing
# keys from the Management API and fails when one of the four settings the API's token
# validation depends on differs from that decision, so that a change is detected rather than
# trusted. Where it runs and its credential: ADR-ARCH-041 and
# .github/workflows/check-supabase-auth-settings.yml.
#
#   SUPABASE_ACCESS_TOKEN=... SUPABASE_PROJECT_REF=<ref> scripts/ci/check-supabase-auth-settings.sh
#   scripts/ci/check-supabase-auth-settings.sh --recorded <auth-config.json> <signing-keys.json>
#
# The second form evaluates two saved answers of the endpoints below and sends no request.
#
# Exit codes: 0 the four settings match; 1 drift, each setting that differs named on a
# "drift: <setting>:" line; 2 usage; 3 unreadable: an answer other than 200, no answer, or a
# body that is not a JSON object, named on an "unreadable: GET <path>:" line. An unreadable
# answer is never evaluated and never reported as a pass. A 200 answer is the project's
# stored configuration and is evaluated whatever the project's state: the provider does not
# document what a paused project (ADR-ARCH-026) answers, and the token's two read permissions
# cannot read the project's status (ADR-ARCH-041).
#
# Needs bash, curl and jq. The token reaches curl on its standard input only, never on its
# command line, and is never printed.

set -euo pipefail

# The Management API (reference "Introduction": base URL, "Authorization: Bearer <token>").
API=https://api.supabase.com

# Expected values, each from its decision:
# - The key in use is an asymmetric ES256 or RS256 key (ADR-ARCH-038 decision 1): with the
#   legacy shared secret (HS256) the JWKS holds no key, and the API accepts no other algorithm.
ALLOWED_ALGORITHMS='["ES256","RS256"]'
# - E-mail and phone confirmations are required (ADR-ARCH-038 decision 5). The API fields are
#   the negation of the CLI's auth.email.enable_confirmations and auth.sms.enable_confirmations
#   (supabase/cli f9df34ff9acd406a5d7818efee6edd12fd2e8265, invertedBoolRow in
#   packages/config/src/project-config/registry-auth.ts), so a required confirmation reads
#   mailer_autoconfirm false and sms_autoconfirm false.
# - The custom access token hook is enabled and points at the versioned function
#   access.custom_access_token_hook (ADR-ARCH-038 decision 5;
#   apps/api/database/SaasBtp.Migrations/Scripts/0004__access__create_access_token_hook.sql),
#   written pg-functions://postgres/<schema>/<function_name> (Supabase, "Auth Hooks").
HOOK_URI=pg-functions://postgres/access/custom_access_token_hook

usage() { echo "check-supabase-auth-settings: $1" >&2; exit 2; }

# unreadable <path> <reason>
unreadable() { echo "unreadable: GET $1: $2" >&2; exit 3; }

# object_or_unreadable <path> <file>: a body that is not a JSON object is not an answer.
object_or_unreadable() {
  jq -e 'type == "object"' "$2" >/dev/null 2>&1 \
    || unreadable "$1" "200, the body is not a JSON object"
}

# fetch <path> <file>: the body of a 200 answer in <file>; anything else is unreadable.
fetch() {
  local status
  if ! status=$(printf 'header = "Authorization: Bearer %s"\n' "$SUPABASE_ACCESS_TOKEN" \
    | curl --silent --show-error --max-time 30 --config - \
      --output "$2" --write-out '%{http_code}' "$API$1"); then
    unreadable "$1" "no answer"
  fi
  [ "$status" = 200 ] || unreadable "$1" "$status"
  object_or_unreadable "$1" "$2"
}

# evaluate <auth config file> <signing keys file>: one "drift:" line per setting that differs.
evaluate() {
  jq -r -n --slurpfile config "$1" --slurpfile keys "$2" \
    --argjson allowed "$ALLOWED_ALGORITHMS" --arg hook "$HOOK_URI" '
    $config[0] as $c
    | $keys[0] as $k
    | (if ($k.keys | type) == "array"
       then [$k.keys[] | objects | select(.status == "in_use")]
       else null end) as $in_use
    | (if $in_use == null then
         "drift: signing key in use: the answer holds no keys array"
       elif ($in_use | length) != 1 then
         "drift: signing key in use: expected exactly one key with status in_use, read \($in_use | length)"
       elif ($in_use[0].algorithm | type) != "string"
            or ($allowed | index([$in_use[0].algorithm])) == null then
         "drift: signing key in use: expected ES256 or RS256, read \($in_use[0].algorithm | tojson)"
       else empty end),
      (if $c.mailer_autoconfirm == false then empty else
         "drift: e-mail confirmations: expected required (mailer_autoconfirm false), read mailer_autoconfirm \($c.mailer_autoconfirm | tojson)" end),
      (if $c.sms_autoconfirm == false then empty else
         "drift: phone confirmations: expected required (sms_autoconfirm false), read sms_autoconfirm \($c.sms_autoconfirm | tojson)" end),
      (if $c.hook_custom_access_token_enabled == true and $c.hook_custom_access_token_uri == $hook
       then empty else
         "drift: access token hook: expected enabled on \($hook), read hook_custom_access_token_enabled \($c.hook_custom_access_token_enabled | tojson), hook_custom_access_token_uri \($c.hook_custom_access_token_uri | tojson)" end)
    '
}

if [ "${1:-}" = --recorded ]; then
  [ $# -eq 3 ] || usage "--recorded takes <auth-config.json> <signing-keys.json>"
  object_or_unreadable "(recorded) $2" "$2"
  object_or_unreadable "(recorded) $3" "$3"
  auth=$2
  keys=$3
else
  [ $# -eq 0 ] || usage "unexpected argument: $1"
  [ -n "${SUPABASE_ACCESS_TOKEN:-}" ] || usage "SUPABASE_ACCESS_TOKEN is not set"
  [ -n "${SUPABASE_PROJECT_REF:-}" ] || usage "SUPABASE_PROJECT_REF is not set"
  # Letters and digits only: a "/", "." or "?" would change the path the token is sent to.
  [[ "$SUPABASE_PROJECT_REF" =~ ^[a-z0-9]+$ ]] || usage "SUPABASE_PROJECT_REF is not a project reference"

  work=$(mktemp -d)
  trap 'rm -rf "$work"' EXIT
  auth=$work/auth-config.json
  keys=$work/signing-keys.json
  fetch "/v1/projects/$SUPABASE_PROJECT_REF/config/auth" "$auth"
  fetch "/v1/projects/$SUPABASE_PROJECT_REF/config/auth/signing-keys" "$keys"
fi

drift=$(evaluate "$auth" "$keys")
if [ -n "$drift" ]; then
  echo "$drift"
  echo "The settings check failed: $(grep -c . <<<"$drift") setting(s) differ from ADR-ARCH-038."
  exit 1
fi
algorithm=$(jq -r '[.keys[] | objects | select(.status == "in_use")][0].algorithm' "$keys")
echo "The four settings of ADR-ARCH-038 match: signing key in use $algorithm," \
  "e-mail and phone confirmations required, access token hook enabled on $HOOK_URI."
