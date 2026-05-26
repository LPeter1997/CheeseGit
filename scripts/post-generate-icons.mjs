import { copyFileSync, rmSync } from "node:fs";

// Remove mobile icon folders we don't use in this project.
rmSync("src-tauri/icons/ios", { recursive: true, force: true });
rmSync("src-tauri/icons/android", { recursive: true, force: true });

// Keep a public copy of the generated app icon for the web UI.
copyFileSync("src-tauri/icons/icon.png", "public/icon.png");
