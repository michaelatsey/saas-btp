#!/usr/bin/env bash
#
# Tests the settings check of ADR-ARCH-038 (scripts/ci/check-supabase-auth-settings.sh) on
# recorded configurations: a matching one passes, and each of the four settings it watches,
# changed alone, fails the check, which names it. The answers of the Management API are
# replayed by a `curl` stub; nothing reaches the network and no credential is used.
#
#   bash scripts/ci/check-supabase-auth-settings.test.sh   # from the repository root
#
# Needs bash and jq. The recorded configuration is
# scripts/ci/fixtures/supabase-auth-settings/{auth-config,signing-keys}.json: a matching
# answer of each endpoint, with placeholder values and no project data. Its fields are those of
# the Management API reference (GET /v1/projects/{ref}/config/auth, GET
# /v1/projects/{ref}/config/auth/signing-keys) and of the answer the provider's CLI records
# for the first endpoint (supabase/cli f9df34ff9acd406a5d7818efee6edd12fd2e8265,
# apps/cli-e2e/fixtures/recorded/GET_v1_projects_replayfixtureprojref_config_auth/
# default.response.json). Each case changes that answer with one jq filter.

set -euo pipefail

CHECK=scripts/ci/check-supabase-auth-settings.sh
FIXTURES=scripts/ci/fixtures/supabase-auth-settings
[ -f "$CHECK" ] || { echo "no such script: $CHECK" >&2; exit 2; }

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# A placeholder of no provider's token format, so that no secret scanner mistakes it for one.
TOKEN=placeholder-token-never-printed
REF=abcdefghijklmnopqrst

# The stub answers each endpoint with the status and body the case sets, writes the body to
# the file named by --output and the status on stdout (the --write-out of the check). It
# records its arguments and its stdin, so that a case can tell where the token went.
mkdir "$work/bin"
cat > "$work/bin/curl" <<'SH'
#!/usr/bin/env bash
printf '%s\n' "$@" >> "$STUB_DIR/argv"
cat >> "$STUB_DIR/stdin"
out= url=
while [ $# -gt 0 ]; do
  case "$1" in
    --output) out=$2; shift ;;
    https://*) url=$1 ;;
  esac
  shift
done
case "$url" in
  */config/auth/signing-keys) status=$STUB_KEYS_STATUS body=$STUB_KEYS_BODY ;;
  */config/auth) status=$STUB_AUTH_STATUS body=$STUB_AUTH_BODY ;;
  *) echo "stub: unexpected url $url" >&2; exit 99 ;;
esac
[ "$status" = fail ] && { echo "curl: (28) Operation timed out" >&2; exit 28; }
cp "$body" "$out"
printf '%s' "$status"
SH
chmod +x "$work/bin/curl"

pass=0
fail=0
ok() { pass=$((pass + 1)); echo "ok   $1"; }
ko() { fail=$((fail + 1)); echo "FAIL $1: $2"; }

# variant <name> <auth-config filter> <signing-keys filter>: writes the two answers of a case.
variant() {
  jq "$2" "$FIXTURES/auth-config.json" > "$work/$1.auth.json"
  jq "$3" "$FIXTURES/signing-keys.json" > "$work/$1.keys.json"
}

# run <name> [auth status] [keys status]: runs the check against the stub; sets $code, $out.
run() {
  local name=$1
  rm -f "$work/argv" "$work/stdin"
  set +e
  out=$(STUB_DIR=$work PATH="$work/bin:$PATH" \
    STUB_AUTH_STATUS=${2:-200} STUB_AUTH_BODY="$work/$name.auth.json" \
    STUB_KEYS_STATUS=${3:-200} STUB_KEYS_BODY="$work/$name.keys.json" \
    SUPABASE_ACCESS_TOKEN=$TOKEN SUPABASE_PROJECT_REF=$REF \
    bash "$CHECK" 2>&1)
  code=$?
  set -e
}

