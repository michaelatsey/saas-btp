#!/usr/bin/env bash
# Tests for review.sh (fix-issue agreement review, issue #135). Every fixture is built at
# runtime in a temporary directory outside the repository; two tests create, then remove,
# one file in the repository to show that the write guard sees it.
#
#   bash .claude/skills/fix-issue/review.test.sh      # from the repository root
#
# Fake secret values are assembled at runtime, so that this file passes its own scan.
set -u
REPO="$(git -C "$(dirname "$(realpath "$0")")" rev-parse --show-toplevel)"
R="$REPO/.claude/skills/fix-issue/review.sh"
W="$(mktemp -d)"; F="$W/fixtures"; mkdir -p "$F"
made_codex_dir=0; made_fixtures=0; made_worktree=0
cleanup() {
  rm -f "$REPO/review-guard-fixture.tmp" "$REPO/rel-out.json" "$REPO/guard-record"
  [ "$made_fixtures" -eq 1 ] && rm -f "$REPO/.env.review-fixture" "$REPO/review-copy-fixture.tmp" "$REPO/review-link-fixture.tmp"
  [ "$made_codex_dir" -eq 1 ] && rm -rf "$REPO/.codex"
  [ "$made_worktree" -eq 1 ] && { git -C "$REPO" worktree remove --force "$W/wt" 2>/dev/null; git -C "$REPO" worktree prune; }
  rm -rf "$W"
}
trap cleanup EXIT
pass=0; fail=0
expect() { # name expected_exit actual_exit
  if [ "$2" = "$3" ]; then echo "PASS $1 (exit $3)"; pass=$((pass+1)); else echo "FAIL $1 (expected $2, got $3)"; fail=$((fail+1)); fi
}

cat > "$F/convention-blocking.json" <<'EOF'
{
  "verdict": "block",
  "summary": "Fixture: a blocking finding whose only basis is a convention.",
  "blocking_findings": [
    {
      "title": "The plan does not cite the provider documentation",
      "detail": "CLAUDE.md requires reading the provider's current documentation before wiring an external service.",
      "location": "Plan, opening paragraph",
      "basis": { "kind": "convention", "reference": "CLAUDE.md, Working rules" },
      "recommendation": "Cite the documentation read."
    }
  ],
  "non_blocking_findings": []
}
EOF
cat > "$F/verdict-disagrees.json" <<'EOF'
{
  "verdict": "approve",
  "summary": "Fixture: verdict approve while a blocking finding is listed.",
  "blocking_findings": [
    {
      "title": "Done when item 2 is not addressed",
      "detail": "The plan has no step for it.",
      "location": null,
      "basis": { "kind": "requirement", "reference": "Issue Done when, item 2" },
      "recommendation": "Add a step."
    }
  ],
  "non_blocking_findings": []
}
EOF
cat > "$F/valid-approve.json" <<'EOF'
{
  "verdict": "approve",
  "summary": "Fixture: a valid, consistent approval.",
  "blocking_findings": [],
  "non_blocking_findings": []
}
EOF

# 1. check: blocking finding based on a convention -> invalid (5)
bash "$R" check "$F/convention-blocking.json" >"$W/1.log" 2>&1; expect "check convention-blocking" 5 $?
# 2. check: verdict disagrees with blocking_findings -> inconsistent (5)
bash "$R" check "$F/verdict-disagrees.json" >"$W/2.log" 2>&1; expect "check verdict-disagrees" 5 $?
# 3. check: valid and consistent -> 0
bash "$R" check "$F/valid-approve.json" >"$W/3.log" 2>&1; expect "check valid-approve" 0 $?

# 4. scan: an input holding a fake GitHub token -> 3, and the value is not printed
tok="ghp_$(printf 'a%.0s' $(seq 36))"
printf '## Issue\nsome text\nGH_TOKEN=%s\n' "$tok" > "$W/secret.in"
bash "$R" scan "$W/secret.in" >"$W/4.log" 2>&1; expect "scan fake secret" 3 $?
if grep -q "$tok" "$W/4.log"; then echo "FAIL scan printed the value"; fail=$((fail+1)); else echo "PASS scan did not print the value"; pass=$((pass+1)); fi

