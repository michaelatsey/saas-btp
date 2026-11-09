#!/usr/bin/env bash
# Mechanical part of the deliver-bp skill (issue #136). SKILL.md says when each one runs;
# this file only says what each one does.
#
#   deliver.sh title  BP-XXX                           the parent issue's title
#   deliver.sh check  <dir> BP-XXX                     0 clean, 8 a DoR or coverage item fails,
#                                                      2 the repository's labels unreadable
#   deliver.sh create <dir> BP-XXX                     0 created and verified, 2 refused before
#                                                      any write, 9 partial or not as planned
#   deliver.sh graph  BP-XXX <out.json> [--parent N]   0 written, 2 refused
#   deliver.sh next   <bp-issues.json>                 0 one `next:` line, 8 stop
#
# <dir> holds the breakdown: manifest.json and one Markdown body per issue (SKILL.md, step 4).
# Paths printed are relative to the repository or to <dir>, never absolute. Every file this
# script writes is outside the repository (exit 2 otherwise).
#
# DELIVER_BP_ROOT, for tests only, replaces the repository as the root the BP documents are
# read from. DELIVER_BP_LABELS, for tests only, names a file holding the repository's labels
# in the REST API's JSON form, read instead of GitHub. GitHub is reached through gh only, from the repository root.

set -euo pipefail

REPO="$(git -C "$(dirname "$(realpath "$0")")" rev-parse --show-toplevel)"
DOCS="${DELIVER_BP_ROOT:-$REPO}"
HEADINGS="Goal|Context|Scope|Out of scope|Done when|Proof"
LIST_LIMIT=1000

die() { echo "deliver.sh: $2" >&2; exit "$1"; }

