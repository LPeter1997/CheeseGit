import os from "os";
import net from "net";
import path from "path";
import { mkdirSync, rmSync, writeFileSync } from "fs";
import { spawn, spawnSync, type ChildProcess } from "child_process";
import { fileURLToPath } from "url";

const __dirname = fileURLToPath(new URL(".", import.meta.url));
const projectRoot = path.resolve(__dirname, "..");
const testRepoDir = path.resolve(__dirname, ".test-repo");

// Isolate e2e app state so tests don't touch the developer's real config.
const testStateDir = path.resolve(__dirname, ".test-state");
const testStatePath = path.resolve(testStateDir, "state.json");
mkdirSync(testStateDir, { recursive: true });
process.env.CHEESEGIT_APPSTATE_PATH = testStatePath;

// tauri-driver child process
let tauriDriver: ChildProcess | undefined;
let shouldExit = false;

// Resolve the debug binary path
const binaryName = process.platform === "win32" ? "cheesegit.exe" : "cheesegit";
const application = path.resolve(
  projectRoot,
  "src-tauri",
  "target",
  "debug",
  binaryName,
);

export const config: any = {
  // ── Connection ────────────────────────────────────────────────────────
  hostname: "127.0.0.1",
  port: 4444,

  // ── Test specs ────────────────────────────────────────────────────────
  specs: ["./specs/**/*.spec.ts"],
  maxInstances: 1,

  // ── Capabilities ──────────────────────────────────────────────────────
  capabilities: [
    {
      maxInstances: 1,
      "tauri:options": {
        application,
      },
    } as any,
  ],

  // ── Framework ─────────────────────────────────────────────────────────
  runner: "local",
  framework: "mocha",
  reporters: ["spec"],

  mochaOpts: {
    ui: "bdd",
    timeout: 60_000,
  },

  // ── Auto-compile TS specs ─────────────────────────────────────────────
  autoCompileOpts: {
    tsNodeOpts: {
      transpileOnly: true,
      esm: true,
    },
  },

  // ── Lifecycle hooks ───────────────────────────────────────────────────

  /**
   * Build the app in debug mode before running the suite.
   */
  onPrepare() {
    // 0. Reset test state so the app starts clean.
    writeFileSync(testStatePath, JSON.stringify({
      open_repos: [],
      active_index: -1,
      last_parent_folder: null,
      skipped_version: null,
      pending_changelog: null,
      pending_changelog_version: null,
      dismiss_desktop_entry: false,
    }, null, 2));

    // 1. Create / refresh test repo
    console.log("⏳ Creating test repository…");
    // Remove any stale repo so the script can create fresh.
    rmDirIfExists(testRepoDir);
    // Invoke create-test-repo via pnpm from e2e directory (avoids workspace pnpmfile loading)
    const repoResult = spawnSync(
      "pnpm",
      ["run", "create-test-repo", testRepoDir],
      {
        cwd: __dirname,  // e2e directory, uses e2e/package.json
        stdio: "inherit",
        env: { ...process.env },
        shell: true,
      },
    );
    if (repoResult.status !== 0) {
      throw new Error("Failed to create test repository");
    }

    // 2. Build the Tauri app (debug) with custom-protocol so the binary
    //    serves embedded frontend files instead of connecting to devUrl.
    //    Both vite and cargo are incremental — a no-op rebuild is ~0.5 s.
    console.log("⏳ Building Tauri app (debug + custom-protocol)…");
    // Use pnpm for frontend build for cross-platform compatibility
    const feResult = spawnSync("pnpm", ["build"], {
      cwd: projectRoot,
      stdio: "inherit",
      shell: true,
    });
    if (feResult.status !== 0) {
      throw new Error("Frontend build failed");
    }
    const cargoResult = spawnSync(
      "cargo",
      ["build", "--features", "custom-protocol"],
      {
        cwd: path.resolve(projectRoot, "src-tauri"),
        stdio: "inherit",
        shell: true,
      },
    );
    if (cargoResult.status !== 0) {
      throw new Error("Cargo debug build failed");
    }
  },

  /**
   * Start tauri-driver before each session.
   * Also reset the test repo to a clean state on `main`.
   */
  beforeSession() {
    // Reset repo to main branch so each session starts from a known branch.
    // Only switch if not already on main (avoids discarding initial dirty state for spec 04).
    const currentBranch = spawnSync("git", ["-C", testRepoDir, "rev-parse", "--abbrev-ref", "HEAD"], { encoding: "utf-8" });
    if (currentBranch.stdout?.trim() !== "main") {
      spawnSync("git", ["-C", testRepoDir, "checkout", "main", "--force"], { stdio: "ignore" });
    }
    // Reset test app state so each session starts clean.
    writeFileSync(testStatePath, JSON.stringify({
      open_repos: [],
      active_index: -1,
    }));

    const tauriDriverPath = path.resolve(
      os.homedir(),
      ".cargo",
      "bin",
      "tauri-driver",
    );

    // Kill any stale tauri-driver / WebKitWebDriver / app processes from a previous crashed run.
    killStaleProcesses();
    // Brief pause so the OS releases the port and resources.
    sleepMs(500);

    tauriDriver = spawn(tauriDriverPath, [], {
      stdio: [null, process.stdout, process.stderr],
    });

    tauriDriver.on("error", (error) => {
      console.error("tauri-driver error:", error);
      process.exit(1);
    });

    tauriDriver.on("exit", (code) => {
      if (!shouldExit) {
        console.error("tauri-driver exited unexpectedly with code:", code);
        process.exit(1);
      }
    });

    // Wait until tauri-driver is actually listening on port 4444.
    return waitForPort(4444, 10_000);
  },

  /**
   * Clean up tauri-driver after each session.
   */
  afterSession() {
    closeTauriDriver();
  },
};

