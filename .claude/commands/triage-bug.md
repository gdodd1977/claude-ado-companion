---
description: Triage an ADO bug - assess actionability, ROI, and Copilot agent readiness
argument-hint: "<bug-id> [--dry-run] [--force]"
allowed-tools: Bash, Read, Grep, Glob
model: opus
---

# Bug Triage Skill

> **What this does**: Fetches a bug from Azure DevOps, scores it on actionability and ROI, determines if it can be assigned to GitHub Copilot coding agent, then applies tags to the work item.

## Usage

```bash
# Triage a single bug
/triage-bug 36500000

# Dry run (scores the bug but does NOT update ADO)
/triage-bug 36500000 --dry-run

# Force re-triage (even if already triaged and unchanged)
/triage-bug 36500000 --force
```

## What You'll Get

1. **Actionability Score** (0-100): Is there enough info to act on this bug?
2. **ROI Score** (0-100): How valuable is fixing this bug?
3. **Copilot Agent Readiness**: Can this be assigned to GitHub Copilot coding agent?
4. **ADO Updates**: Tags applied (unless `--dry-run`)

## Prerequisites

1. **ADO Access**: `az` CLI installed and authenticated (`az login`)
2. **Work Item**: A Bug work item ID from your configured ADO project

---

# Implementation

## Arguments

Raw arguments: $ARGUMENTS

**Parse:**
- `bug-id`: Extract the numeric work item ID (required)
- `--dry-run`: If present, skip all ADO write operations (tag updates)
- `--force`: If present, re-triage even if the bug was already triaged and hasn't changed

If no bug ID is provided, ask the user: "Please provide a bug work item ID to triage."

## Step 0: Read Configuration

Before doing anything else, read the app configuration to get ADO connection details.

1. Look for `appsettings.local.json` in the `src/` directory. If it exists, read it.
2. Fall back to `appsettings.json` in the `src/` directory.
3. Extract the `Dashboard` section and note these values:
   - `ADO_ORG` = `Dashboard.AdoOrg` (e.g., `https://dev.azure.com/myorg`)
   - `ADO_PROJECT` = `Dashboard.AdoProject` (e.g., `MyProject`)
   - `COPILOT_USER_ID` = `Dashboard.CopilotUserId`
   - `REPO_PROJECT_GUID` = `Dashboard.RepoProjectGuid`
   - `REPO_GUID` = `Dashboard.RepoGuid`
   - `BRANCH_REF` = `Dashboard.BranchRef` (default: `GBmain`)

If `ADO_ORG` or `ADO_PROJECT` are empty, tell the user: "ADO connection is not configured. Please run the app and complete the setup form first."

## Step 0b: Setup

Run once at the start:
```bash
az config set core.only_show_errors=true 2>nul
```

## Step 1: Fetch Bug Details

Use the Azure CLI to get full bug details (using config values from Step 0):
```bash
az boards work-item show --id <bug-id> --expand all --org <ADO_ORG> --project <ADO_PROJECT> -o json
```

**Extract and note the following fields:**
- `System.Title` → title
- `Microsoft.VSTS.TCM.ReproSteps` → repro steps (HTML - strip tags mentally for analysis)
- `Microsoft.VSTS.Common.Severity` → severity (e.g., "2 - High")
- `Microsoft.VSTS.Common.Priority` → priority (1-4)
- `System.State` → state (Active, New, Resolved, Closed)
- `System.Tags` → existing tags (semicolon-separated)
- `System.AreaPath` → area path
- `System.CreatedDate` → created date
- `System.AssignedTo` → assigned to
- `System.ChangedDate` → last modified date
- Relations → count attachments (relation type `AttachedFile`), count "Blocks" relations

If the work item type is not "Bug", warn the user but continue triaging.

### Already-triaged check

Check the existing tags for `triaged`. If found:

1. **If `--force` is passed** — continue with re-triage regardless.
2. **Otherwise** — Print:
   ```
   Bug #<id> was already triaged (has 'triaged' tag). Skipping. Use --force to re-triage.
   ```
   Stop processing this bug (return early).

## Step 2: Actionability Assessment (Score 0-100)

Score the bug on whether it has enough information to act on. Evaluate each criterion:

| Criterion | Points | How to Check |
|-----------|--------|--------------|
| Has title | +10 | `System.Title` is non-empty |
| Has repro steps | +30 | `Microsoft.VSTS.TCM.ReproSteps` is populated with meaningful content (not just a template or placeholder) |
| Has expected/actual behavior | +20 | Repro steps or title describe both what should happen and what actually happens |
| Has environment context | +10 | Mentions environment name, browser, version, tenant, or region |
| Has correlation ID | +10 | Contains a GUID pattern (`[0-9a-f]{8}-[0-9a-f]{4}-...`) or explicit correlation/session ID |
| Has screenshots/attachments | +10 | Has `AttachedFile` relations |
| Has error message/stack trace | +10 | Contains exception text, error codes, HTTP status codes, or stack traces |

**Sum the points. Classify:**
- **80-100**: Fully Actionable - ready to work on
- **50-79**: Partially Actionable - could proceed but may need clarification
- **0-49**: Needs More Info - not enough detail to act on

**Track which criteria are present vs missing** - you'll need this for the output summary.

## Step 3: ROI Assessment (Score 0-100)

Estimate the return on investment for fixing this bug. Start at 0, add points:

| Factor | Points | Condition |
|--------|--------|-----------|
| Severity 1 (Critical) | +30 | `Microsoft.VSTS.Common.Severity` = "1 - Critical" |
| Severity 2 (High) | +20 | Severity = "2 - High" |
| Severity 3 (Medium) | +10 | Severity = "3 - Medium" |
| Priority 1 | +20 | `Microsoft.VSTS.Common.Priority` = 1 |
| Priority 2 | +10 | Priority = 2 |
| Customer-reported | +15 | Tags or description mention "customer", "partner", "production", or bug source is external |
| Regression | +15 | Tags contain "regression" OR description mentions "was working", "broke", "stopped", "used to" |
| Blocks other work | +10 | Has "System.LinkTypes.Dependency-Forward" (Blocks) relations to other work items |
| Frequency indicator | +10 | Description mentions "always", "every time", "consistent", "100%", "reproducible" |
| Affects core flow | +10 | Area path contains "Copilot" or "AI" or "Planner" or "Runtime" |

**Apply multipliers** (multiply the raw score):
- Bug is in Active state: ×1.0 (no change)
- Bug has been open >30 days (compare `System.CreatedDate` to today): ×0.8

**Cap at 100. Classify:**
- **70-100**: High ROI - prioritize fixing
- **40-69**: Medium ROI - fix when bandwidth allows
- **0-39**: Low ROI - backlog candidate

**Track the top 2-3 factors** driving the score for the output summary.

## Step 4: Copilot Agent Readiness Assessment

Determine whether this bug can be assigned to **GitHub Copilot coding agent** to fix autonomously. Copilot coding agent works by reading the issue description, exploring the repo, writing a fix, and opening a PR. It succeeds when the issue is self-contained and code-scoped.

### 4a: Copilot Readiness Score (0-100)

Score each factor — these represent what Copilot needs to work effectively:

| Factor | Points | How to Check |
|--------|--------|--------------|
| Clear problem statement | +20 | Title + description clearly describe what's wrong (not just symptoms) |
| Expected behavior defined | +15 | Describes what the correct behavior should be |
| Specific code pointers | +15 | Mentions file names, class names, method names, endpoints, or error types that point to the relevant code |
| Reproducible / deterministic | +10 | Not "flaky" or "intermittent" — the bug is consistent and a fix can be validated |
| Small, isolated scope | +15 | Likely touches 1-5 files; single behavior to fix; not a systemic or cross-cutting change |
| Has acceptance criteria | +10 | Clear definition of done — how to verify the fix is correct (test case, expected output, or behavior description) |
| Code-only fix | +15 | Fix is entirely in code (no infra, config, external service, UI/UX design, or deployment changes needed) |

**Sum the points. Classify:**
- **75-100**: **Copilot Ready** — assign directly to Copilot coding agent
- **50-74**: **Copilot Possible** — could work but issue description should be enriched first (add missing context, clarify scope)
- **0-49**: **Human Required** — needs human judgment, investigation, or access that Copilot can't provide

### 4b: Blockers for Copilot (check for disqualifiers)

Even if the score is high, these are **hard blockers** that make the bug unsuitable for Copilot. If ANY apply, override to **Human Required**:

- **Requires UI/UX design decisions** — visual changes, layout, user experience choices
- **Requires external service changes** — fix is outside this repo (APIM config, Azure resource, partner service)
- **Requires production data access** — needs live data, customer tenant access, or prod debugging to understand
- **Requires cross-team coordination** — fix spans multiple teams or repos
- **Requires infrastructure/deployment changes** — pipeline, Service Fabric config, certificates, networking
- **Ambiguous root cause** — the bug description doesn't identify the root cause and significant investigation is needed first
- **AI prompt/quality issue** — the bug is about AI output quality (wrong actions, hallucinations, prompt tuning); these need specialized eval workflows, not a code fix

### 4c: Enrichment Suggestions

If classification is **Copilot Possible**, identify what's missing and suggest specific additions to the issue that would make it Copilot Ready. Examples:

- "Add the specific file/class where the error originates"
- "Describe the expected behavior after the fix"
- "Add a test case or validation criteria"
- "Clarify whether this requires only code changes or also config/infra"
- "Narrow the scope — which specific behavior should change?"

### 4d: Confidence Level
- **High**: Score >= 75, no blockers, clear signals
- **Medium**: Score 50-74, no hard blockers but some ambiguity
- **Low**: Score < 50, or hard blockers present, or insufficient info to assess

## Step 5: Update Work Item (skip if `--dry-run`)

### 5a: Update tags

First, read the current tags from the work item (already fetched in Step 1).

Determine which new tags to add based on triage results:
- Always add `triaged` (to mark as processed)
- Actionability < 50 → add `needs-info`
- ROI >= 70 → add `high-roi`
- Copilot Readiness >= 75 (Copilot Ready) → add `copilot-ready`
- Copilot Readiness 50-74 (Copilot Possible) → add `copilot-possible`
- Copilot Readiness < 50 (Human Required) → add `human-required`

**Merge with existing tags** - do NOT overwrite. Parse existing `System.Tags` (semicolon-separated), add new tags that aren't already present, join back with `; `.

```bash
az boards work-item update --id <bug-id> --fields "System.Tags=<merged-tags>" --org <ADO_ORG> --project <ADO_PROJECT>
```

### 5b: Assign to Copilot and link branch (Copilot Ready only)

If the Copilot Readiness classification is **Copilot Ready** (score >= 75, no blockers), and `COPILOT_USER_ID` is configured (non-empty), automatically assign the bug to GitHub Copilot coding agent and link the repo branch.

**Assign to Copilot:**
```bash
az boards work-item update --id <bug-id> --fields "System.AssignedTo=<COPILOT_USER_ID>" --org <ADO_ORG> --project <ADO_PROJECT>
```

**Link the branch** (only if `REPO_PROJECT_GUID` and `REPO_GUID` are configured) using the ADO REST API to add a branch artifact link:
```bash
az rest --method patch \
  --url "<ADO_ORG>/<ADO_PROJECT>/_apis/wit/workItems/<bug-id>?api-version=7.1" \
  --resource "499b84ac-1321-427f-aa17-267ca6975798" \
  --headers "Content-Type=application/json-patch+json" \
  --body "[{\"op\": \"add\", \"path\": \"/relations/-\", \"value\": {\"rel\": \"ArtifactLink\", \"url\": \"vstfs:///Git/Ref/<REPO_PROJECT_GUID>/<REPO_GUID>/<BRANCH_REF>\", \"attributes\": {\"name\": \"Branch\"}}}]"
```

If `COPILOT_USER_ID` is empty, skip the assignment and report: "Copilot User ID not configured — skipping auto-assignment."
If `REPO_PROJECT_GUID` or `REPO_GUID` are empty, skip the branch link and report: "Repo GUIDs not configured — skipping branch link."

If assignment or branch linking fails, report the error but don't roll back the tags.

## Step 6: Output Summary

Print a concise console summary:

```
Bug #<id>: "<title>"
  Actionability:    <score>/100 (<classification>)
  ROI:              <score>/100 (<classification>)
  Copilot Ready:    <score>/100 (<Copilot Ready / Copilot Possible / Human Required>) [<confidence> confidence]
  Tags added:       <comma-separated new tags>
  Assigned to:      GitHub Copilot ✓ (or "unchanged" if not Copilot Ready)
```

If `--dry-run`, prefix with:
```
[DRY RUN] No ADO updates were made.
```

---

## Error Handling

- If the work item doesn't exist or can't be fetched, report the error and stop.
- If tag update fails, report the error.
- Never silently swallow errors - always report what failed and why.