# expect_pass <case>
expect_pass() {
  run "$1"
  if [ "$code" -eq 0 ]; then ok "$1"; else ko "$1" "exit $code, expected 0: $out"; fi
}

# expect_drift <case> <setting name>: exit 1, the setting named, no other setting named.
expect_drift() {
  run "$1"
  local named others=0 s
  named=$(grep -c "^drift: $2:" <<<"$out" || true)
  for s in "signing key in use" "e-mail confirmations" "phone confirmations" "access token hook"; do
    [ "$s" = "$2" ] && continue
    if grep -q "^drift: $s:" <<<"$out"; then others=$((others + 1)); fi
  done
  if [ "$code" -eq 1 ] && [ "$named" -eq 1 ] && [ "$others" -eq 0 ]; then
    ok "$1"
  else
    ko "$1" "exit $code, '$2' named $named time(s), other settings named $others: $out"
  fi
}

# expect_unreadable <case> <endpoint path> <status> <auth status> <keys status>
expect_unreadable() {
  run "$1" "$4" "$5"
  if [ "$code" -eq 3 ] && grep -q "^unreadable: GET $2: $3" <<<"$out" \
    && ! grep -qi "match" <<<"$out"; then
    ok "$1"
  else
    ko "$1" "exit $code, expected 3 naming 'GET $2: $3': $out"
  fi
}

AUTH_PATH="/v1/projects/$REF/config/auth"
KEYS_PATH="/v1/projects/$REF/config/auth/signing-keys"
# in_use <algorithm>: the filter giving the key in use that algorithm.
in_use() { printf '.keys |= map(if .status == "in_use" then .algorithm = "%s" else . end)' "$1"; }

variant matching_configuration_passes . .
expect_pass matching_configuration_passes

variant rs256_key_in_use_passes . "$(in_use RS256)"
expect_pass rs256_key_in_use_passes

variant signing_key_in_use_hs256_fails_naming_signing_key . "$(in_use HS256)"
expect_drift signing_key_in_use_hs256_fails_naming_signing_key "signing key in use"

variant signing_key_in_use_eddsa_fails_naming_signing_key . "$(in_use EdDSA)"
expect_drift signing_key_in_use_eddsa_fails_naming_signing_key "signing key in use"

variant signing_key_algorithm_as_array_fails_naming_signing_key . \
  '.keys |= map(if .status == "in_use" then .algorithm = ["ES256"] else . end)'
expect_drift signing_key_algorithm_as_array_fails_naming_signing_key "signing key in use"

variant no_signing_key_in_use_fails_naming_signing_key . \
  '.keys |= map(if .status == "in_use" then .status = "standby" else . end)'
expect_drift no_signing_key_in_use_fails_naming_signing_key "signing key in use"

variant two_signing_keys_in_use_fails_naming_signing_key . '.keys |= map(.status = "in_use" | .algorithm = "ES256")'
expect_drift two_signing_keys_in_use_fails_naming_signing_key "signing key in use"

# The legacy secret in use, with an ES256 key on standby: the key that signs is the one in use.
variant standby_es256_with_hs256_in_use_fails_naming_signing_key . \
  '.keys |= map(if .algorithm == "ES256" then .status = "standby" else .status = "in_use" end)'
expect_drift standby_es256_with_hs256_in_use_fails_naming_signing_key "signing key in use"

variant signing_keys_without_keys_array_fails_naming_signing_key . '{keys: null}'
expect_drift signing_keys_without_keys_array_fails_naming_signing_key "signing key in use"

variant email_confirmations_not_required_fails_naming_email_confirmations '.mailer_autoconfirm = true' .
expect_drift email_confirmations_not_required_fails_naming_email_confirmations "e-mail confirmations"

variant email_confirmations_setting_absent_fails_naming_email_confirmations 'del(.mailer_autoconfirm)' .
expect_drift email_confirmations_setting_absent_fails_naming_email_confirmations "e-mail confirmations"

