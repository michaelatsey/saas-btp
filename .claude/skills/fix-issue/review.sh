#!/usr/bin/env bash
# Mechanical checks of the fix-issue agreement review (ADR-ORG-004, section 2).
# SKILL.md says when each one runs; this file only says what each one does.
#
#   review.sh scan    <input>                  0 clean, 3 secret-shaped value found
#   review.sh guard   <record>                 writes the working-tree fingerprint
#   review.sh compare <before> <after>         0 identical, 4 different
#   review.sh check   <output.json>            0 valid and consistent, 5 invalid or
#                                              inconsistent, 7 the validator itself failed
#   review.sh codex   <input> <output.json> <prefix>
#       scan, auth record, guard, codex exec, guard, check.
#       0 approve or block (read the verdict), 3 secret, 4 write, 5 invalid,
#       6 second reviewer unavailable, 7 validator failed.
#
# Paths are resolved once, from the caller's directory. Every path this script writes or
# validates (guard records, outputs, codex prefixes and inputs) must be outside the
# repository (exit 2 otherwise).

set -euo pipefail

REPO="$(git -C "$(dirname "$(realpath "$0")")" rev-parse --show-toplevel)"
SCHEMA=".claude-context/context/ai-os/review-output.schema.json"

die() { echo "review.sh: $2" >&2; exit "$1"; }

# Resolve a path once, from the caller's directory; every later use is absolute.
abs() { realpath -m -- "$1"; }

