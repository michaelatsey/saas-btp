# ADR-ORG-006 — Claude Code works under a machine account; `main` requires one approval, in a ruleset of its own that the repository admin bypasses for pull requests only

Status: accepted
Date: 2026-11-23
Supersedes: in ADR-ORG-005, §3 (the deferral of the identity), the §2 sentence "On the server,
a merge into `main` is protected by the ruleset alone, which does not require an approval", and
the Consequence beginning "Until the identity question is decided"; from the ruleset change of
#245 (see "When this takes effect").
Refs: issue #142; ADR-ORG-002; ADR-ORG-004; ADR-ORG-005; BP-001 pilot #138; implementation
#245; parent #132.

## Context
Claude Code acts under the maintainer's GitHub identity, so the server cannot tell its actions
from the maintainer's. Ruleset 18697625 protects `main` (read with
`gh api repos/michaelatsey/saas-btp/rulesets/18697625` on 2026-11-23 [R1]): deletion and
non-fast-forward refused, linear history, a pull request with squash as the only merge method,
stale approvals dismissed on push, the required checks `api-build-test` and `api-images`, no
bypass actor, and zero required approving reviews. Zero, because "Pull request authors cannot
approve their own pull requests." [S1]: the maintainer cannot approve a pull request opened
under their own identity.

ADR-ORG-005 denies the merge commands in `.claude/settings.json` as a second layer next to the
auto-mode classifier, not a boundary, and defers a separate identity to after the BP-001 pilot
(#138), its costs (token handling, commit attribution, the Claude.ai synchronisation of the
repository) being unmeasured. Its Context records that GitHub's own coding agent works under a
distinct identity that cannot approve or merge its pull requests.