variant email_confirmations_setting_null_fails_naming_email_confirmations '.mailer_autoconfirm = null' .
expect_drift email_confirmations_setting_null_fails_naming_email_confirmations "e-mail confirmations"

variant phone_confirmations_not_required_fails_naming_phone_confirmations '.sms_autoconfirm = true' .
expect_drift phone_confirmations_not_required_fails_naming_phone_confirmations "phone confirmations"

variant phone_confirmations_setting_as_string_fails_naming_phone_confirmations '.sms_autoconfirm = "false"' .
expect_drift phone_confirmations_setting_as_string_fails_naming_phone_confirmations "phone confirmations"

variant access_token_hook_disabled_fails_naming_access_token_hook '.hook_custom_access_token_enabled = false' .
expect_drift access_token_hook_disabled_fails_naming_access_token_hook "access token hook"

variant access_token_hook_other_function_fails_naming_access_token_hook \
  '.hook_custom_access_token_uri = "pg-functions://postgres/public/custom_access_token_hook"' .
expect_drift access_token_hook_other_function_fails_naming_access_token_hook "access token hook"

variant access_token_hook_without_uri_fails_naming_access_token_hook '.hook_custom_access_token_uri = null' .
expect_drift access_token_hook_without_uri_fails_naming_access_token_hook "access token hook"

# Two settings changed: each is named, and the check fails once.
variant each_failing_setting_is_named '.sms_autoconfirm = true | .hook_custom_access_token_enabled = false' .
run each_failing_setting_is_named
if [ "$code" -eq 1 ] && grep -q "^drift: phone confirmations:" <<<"$out" \
  && grep -q "^drift: access token hook:" <<<"$out" \
  && ! grep -q "^drift: signing key in use:" <<<"$out" \
  && ! grep -q "^drift: e-mail confirmations:" <<<"$out"; then
  ok each_failing_setting_is_named
else
  ko each_failing_setting_is_named "exit $code: $out"
fi

# Any answer other than a success is unreadable, named by endpoint and status, never a pass.
for s in 401 403 404 429 500; do
  variant "auth_config_answer_${s}_fails_naming_endpoint_and_status" . .
  expect_unreadable "auth_config_answer_${s}_fails_naming_endpoint_and_status" "$AUTH_PATH" "$s" "$s" 200
  variant "signing_keys_answer_${s}_fails_naming_endpoint_and_status" . .
  expect_unreadable "signing_keys_answer_${s}_fails_naming_endpoint_and_status" "$KEYS_PATH" "$s" 200 "$s"
done
variant auth_config_request_failure_fails_naming_endpoint . .
expect_unreadable auth_config_request_failure_fails_naming_endpoint "$AUTH_PATH" "no answer" fail 200
variant signing_keys_request_failure_fails_naming_endpoint . .
expect_unreadable signing_keys_request_failure_fails_naming_endpoint "$KEYS_PATH" "no answer" 200 fail
variant token_not_printed_when_unreadable . .

printf 'Service paused\n' > "$work/not_json.body"
cp "$work/not_json.body" "$work/auth_config_body_not_json_fails_as_unreadable.auth.json"
cp "$FIXTURES/signing-keys.json" "$work/auth_config_body_not_json_fails_as_unreadable.keys.json"
expect_unreadable auth_config_body_not_json_fails_as_unreadable "$AUTH_PATH" "200, the body is not a JSON object" 200 200

cp "$FIXTURES/auth-config.json" "$work/signing_keys_body_not_json_fails_as_unreadable.auth.json"
cp "$work/not_json.body" "$work/signing_keys_body_not_json_fails_as_unreadable.keys.json"
expect_unreadable signing_keys_body_not_json_fails_as_unreadable "$KEYS_PATH" "200, the body is not a JSON object" 200 200