outside_repo() {
  local p
  p="$(realpath -m -- "$1")"
  case "$p/" in "$REPO"/*) die 2 "refused, inside the repository: $p" ;; esac
}

# Shapes of secret values. Each pattern is written so that it does not match its own
# text: the character right after each literal prefix is a bracket or a parenthesis,
# which the pattern itself does not accept there. The OpenAI prefix must start a word,
# so that kebab-case words ending in "sk-" ("risk-assessment-...") do not match.
SECRET_PATTERNS=(
  'openai-key|(^|[^A-Za-z0-9_-])sk-(proj-|svcacct-|admin-)?[A-Za-z0-9_-]{20,}'
  'github-token|gh[pousr]_[A-Za-z0-9]{36,}'
  'github-pat|github_pat_[A-Za-z0-9_]{22,}'
  'private-key|-----BEGIN ([A-Z0-9]+ )*PRIVATE KEY-----'
  'jwt|eyJ[A-Za-z0-9_-]{10,}[.]eyJ[A-Za-z0-9_-]{10,}[.][A-Za-z0-9_-]{10,}'
)

cmd_scan() {
  local input="$1" found=0 entry name re lines
  [ -f "$input" ] || die 2 "no such input: $input"
  for entry in "${SECRET_PATTERNS[@]}"; do
    name="${entry%%|*}"; re="${entry#*|}"
    # Print the pattern name and line numbers only, never the matched value.
    lines="$(grep -n -E -e "$re" -- "$input" | cut -d: -f1 | paste -sd, -)" || true
    if [ -n "$lines" ]; then
      echo "scan: $name shape at line(s) $lines of $input"
      found=1
    fi
  done
  if [ "$found" -eq 1 ]; then
    echo "scan: STOP, input not sent"
    return 3
  fi
  echo "scan: clean ($input)"
}

# Fingerprint: git status of tracked and untracked files, and the sha256 of every file
# git does not ignore. Ignored files are not hashed: they include the secret files that
# .claude/settings.json denies to Claude.
cmd_guard() {
  local record="$1"
  outside_repo "$record"
  {
    echo "# git status --porcelain=v1 --untracked-files=all"
    git -C "$REPO" status --porcelain=v1 --untracked-files=all
    echo "# sha256 of tracked and untracked, non-ignored files"
    (cd "$REPO" && git ls-files -z --cached --others --exclude-standard --deduplicate \
      | xargs -0 -r sh -c 'for f; do [ -f "$f" ] && printf "%s\0" "$f"; done; true' _ \
      | xargs -0 -r sha256sum --)
  } > "$record"
  echo "guard: $(basename "$record") files=$(grep -c '^[0-9a-f]\{64\} ' "$record" || true) digest=$(sha256sum "$record" | cut -c1-16)"
}

cmd_compare() {
  local before="$1" after="$2"
  if diff -u "$before" "$after" > /dev/null; then
    echo "guard: unchanged ($(basename "$before") = $(basename "$after"))"
  else
    echo "guard: CHANGED during the review, STOP"
    diff -u "$before" "$after" | grep -E '^[+-][^+-]' || true
    return 4
  fi
}

# A reviewer's output that is not JSON, or that the schema rejects, is the reviewer's
# (5). A validator that cannot run at all is not (7): the step stops for the maintainer.
cmd_check() {
  local out="$1" rc=0 log arc=0
  outside_repo "$out"
  [ -s "$out" ] || { echo "check: no output at $out"; return 5; }
  case "$out" in *.json) ;; *) die 2 "output must end in .json: $out" ;; esac
  echo "check: $(basename "$out") sha256 $(sha256sum "$out" | cut -c1-16)"
  if ! jq -e . "$out" > /dev/null 2>&1; then
    echo "check: INVALID, not a JSON document, counts as one blocking finding"
    return 5
  fi
  echo "check: pnpm $(pnpm --version 2>/dev/null || echo unavailable), ajv-cli 5.0.0"
  log="$(cd "$REPO" && pnpm dlx ajv-cli@5.0.0 validate --spec=draft2020 --strict=true \
        -s "$SCHEMA" -d "$out" 2>&1)" || arc=$?
  printf '%s\n' "$log"
  if [ "$arc" -eq 0 ] && grep -qxF "$out valid" <<< "$log"; then
    :
  elif grep -qxF "$out invalid" <<< "$log"; then
    echo "check: INVALID against $SCHEMA, counts as one blocking finding"
    rc=5
  else
    echo "check: VALIDATOR FAILED (exit $arc), not the reviewer's output, STOP"
    return 7
  fi
  if [ "$rc" -eq 0 ]; then
    if jq -e '(.verdict == "block") == ((.blocking_findings | length) > 0)' "$out" > /dev/null; then
      echo "check: verdict $(jq -r .verdict "$out"), blocking $(jq '.blocking_findings | length' "$out"), non-blocking $(jq '.non_blocking_findings | length' "$out")"
    else
      echo "check: INCONSISTENT, verdict $(jq -r .verdict "$out") with $(jq '.blocking_findings | length' "$out") blocking finding(s), counts as one blocking finding"
      rc=5
    fi
  fi
  return "$rc"
}

# Which authentication the run uses. `codex login status` prints the active mode
# (Codex docs, developer commands); it is reduced to a label so that no part of a
# credential reaches the record. Environment variables are reported by name only.
auth_record() {
  local status rc label v names=""
  status="$(codex login status 2>&1)" && rc=0 || rc=$?
  if   grep -qi 'not logged in' <<< "$status"; then label="not logged in"
  elif grep -qi 'chatgpt'       <<< "$status"; then label="ChatGPT login"
  elif grep -qi 'api key'       <<< "$status"; then label="API key"
  elif grep -qi 'access token'  <<< "$status"; then label="access token"
  else label="unrecognised"; fi
  for v in OPENAI_API_KEY CODEX_API_KEY CODEX_ACCESS_TOKEN; do
    [ -n "${!v:-}" ] && names="$names $v"
  done
  echo "auth: login status '$label' (exit $rc); CODEX_HOME=${CODEX_HOME:-default ~/.codex}; credential variables set:${names:- none}"
}

cmd_codex() {
  local input="$1" out="$2" prefix="$3" rc=0 wrc=0
  outside_repo "$input"; outside_repo "$out"; outside_repo "$prefix"
  case "$out" in *.json) ;; *) die 2 "output must end in .json: $out" ;; esac
  rm -f -- "$out"

  # Nothing reaches codex, not even `codex login status`, before the scan.
  cmd_scan "$input" || return 3

  echo "codex: $(codex --version 2>&1 | grep -m1 -E '^codex-cli' || echo 'version unknown')"
  auth_record
  cmd_guard "$prefix.before"

  (cd "$REPO" && codex exec --sandbox read-only --ephemeral --ignore-user-config -C "$REPO" \
      --output-schema "$SCHEMA" -o "$out" - < "$input") \
      > "$prefix.stdout" 2> "$prefix.stderr" || rc=$?
  echo "codex: exit $rc"

  cmd_guard "$prefix.after"
  cmd_compare "$prefix.before" "$prefix.after" || wrc=$?

  echo "codex: model $(grep -m1 -E '^model:' "$prefix.stderr" | sed 's/^model:[[:space:]]*//' || true)"
  echo "codex: tokens used $(grep -A1 -m1 -E '^tokens used' "$prefix.stderr" | tail -1 | tr -d '[:space:]' || true)"

  [ "$wrc" -eq 0 ] || return 4
  if [ "$rc" -ne 0 ] || [ ! -s "$out" ]; then
    echo "codex: UNAVAILABLE (exit $rc, output $( [ -s "$out" ] && echo present || echo missing )), STOP, not retried"
    tail -5 "$prefix.stderr" | sed 's/^/codex stderr: /'
    return 6
  fi
  cmd_check "$out" || return $?
}

case "${1:-}" in
  scan)    [ $# -eq 2 ] || die 2 "usage: scan <input>";                 cmd_scan "$(abs "$2")" ;;
  guard)   [ $# -eq 2 ] || die 2 "usage: guard <record>";               cmd_guard "$(abs "$2")" ;;
  compare) [ $# -eq 3 ] || die 2 "usage: compare <before> <after>";     cmd_compare "$(abs "$2")" "$(abs "$3")" ;;
  check)   [ $# -eq 2 ] || die 2 "usage: check <output.json>";          cmd_check "$(abs "$2")" ;;
  codex)   [ $# -eq 4 ] || die 2 "usage: codex <input> <output.json> <prefix>"
           cmd_codex "$(abs "$2")" "$(abs "$3")" "$(abs "$4")" ;;
  *) die 2 "usage: review.sh scan|guard|compare|check|codex ..." ;;
esac
