#!/usr/bin/env bash
#
# Tests the environment gate of check-supabase-auth-settings.yml (ADR-ARCH-041): the gate's
# script is read from the workflow file, between its "# gate:begin" and "# gate:end" lines, and
# run against recorded answers of the GitHub REST API, with `curl` replaced by a stub; nothing
# reaches the network. Then checks the workflows' references: only this workflow names the
# Environment or its token, and it names neither deployment credential.
#
#   bash scripts/ci/check-settings-check-environment.test.sh [workflow file]   # from the repository root
#
# Needs bash and jq. The answers have the shapes of the GitHub REST reference, "REST API
# endpoints for deployment environments" (Get an environment: deployment_branch_policy with
# protected_branches and custom_branch_policies) and "REST API endpoints for deployment branch
# policies" (branch_policies[] with name and type).

set -euo pipefail

WORKFLOW=${1:-.github/workflows/check-supabase-auth-settings.yml}
[ -f "$WORKFLOW" ] || { echo "no such workflow file: $WORKFLOW" >&2; exit 2; }

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# The gate's script, without the run block's indentation.
sed -n '/# gate:begin/,/# gate:end/p' "$WORKFLOW" > "$work/gate.indented"
[ "$(grep -c 'gate:' "$work/gate.indented")" -eq 2 ] || { echo "expected one gate block in $WORKFLOW" >&2; exit 2; }
indent=$(head -1 "$work/gate.indented" | sed 's/[^ ].*//' | wc -c)
cut -c"$indent"- "$work/gate.indented" > "$work/gate.sh"

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
  */environments/settings-check-staging/deployment-branch-policies)
    status=$STUB_POLICIES_STATUS body=$STUB_POLICIES_BODY ;;
  */environments/settings-check-staging) status=$STUB_ENV_STATUS body=$STUB_ENV_BODY ;;
  *) echo "stub: unexpected url $url" >&2; exit 99 ;;
esac
[ "$status" = fail ] && { echo "curl: (28) Operation timed out" >&2; exit 28; }
printf '%s' "$body" > "$out"
printf '%s' "$status"
SH
chmod +x "$work/bin/curl"

TOKEN=ghs_test-token-never-printed
RESTRICTED='{"name":"settings-check-staging","protection_rules":[{"type":"branch_policy"}],"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}'
MAIN_ONLY='{"total_count":1,"branch_policies":[{"id":1,"node_id":"x","name":"main","type":"branch"}]}'

pass=0
fail=0
ok() { pass=$((pass + 1)); echo "ok   $1"; }
ko() { fail=$((fail + 1)); echo "FAIL $1: $2"; }

# gate [ref] [env status] [env body] [policies status] [policies body]: sets $code, $out.
# ENVIRONMENT, settings-check-staging unless set, is the name the workflow gives the gate.
gate() {
  rm -f "$work/argv" "$work/stdin"
  set +e
  out=$(STUB_DIR=$work PATH="$work/bin:$PATH" RUNNER_TEMP=$work \
    GITHUB_REF=${1:-refs/heads/main} GITHUB_API_URL=https://api.github.com \
    GITHUB_REPOSITORY=owner/repo GITHUB_TOKEN=$TOKEN ENVIRONMENT=${ENVIRONMENT:-settings-check-staging} \
    STUB_ENV_STATUS=${2:-200} STUB_ENV_BODY=${3:-$RESTRICTED} \
    STUB_POLICIES_STATUS=${4:-200} STUB_POLICIES_BODY=${5:-$MAIN_ONLY} \
    bash -eo pipefail "$work/gate.sh" 2>&1)
  code=$?
  set -e
}

# expect_refused <case> <text the error carries> [gate arguments...]
expect_refused() {
  local name=$1 text=$2
  shift 2
  gate "$@"
  if [ "$code" -ne 0 ] && grep -q "^::error::.*$text" <<<"$out" && ! grep -qF "$TOKEN" <<<"$out"; then
    ok "$name"
  else
    ko "$name" "exit $code, expected an error carrying '$text': $out"
  fi
}

gate
if [ "$code" -eq 0 ] && grep -q "restricted to main" <<<"$out"; then
  ok environment_restricted_to_main_passes
else
  ko environment_restricted_to_main_passes "exit $code: $out"
fi
if ! grep -qF "$TOKEN" "$work/argv" && grep -qF "Authorization: Bearer $TOKEN" "$work/stdin"; then
  ok token_given_on_stdin_only
else
  ko token_given_on_stdin_only "token in curl's arguments, or not on its stdin"
fi

