# Terminal macros (experimental branch)

Open **Tools → Macros → Start recording** in the terminal pane you want to record. Type the command and respond to its prompts. Choose **Stop recording** (the same menu item), or click the recording indicator beside the preview controls. Enter a name, optionally assign a password and a shortcut, review the expected prompts, and save.

**Tools → Macros → Run** lists saved recordings. **Manage recorded macros** lets you run, rename, edit prompt/delay values, assign shortcuts, change password protection, or delete a recording. **Macro settings** opens the Macros tab of the application Settings dialog; changes take effect with Apply.

- Interactive mode is the default. Input is grouped until Enter; each subsequent group waits for the recorded prompt at the terminal cursor. Matching uses logical text after terminal escape sequences have been interpreted, including wrapped prompts. The first command starts immediately. Review prompt text before saving, especially when input was entered before a prompt appeared. A missing prompt prevents saving an interactive recording.
- Timed mode records each input write and its elapsed delay. Use it for workflows without stable prompts; it does not wait for server readiness.
- Interactive timeout is configurable from 5 to 600 seconds. A timeout or closed target stops playback rather than sending subsequent input blindly.
- The toolbar shows recording/running state and the target pane. Click it to stop, or press Escape during playback. Stopping does not undo commands already sent.
- Recording and playback remain bound to the original pane when tabs or focus change. Input from other panes is not recorded. Manual text/paste in the playback pane is blocked while it runs. Terminal protocol replies and mouse/focus reports are not recorded.
- Optional shortcuts are **Ctrl+Shift+Alt+1** through **Ctrl+Shift+Alt+9**. Each number can belong to only one macro. Disable these shortcuts in Settings → Macros to pass them through to the terminal.

## Storage and passwords

Recordings are stored in `%LOCALAPPDATA%\RtlTerminal\Macros` as encrypted `.rtmacro` files. The folder has a restricted Windows ACL. All file contents, including names and prompt text, are protected with Windows DPAPI for the current user. Files are written atomically; there are no plaintext macro files or input logs.

An optional independent password adds AES-256-GCM authenticated encryption with a random salt and nonce, and PBKDF2-SHA256 (600,000 iterations). The password is requested for each run or edit and is not saved. Names and shortcut metadata remain accessible to the signed-in Windows account. Forgotten passwords cannot be recovered.

DPAPI alone is not a security boundary against software already running as the same Windows user. Use an independent strong password for sensitive macros. Neither encryption option protects against malware inspecting the process while a macro is unlocked, or a compromised server. The terminal and target program can still echo or log input according to their own behavior. Input values are masked in the macro editor.

## Scope and testing

Interactive mode targets command/prompt workflows; it is not a universal automation engine for changing full-screen TUIs. Prompt text must be stable. Multiline paste is recorded as one input write, so use individual submissions when subsequent lines must wait for different prompts. Timed mode reproduces input timing without interpreting application state.

The render test suite includes a real ConPTY login process with a split ANSI-colored username prompt and non-echoing password input, recording/grouping tests, prompt mismatch and stale-output guards, timeout/cancellation, timed replay, encryption/password/tamper tests, shortcut uniqueness, and live pane isolation checks. UI previews are generated under `artifacts/render-checks`.

This work lives on `feature/secure-terminal-macros`. It must not be merged into `main` until the user approves the trial.
