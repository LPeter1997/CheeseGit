# CheeseGit

A cross-platform version control UI application built with Tauri v2, React, and TypeScript.

**Disclaimer: The application uses quite a lot of AI-generated code, especially on the frontend. I review all the code before adding it to the project, but I won't claim to be a frontend expert (or even a frontend developer), mistakes can happen.**

## Running the App locally

```sh
pnpm install
pnpm tauri dev
```

## Running Tests

```sh
# Backend (Rust)
cd src-tauri && cargo test

# Frontend (TypeScript)
pnpm test

# E2E (requires WebKitWebDriver + tauri-driver)
pnpm run test:e2e
```

### E2E Test Prerequisites

The e2e suite uses [WebdriverIO](https://webdriver.io/) + [tauri-driver](https://crates.io/crates/tauri-driver) to control the real application via the WebDriver protocol.

1. **Install tauri-driver:**
   ```sh
   cargo install tauri-driver
   ```

2. **Install WebKitWebDriver:**
   - **Debian/Ubuntu:** `sudo apt install webkit2gtk-driver`
   - **Fedora:** `sudo dnf install webkitgtk6.0-devel` (includes the driver)
   - **Arch Linux:** Build from [WebKitGTK source](https://webkitgtk.org/) with `-DENABLE_WEBDRIVER=ON`

3. **Install e2e dependencies:**
   ```sh
   cd e2e && pnpm install
   ```

4. **Run the suite:**
   ```sh
   pnpm run test:e2e
   ```
   This will automatically create a test repository, build the debug binary, and run all specs.

### Running Individual E2E Specs

Each spec file is self-contained and can be run independently. Use the `--spec` flag:

```sh
cd e2e

# Run a single spec
npx wdio run wdio.conf.ts --spec specs/22-diff-regression.spec.ts

# Run multiple specs in sequence
npx wdio run wdio.conf.ts --spec specs/04-staging.spec.ts --spec specs/05-branches.spec.ts
```

The first run will build the app and create the test repository template. Each spec resets the repo to a pristine state in its `before()` hook, so ordering doesn't matter.

## Versioning

The application version is defined in `Cargo.toml` under the `version` field. To publish a new version, update this value before building:

```toml
[package]
name = "cheesegit"
version = "x.y.z"
...
```

Then build the release:

```sh
pnpm tauri build
```

The About dialog reads the version at runtime via the Tauri API. During development (`pnpm tauri dev`), it shows "Development" instead of a version number.
