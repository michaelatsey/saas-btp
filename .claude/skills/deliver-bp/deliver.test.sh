#!/usr/bin/env bash
# Tests for deliver.sh (deliver-bp skill, issue #136). Every fixture is built at runtime in a
# temporary directory outside the repository. No test reaches GitHub: the subcommands that
# call gh run with a stub gh first on PATH, which answers from fixture files and records its
# arguments.
#
#   bash .claude/skills/deliver-bp/deliver.test.sh      # from the repository root
set -u
REPO="$(git -C "$(dirname "$(realpath "$0")")" rev-parse --show-toplevel)"
D="$REPO/.claude/skills/deliver-bp/deliver.sh"
W="$(mktemp -d)"
trap 'rm -rf "$W"' EXIT
pass=0; fail=0
ok()  { echo "PASS $1"; pass=$((pass+1)); }
ko()  { echo "FAIL $1"; fail=$((fail+1)); }
expect() { # name expected_exit actual_exit
  if [ "$2" = "$3" ]; then ok "$1 (exit $3)"; else ko "$1 (expected exit $2, got $3)"; fi
}
has()  { grep -qF -- "$2" "$1"; }                  # file text
line() { [ "$(grep -m1 '^next:' "$1")" = "$2" ]; } # file exact-next-line

# ---------------------------------------------------------------------------------------
# next: the selection rule, on fixture issue graphs. Pure: the gh first on PATH records any
# call and fails, and the test asserts it was never called.
# ---------------------------------------------------------------------------------------
G="$W/graphs"; mkdir -p "$G" "$W/nogh"
printf '#!/usr/bin/env bash\necho "$*" >> "%s/gh-called"\nexit 1\n' "$W" > "$W/nogh/gh"; chmod +x "$W/nogh/gh"
# Each sub-issue: number, state, labels, blocked_by (repository, number, state).
sub() { # number state prio blockers-json
  local labels='["ctx:access","type:story"'
  [ -n "$3" ] && labels="$labels,\"$3\""
  printf '{"number":%s,"state":"%s","labels":%s],"blocked_by":%s}' "$1" "$2" "$labels" "${4:-[]}"
}
blk() { printf '{"repository":"%s","number":%s,"state":"%s"}' "${3:-michaelatsey/saas-btp}" "$1" "$2"; }
graph() { # file sub...
  local f="$1"; shift
  local IFS=,
  printf '{"parent":{"number":300,"state":"open"},"sub_issues":[%s]}\n' "$*" > "$G/$f"
}
nx() { # fixture -> runs next with no gh reachable, output in $W/<fixture>.out
  PATH="$W/nogh:$PATH" bash "$D" next "$G/$1" > "$W/$1.out" 2>&1
}

# An open blocker: 301 (critical) is blocked by an open issue, so 302 (high) is named.
graph open-blocker.json "$(sub 301 open prio:critical "[$(blk 250 open)]")" "$(sub 302 open prio:high)"
nx open-blocker.json; expect "next open blocker" 0 $?
line "$W/open-blocker.json.out" "next: /fix-issue 302" && ok "open blocker: blocked 301 skipped, 302 named" || ko "open blocker: $(cat "$W/open-blocker.json.out")"

# A closed blocker counts as done: 301 (critical) is named.
graph closed-blocker.json "$(sub 301 open prio:critical "[$(blk 250 closed)]")" "$(sub 302 open prio:high)"
nx closed-blocker.json; expect "next closed blocker" 0 $?
line "$W/closed-blocker.json.out" "next: /fix-issue 301" && ok "closed blocker: 301 named" || ko "closed blocker: $(cat "$W/closed-blocker.json.out")"

# A blocker in another repository is read by its state like any other.
graph other-repo.json "$(sub 301 open prio:critical "[$(blk 7 open other/repo)]")" "$(sub 305 open prio:low)"
nx other-repo.json; line "$W/other-repo.json.out" "next: /fix-issue 305" && ok "blocker in another repository is honoured" || ko "other repo: $(cat "$W/other-repo.json.out")"

# Priority beats issue number: 309 (high) before 303 (normal).
graph prio-order.json "$(sub 303 open prio:normal)" "$(sub 309 open prio:high)" "$(sub 304 open prio:low)"
nx prio-order.json; line "$W/prio-order.json.out" "next: /fix-issue 309" && ok "priority beats issue number" || ko "prio order: $(cat "$W/prio-order.json.out")"

