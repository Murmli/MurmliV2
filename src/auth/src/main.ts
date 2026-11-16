import { register } from "import-in-the-middle/register-hooks.mjs";

// ESM imports of node:http are only instrumented when the hooks are registered before the first import
register({ include: ["http", "https"] });

const { startTelemetry } = await import("./telemetry.ts");
const sdk = startTelemetry();

const { run } = await import("./server.ts");
await run(() => sdk.shutdown());