outside_repo() {
  local p
  p="$(realpath -m -- "$1")"
  case "$p/" in "$REPO"/*) die 2 "refused, inside the repository: ${p#"$REPO"/}" ;; esac
}

# The BP folder, relative to the documents root: exactly one docs/business-processes/BP-XXX-*/.
bp_dir() {
  [[ "$1" =~ ^BP-[0-9]{3}$ ]] || die 2 "not a BP identifier: $1"
  local found=() d
  shopt -s nullglob
  for d in "$DOCS"/docs/business-processes/"$1"-*/; do found+=("${d%/}"); done
  shopt -u nullglob
  [ "${#found[@]}" -eq 1 ] || die 2 "expected exactly one docs/business-processes/$1-*/ folder, found ${#found[@]}"
  printf '%s\n' "${found[0]#"$DOCS"/}"
}

# The value of <key> in the front matter that opens <file>.
front() {
  awk -v k="$2" 'NR == 1 && $0 != "---" { exit } NR > 1 && $0 == "---" { exit }
                 NR > 1 && index($0, k ": ") == 1 { print substr($0, length(k) + 3); exit }' "$1"
}

cmd_title() {
  local rel t first
  rel="$(bp_dir "$1")"
  [ -f "$DOCS/$rel/process.md" ] || die 2 "no $rel/process.md"
  t="$(front "$DOCS/$rel/process.md" title)"
  [ -n "$t" ] || die 2 "no title in the front matter of $rel/process.md"
  # Lower-case after the first word (issues-convention.md section 1), except a leading acronym.
  first="${t:0:1}"
  [[ "${t:1:1}" =~ [[:upper:]] ]] || first="${first,}"
  printf 'Implement %s (%s%s)\n' "$1" "$first" "${t:1}"
}

# ---------------------------------------------------------------------------------------
# check
# ---------------------------------------------------------------------------------------

# The names of the repository's labels, one per line. A failed or empty read stops check
# (exit 2): it never passes without them.
repo_labels() {
  local raw names
  if [ -n "${DELIVER_BP_LABELS:-}" ]; then
    raw="$(cat -- "$DELIVER_BP_LABELS")" || die 2 "could not read the injected label list; check cannot pass"
  else
    raw="$(cd "$REPO" && gh api --paginate "repos/michaelatsey/saas-btp/labels?per_page=100")" \
      || die 2 "could not read the repository's labels; check cannot pass"
  fi
  names="$(jq -rs 'add // [] | .[].name' <<< "$raw" 2> /dev/null)" \
    || die 2 "the repository's labels could not be parsed; check cannot pass"
  [ -n "$names" ] || die 2 "the repository's label list is empty; check cannot pass"
  printf '%s\n' "$names"
}

FAILS=0
fail() { echo "check: FAIL $*"; FAILS=$((FAILS + 1)); }

# One "<heading>\t<non-blank lines>" per level-2 heading, outside code fences.
sections() {
  awk '/^```/ { f = !f; if (h != "") n++; next }
       !f && /^## / { if (h != "") print h "\t" n; h = $0; sub(/^## +/, "", h); sub(/[ \t]+$/, "", h); n = 0; next }
       h != "" && /[^[:space:]]/ { n++ }
       END { if (h != "") print h "\t" n }' "$1"
}

# The lines of one level-2 section of <file>, outside code fences.
section() {
  awk -v want="$2" '/^```/ { f = !f } !f && /^## / { h = $0; sub(/^## +/, "", h); sub(/[ \t]+$/, "", h); next }
                    h == want { print }' "$1"
}

# Does <file> name <id>, not as the prefix of a longer number (contract.md §1 is not §10)?
names() {
  local re
  re="$(printf '%s' "$2" | sed 's/[.[\*^$()+?{|]/\\&/g')"
  grep -qE -- "$re([^0-9]|$)" "$1"
}

check_issue() { # where body-file title labels-json
  local where="$1" body="$2" title="$3" labels="$4" want got empty offline l
  if [ ! -f "$body" ]; then fail "$where: body file $(basename "$body") not found"; return; fi
  case "$title" in *.) fail "$where: title ends with a period" ;; esac
  offline="$(jq -r 'index("offline") != null' <<< "$labels")"
  want="$HEADINGS"; [ "$offline" = true ] && want="$want|Offline behavior"
  got="$(sections "$body" | cut -f1 | paste -sd'|' -)"
  [ "$got" = "$want" ] || fail "$where: headings are \"${got//|/, }\", expected \"${want//|/, }\""
  empty="$(sections "$body" | awk -F'\t' '$2 == 0 { print $1 }' | paste -sd, -)"
  [ -z "$empty" ] || fail "$where: empty section(s): $empty"
  grep -qF -- "$REL" "$body" || fail "$where: the body does not link $REL"
  # Label rules, collected in this shell so that each failure is counted by fail().
  while IFS= read -r msg; do fail "$where: $msg"; done < <(jq -r '
    def count(p): [.[] | select(test(p))] | length;
    (if count("^ctx:") == 1 then empty else "exactly one ctx:* label, found \(count("^ctx:"))" end),
    (if count("^type:(story|spike|chore|bug)$") == 1 and count("^type:") == 1 then empty
     else "exactly one type:story|spike|chore|bug label" end),
    (if count("^prio:(critical|high|normal|low)$") == 1 and count("^prio:") == 1 then empty
     else "exactly one prio:critical|high|normal|low label" end),
    (.[] | select(test("^(ctx:[a-z-]+|type:[a-z]+|prio:[a-z]+|offline)$") | not) | "unexpected label \"\(.)\""),
    (group_by(.)[] | select(length > 1) | "label \"\(.[0])\" given twice")
  ' <<< "$labels")
  # Every label exists in the repository (the convention adds labels just-in-time, so the
  # repository, not a list in this script, is the reference).
  while IFS= read -r l; do
    grep -qxF -- "$l" <<< "$REPO_LABELS" || fail "$where: label \"$l\" does not exist in the repository"
  done < <(jq -r '.[]' <<< "$labels")
  return 0
}

# Creation order: each sub-issue after the sub-issues it is blocked by, ties in manifest
# order. Fails on a cycle.
order() {
  jq -r '
    [.sub_issues[] | {k: .key, d: [(.blocked_by // [])[] | select(type == "string")]}] as $n
    | def step($done; $left):
        if ($left | length) == 0 then $done
        else
          [$left[] | . as $k | select(($n[] | select(.k == $k) | .d) | all(. as $x | $done | index($x) != null))] as $ready
          | if ($ready | length) == 0 then error("cycle among: " + ($left | join(", ")))
            else step($done + [$ready[0]]; $left - [$ready[0]]) end
        end;
      step([]; [$n[].k]) | .[]' "$1"
}

cmd_check() {
  local dir="$1" bp="$2" m shape bpc ids id s n where body title labels known err
  outside_repo "$dir"
  REL="$(bp_dir "$bp")"
  m="$dir/manifest.json"
  [ -f "$m" ] || die 2 "no manifest.json in the breakdown directory"
  for f in process.md acceptance.md contract.md; do
    [ -f "$DOCS/$REL/$f" ] || die 2 "no $REL/$f"
  done
  if ! jq -e 'type == "object"' "$m" > /dev/null 2>&1; then fail "manifest.json is not a JSON object"; return 8; fi

  shape="$(jq -r --arg bp "$bp" '
    def str: type == "string" and length > 0;
    def issue($w):
      if type != "object" then "\($w): not an object" else
        (if (.title | str) then empty else "\($w): no title" end),
        (if (.body | str) and (.body | test("^[^/]") ) and (.body | test("\\.\\.") | not) then empty
         else "\($w): body must name a file inside the breakdown directory" end),
        (if (.labels | type) == "array" and all(.labels[]; type == "string") then empty
         else "\($w): labels must be a list of names" end)
      end;
    (if .bp == $bp then empty else "manifest: bp is not \($bp)" end),
    (.parent | issue("parent")),
    (if (.sub_issues | type) == "array" and (.sub_issues | length) > 0 then
       .sub_issues | to_entries[] | .key as $i | .value as $s
       | if ($s | type) != "object" then "sub_issues[\($i)]: not an object" else
         ($s | issue("sub_issues[\($i)]")),
         (if ($s.key | type) == "string" and ($s.key | test("^[a-z0-9][a-z0-9-]*$")) then empty
          else "sub_issues[\($i)]: key missing or not kebab-case" end),
         (if ($s.blocked_by // [] | type) == "array"
             and all(($s.blocked_by // [])[]; type == "string" or (type == "number" and . == floor and . > 0))
          then empty else "sub_issues[\($i)]: blocked_by must list sub-issue keys or issue numbers" end),
         (if ($s.covers | type) == "array" and all($s.covers[]; type == "string") then empty
          else "sub_issues[\($i)]: covers must be a list of identifiers" end)
         end
     else "manifest: sub_issues missing or empty" end),
    (if (.deferred // [] | type) == "array" and all((.deferred // [])[]; type == "object" and (.id | str) and (.reason | str))
     then empty else "manifest: each deferred entry needs an id and a reason" end)
  ' "$m")"
  if [ -n "$shape" ]; then
    while IFS= read -r l; do fail "$l"; done <<< "$shape"
    return 8
  fi
  REPO_LABELS="$(repo_labels)"     # an assignment: a failed read stops check here (exit 2)

  # Titles, keys and the parent title.
  while IFS= read -r l; do fail "$l"; done < <(jq -r '
    ([.parent.title, .sub_issues[].title] | group_by(.)[] | select(length > 1) | "title \"\(.[0])\" used twice"),
    ([.sub_issues[].key] | group_by(.)[] | select(length > 1) | "key \"\(.[0])\" used twice")' "$m")
  title="$(cmd_title "$bp")"
  [ "$(jq -r .parent.title "$m")" = "$title" ] || fail "parent: title must be \"$title\""

  # Each issue: headings, sections, BP link, labels.
  check_issue parent "$dir/$(jq -r .parent.body "$m")" "$(jq -r .parent.title "$m")" "$(jq -c .parent.labels "$m")"
  n="$(jq '.sub_issues | length' "$m")"
  for ((s = 0; s < n; s++)); do
    where="$(jq -r ".sub_issues[$s].key" "$m")"
    check_issue "$where" "$dir/$(jq -r ".sub_issues[$s].body" "$m")" \
      "$(jq -r ".sub_issues[$s].title" "$m")" "$(jq -c ".sub_issues[$s].labels" "$m")"
  done

  # Dependencies: known keys, no self-reference, no cycle.
  known="$(jq -r '
    [.sub_issues[].key] as $keys
    | .sub_issues[] | .key as $k | (.blocked_by // [])[] | select(type == "string")
    | if . == $k then "\($k): blocked by itself"
      elif (. as $b | $keys | index($b)) == null then "\($k): blocked by unknown key \"\(.)\""
      else empty end' "$m")"
  if [ -n "$known" ]; then
    while IFS= read -r l; do fail "$l"; done <<< "$known"
  elif ! err="$(order "$m" 2>&1 > /dev/null)"; then
    fail "dependencies: $(sed -n 's/.*cycle among/cycle among/p' <<< "$err" | head -1)"
  fi

  # Coverage: every scenario, every invariant the contract applies, every contract section.
  bpc="${bp//-/}"
  ids="$( { grep -oE "^\*\*$bpc-AC-[0-9]+" "$DOCS/$REL/acceptance.md" | sed 's/^\*\*//'
            front "$DOCS/$REL/contract.md" applies | grep -oE 'INV-[A-Z]+-[0-9]+' || true
            grep -oE '^## [0-9]+\.' "$DOCS/$REL/contract.md" | sed -E 's/^## ([0-9]+)\./contract.md §\1/'
          } | awk '!seen[$0]++')"
  [ -n "$ids" ] || fail "coverage: no scenario, invariant or contract section found in $REL"
  for ((s = 0; s < n; s++)); do
    where="$(jq -r ".sub_issues[$s].key" "$m")"
    body="$dir/$(jq -r ".sub_issues[$s].body" "$m")"
    while IFS= read -r id; do
      [ -n "$id" ] || continue
      if ! grep -qxF -- "$id" <<< "$ids"; then fail "$where: covers unknown identifier \"$id\""
      elif [ -f "$body" ] && ! names "$body" "$id"; then fail "$where: covers $id but its body does not name it"
      fi
    done < <(jq -r ".sub_issues[$s].covers[]" "$m")
  done
  body="$dir/$(jq -r .parent.body "$m")"
  while IFS= read -r id; do
    [ -n "$id" ] || continue
    if ! grep -qxF -- "$id" <<< "$ids"; then fail "deferred: unknown identifier \"$id\""
    elif [ -f "$body" ] && ! names <(section "$body" "Out of scope") "$id"; then fail "deferred: $id is not stated in the parent's Out of scope"
    fi
  done < <(jq -r '(.deferred // [])[].id' "$m")
  while IFS= read -r id; do
    jq -e --arg id "$id" 'any(.sub_issues[].covers[]; . == $id) or any((.deferred // [])[].id; . == $id)' "$m" > /dev/null \
      || fail "coverage: $id is neither covered by a sub-issue nor deferred"
  done <<< "$ids"

  if [ "$FAILS" -gt 0 ]; then
    echo "check: $FAILS item(s) failed for $bp ($REL)"
    return 8
  fi
  echo "check: clean, $bp ($REL): parent and $n sub-issue(s); $(grep -c . <<< "$ids") identifier(s) covered or deferred; creation order: $(order "$m" | paste -sd' ' -)"
}

# ---------------------------------------------------------------------------------------
# GitHub reads
# ---------------------------------------------------------------------------------------

# The number of the issue titled exactly <title>, in any state, or nothing. Read from the
# issues list, not the search index, so that an issue created a moment ago is found.
find_parent() {
  local list n
  list="$(gh issue list --state all --limit "$LIST_LIMIT" --json number,title)" || die 2 "could not list the issues"
  [ "$(jq length <<< "$list")" -lt "$LIST_LIMIT" ] \
    || die 2 "the issue listing reached its limit of $LIST_LIMIT; the parent cannot be found reliably, STOP"
  n="$(jq --arg t "$1" '[.[] | select(.title == $t)] | length' <<< "$list")"
  [ "$n" -le 1 ] || die 2 "$n issues are titled \"$1\"; STOP"
  jq -r --arg t "$1" '.[] | select(.title == $t) | .number' <<< "$list"
}

pages() { # endpoint -> one array, every page merged
  gh api --paginate --slurp "$1" | jq -c 'add // []'
}

cmd_graph() {
  local bp="$1" out="$2" p="${3:-}" title parent subs s blk acc='{}'
  outside_repo "$out"
  cd "$REPO"
  title="$(cmd_title "$bp")"
  if [ -z "$p" ]; then
    p="$(find_parent "$title")"
    if [ -z "$p" ]; then
      echo '{"parent":null}' > "$out"
      echo "graph: no issue titled \"$title\" (parent null)"
      return 0
    fi
  fi
  [[ "$p" =~ ^[0-9]+$ ]] || die 2 "not an issue number: $p"
  parent="$(gh api "repos/{owner}/{repo}/issues/$p" | jq -c '{number, state, title}')" || die 2 "could not read issue #$p"
  subs="$(pages "repos/{owner}/{repo}/issues/$p/sub_issues?per_page=100" \
          | jq -c 'map({number, title, state, labels: (.labels | map(if type == "object" then .name else . end))})')" \
    || die 2 "could not list the sub-issues of #$p"
  for s in $(jq -r '.[].number' <<< "$subs"); do
    blk="$(pages "repos/{owner}/{repo}/issues/$s/dependencies/blocked_by?per_page=100" \
           | jq -c 'map({repository: (.repository_url | sub("^.*/repos/"; "")), number, state})')" \
      || die 2 "could not list the blockers of #$s"
    acc="$(jq -c --arg n "$s" --argjson b "$blk" '. + {($n): $b}' <<< "$acc")"
  done
  jq -n --argjson parent "$parent" --argjson subs "$subs" --argjson blk "$acc" \
    '{parent: $parent, sub_issues: [$subs[] | . + {blocked_by: $blk[(.number | tostring)]}]}' > "$out"
  echo "graph: parent #$p ($(jq -r .state <<< "$parent")), $(jq length <<< "$subs") sub-issue(s)"
}

# ---------------------------------------------------------------------------------------
# next: the selection rule. Reads the graph file only.
# ---------------------------------------------------------------------------------------

cmd_next() {
  local g="$1" out
  [ -f "$g" ] || die 2 "no such graph: $(basename "$g")"
  jq -e '(.parent == null) or ((.parent.number | type) == "number" and (.sub_issues | type) == "array"
         and all(.sub_issues[]; (.number | type) == "number" and (.state | type) == "string"
                 and (.labels | type) == "array" and (.blocked_by | type) == "array"))' "$g" > /dev/null 2>&1 \
    || die 2 "not a graph written by deliver.sh graph: $(basename "$g")"
  out="$(jq -r '
    {"prio:critical": 0, "prio:high": 1, "prio:normal": 2, "prio:low": 3} as $rank
    | def prios: [.labels[] | select(startswith("prio:"))];
    if .parent == null then "stop: no parent issue for this BP; the breakdown has not been created"
    elif (.sub_issues | length) == 0 then
      "stop: parent #\(.parent.number) has no sub-issue; the breakdown is incomplete, for the maintainer"
    else
      .parent.number as $p
      | [.sub_issues[] | select(.state == "open")] as $open
      | [$open[] | select(all(.blocked_by[]; .state == "closed"))] as $cand
      | [$cand[] | select((prios | length) != 1 or $rank[prios[0]] == null)] as $bad
      | if ($open | length) == 0 then "next: none, every sub-issue of #\($p) is closed"
        elif ($bad | length) > 0 then
          "stop: open unblocked sub-issue(s) without exactly one prio:critical|high|normal|low label: "
          + ([$bad[] | "#\(.number) (\(prios | join(", ") | if . == "" then "none" else . end))"] | join(", "))
        elif ($cand | length) == 0 then
          "next: none, no open sub-issue of #\($p) is unblocked",
          ($open | sort_by(.number)[]
           | "  #\(.number) blocked by " + ([.blocked_by[] | select(.state != "closed") | "\(.repository)#\(.number)"] | join(", ")))
        else
          "next: /fix-issue \($cand | sort_by([$rank[prios[0]], .number]) | .[0].number)"
        end
    end' "$g")"
  printf '%s\n' "$out"
  case "$out" in stop:*) return 8 ;; esac
}

# ---------------------------------------------------------------------------------------
# create
# ---------------------------------------------------------------------------------------

CREATED=""; PARENT_NO=""
partial() {
  echo "create: STOPPED, $1"
  echo "create: created so far (created.json):"; jq . "$CREATED"
  if [ -n "$PARENT_NO" ]; then
    echo "create: sub-issues GitHub lists under #$PARENT_NO, by title:"
    pages "repos/{owner}/{repo}/issues/$PARENT_NO/sub_issues?per_page=100" | jq -r '.[] | "  #\(.number) \(.title)"' \
      || echo "  (could not be listed)"
  fi
  echo "create: the maintainer finishes or removes this partial creation before the next run"
  exit 9
}

record() { # jq-update key number -> created.json
  jq --arg k "$2" --argjson n "$3" "$1" "$CREATED" > "$CREATED.tmp" && mv "$CREATED.tmp" "$CREATED"
}

issue_no() { # gh issue create output -> number
  local url
  url="$(tail -1 <<< "$1")"
  [[ "$url" =~ /issues/([0-9]+)$ ]] && printf '%s\n' "${BASH_REMATCH[1]}"
}

cmd_create() {
  local dir m title existing known used l n k t body args out num blockers repo
  dir="$(realpath -m -- "$1")"; m="$dir/manifest.json"
  outside_repo "$dir"
  if ! (cmd_check "$dir" "$2") > "$dir/check.log" 2>&1; then
    cat "$dir/check.log"; die 2 "refused, the breakdown fails check; nothing created"
  fi
  cd "$REPO"
  title="$(jq -r .parent.title "$m")"
  existing="$(find_parent "$title")"     # an assignment: a failed lookup stops the run here
  [ -z "$existing" ] || die 2 "refused, an issue titled \"$title\" already exists (#$existing); nothing created"
  known="$(gh label list --limit "$LIST_LIMIT" --json name | jq -r '.[].name')" || die 2 "could not list the labels"
  used="$(jq -r '[.parent.labels[], .sub_issues[].labels[]] | unique[]' "$m")"
  while IFS= read -r l; do
    grep -qxF -- "$l" <<< "$known" || die 2 "refused, label \"$l\" does not exist in the repository; nothing created"
  done <<< "$used"
  for n in $(jq -r '[.sub_issues[].blocked_by // [] | .[] | select(type == "number")] | unique[]' "$m"); do
    gh issue view "$n" --json number > /dev/null 2>&1 \
      || die 2 "refused, blocker #$n is not an issue of this repository; nothing created"
  done
  repo="$(gh repo view --json nameWithOwner -q .nameWithOwner)" || die 2 "could not read the repository name"

  CREATED="$dir/created.json"
  echo '{"parent": null, "sub_issues": {}}' > "$CREATED"
  args=(--title "$title" --body-file "$dir/$(jq -r .parent.body "$m")")
  while IFS= read -r l; do args+=(--label "$l"); done < <(jq -r '.parent.labels[]' "$m")
  if ! out="$(gh issue create "${args[@]}")"; then
    num="$(issue_no "$out")" && { PARENT_NO="$num"; record '.parent = $n' "" "$num"; }
    partial "the parent could not be created${num:+ as planned (gh failed after creating #$num)}"
  fi
  num="$(issue_no "$out")" || partial "no issue number in gh's answer for the parent: $out"
  PARENT_NO="$num"
  record '.parent = $n' "" "$num"
  echo "create: parent #$num $title"

  for k in $(order "$m"); do
    t="$(jq -r --arg k "$k" '.sub_issues[] | select(.key == $k) | .title' "$m")"
    body="$(jq -r --arg k "$k" '.sub_issues[] | select(.key == $k) | .body' "$m")"
    args=(--title "$t" --body-file "$dir/$body" --parent "$PARENT_NO")
    while IFS= read -r l; do args+=(--label "$l"); done < <(jq -r --arg k "$k" '.sub_issues[] | select(.key == $k) | .labels[]' "$m")
    blockers="$(jq -r --arg k "$k" --slurpfile c "$CREATED" '
      .sub_issues[] | select(.key == $k) | [(.blocked_by // [])[] | if type == "string" then $c[0].sub_issues[.] else . end]
      | map(tostring) | join(",")' "$m")"
    [ -z "$blockers" ] || args+=(--blocked-by "$blockers")
    if ! out="$(gh issue create "${args[@]}")"; then
      num="$(issue_no "$out")" && record '.sub_issues[$k] = $n' "$k" "$num"
      partial "sub-issue \"$k\" could not be created${num:+ as planned (gh failed after creating #$num)}"
    fi
    num="$(issue_no "$out")" || partial "no issue number in gh's answer for \"$k\": $out"
    record '.sub_issues[$k] = $n' "$k" "$num"
    echo "create: #$num $k: $t${blockers:+ (blocked by $blockers)}"
  done

  # What GitHub now holds, compared with the manifest: every sub-issue under the parent, with
  # exactly its planned blockers.
  (cmd_graph "$2" "$dir/created-graph.json" "$PARENT_NO") > /dev/null 2>&1 \
    || partial "the created issues could not be read back from GitHub"
  out="$(jq -r --arg repo "$repo" --slurpfile c "$CREATED" --slurpfile g "$dir/created-graph.json" '
    $c[0].sub_issues as $nums | $g[0].sub_issues as $gs
    | (.sub_issues[] | .key as $k | $nums[$k] as $n | ($gs[] | select(.number == $n)) as $got
       | ([(.blocked_by // [])[] | if type == "string" then $nums[.] else . end] | map("\($repo)#\(.)") | sort) as $want
       | ([$got.blocked_by[] | "\(.repository)#\(.number)"] | sort) as $have
       | if $want == $have then empty else "#\($n) \($k): blocked by \($have | join(", ") | if . == "" then "nothing" else . end), planned \($want | join(", ") | if . == "" then "nothing" else . end)" end),
      (.sub_issues[] | .key as $k | select(([$gs[].number] | index($nums[$k])) == null) | "#\($nums[$k]) \($k): not listed under the parent"),
      ($gs[] | .number as $x | select(([$nums[]] | index($x)) == null) | "#\(.number): under the parent but not in the manifest")' "$m")"
  [ -z "$out" ] || partial "GitHub does not hold the breakdown as planned:
$out"
  echo "create: verified, GitHub holds the parent #$PARENT_NO and its $(jq '.sub_issues | length' "$m") sub-issue(s) with their planned blockers"
}

case "${1:-}" in
  title)  [ $# -eq 2 ] || die 2 "usage: title BP-XXX";                       cmd_title "$2" ;;
  check)  [ $# -eq 3 ] || die 2 "usage: check <dir> BP-XXX";                 cmd_check "$(realpath -m -- "$2")" "$3" ;;
  create) [ $# -eq 3 ] || die 2 "usage: create <dir> BP-XXX";                cmd_create "$2" "$3" ;;
  graph)  if [ $# -eq 3 ]; then cmd_graph "$2" "$(realpath -m -- "$3")"
          elif [ $# -eq 5 ] && [ "$4" = --parent ]; then cmd_graph "$2" "$(realpath -m -- "$3")" "$5"
          else die 2 "usage: graph BP-XXX <out.json> [--parent N]"; fi ;;
  next)   [ $# -eq 2 ] || die 2 "usage: next <bp-issues.json>";              cmd_next "$(realpath -m -- "$2")" ;;
  *) die 2 "usage: deliver.sh title|check|create|graph|next ..." ;;
esac