# A priority tie is broken by the lowest issue number, whatever the listing order.
graph prio-tie.json "$(sub 312 open prio:high)" "$(sub 306 open prio:high)" "$(sub 310 open prio:high)"
nx prio-tie.json; expect "next priority tie" 0 $?
line "$W/prio-tie.json.out" "next: /fix-issue 306" && ok "priority tie: lowest number 306 named" || ko "prio tie: $(cat "$W/prio-tie.json.out")"

# Every rank, from critical to low.
graph ranks.json "$(sub 301 open prio:low)" "$(sub 302 open prio:normal)" "$(sub 303 open prio:high)" "$(sub 304 open prio:critical)"
nx ranks.json; line "$W/ranks.json.out" "next: /fix-issue 304" && ok "critical ranks first" || ko "ranks: $(cat "$W/ranks.json.out")"

# A closed sub-issue is never named, even unblocked and of a higher priority.
graph closed-sub.json "$(sub 301 closed prio:critical)" "$(sub 302 open prio:low)"
nx closed-sub.json; line "$W/closed-sub.json.out" "next: /fix-issue 302" && ok "closed sub-issue never named" || ko "closed sub: $(cat "$W/closed-sub.json.out")"

# No open sub-issue is unblocked: none is named, and each open one is listed with its blockers.
graph none-unblocked.json "$(sub 301 open prio:high "[$(blk 250 open)]")" "$(sub 302 open prio:high "[$(blk 301 open),$(blk 251 closed)]")" "$(sub 303 closed prio:high)"
nx none-unblocked.json; expect "next none unblocked" 0 $?
line "$W/none-unblocked.json.out" "next: none, no open sub-issue of #300 is unblocked" && ok "none unblocked: none named" || ko "none unblocked: $(cat "$W/none-unblocked.json.out")"
grep -q '/fix-issue' "$W/none-unblocked.json.out" && ko "none unblocked: an issue was named" || ok "none unblocked: no /fix-issue line"
has "$W/none-unblocked.json.out" "#301 blocked by michaelatsey/saas-btp#250" && has "$W/none-unblocked.json.out" "#302 blocked by michaelatsey/saas-btp#301" \
  && ok "none unblocked: open blockers listed" || ko "none unblocked: blockers not listed: $(cat "$W/none-unblocked.json.out")"
has "$W/none-unblocked.json.out" "#251" && ko "none unblocked: a closed blocker was listed" || ok "none unblocked: closed blocker not listed"

# Every sub-issue closed.
graph all-closed.json "$(sub 301 closed prio:high)" "$(sub 302 closed prio:low)"
nx all-closed.json; expect "next all closed" 0 $?
line "$W/all-closed.json.out" "next: none, every sub-issue of #300 is closed" && ok "all closed reported" || ko "all closed: $(cat "$W/all-closed.json.out")"

# A candidate without exactly one prio:* label stops (8) and is named; nothing is guessed.
graph no-prio.json "$(sub 301 open "")" "$(sub 302 open prio:low)"
nx no-prio.json; expect "next candidate without priority stops" 8 $?
has "$W/no-prio.json.out" "#301" && ok "candidate without priority named in the stop" || ko "no prio: $(cat "$W/no-prio.json.out")"
grep -q '/fix-issue' "$W/no-prio.json.out" && ko "no prio: an issue was named" || ok "no prio: nothing named"
graph two-prio.json "$(sub 301 open 'prio:high","prio:low')"
nx two-prio.json; expect "next candidate with two priorities stops" 8 $?

# A closed or blocked sub-issue without a priority does not prevent naming the candidate.
graph closed-no-prio.json "$(sub 301 closed "")" "$(sub 302 open "" "[$(blk 250 open)]")" "$(sub 303 open prio:normal)"
nx closed-no-prio.json; expect "next ignores labels of non-candidates" 0 $?
line "$W/closed-no-prio.json.out" "next: /fix-issue 303" && ok "non-candidates without priority ignored" || ko "closed no prio: $(cat "$W/closed-no-prio.json.out")"

