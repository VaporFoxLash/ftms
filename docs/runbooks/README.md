# Runbooks

**Status: empty.** This directory is the slot `docs/README.md` reserves for operational
procedure — incident response, restore drills, operations (doc 10 §1).

A runbook is written for someone at 3am who did not build the system and cannot ask anyone who
did. That constraint is what separates a runbook from documentation: exact commands, expected
output, and what to do when the output differs.

## What belongs here

The design already names the operational promises FTMS has to keep. Each of these needs a
procedure before it can be claimed to hold:

| Runbook | Why the design demands it |
| --- | --- |
| `restore-drill.md` | A backup that has never been restored is a hypothesis. Doc 06 §5.1 makes the database the system of record; restoring it is the recovery path for every other failure. |
| `migration-runbook.md` | Outside Development, migrations run at deployment time under a **separate elevated login**, because the application login has no DDL rights (doc 06 §5.1). That handover needs writing down. |
| `incident-response.md` | Doc 06 §7 — logs and audit rows carry user identifiers, never tokens. An incident that involves pulling logs needs the rule stated where it will be read under pressure. |
| `audit-sweep.md` | Doc 08 §7.3 specifies a quarterly internal authorisation matrix sweep. A recurring obligation with no procedure is a recurring obligation that gets skipped. |
| `access-review.md` | Roles are `Capturer`, `Manager`, `Auditor`, `Admin`, and Admin deliberately holds **no** transaction rights. Segregation of duty is only real if someone periodically checks it still holds. |

## Format

One file per procedure, Markdown, in the imperative. Every command copy-pasteable. Every step
states what success looks like, so a reader can tell whether it worked without knowing the system.

Nothing here is written yet, and this file exists to say so plainly rather than to let an empty
directory imply the work was done.
