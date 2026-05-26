import { spawnSync } from "node:child_process";

const result = spawnSync("pnpm", ["exec", "wdio", "run", "wdio.conf.ts"], {
  stdio: "inherit",
  shell: true,
  env: {
    ...process.env,
    HEADLESS: "false",
  },
});

process.exit(result.status ?? 1);
