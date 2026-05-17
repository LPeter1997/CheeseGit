# CheeseGit

A cross-platform version control UI application built with Tauri v2, React, and TypeScript.

## Running the App

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
```

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