# No parent, and a parent with no sub-issue: stop (8), never "every sub-issue is closed".
echo '{"parent":null}' > "$G/no-parent.json"
nx no-parent.json; expect "next no parent stops" 8 $?
has "$W/no-parent.json.out" "no parent issue" && ok "no parent: reason given" || ko "no parent: $(cat "$W/no-parent.json.out")"
echo '{"parent":{"number":300,"state":"open"},"sub_issues":[]}' > "$G/no-subs.json"
nx no-subs.json; expect "next parent without sub-issue stops" 8 $?
has "$W/no-subs.json.out" "every sub-issue" && ko "no subs: reported as all closed" || ok "no subs: not reported as all closed"
[ -e "$W/gh-called" ] && ko "next called gh: $(cat "$W/gh-called")" || ok "next never called gh"

# ---------------------------------------------------------------------------------------
# title and check: fixture BP documents under a fixture root (DELIVER_BP_ROOT).
# ---------------------------------------------------------------------------------------
FR="$W/root"; BPD="$FR/docs/business-processes/BP-777-fixture-process"; mkdir -p "$BPD" "$FR/docs/business-processes/BP-778-acronym"
cat > "$BPD/process.md" <<'EOF'
---
id: BP-777
title: Fixture process
status: validated
---
# BP-777
EOF
cat > "$BPD/acceptance.md" <<'EOF'
---
status: validated
---
**BP777-AC-01 — First**
**BP777-AC-02 — Second**
EOF
cat > "$BPD/contract.md" <<'EOF'
---
status: validated
applies: [INV-FIX-01, ADR-ARCH-018, naming.md#x]
---
## 1. Owner
## 2. Interface
EOF
printf -- '---\ntitle: QHSE audit\n---\n' > "$FR/docs/business-processes/BP-778-acronym/process.md"
# The repository's labels, injected: no GitHub call. type:spike is left out on purpose.
jq -n '[ "ctx:access", "ctx:site", "ctx:safety", "ctx:corrective-actions", "ctx:media", "ctx:notifications",
         "ctx:platform", "type:story", "type:chore", "type:bug", "prio:critical", "prio:high",
         "prio:normal", "prio:low", "offline" ] | map({name: .})' > "$W/labels.json"
DR() { DELIVER_BP_ROOT="$FR" DELIVER_BP_LABELS="$W/labels.json" PATH="$W/nogh:$PATH" bash "$D" "$@"; }

[ "$(DR title BP-777)" = "Implement BP-777 (fixture process)" ] && ok "title lower-cases the first letter" || ko "title: $(DR title BP-777)"
[ "$(DR title BP-778)" = "Implement BP-778 (QHSE audit)" ] && ok "title keeps a leading acronym" || ko "title acronym: $(DR title BP-778)"
DR title BP-779 >/dev/null 2>&1; expect "title of a BP with no folder" 2 $?
DR title BP-7 >/dev/null 2>&1; expect "title of a malformed BP identifier" 2 $?

body() { # file scope-text [offline] [out-of-scope-text]
  {
    printf '## Goal\nG.\n\n## Context\nFrom docs/business-processes/BP-777-fixture-process/.\n\n'
    printf '## Scope\n%s\n\n## Out of scope\n%s\n\n## Done when\nIt holds.\n\n## Proof\nA test.\n' "$2" "${4:-Nothing.}"
    [ -n "${3:-}" ] && printf '\n## Offline behavior\nKept on the device.\n'
    true
  } > "$1"
}
V="$W/valid"; mkdir -p "$V"
body "$V/parent.md" "All the sub-issues." "" "BP777-AC-02, the client part."
body "$V/a.md" "BP777-AC-01, INV-FIX-01, contract.md §1."
body "$V/b.md" "contract.md §2." offline
cat > "$V/manifest.json" <<'EOF'
{
  "bp": "BP-777",
  "parent": {"title": "Implement BP-777 (fixture process)", "labels": ["ctx:access", "type:story", "prio:high"], "body": "parent.md"},
  "sub_issues": [
    {"key": "b", "title": "Keep the request offline", "labels": ["ctx:access", "type:story", "prio:normal", "offline"], "body": "b.md", "blocked_by": ["a", 184], "covers": ["contract.md §2"]},
    {"key": "a", "title": "Create the schema", "labels": ["ctx:access", "type:chore", "prio:high"], "body": "a.md", "blocked_by": [], "covers": ["BP777-AC-01", "INV-FIX-01", "contract.md §1"]}
  ],
  "deferred": [{"id": "BP777-AC-02", "reason": "client part, not in the pilot"}]
}
EOF
DR check "$V" BP-777 > "$W/check-valid.out" 2>&1; expect "check valid breakdown" 0 $?
has "$W/check-valid.out" "creation order: a b" && ok "check: creation order follows blocked_by" || ko "check order: $(cat "$W/check-valid.out")"

