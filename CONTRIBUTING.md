# Contributing

Thanks for your interest in improving Hibernate Scheduler.

## How changes get in

- `main` is protected. Nobody pushes to it directly.
- Every change goes through a pull request, must pass the **Build** check, and needs an approving review from the maintainer ([@damola-m](https://github.com/damola-m)) before it can be merged.
- For a first-time contributor, GitHub Actions won't run on your pull request until the maintainer approves it. That's normal; please be patient.

## Before you start

- For anything bigger than a small fix, open an issue first so we can agree on the approach before you put time in.
- Keep the app small and focused: one hibernate at a time, a simple UI, and no network services. Ideas that add a cloud service, a listening port or telemetry are unlikely to be accepted.

## Workflow

1. Fork the repo and create a branch from `main` (for example `fix/countdown-focus`).
2. Make your change. Build locally:
   ```powershell
   dotnet build src/HibernateScheduler -c Release
   ```
3. Test it by hand. Please avoid testing a real hibernate on a machine you're working on; use a long delay (for example `hibernate 600`) and cancel it.
4. Open a pull request against `main` and fill in the template.

## Code style

- C# with nullable reference types on; match the style of the surrounding code.
- Each source file starts with a header block:
  ```csharp
  /* ===================================
     FileName.cs
     -----------------------------------
     - One-line description of what this file does.
     =================================== */
  ```
- Group class-level members under section headers (`// ===...` + `// SECTION NAME` + `// ===...`) and split longer methods into `// Part N — Description` blocks, as in the existing files.
- Keep comments short and about *why*, not *what*.
- UI colours and sizes come from the shared resources in `App.xaml`; don't hard-code new ones in individual windows.

## Reporting bugs

Use the **Bug report** issue template and include your Windows version and the output of `powercfg /a`.
