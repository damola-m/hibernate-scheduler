# Security policy

## Reporting a vulnerability

Please **don't** open a public issue for security problems. Instead, use GitHub's private reporting: go to the **Security** tab of this repository and choose **Report a vulnerability**.

You'll get a reply within a week. Once a fix is released, the report is published with credit to you unless you'd rather stay anonymous.

## Scope

Things worth reporting include:

- A way to trigger the app's actions without access to the user's OneDrive folder
- A folder or file name that makes the app do anything other than the documented commands
- Anything that causes the app to read or write outside its settings file and its `HibernateRemote` folder

## Design notes

- The app makes no network connections and opens no ports. The remote trigger works only through a local folder that OneDrive syncs.
- Command names are matched against a small fixed set of verbs and are never executed, opened or passed to a shell.
- Commands older than 10 minutes, or created while the machine was asleep, are ignored.