# One mutation per rule; each must fail (8) and name its cause.
mut() { # name expected-text jq-filter [file sed-expression]
  local c="$W/m-$1"; rm -rf "$c"; cp -r "$V" "$c"
  jq "$3" "$V/manifest.json" > "$c/manifest.json"
  [ -n "${4:-}" ] && sed -i -e "$5" "$c/$4"
  DR check "$c" BP-777 > "$c.out" 2>&1; local rc=$?
  if [ "$rc" = 8 ] && has "$c.out" "$2"; then ok "check fails on $1"; else ko "check $1 (exit $rc): $(cat "$c.out")"; fi
  grep -qF -- "$REPO" "$c.out" && ko "check $1 printed the repository path" || true
  grep -qF -- "$FR" "$c.out" && ko "check $1 printed the documents root" || true
}
mut missing-heading     'headings are'           '.' a.md '/^## Proof/,$d'
mut heading-order       'headings are'           '.' a.md 's/^## Scope/## Tmp/; s/^## Context/## Scope/; s/^## Tmp/## Context/'
mut extra-heading       'headings are'           '.' a.md '$a ## Notes\nx'
mut empty-section       'empty section(s): Done when' '.' a.md 's/^It holds\.$//'
mut offline-missing     'Offline behavior'       '.' b.md '/^## Offline behavior/,$d'
mut offline-misplaced   'headings are'           '.' b.md '/^## Goal/i ## Offline behavior\nFirst.\n'
mut offline-unlabelled  'headings are'           '(.sub_issues[0].labels) -= ["offline"]'
mut two-ctx             'exactly one ctx:*'      '.sub_issues[1].labels += ["ctx:site"]'
mut no-prio             'exactly one prio:'      '.sub_issues[1].labels -= ["prio:high"]'
mut unknown-label       'unexpected label "bug"' '.sub_issues[1].labels += ["bug"]'
mut no-type             'exactly one type:' '.sub_issues[1].labels -= ["type:chore"]'
mut duplicate-prio      'exactly one prio:' '.sub_issues[1].labels += ["prio:low"]'
mut duplicate-label     'label "ctx:access" given twice' '.sub_issues[1].labels += ["ctx:access"]'
mut parent-no-prio      'parent: exactly one prio:' '.parent.labels -= ["prio:high"]'
c="$W/m-counted"; rm -rf "$c"; cp -r "$V" "$c"; jq '.sub_issues[1].labels -= ["prio:high"] | .parent.labels += ["bug"]' "$V/manifest.json" > "$c/manifest.json"
DR check "$c" BP-777 > "$c.out" 2>&1; expect "check counts every label failure" 8 $?
# Three failures: no prio, and "bug" both outside the convention and absent from the repository.
has "$c.out" "check: 3 item(s) failed" && ok "check: every label failure counted" || ko "check label count: $(cat "$c.out")"
# Labels against the repository's labels (F3, maintainer decision (b)).
mut unknown-ctx         'label "ctx:unknown" does not exist in the repository' '.sub_issues[1].labels = ["ctx:unknown", "type:chore", "prio:high"]'
mut unknown-non-ctx     'label "type:spike" does not exist in the repository'  '.sub_issues[1].labels = ["ctx:access", "type:spike", "prio:high"]'
mut unknown-parent      'parent: label "ctx:unknown" does not exist'           '.parent.labels = ["ctx:unknown", "type:story", "prio:high"]'
# A failed read of the labels makes check fail, never pass: the gh first on PATH fails, so no
# GitHub call is made.
mkdir -p "$W/failgh"; printf '#!/usr/bin/env bash
echo "$*" >> "%s/failgh-called"
echo "HTTP 502" >&2
exit 1
' "$W" > "$W/failgh/gh"; chmod +x "$W/failgh/gh"
DELIVER_BP_ROOT="$FR" PATH="$W/failgh:$PATH" bash "$D" check "$V" BP-777 > "$W/labels-fail.out" 2>&1; expect "check fails when the labels cannot be read" 2 $?
has "$W/labels-fail.out" "could not read the repository's labels" && ! has "$W/labels-fail.out" "check: clean" \
  && ok "check: a failed label read is reported, never clean" || ko "labels fail: $(cat "$W/labels-fail.out")"