expect_refused run_not_on_main_fails_naming_ref "this run is on refs/heads/feature" refs/heads/feature
if [ ! -s "$work/argv" ]; then ok run_not_on_main_sends_no_request; else ko run_not_on_main_sends_no_request "a request was sent"; fi
expect_refused run_on_a_tag_named_main_fails_naming_ref "this run is on refs/tags/main" refs/tags/main

expect_refused environment_missing_fails_naming_environment \
  "Environment settings-check-staging does not exist" refs/heads/main 404 '{"message":"Not Found"}'
for s in 401 403 500; do
  expect_refused "environment_unreadable_${s}_fails_naming_environment_and_status" \
    "Environment settings-check-staging: the REST API answered $s\\." refs/heads/main "$s" '{}'
done
expect_refused environment_no_answer_fails_naming_environment \
  "Environment settings-check-staging: the REST API gave no answer" refs/heads/main fail '{}'

expect_refused environment_body_not_json_fails_naming_environment \
  "Environment settings-check-staging: the REST API answered 200 with a body that is not a JSON object" \
  refs/heads/main 200 '<html>'
expect_refused branch_policies_body_not_json_fails_naming_environment \
  "Environment settings-check-staging: the REST API answered 200 for its branch policies with a body that is not a JSON object" \
  refs/heads/main 200 "$RESTRICTED" 200 '<html>'
ENVIRONMENT='settings-check-staging/../../x' expect_refused environment_name_not_of_this_check_is_refused \
  "Not an environment of the settings check" refs/heads/main
if [ ! -s "$work/argv" ]; then ok environment_name_refused_before_any_request; else ko environment_name_refused_before_any_request "a request was sent"; fi

expect_refused no_branch_policy_fails_naming_environment \
  "Environment settings-check-staging: deployment branches are not restricted" refs/heads/main 200 \
  '{"name":"settings-check-staging","deployment_branch_policy":null}'
expect_refused protected_branches_policy_fails_naming_environment \
  "Environment settings-check-staging: deployment branches are not restricted" refs/heads/main 200 \
  '{"deployment_branch_policy":{"protected_branches":true,"custom_branch_policies":false}}'

for policies in \
  '{"total_count":1,"branch_policies":[{"name":"develop","type":"branch"}]}' \
  '{"total_count":1,"branch_policies":[{"name":"main","type":"tag"}]}' \
  '{"total_count":2,"branch_policies":[{"name":"main","type":"branch"},{"name":"release/*","type":"branch"}]}' \
  '{"total_count":1,"branch_policies":[{"name":"*","type":"branch"}]}' \
  '{"total_count":0,"branch_policies":[]}'; do
  expect_refused "branch_policy_other_than_main_fails_naming_environment $(jq -c '[.branch_policies[] | .name + ":" + .type]' <<<"$policies")" \
    "Environment settings-check-staging: its deployment branches must be exactly the branch main" \
    refs/heads/main 200 "$RESTRICTED" 200 "$policies"
done
expect_refused branch_policies_unreadable_fails_naming_environment_and_status \
  "Environment settings-check-staging: the REST API answered 403 for its branch policies" \
  refs/heads/main 200 "$RESTRICTED" 403 '{}'

# The workflows' references (ADR-ARCH-041): only this workflow names the Environment or its
# token, it does not name production's Environment, and it uses no deployment credential.
shopt -s nullglob
workflows=(.github/workflows/*.yml .github/workflows/*.yaml)
shopt -u nullglob
others=$(grep -l -E 'settings-check-|DOPPLER_SETTINGS_CHECK_TOKEN' "${workflows[@]}" | grep -v -x -F "$WORKFLOW" || true)
if [ -z "$others" ]; then
  ok only_this_workflow_names_the_environment_and_its_token
else
  ko only_this_workflow_names_the_environment_and_its_token "also named in: $others"
fi
# A secret is used through secrets.NAME, secrets['NAME'], secrets: inherit or the whole context.
uses="secrets *(\\. *|\\[ *['\"])(DOPPLER_MIGRATOR_TOKEN|COOLIFY_)|secrets: *inherit|toJSON\\( *secrets *\\)|vars *(\\. *|\\[ *['\"])COOLIFY_"
if ! grep -q -E "$uses" "$WORKFLOW"; then
  ok workflow_receives_no_deployment_credential
else
  ko workflow_receives_no_deployment_credential "$(grep -n -E "$uses" "$WORKFLOW")"
fi
if ! grep -q 'settings-check-production' "$WORKFLOW"; then
  ok workflow_does_not_name_production
else
  ko workflow_does_not_name_production "settings-check-production is named"
fi

echo
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
