# Player renames

Pending routine Windows check after merge. Automated signed client/server
tests cover refusal, recovery, simultaneous attempts and saved names; this
file records the remaining hands-on check.

- Open an agent's Profile and try a full name held by another agent. Confirm
  the explanation is readable, the name stays unchanged and the field stays
  open with the attempted name.
- Choose a different name. Confirm the Profile and roster update, then save
  and reopen the disposable world.
- Try a deceased agent's full name and a name that differs only in case or
  spaces. Confirm both are refused, while similar distinct surnames work.
- Read a past conversation after renaming. Confirm its words stay as spoken.