grep -qxF -- 'api --paginate repos/michaelatsey/saas-btp/labels?per_page=100' "$W/failgh-called" \
  && ok "check: labels read from repos/michaelatsey/saas-btp/labels, paginated" || ko "labels endpoint: $(cat "$W/failgh-called" 2>/dev/null)"
echo '[]' > "$W/labels-empty.json"
DELIVER_BP_ROOT="$FR" DELIVER_BP_LABELS="$W/labels-empty.json" PATH="$W/nogh:$PATH" bash "$D" check "$V" BP-777 > "$W/labels-empty.out" 2>&1
expect "check fails on an empty label list" 2 $?
printf 'not json' > "$W/labels-bad.json"
DELIVER_BP_ROOT="$FR" DELIVER_BP_LABELS="$W/labels-bad.json" PATH="$W/nogh:$PATH" bash "$D" check "$V" BP-777 > "$W/labels-bad.out" 2>&1
expect "check fails on an unreadable label list" 2 $?
mut no-bp-link          'does not link docs/business-processes/BP-777-fixture-process' '.' a.md 's#docs/business-processes/BP-777-fixture-process/#elsewhere#'
mut unknown-key         'unknown key "zz"'       '.sub_issues[0].blocked_by += ["zz"]'
mut self-blocked        'blocked by itself'      '.sub_issues[1].blocked_by = ["a"]'
mut cycle               'cycle among: b, a'      '.sub_issues[1].blocked_by = ["b"]'
mut uncovered           'BP777-AC-01 is neither covered' '.sub_issues[1].covers -= ["BP777-AC-01"]'
mut invariant-uncovered 'INV-FIX-01 is neither covered' '.sub_issues[1].covers -= ["INV-FIX-01"]'
mut section-uncovered   'contract.md §2 is neither covered' '.sub_issues[0].covers = []'
mut covered-not-named   'covers contract.md §1 but its body does not name it' '.' a.md 's/contract.md §1/contract.md §10/'
mut unknown-cover       'covers unknown identifier "BP777-AC-09"' '.sub_issues[1].covers += ["BP777-AC-09"]'
mut deferred-not-stated "BP777-AC-02 is not stated in the parent's Out of scope" '.' parent.md 's/BP777-AC-02/the second scenario/'
mut deferred-in-scope   "BP777-AC-02 is not stated in the parent's Out of scope" '.' parent.md 's/^All the sub-issues\.$/All, BP777-AC-02 included./; s/^BP777-AC-02, the client part\.$/Nothing./'
mut sub-not-object      'sub_issues[2]: not an object' '.sub_issues += ["x"]'
mut deferred-not-object 'needs an id and a reason' '.deferred = ["BP777-AC-02"]'
mut manifest-not-object 'not a JSON object' '[.]'
DR check "$REPO/.claude" BP-777 > "$W/check-inrepo.out" 2>&1; expect "check refuses a breakdown inside the repository" 2 $?
DR next "$W/missing.json" > "$W/next-missing.out" 2>&1; expect "next on a missing graph" 2 $?
grep -qF -- "$W" "$W/next-missing.out" && ko "next printed an absolute path" || ok "next prints no absolute path"
mut deferred-no-reason  'needs an id and a reason' '.deferred[0].reason = ""'
mut parent-title        'parent: title must be "Implement BP-777 (fixture process)"' '.parent.title = "Implement the fixture"'
mut duplicate-title     'used twice'             '.sub_issues[0].title = "Create the schema"'
mut trailing-period     'title ends with a period' '.sub_issues[1].title = "Create the schema."'
mut wrong-bp            'bp is not BP-777'       '.bp = "BP-001"'
mut body-escape         'inside the breakdown directory' '.sub_issues[1].body = "../a.md"'