# 4b. scan and codex: one fake value per shape, each built at runtime; each stops before codex
rep() { printf "$1%.0s" $(seq "$2"); }
declare -A SHAPES=(
  [openai-key]="sk-proj-$(rep A 40)"
  [github-token]="gho_$(rep b 36)"
  [github-pat]="github_pat_$(rep C 30)"
  [private-key]="-----BEGIN RSA PRIV""ATE KEY-----"
  [jwt]="eyJ$(rep h 12).eyJ$(rep p 12).$(rep s 12)"
)
mkdir -p "$W/bin"
for shape in openai-key github-token github-pat private-key jwt; do
  printf '## Issue\ntext\n%s\n' "${SHAPES[$shape]}" > "$W/shape-$shape.in"
  bash "$R" scan "$W/shape-$shape.in" >"$W/4b-$shape.log" 2>&1; expect "scan $shape" 3 $?
  grep -q "^scan: $shape shape" "$W/4b-$shape.log" && { echo "PASS scan named $shape"; pass=$((pass+1)); } || { echo "FAIL scan did not name $shape"; fail=$((fail+1)); }
  grep -qF -- "${SHAPES[$shape]}" "$W/4b-$shape.log" && { echo "FAIL scan printed the $shape value"; fail=$((fail+1)); }
  rm -f "$W/codex-called"
  printf '#!/usr/bin/env bash\necho "$*" >> "%s/codex-called"\nexit 0\n' "$W" > "$W/bin/codex"; chmod +x "$W/bin/codex"
  PATH="$W/bin:$PATH" bash "$R" codex "$W/shape-$shape.in" "$W/shape-$shape-out.json" "$W/shape-$shape" >"$W/4c-$shape.log" 2>&1; expect "codex stops on $shape" 3 $?
  [ -e "$W/codex-called" ] && { echo "FAIL codex called for $shape"; fail=$((fail+1)); } || { echo "PASS codex not called for $shape"; pass=$((pass+1)); }
done

# 5. scan: the scanner's own source and SKILL.md hold no match -> 0
bash "$R" scan "$R" >"$W/5.log" 2>&1; expect "scan own source" 0 $?
bash "$R" scan "$REPO/.claude/skills/fix-issue/SKILL.md" >"$W/5b.log" 2>&1; expect "scan SKILL.md" 0 $?
bash "$R" scan "$REPO/.claude/skills/fix-issue/review.test.sh" >"$W/5c.log" 2>&1; expect "scan review.test.sh" 0 $?

# 6. codex: fake secret in the input -> stops with 3 before calling codex (stub records calls)
mkdir -p "$W/bin"; cat > "$W/bin/codex" <<STUB
#!/usr/bin/env bash
echo "\$*" >> "$W/codex-called"
exit 0
STUB
chmod +x "$W/bin/codex"
PATH="$W/bin:$PATH" bash "$R" codex "$W/secret.in" "$W/secret-out.json" "$W/secret" >"$W/6.log" 2>&1; expect "codex stops on secret" 3 $?
if [ -e "$W/codex-called" ]; then echo "FAIL codex was called"; fail=$((fail+1)); else echo "PASS codex was not called"; pass=$((pass+1)); fi

# 7. codex: stub exits non-zero -> unavailable (6)
cat > "$W/bin/codex" <<'STUB'
#!/usr/bin/env bash
case "$1" in --version) echo "codex-cli 0.0.0-stub";; login) echo "Not logged in"; exit 1;; *) echo "stub failure" >&2; exit 1;; esac
STUB
printf '## Issue\nclean input\n' > "$W/clean.in"
PATH="$W/bin:$PATH" bash "$R" codex "$W/clean.in" "$W/unavail-out.json" "$W/unavail" >"$W/7.log" 2>&1; expect "codex unavailable (stub exit 1)" 6 $?

# 8. codex: stub exits 0 but writes no output -> unavailable (6)
cat > "$W/bin/codex" <<'STUB'
#!/usr/bin/env bash
case "$1" in --version) echo "codex-cli 0.0.0-stub";; login) echo "Logged in using ChatGPT";; *) exit 0;; esac
STUB
PATH="$W/bin:$PATH" bash "$R" codex "$W/clean.in" "$W/empty-out.json" "$W/empty" >"$W/8.log" 2>&1; expect "codex no output" 6 $?

