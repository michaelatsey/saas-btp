#!/usr/bin/env bash
#
# Tests the comparison in the "Push by digest" step of publish-api-images.yml: the pushed image
# is attested only if it is the one the non-root check inspected (ADR-ARCH-030). The step's own
# script is read from the workflow file and run against fixture metadata files, with `docker`
# replaced by a stub that writes the push's metadata; nothing is built or pushed.
#
#   bash scripts/ci/publish-api-images.test.sh [workflow file]   # from the repository root
#
# Needs bash, jq and python3 with PyYAML. The metadata keys are those buildx writes: with the
# classic image store the load's metadata carries containerimage.config.digest; with the
# containerd store (Docker 29 on ubuntu-26.04) it carries only the manifest digest,
# containerimage.digest (docker/buildx v0.38.1, build/build.go, prefer-image-digest).

set -euo pipefail

WORKFLOW=${1:-.github/workflows/publish-api-images.yml}
[ -f "$WORKFLOW" ] || { echo "no such workflow file: $WORKFLOW" >&2; exit 2; }

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

python3 -I - "$WORKFLOW" "$work/push.sh" <<'PY'
import sys, yaml
steps = yaml.safe_load(open(sys.argv[1]))["jobs"]["publish"]["steps"]
script = [s["run"] for s in steps if s.get("name") == "Push by digest"]
if len(script) != 1:
    sys.exit("expected one 'Push by digest' step")
open(sys.argv[2], "w").write(script[0])
PY

# The stub writes $PUSH_METADATA to the file named by --metadata-file.
mkdir "$work/bin"
cat > "$work/bin/docker" <<'SH'
#!/usr/bin/env bash
while [ $# -gt 0 ]; do
  if [ "$1" = --metadata-file ]; then cp "$PUSH_METADATA" "$2"; exit 0; fi
  shift
done
exit 0
SH
chmod +x "$work/bin/docker"

M1=sha256:1111111111111111111111111111111111111111111111111111111111111111
M2=sha256:2222222222222222222222222222222222222222222222222222222222222222
C1=sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
C2=sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
OCI=application/vnd.oci.image.manifest.v1+json
DOCKER_MANIFEST=application/vnd.docker.distribution.manifest.v2+json

# metadata <manifest digest> <config digest or ""> <media type>
metadata() {
  jq -n --arg m "$1" --arg c "$2" --arg t "$3" '
    {"containerimage.digest": $m, "containerimage.descriptor": {mediaType: $t, digest: $m}}
    + (if $c == "" then {} else {"containerimage.config.digest": $c} end)'
}

pass=0
fail=0

# case <name> <expected exit: 0|1> <expected output fragment> <load metadata> <push metadata>
case_() {
  local name=$1 want=$2 fragment=$3 dir="$work/$pass-$fail" code
  mkdir -p "$dir"
  printf '%s\n' "$4" > "$dir/load-metadata.json"
  printf '%s\n' "$5" > "$dir/push.json"
  : > "$dir/output"
  set +e
  PATH="$work/bin:$PATH" PUSH_METADATA="$dir/push.json" RUNNER_TEMP="$dir" GITHUB_OUTPUT="$dir/output" \
    IMAGE=ghcr.io/owner/image SHA=0123456789abcdef0123456789abcdef01234567 \
    GITHUB_REPOSITORY=owner/repo DOCKERFILE=Dockerfile \
    bash -eo pipefail "$work/push.sh" > "$dir/log" 2>&1
  code=$?
  set -e
  if [ "$code" -eq "$want" ] && grep -q -F -e "$fragment" "$dir/log"; then
    pass=$((pass + 1))
    echo "ok   $name"
  else
    fail=$((fail + 1))
    echo "FAIL $name (exit $code, expected $want and '$fragment')"
    sed 's/^/     | /' "$dir/log"
  fi
}

case_ "classic store, same config: pushed" 0 "Pushed ghcr.io/owner/image@$M1" \
  "$(metadata "$M1" "$C1" "$DOCKER_MANIFEST")" "$(metadata "$M1" "$C1" "$OCI")"
case_ "classic store, other config: refused" 1 "has config '$C2', not the checked '$C1'" \
  "$(metadata "$M1" "$C1" "$DOCKER_MANIFEST")" "$(metadata "$M2" "$C2" "$OCI")"
case_ "containerd store, same manifest: pushed" 0 "Pushed ghcr.io/owner/image@$M1" \
  "$(metadata "$M1" "" "$OCI")" "$(metadata "$M1" "$C1" "$OCI")"
case_ "containerd store, other manifest: refused" 1 "is not the checked manifest '$M1'" \
  "$(metadata "$M1" "" "$OCI")" "$(metadata "$M2" "$C1" "$OCI")"
case_ "no digest in the load's metadata: refused" 1 "is not the checked manifest '<none>'" \
  '{}' "$(metadata "$M1" "$C1" "$OCI")"
case_ "no pushed digest: refused" 1 "Could not read the pushed digest" \
  "$(metadata "$M1" "" "$OCI")" '{}'
case_ "pushed reference not an image manifest: refused" 1 "not a single image manifest" \
  "$(metadata "$M1" "" "$OCI")" "$(metadata "$M1" "$C1" "$DOCKER_MANIFEST")"

echo "publish-api-images.test.sh: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