# ---------------------------------------------------------------------------------------
# graph and create: a fake gh that keeps a small issue store in $GHS. Lists come back one
# item per page, so every list read goes through pagination.
# ---------------------------------------------------------------------------------------
mkdir -p "$W/fakegh"
cat > "$W/fakegh/gh" <<'STUB'
#!/usr/bin/env bash
S="$GHS"; printf '%s\n' "$*" >> "$S/calls"
db() { cat "$S/db.json"; }
case "$1 $2" in
  "issue list")  cat "$S/issues.json" ;;
  "label list")  printf '[{"name":"ctx:access"},{"name":"type:story"},{"name":"type:chore"},{"name":"prio:high"},{"name":"prio:normal"},{"name":"offline"}]\n' ;;
  "repo view")   echo "michaelatsey/saas-btp" ;;
  "issue view")  [ -e "$S/exists-$3" ] && echo "{\"number\":$3}" || { echo "Could not resolve to an issue" >&2; exit 1; } ;;
  "issue create")
    shift 2; title=""; parent=""; blocked=""; labels="[]"
    while [ $# -gt 0 ]; do case "$1" in
      --title) title="$2"; shift ;; --parent) parent="$2"; shift ;; --blocked-by) blocked="$2"; shift ;;
      --label) labels="$(jq -c --arg l "$2" '. + [$l]' <<< "$labels")"; shift ;; esac; shift; done
    case "$title" in *FAILME*) echo "GraphQL: something went wrong" >&2; exit 1 ;; esac
    n="$(cat "$S/next")"; echo $((n + 1)) > "$S/next"
    case "$title" in *NOLINK*) blocked="" ;; esac   # created, but its blocker link silently lost
    jq --argjson n "$n" --arg t "$title" --arg p "$parent" --arg b "$blocked" --argjson l "$labels" \
      '. + [{number: $n, title: $t, state: "open", parent: $p, labels: $l,
             blocked_by: ($b | if . == "" then [] else split(",") | map(tonumber) end)}]' "$S/db.json" > "$S/db.tmp" && mv "$S/db.tmp" "$S/db.json"
    echo "https://github.com/michaelatsey/saas-btp/issues/$n"
    case "$title" in *HALFWAY*) echo "failed to add the sub-issue" >&2; exit 1 ;; esac ;;  # created, then gh fails
  "api "*)
    ep="${@: -1}"; ep="${ep%%\?*}"; n="$(sed -E 's#^repos/\{owner\}/\{repo\}/issues/([0-9]+).*#\1#' <<< "$ep")"
    state() { db | jq -r --argjson n "$1" 'first(.[] | select(.number == $n) | .state) // empty' | grep . || cat "$S/state-$1" 2>/dev/null || echo open; }
    case "$ep" in
      */sub_issues)
        db | jq -c --arg p "$n" '[.[] | select(.parent == $p) | {number, title, state, labels: (.labels | map({name: .}))}] | if length == 0 then [[]] else map([.]) end' ;;
      */dependencies/blocked_by)
        for b in $(db | jq -r --argjson n "$n" '.[] | select(.number == $n) | .blocked_by[]'); do
          printf '{"number":%s,"state":"%s","repository_url":"https://api.github.com/repos/michaelatsey/saas-btp"}\n' "$b" "$(state "$b")"
        done | jq -cs 'if length == 0 then [[]] else map([.]) end' ;;
      *) [ -e "$S/fail-read-$n" ] && { echo "HTTP 502" >&2; exit 1; }
         db | jq -c --argjson n "$n" 'first(.[] | select(.number == $n)) // {number: $n, state: "open", title: "?"}' ;;
    esac ;;
  *) echo "fake gh: unhandled: $*" >&2; exit 1 ;;
esac
STUB
chmod +x "$W/fakegh/gh"
fresh() { # a new fake store: issues listed for title lookup, existing issue numbers
  GHS="$W/gh-$1"; rm -rf "$GHS"; mkdir -p "$GHS"; export GHS
  echo 500 > "$GHS/next"; echo '[]' > "$GHS/db.json"; echo "${2:-[]}" > "$GHS/issues.json"
  touch "$GHS/exists-184"
}
FG() { DELIVER_BP_ROOT="$FR" DELIVER_BP_LABELS="$W/labels.json" PATH="$W/fakegh:$PATH" bash "$D" "$@"; }
T='Implement BP-777 (fixture process)'