function closeTauriDriver() {
  shouldExit = true;
  tauriDriver?.kill();
  // Ensure lingering processes are cleaned up (in case tauri-driver shutdown wasn't graceful)
  killProcessByName("WebKitWebDriver");
  killProcessByName("cheesegit");
}

function onShutdown(fn: () => void) {
  const cleanup = () => {
    try {
      fn();
    } finally {
      process.exit();
    }
  };
  process.on("exit", cleanup);
  process.on("SIGINT", cleanup);
  process.on("SIGTERM", cleanup);
  process.on("SIGHUP", cleanup);
}

onShutdown(() => closeTauriDriver());

function rmDirIfExists(targetPath: string): void {
  rmSync(targetPath, { recursive: true, force: true });
}

function sleepMs(ms: number): void {
  const signal = new Int32Array(new SharedArrayBuffer(4));
  Atomics.wait(signal, 0, 0, ms);
}

function killStaleProcesses(): void {
  killProcessByName("tauri-driver");
  killProcessByName("WebKitWebDriver");
  killProcessByName("cheesegit");
}

function killProcessByName(name: string): void {
  if (process.platform === "win32") {
    const candidates = name.endsWith(".exe") ? [name] : [`${name}.exe`, name];
    for (const candidate of candidates) {
      spawnSync("taskkill", ["/F", "/T", "/IM", candidate], {
        stdio: "ignore",
        shell: true,
      });
    }
    return;
  }

  spawnSync("pkill", ["-f", name], { stdio: "ignore" });
}

/**
 * Probe a TCP port until it accepts connections (or timeout).
 * Fails fast with a clear error instead of letting WebDriverIO hang on
 * "Could not connect to localhost".
 */
function waitForPort(port: number, timeoutMs: number): Promise<void> {
  const start = Date.now();
  return new Promise((resolve, reject) => {
    function probe() {
      const sock = net.createConnection({ host: "127.0.0.1", port });
      sock.once("connect", () => {
        sock.destroy();
        resolve();
      });
      sock.once("error", () => {
        sock.destroy();
        if (Date.now() - start > timeoutMs) {
          reject(
            new Error(
              `tauri-driver did not start listening on port ${port} within ${timeoutMs}ms. ` +
              `Check for stale processes: pgrep -af tauri-driver`,
            ),
          );
        } else {
          setTimeout(probe, 100);
        }
      });
    }
    probe();
  });
}