# 9. codex: stub writes into the working tree -> write guard (4)
cat > "$W/bin/codex" <<STUB
#!/usr/bin/env bash
case "\$1" in --version) echo "codex-cli 0.0.0-stub";; login) echo "Logged in using ChatGPT";;
  *) out=""; while [ \$# -gt 0 ]; do [ "\$1" = "-o" ] && out="\$2"; shift; done
     echo stub >> "$REPO/review-guard-fixture.tmp"; cp "$F/valid-approve.json" "\$out";; esac
STUB
PATH="$W/bin:$PATH" bash "$R" codex "$W/clean.in" "$W/write-out.json" "$W/write" >"$W/9.log" 2>&1; expect "codex write guard" 4 $?
rm -f "$REPO/review-guard-fixture.tmp"

# 10. codex: stub writes an inconsistent output -> 5
cat > "$W/bin/codex" <<STUB
#!/usr/bin/env bash
case "\$1" in --version) echo "codex-cli 0.0.0-stub";; login) echo "Logged in using ChatGPT";;
  *) out=""; while [ \$# -gt 0 ]; do [ "\$1" = "-o" ] && out="\$2"; shift; done
     cp "$F/verdict-disagrees.json" "\$out";; esac
STUB
PATH="$W/bin:$PATH" bash "$R" codex "$W/clean.in" "$W/bad-out.json" "$W/bad" >"$W/10.log" 2>&1; expect "codex inconsistent output" 5 $?

# 11. guard/compare: identical records -> 0; a changed file -> 4
bash "$R" guard "$W/g1" >/dev/null 2>&1; bash "$R" guard "$W/g2" >/dev/null 2>&1
bash "$R" compare "$W/g1" "$W/g2" >"$W/11.log" 2>&1; expect "compare unchanged" 0 $?
echo x > "$REPO/review-guard-fixture.tmp"; bash "$R" guard "$W/g3" >/dev/null 2>&1; rm -f "$REPO/review-guard-fixture.tmp"
bash "$R" compare "$W/g1" "$W/g3" >"$W/11b.log" 2>&1; expect "compare changed" 4 $?

# 12. paths inside the repository are refused (2)
bash "$R" guard "$REPO/guard-record" >"$W/12.log" 2>&1; expect "refuse record inside repo" 2 $?
[ -e "$REPO/guard-record" ] && { echo "FAIL record written in repo"; fail=$((fail+1)); rm -f "$REPO/guard-record"; }

# 13. scan: kebab-case words ending in "sk-" are not OpenAI keys (code-review finding 1)
printf 'docs/business-processes/BP-007-risk-assessment-and-mitigation/process.md\ntask-management-improvements-xyz-abc\n' > "$W/kebab.in"
bash "$R" scan "$W/kebab.in" >"$W/13.log" 2>&1; expect "scan kebab-case path not a key" 0 $?

# 14. check: a relative output path, run from outside the repository (code-review finding 3)
cp "$F/valid-approve.json" "$W/rel-ok.json"
(cd "$W" && bash "$R" check rel-ok.json) >"$W/14.log" 2>&1; expect "check relative valid output" 0 $?

# 15. codex: a relative output path resolves outside the repository, codex writes there (finding 2)
cat > "$W/bin/codex" <<STUB
#!/usr/bin/env bash
case "\$1" in --version) echo "codex-cli 0.0.0-stub";; login) echo "Logged in using ChatGPT";;
  *) out=""; while [ \$# -gt 0 ]; do [ "\$1" = "-o" ] && out="\$2"; shift; done
     cp "$F/valid-approve.json" "\$out";; esac
STUB
chmod +x "$W/bin/codex"
(cd "$W" && PATH="$W/bin:$PATH" bash "$R" codex clean.in rel-out.json rel) >"$W/15.log" 2>&1; expect "codex relative paths" 0 $?
[ -e "$REPO/rel-out.json" ] && { echo "FAIL codex wrote rel-out.json in the repository"; fail=$((fail+1)); rm -f "$REPO/rel-out.json"; } || { echo "PASS nothing written in the repository"; pass=$((pass+1)); }

# 16. check: the validator itself fails (pnpm unavailable) -> tool failure 7, not the reviewer's 5 (finding 4)
mkdir -p "$W/nopnpm"; printf '#!/usr/bin/env bash\necho "pnpm: registry unreachable" >&2\nexit 1\n' > "$W/nopnpm/pnpm"; chmod +x "$W/nopnpm/pnpm"
PATH="$W/nopnpm:$PATH" bash "$R" check "$F/valid-approve.json" >"$W/16.log" 2>&1; expect "check validator failure" 7 $?
# 17. check: unparsable JSON is the reviewer's invalid output (5)
printf '```json\n{"verdict":"approve"}\n```\n' > "$W/fenced.json"
bash "$R" check "$W/fenced.json" >"$W/17.log" 2>&1; expect "check unparsable output" 5 $?
# 18. check prints the pnpm version (finding 8)
grep -q '^check: pnpm [0-9]' "$W/3.log" && { echo "PASS check printed pnpm version"; pass=$((pass+1)); } || { echo "FAIL check did not print pnpm version"; fail=$((fail+1)); }

# 19-23. codex runs on a copy of the working tree, under a permission profile (issue #192).
# Fixtures in the repository, removed by the EXIT trap: an ignored file (matches .env.*), an
# untracked file, and an untracked symlink to the ignored file. Their content is not secret.
ign="$REPO/.env.review-fixture"; unt="$REPO/review-copy-fixture.tmp"; lnk="$REPO/review-link-fixture.tmp"
marker="ignored-fixture-content-$$-$RANDOM"
for p in "$ign" "$unt" "$lnk"; do [ -e "$p" ] || [ -L "$p" ] && { echo "FAIL fixture path already exists, left untouched: $p"; trap - EXIT; rm -rf "$W"; exit 1; }; done
made_fixtures=1
printf '%s\n' "$marker" > "$ign"; echo untracked > "$unt"; ln -s .env.review-fixture "$lnk"
git -C "$REPO" check-ignore -q "$ign" && ! git -C "$REPO" check-ignore -q "$unt" \
  && { echo "PASS fixtures: one ignored, one not"; pass=$((pass+1)); } || { echo "FAIL fixtures ignore status"; fail=$((fail+1)); }
# The stub records its arguments one per line, keeps a copy of the -C directory (symlinks
# kept as links), and writes a valid output; STUB_EXIT makes it fail after recording.
cat > "$W/bin/codex" <<STUB
#!/usr/bin/env bash
case "\$1" in --version) echo "codex-cli 0.0.0-stub";; login) echo "Logged in using ChatGPT";;
  *) printf '%s\n' "\$@" > "$W/args"; out=""; cd_dir=""
     while [ \$# -gt 0 ]; do case "\$1" in -o) out="\$2";; -C) cd_dir="\$2";; esac; shift; done
     printf '%s\n' "\$cd_dir" > "$W/cdir"; rm -rf "$W/seen"; cp -a "\$cd_dir" "$W/seen"
     [ -n "\${STUB_EXIT:-}" ] && exit "\$STUB_EXIT"
     cp "$F/valid-approve.json" "\$out";; esac
STUB
chmod +x "$W/bin/codex"
PATH="$W/bin:$PATH" bash "$R" codex "$W/clean.in" "$W/copy-out.json" "$W/copy" >"$W/19.log" 2>&1; expect "codex on the copy" 0 $?
cdir="$(cat "$W/cdir" 2>/dev/null)"
ok() { if eval "$2"; then echo "PASS $1"; pass=$((pass+1)); else echo "FAIL $1"; fail=$((fail+1)); fi; }
# 19. the copy: outside the repository and the scratch directory, tracked and new files, no ignored file
ok "copy is outside the repository" '[ -n "$cdir" ] && case "$cdir/" in "$REPO"/*) false;; *) true;; esac'
ok "copy is outside the scratch directory" '[ -n "$cdir" ] && case "$cdir/" in "$W"/*) false;; *) true;; esac'
ok "copy holds a tracked file" '[ -f "$W/seen/AGENTS.md" ]'
ok "copy holds a new untracked file" '[ -f "$W/seen/review-copy-fixture.tmp" ]'
ok "copy holds no ignored file" '[ ! -e "$W/seen/.env.review-fixture" ]'
# 20. the call: a permission profile denying every path of the repository root, no legacy sandbox
ok "no --sandbox" '! grep -qxE -e "--sandbox(=.*)?|-s" "$W/args"'
ok "no -m" '! grep -qxE -e "-m|--model(=.*)?" "$W/args"'
ok "skip git repo check" 'grep -qxF -e "--skip-git-repo-check" "$W/args"'
ok "profile extends :read-only" 'grep -qxF "permissions.fix-issue-review.extends=\":read-only\"" "$W/args"'
ok "profile selected" 'grep -qxF "default_permissions=\"fix-issue-review\"" "$W/args"'
fsarg="$(grep -m1 '^permissions\.fix-issue-review\.filesystem=' "$W/args")"
ok "repository root denied" 'grep -qF "\"$REPO\"=\"deny\"" <<< "$fsarg"'
# every denied path is a worktree root of this repository (or its common git directory),
# reached through some mount
ids="$( { git -C "$REPO" worktree list --porcelain | sed -n 's/^worktree //p'; git -C "$REPO" rev-parse --path-format=absolute --git-common-dir; } \
  | while IFS= read -r d; do stat -L -c %d:%i "$d" 2>/dev/null; done)"; all_same=1
while IFS= read -r p; do grep -qxF -- "$(stat -L -c %d:%i "$p" 2>/dev/null)" <<< "$ids" || all_same=0
done < <(grep -oE '"[^"]+"="deny"' <<< "$fsarg" | sed -E 's/^"(.*)"="deny"$/\1/')
ok "every denied path is a root of this repository" '[ "$all_same" -eq 1 ]'
if [ -e "/mnt/wslg/distro$REPO" ]; then
  ok "WSL second mount of the repository denied" 'grep -qF "\"/mnt/wslg/distro$REPO\"=\"deny\"" <<< "$fsarg"'
fi
# 21. symlinks are copied as links, never followed
ok "symlink copied as a link" '[ -L "$W/seen/review-link-fixture.tmp" ]'
ok "ignored content not in the copy" '! grep -rqF -- "$marker" "$W/seen"'
# 22. the copy is removed, after a normal run and after a failing one
ok "copy removed after the run" '[ -n "$cdir" ] && [ ! -e "$cdir" ]'
rm -f "$W/cdir"
STUB_EXIT=1 PATH="$W/bin:$PATH" bash "$R" codex "$W/clean.in" "$W/copyfail-out.json" "$W/copyfail" >"$W/22.log" 2>&1; expect "codex failing on the copy" 6 $?
cdir="$(cat "$W/cdir" 2>/dev/null)"
ok "copy removed after a failing run" '[ -n "$cdir" ] && [ ! -e "$cdir" ]'
# 23. an untracked .codex/ would be copied and could restore the legacy sandbox: refused (2)
if [ -e "$REPO/.codex" ]; then
  echo "SKIP .codex/ refusal: $REPO/.codex already exists and is not this test's to remove"
else
  made_codex_dir=1; mkdir "$REPO/.codex"; echo 'sandbox_mode = "read-only"' > "$REPO/.codex/config.toml"; rm -f "$W/args"
  copies_before="$(ls -d "${TMPDIR:-/tmp}"/fix-issue-review.* 2>/dev/null | sort)"
  PATH="$W/bin:$PATH" bash "$R" codex "$W/clean.in" "$W/codexdir-out.json" "$W/codexdir" >"$W/23.log" 2>&1; expect "codex refuses a .codex/ entry" 2 $?
  ok "codex not called with a .codex/ entry" '[ ! -e "$W/args" ]'
  ok "no copy left after a refused run" '[ "$(ls -d "${TMPDIR:-/tmp}"/fix-issue-review.* 2>/dev/null | sort)" = "$copies_before" ]'
  rm -rf "$REPO/.codex"; made_codex_dir=0
fi
rm -f "$ign" "$unt" "$lnk"; made_fixtures=0

# 24. run from a linked worktree, the main checkout is denied too (code-review finding 2):
# a temporary worktree of HEAD, holding the working tree's review.sh, removed afterwards.
wt="$W/wt"
if git -C "$REPO" worktree add -q --detach "$wt" HEAD >/dev/null 2>&1; then
  made_worktree=1
  cp "$R" "$wt/.claude/skills/fix-issue/review.sh"; rm -f "$W/args"
  PATH="$W/bin:$PATH" bash "$wt/.claude/skills/fix-issue/review.sh" codex "$W/clean.in" "$W/wt-out.json" "$W/wtp" >"$W/24.log" 2>&1; expect "codex from a linked worktree" 0 $?
  wfs="$(grep -m1 '^permissions\.fix-issue-review\.filesystem=' "$W/args" 2>/dev/null)"
  ok "linked worktree denied" 'grep -qF "\"$wt\"=\"deny\"" <<< "$wfs"'
  ok "main checkout denied from a linked worktree" 'grep -qF "\"$REPO\"=\"deny\"" <<< "$wfs"'
  git -C "$REPO" worktree remove --force "$wt"; made_worktree=0
else
  echo "FAIL could not create a temporary worktree"; fail=$((fail+1))
fi

echo "== $pass passed, $fail failed"
[ "$fail" -eq 0 ]
