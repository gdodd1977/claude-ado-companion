---
description: "Batch triage all active bugs in your configured ADO area path. Queries for non-closed bugs, runs triage-bug on each, and produces an aggregate summary with statistics. Use when asked to triage multiple bugs, scan the backlog, or assess all open bugs at once."
argument-hint: "[--tag=<filter-tag>] [--max=<count>] [--dry-run]"
allowed-tools: Bash, Read, Grep, Glob, Skill
model: opus
---

# Batch Bug Triage Skill

> **What this does**: Queries your configured ADO area path for all active bugs, then runs `/triage-bug --force` on each one. Already-triaged bugs are re-assessed. Produces an aggregate summary report at the end.

## Usage

```bash
# Triage all bugs (default max: 20)
/triage-bugs

# Limit to 5 bugs
/triage-bugs --max=5

# Only triage bugs with a specific tag
/triage-bugs --tag=customer-reported

# Dry run - score bugs but don't update ADO
/triage-bugs --dry-run

# Combine flags
/triage-bugs --max=10 --tag=regression --dry-run
```

## What You'll Get

1. **Individual Triage**: Each bug triaged with actionability, ROI, and agent classification
2. **Aggregate Summary Table**: All triaged bugs in one table
3. **Statistics**: Breakdown by actionability, ROI tier, and agent classification

## Prerequisites

1. **ADO Access**: `az` CLI installed and authenticated (`az login`)
2. **Board Access**: Access to your configured ADO project and area path

---

# Implementation

## Arguments

Raw arguments: $ARGUMENTS

**Parse:**
- `--tag=<tag>`: Optional tag filter. If provided, add an additional WIQL filter for this tag.
- `--max=<N>`: Maximum number of bugs to process. Default: 20.
- `--dry-run`: If present, pass `--dry-run` to each `/triage-bug` invocation.

## Step 0: Read Configuration

Before doing anything else, read the app configuration to get ADO connection details.

1. Look for `appsettings.local.json` in the `src/` directory. If it exists, read it.
2. Fall back to `appsettings.json` in the `src/` directory.
3. Extract the `Dashboard` section and note these values:
   - `ADO_ORG` = `Dashboard.AdoOrg`
   - `ADO_PROJECT` = `Dashboard.AdoProject`
   - `AREA_PATH` = `Dashboard.AreaPath`

If `ADO_ORG`, `ADO_PROJECT`, or `AREA_PATH` are empty, tell the user: "ADO connection is not configured. Please run the app and complete the setup form first."

## Step 0b: Setup

Run once at the start:
```bash
az config set core.only_show_errors=true 2>nul
```

## Step 1: Query for Bugs

Build and execute a WIQL query to find active bugs in the area path. All bugs are returned regardless of triage status — already-triaged bugs will be re-triaged (the `/triage-bug` skill handles `--force` automatically for these).

**Base WIQL** (using values from Step 0):
```sql
SELECT [System.Id], [System.Title], [System.State], [System.CreatedDate]
FROM WorkItems
WHERE [System.WorkItemType] = 'Bug'
  AND [System.TeamProject] = '<ADO_PROJECT>'
  AND [System.AreaPath] UNDER '<AREA_PATH>'
  AND [System.State] <> 'Closed'
ORDER BY [System.CreatedDate] DESC
```

**If `--tag` is provided**, add this clause before the ORDER BY:
```sql
  AND [System.Tags] CONTAINS '<tag-value>'
```

Execute the query:
```bash
az boards query --wiql "<WIQL>" --org <ADO_ORG> --project <ADO_PROJECT> -o json
```

**Parse the results** to get a list of bug IDs. Apply the `--max` limit (default 20) to the result set.

If no bugs are found, report "No bugs found matching the criteria." and stop.

Otherwise, report how many bugs were found:
```
Found N bugs. Processing up to <max>...
```

## Step 2: Triage Each Bug

For each bug ID in the result set, invoke the `/triage-bug` skill with `--force` (so already-triaged bugs are re-assessed):

```
Skill("triage-bug", args: "<bug-id> --force [--dry-run if applicable]")
```

**Process bugs sequentially** (one at a time) to avoid rate limiting on ADO APIs.

After each bug is triaged, capture the results from the skill output:
- Bug ID and title
- Actionability score and classification
- ROI score and classification
- Copilot readiness score and classification
- Tags that were added

If a triage fails for a specific bug, log the error and continue with the next bug. Do not abort the entire batch.

## Step 3: Aggregate Summary Report

After all bugs are processed, output a summary report:

```
============================================================
Batch Triage Summary - [YYYY-MM-DD]
============================================================

Found: N bugs | Processed: M bugs | Errors: E bugs

| Bug ID | Title (truncated to 40 chars) | Actionability | ROI | Copilot Readiness | Tags Added |
|--------|-------------------------------|---------------|-----|-------------------|------------|
| #12345 | Wrong trigger in email flow   | 85 (Actionable) | 72 (High) | 80 (Ready) | copilot-ready, high-roi |
| #12346 | Null ref in runtime service    | 90 (Actionable) | 55 (Medium) | 65 (Possible) | copilot-possible |
| #12347 | Flow fails sometimes          | 30 (Needs Info) | 40 (Medium) | 20 (Human) | needs-info, human-required |

------------------------------------------------------------
Summary Statistics:
------------------------------------------------------------
Actionability:
  - Fully Actionable (80-100): X bugs
  - Partially Actionable (50-79): Y bugs
  - Needs More Info (0-49): Z bugs

ROI:
  - High (70-100): A bugs
  - Medium (40-69): B bugs
  - Low (0-39): C bugs

Copilot Agent Readiness:
  - Copilot Ready (75-100): R bugs
  - Copilot Possible (50-74): P bugs
  - Human Required (0-49): H bugs

Copilot Ready (assign directly):
  #12345: "Wrong trigger in email flow"
Copilot Possible (enrich issue first):
  #12346: "Null ref in runtime service" — missing: acceptance criteria, expected behavior
============================================================
```

If `--dry-run`, prefix the report header with `[DRY RUN]`.

## Error Handling

- If the WIQL query fails, report the error and stop. Common causes: auth issues, incorrect area path.
- If a single bug triage fails, log the error and continue with remaining bugs.
- Report total errors in the summary.
- If `az boards query` returns no results, check if the area path is correct by suggesting the user verify with a known bug ID.