# graph: no parent, an exact match among near-matches, two parents, the listing limit.
fresh g0 '[{"number":10,"title":"Implement BP-777 (fixture process) again"},{"number":11,"title":"implement BP-777 (fixture process)"}]'
FG graph BP-777 "$W/g0.json" > "$W/g0.out" 2>&1; expect "graph without a parent" 0 $?
[ "$(jq -c . "$W/g0.json")" = '{"parent":null}' ] && ok "graph: near-matches are not the parent" || ko "graph g0: $(cat "$W/g0.json")"
fresh g1 "[{\"number\":10,\"title\":\"$T again\"},{\"number\":42,\"title\":\"$T\"}]"
jq '. + [{number: 42, title: "x", state: "open", parent: "", labels: [], blocked_by: []},
         {number: 43, title: "s1", state: "open", parent: "42", labels: ["prio:high"], blocked_by: [184, 44]},
         {number: 44, title: "s2", state: "closed", parent: "42", labels: ["prio:low"], blocked_by: []}]' "$GHS/db.json" > "$GHS/t" && mv "$GHS/t" "$GHS/db.json"
echo closed > "$GHS/state-184"
FG graph BP-777 "$W/g1.json" > "$W/g1.out" 2>&1; expect "graph with a parent" 0 $?
[ "$(jq -c '[.parent.number, (.sub_issues | map(.number)), (.sub_issues[0].blocked_by | map("\(.number):\(.state)"))]' "$W/g1.json")" = '[42,[43,44],["184:closed","44:closed"]]' ] \
  && ok "graph: exact parent, every page of sub-issues and blockers, with their state" || ko "graph g1: $(cat "$W/g1.json")"
[ "$(jq -c '.sub_issues[0].labels' "$W/g1.json")" = '["prio:high"]' ] && ok "graph: label objects reduced to names" || ko "graph labels: $(jq -c . "$W/g1.json")"
FG next "$W/g1.json" | grep -qx 'next: /fix-issue 43' && ok "graph then next names #43" || ko "graph then next"
fresh g2 "[{\"number\":42,\"title\":\"$T\"},{\"number\":43,\"title\":\"$T\"}]"
FG graph BP-777 "$W/g2.json" > "$W/g2.out" 2>&1; expect "graph with two parents stops" 2 $?
fresh g3 "$(jq -nc '[range(1000) | {number: ., title: "x"}]')"
FG graph BP-777 "$W/g3.json" > "$W/g3.out" 2>&1; expect "graph when the listing reaches its limit stops" 2 $?
fresh g4 '[]'
FG graph BP-777 "$W/g4.json" --parent 42 > "$W/g4.out" 2>&1; expect "graph --parent" 0 $?
grep -q '^issue list' "$GHS/calls" && ko "graph --parent looked the title up" || ok "graph --parent makes no title lookup"
FG graph BP-777 "$REPO/bp-issues.json" > /dev/null 2>&1; expect "graph refuses an output inside the repository" 2 $?
[ -e "$REPO/bp-issues.json" ] && { ko "graph wrote inside the repository"; rm -f "$REPO/bp-issues.json"; }

# create: dependency order, mapped blockers, external blocker passed through, verified.
fresh c1 '[]'; C="$W/c1"; cp -r "$V" "$C"
FG create "$C" BP-777 > "$W/c1.out" 2>&1; expect "create a valid breakdown" 0 $?
creates="$(grep '^issue create' "$GHS/calls")"
[ "$(sed -n 1p <<< "$creates" | grep -c -- "--title $T")" = 1 ] && ok "create: parent first" || ko "create order: $creates"
sed -n 2p <<< "$creates" | grep -q -- '--title Create the schema .*--parent 500' && ok "create: blocker a before b, under the parent" || ko "create: $creates"
sed -n 3p <<< "$creates" | grep -q -- '--parent 500 .*--blocked-by 501,184' && ok "create: blocked_by mapped to #501, external #184 passed through" || ko "create blockers: $creates"
[ "$(jq -c . "$C/created.json")" = '{"parent":500,"sub_issues":{"a":501,"b":502}}' ] && ok "create: numbers recorded" || ko "created.json: $(cat "$C/created.json")"
has "$W/c1.out" "create: verified" && ok "create: verified against GitHub" || ko "create verify: $(cat "$W/c1.out")"