The pilot is closed. Its proof (#138, comment 7113891743): none of the interruptions of its
section 4 came from the identity; the 15 timed merge reviews of its section 3 took 34 seconds to
5 minutes; its section 7 ties the auto-merge revisit to this decision.

On 2026-11-23 the maintainer compared three options (a machine account, a GitHub App, staying on
their own identity) against GitHub's and Anthropic's documentation and chose the machine account,
with the reservations below.

## Decision

### 1. A machine account that collaborates on this repository only
Claude Code works under a machine account, a collaborator on this repository and on no other.
GitHub's Terms of Service, B.3 [S5]: "A machine account is an Account set up by an individual
human who accepts the Terms on behalf of the Account, provides a valid email address, and is
responsible for its actions." "A machine account is used exclusively for performing automated
tasks." "You may maintain no more than one free machine account in addition to your free
Personal Account."

### 2. A personal access token (classic), `public_repo` and `workflow`, with an expiration date
The machine account's token is a personal access token (classic) with the `public_repo` and
`workflow` scopes and an expiration date, given to Claude Code sessions as `GH_TOKEN`.

- Not a fine-grained token: among the gaps of fine-grained tokens GitHub lists "Using
  fine-grained personal access token to contribute to repositories where the user is an outside
  or repository collaborator.", and states "Only personal access tokens (classic) have write
  access for public repositories that are not owned by you or an organization that you are not a
  member of." [S6]. The repository is public (ADR-ORG-001).
- `public_repo` "Limits access to public repositories." [S7].
- `workflow` "Grants the ability to add and update GitHub Actions workflow files." [S7]; pull
  requests of this repository change workflow files (#239 added
  `.github/workflows/check-supabase-auth-settings.yml`).
- An expiration date: "we highly recommend adding an expiration to your personal access
  tokens." [S6].
- `GH_TOKEN` is "an authentication token that will be used when a command targets
  `github.com`", and setting it "takes precedence over previously stored credentials." [S11].

### 3. One required approval, in a ruleset of its own, bypassed by the admin for pull requests only
`main` is protected by two rulesets:

- a new ruleset holding the approval requirement: one approving review, stale approvals still
  dismissed when commits are pushed ("Dismiss stale pull request approvals when new commits are
  pushed" [S2]), with a bypass "For pull requests only" for the repository admin role;
- ruleset 18697625, keeping every other rule, the required checks `api-build-test` and
  `api-images` included, with no bypass.

Several rulesets protect one branch together: "Multiple rulesets can apply to the same branch at
the same time"; "if multiple rulesets target the same branch or tag in a repository, the rules in
each of these rulesets are aggregated."; "If the same rule is defined in different ways across
the aggregated rulesets, the most restrictive version of the rule applies." [S4].

The bypass. The repository admin role is among the actors a ruleset can let bypass: "Repository
admins, organization owners, and enterprise owners" [S3]. For pull requests only: "The selected
actor is now required to open a pull request to make changes to a repository" and "The actor can
then choose to bypass any branch protections and merge that pull request." [S3]. The page does
not limit the bypass to pull requests the actor authored; this decision takes it as a derogation
that applies to any pull request the admin merges, not only the maintainer's own. The owner holds
the role here: the repository's only collaborator is its owner, with `role_name: admin` [R2]. The
machine account does not: "A repository owned by a personal account has two permission levels:
the repository owner and collaborators." [S10].

### 4. The merge deny rules stay, merging stays the maintainer's
The merge deny rules of ADR-ORG-005 §1 stay as a second layer. The human gate GitHub enforces
becomes the approval; merging stays the maintainer's.

### 5. The maintainer's own credentials are a separate limit
Giving Claude Code its own identity does not keep it from the maintainer's credentials. On the
current workstation Claude Code can still reach them: "The sandbox covers shell commands only.
Claude's file tools, MCP servers, and hooks run outside it." [S12]; and the Docker daemon, of
which Docker writes "only trusted users should be allowed to control your Docker daemon." [S13],
is reachable from the session (`docker version` run from a Claude Code session answered with a
server version on 2026-11-23 [R3]). The guarantee is therefore in depth, not a boundary. An
execution environment for agents where those credentials are absent is a possible evolution, not
a prerequisite.

### 6. Implementation
The account, its token, the session setup and the two rulesets are #245.

### When this takes effect
The parts of ADR-ORG-005 this decision supersedes are superseded when #245 changes the rulesets
of `main`. Until then, ADR-ORG-005 still describes the server's behaviour.

## Consequences
- A Claude Code pull request, authored by the machine account, cannot be merged before the
  maintainer approves it, except by the maintainer through the bypass (third item below), and
  a push that changes its diff dismisses the approval: stale
  approvals are dismissed "when commits are pushed that affect the diff in the pull request"
  [S2].
- The approval guards pull requests authored by the machine account only. A collaborator on a
  personal repository can "Submit a review on a pull request that affects the mergeability of the
  pull request" and "Create, merge, and close pull requests in the repository" [S10]: the machine
  account can approve a pull request authored by the maintainer and then merge it. The deny
  rules of ADR-ORG-005 §1 cover merge commands only (`.claude/settings.json` holds no other
  `Bash` rule), so the approval is stopped only by the classifier in auto mode or the
  permission prompt in manual mode; the merge that follows, by the ADR-ORG-005 deny rules too.
  Once the maintainer has approved a
  Claude Code pull request, the machine account can likewise merge it, stopped by the same two
  layers only. This limit leaves ADR-ORG-004 §3 ("Merging stays human") and §4 ("Every merge")
  in force; the server enforces them only in part.
- The maintainer can merge any pull request, a Claude Code one included, without an approval,
  through the bypass. The required checks and the other rules stay in ruleset 18697625, which has
  no bypass. The documentation does not state explicitly that a bypass granted in one ruleset
  leaves the rules of another in force; #245 tests it.
- The costs ADR-ORG-005 named. Token handling: a classic token with an expiration date, its
  storage and rotation in #245. Attribution: `gh` calls from sessions carry the machine account
  through `GH_TOKEN` [S11]; the git identity and the credentials `git push` uses are #245's. The
  Claude.ai synchronisation does not depend on who authors commits: "Only files (names and
  contents) in a repo on a specific branch are synced." "We do not retrieve commit history, PRs,
  or other metadata." [S14].
- "Your personal access token (classic) can access every repository that you can access." [S6]:
  the token reaches what the machine account reaches, which is why it collaborates on this
  repository only.
- Credentials on the workstation stay reachable (§5). With the bypass, the maintainer's
  credentials, if a session reaches them, are enough to merge any pull request without an
  approval; only the ADR-ORG-005 deny rules and the classifier stand in the way.
- ADR-ORG-004 is unchanged, auto-merge included (ADR-ORG-004 §3; #138 section 7). ADR-ORG-005's
  Consequence on revisiting its auto-merge rules with auto-merge is unchanged.

## Alternatives considered
- A GitHub App: "The installation access token will expire after 1 hour." [S8], and it is
  requested with a JSON web token, for which "In all cases, you must have a private key and the
  ID of your GitHub App." [S9]. A key to hold and tokens to renew every hour from a workstation
  session. Worth revisiting with an execution environment for agents or an organization.
- A fine-grained token: it cannot contribute to a repository where its user is a collaborator
  [S6] (§2).
- Staying on the maintainer's identity: the author cannot approve their own pull requests [S1],
  so `main` keeps zero required approvals and GitHub enforces no human gate.
- No bypass: a maintainer's own pull request would then need an approval from another
  collaborator, approvals coming from "people with write permissions in the repository" [S2].
  The repository has no collaborator but its owner and no pending invitation [R2]: once #245
  adds the machine account, Claude Code would be the only one able to approve the maintainer's
  pull requests.

## Sources
Read on 2026-11-23.

- [S1] https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/reviewing-changes-in-pull-requests/approving-a-pull-request-with-required-reviews
- [S2] https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets
- [S3] https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/creating-rulesets-for-a-repository
- [S4] https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/about-rulesets
- [S5] https://docs.github.com/en/site-policy/github-terms/github-terms-of-service
- [S6] https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens
- [S7] https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/scopes-for-oauth-apps
- [S8] https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-an-installation-access-token-for-a-github-app
- [S9] https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-json-web-token-jwt-for-a-github-app
- [S10] https://docs.github.com/en/account-and-profile/reference/permission-levels-for-a-personal-account-repository
- [S11] https://cli.github.com/manual/gh_help_environment
- [S12] https://code.claude.com/docs/en/sandboxing
- [S13] https://docs.docker.com/engine/security/
- [S14] https://support.claude.com/en/articles/10167454-use-the-github-integration
- [R1] `gh api repos/michaelatsey/saas-btp/rulesets/18697625`
- [R2] `gh api repos/michaelatsey/saas-btp/collaborators` and `gh api repos/michaelatsey/saas-btp/invitations`
- [R3] `docker version`, run from a Claude Code session on the maintainer's workstation