echo '[]' > "$work/auth_config_body_not_an_object_fails_as_unreadable.auth.json"
cp "$FIXTURES/signing-keys.json" "$work/auth_config_body_not_an_object_fails_as_unreadable.keys.json"
expect_unreadable auth_config_body_not_an_object_fails_as_unreadable "$AUTH_PATH" "200, the body is not a JSON object" 200 200

# Without its token or its project reference, the check refuses to run: exit 2.
for missing in SUPABASE_ACCESS_TOKEN SUPABASE_PROJECT_REF; do
  rm -f "$work/argv"
  set +e
  out=$(export SUPABASE_ACCESS_TOKEN=$TOKEN SUPABASE_PROJECT_REF=$REF; unset "$missing"
    STUB_DIR=$work PATH="$work/bin:$PATH" bash "$CHECK" 2>&1)
  code=$?
  set -e
  [ -s "$work/argv" ] && code="$code, a request was sent"
  if [ "$code" -eq 2 ] && grep -q "$missing" <<<"$out"; then
    ok "missing_${missing,,}_is_a_usage_error"
  else
    ko "missing_${missing,,}_is_a_usage_error" "exit $code: $out"
  fi
done

# A project reference that is not one is refused before any request.
rm -f "$work/argv"
set +e
out=$(STUB_DIR=$work PATH="$work/bin:$PATH" SUPABASE_ACCESS_TOKEN=$TOKEN \
  SUPABASE_PROJECT_REF='abc/../../v1/organizations' bash "$CHECK" 2>&1)
code=$?
set -e
if [ "$code" -eq 2 ] && [ ! -s "$work/argv" ]; then
  ok project_ref_not_a_reference_is_a_usage_error
else
  ko project_ref_not_a_reference_is_a_usage_error "exit $code: $out"
fi

# The token reaches curl on stdin only: never in its arguments, never in the output.
run matching_configuration_passes
if ! grep -qF "$TOKEN" "$work/argv" && grep -qF "Authorization: Bearer $TOKEN" "$work/stdin" \
  && ! grep -qF "$TOKEN" <<<"$out"; then
  ok token_given_on_stdin_and_never_printed
else
  ko token_given_on_stdin_and_never_printed "token found in argv or output, or not on stdin"
fi
run signing_key_in_use_hs256_fails_naming_signing_key
if ! grep -qF "$TOKEN" <<<"$out"; then ok token_not_printed_on_drift; else ko token_not_printed_on_drift "$out"; fi
run token_not_printed_when_unreadable 401 200
if [ "$code" -eq 3 ] && ! grep -qF "$TOKEN" <<<"$out"; then
  ok token_not_printed_when_unreadable
else
  ko token_not_printed_when_unreadable "exit $code: $out"
fi

# Recorded answers evaluated without a request: the mode #226 can use on saved answers.
rm -f "$work/argv"
set +e
out=$(env -u SUPABASE_ACCESS_TOKEN -u SUPABASE_PROJECT_REF PATH="$work/bin:$PATH" STUB_DIR="$work" \
  bash "$CHECK" --recorded "$FIXTURES/auth-config.json" "$FIXTURES/signing-keys.json" 2>&1)
code=$?
set -e
if [ "$code" -eq 0 ] && [ ! -s "$work/argv" ]; then
  ok recorded_matching_configuration_passes_without_request
else
  ko recorded_matching_configuration_passes_without_request "exit $code: $out"
fi
set +e
out=$(bash "$CHECK" --recorded "$work/access_token_hook_disabled_fails_naming_access_token_hook.auth.json" \
  "$FIXTURES/signing-keys.json" 2>&1)
code=$?
set -e
if [ "$code" -eq 1 ] && grep -q "^drift: access token hook:" <<<"$out"; then
  ok recorded_drift_fails_naming_setting
else
  ko recorded_drift_fails_naming_setting "exit $code: $out"
fi

echo
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