# create refuses before any write: parent title exists, failing check, unknown label, external blocker not an issue.
refused() { # name expected-text
  if [ "$rc" = 2 ] && has "$W/$1.out" "$2" && ! grep -qs '^issue create' "$GHS/calls"; then ok "create refuses $1, nothing created"; else ko "create $1 (exit $rc): $(cat "$W/$1.out")"; fi
}
fresh c2 "[{\"number\":42,\"title\":\"$T\"}]"; C="$W/c2"; cp -r "$V" "$C"
FG create "$C" BP-777 > "$W/c2.out" 2>&1; rc=$?; refused c2 "already exists"
fresh c3 '[]'; C="$W/c3"; cp -r "$W/m-cycle" "$C"
FG create "$C" BP-777 > "$W/c3.out" 2>&1; rc=$?; refused c3 "fails check"
fresh c4 '[]'; C="$W/c4"; cp -r "$V" "$C"; jq '.sub_issues[1].labels = ["ctx:site", "type:chore", "prio:high"]' "$V/manifest.json" > "$C/manifest.json"
FG create "$C" BP-777 > "$W/c4.out" 2>&1; rc=$?; refused c4 'label "ctx:site" does not exist'
fresh c5 '[]'; rm -f "$GHS/exists-184"; C="$W/c5"; cp -r "$V" "$C"
FG create "$C" BP-777 > "$W/c5.out" 2>&1; rc=$?; refused c5 "blocker #184 is not an issue"

# create stops (9) on a failed call, and lists what GitHub holds under the parent.
fresh c6 '[]'; C="$W/c6"; cp -r "$V" "$C"; jq '.sub_issues[0].title = "Keep the request offline FAILME"' "$V/manifest.json" > "$C/manifest.json"
FG create "$C" BP-777 > "$W/c6.out" 2>&1; expect "create stops on a failed call" 9 $?
has "$W/c6.out" '"a": 501' && has "$W/c6.out" "#501 Create the schema" && ok "create: partial creation reported" || ko "create partial: $(cat "$W/c6.out")"
# create stops (9) when an issue exists without its planned blocker link.
fresh c7 '[]'; C="$W/c7"; cp -r "$V" "$C"; jq '.sub_issues[0].title = "Keep the request offline NOLINK"' "$V/manifest.json" > "$C/manifest.json"
FG create "$C" BP-777 > "$W/c7.out" 2>&1; expect "create detects a missing blocker link" 9 $?
has "$W/c7.out" "#502 b: blocked by nothing, planned michaelatsey/saas-btp#184, michaelatsey/saas-btp#501" && ok "create: missing link named" || ko "create nolink: $(cat "$W/c7.out")"
FG create "$REPO/.claude" BP-777 > /dev/null 2>&1; expect "create refuses a breakdown inside the repository" 2 $?
# create refuses when two issues already carry the parent title (the lookup's own stop is kept).
fresh c8 "[{\"number\":42,\"title\":\"$T\"},{\"number\":43,\"title\":\"$T\"}]"; C="$W/c8"; cp -r "$V" "$C"
FG create "$C" BP-777 > "$W/c8.out" 2>&1; rc=$?; refused c8 "2 issues are titled"
# create stops (9), not 2, when the read-back fails after the issues exist.
fresh c9 '[]'; touch "$GHS/fail-read-500"; C="$W/c9"; cp -r "$V" "$C"
FG create "$C" BP-777 > "$W/c9.out" 2>&1; expect "create: a failed read-back is a partial creation" 9 $?
has "$W/c9.out" '"b": 502' && ok "create: read-back failure still reports what was created" || ko "create c9: $(cat "$W/c9.out")"
# create keeps the number of an issue gh created before failing.
fresh c10 '[]'; C="$W/c10"; cp -r "$V" "$C"; jq '.sub_issues[0].title = "Keep the request offline HALFWAY"' "$V/manifest.json" > "$C/manifest.json"
FG create "$C" BP-777 > "$W/c10.out" 2>&1; expect "create: gh failing after creating" 9 $?
has "$W/c10.out" '"b": 502' && has "$W/c10.out" "gh failed after creating #502" && ok "create: issue created before the failure recorded" || ko "create c10: $(cat "$W/c10.out")"

echo
echo "deliver.test.sh: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
