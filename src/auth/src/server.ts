import { createHash, timingSafeEqual } from "node:crypto";
import {
	createServer,
	type IncomingMessage,
	type ServerResponse,
} from "node:http";
import type { TLSSocket } from "node:tls";
import { getMigrations } from "better-auth/db/migration";
import { getRequest, setResponse } from "better-call/node";
import { auth, baseURL } from "./auth.ts";
import { pool } from "./database.ts";
import { requireEnvironment } from "./environment.ts";
import { createCredentialUser, deleteUser, userExists } from "./users.ts";

const maximumRequestBodyLength = 4096;
const maximumAuthRequestBodyLength = 64 * 1024;

const serviceAuthorizationDigest = digest(
	`Bearer ${requireEnvironment("AUTH_SERVICE_KEY")}`,
);

const discoveryDocument = JSON.stringify({
	issuer: baseURL,
	jwks_uri: `${baseURL}/api/auth/jwks`,
});

export async function run(
	shutdownTelemetry: () => Promise<void>,
): Promise<void> {
	try {
		const { runMigrations } = await getMigrations(auth.options);
		await runMigrations();
		await seedUser();
	} catch (error) {
		console.error(error);
		await shutdownTelemetry();
		await pool.end();
		process.exit(1);
	}

	const server = createServer(async (request, response) => {
		try {
			if (
				Number(request.headers["content-length"]) > maximumAuthRequestBodyLength
			) {
				response.writeHead(413);
				response.end();
				return;
			}
			const { pathname } = new URL(request.url ?? "/", "http://localhost");
			if (request.method === "GET" && pathname === "/health") {
				response.writeHead(200, { "content-type": "text/plain" });
				response.end("Healthy");
			} else if (
				request.method === "GET" &&
				pathname === "/.well-known/openid-configuration"
			) {
				response.writeHead(200, { "content-type": "application/json" });
				response.end(discoveryDocument);
			} else if (
				request.method === "POST" &&
				pathname === "/service/delete-user"
			) {
				await handleDeleteUser(request, response);
			} else {
				await handleAuthRequest(request, response);
			}
		} catch (error) {
			console.error(error);
			if (!response.headersSent) {
				response.writeHead(500);
			}
			response.end();
		}
	});
	server.listen(Number(requireEnvironment("PORT")));

	let shuttingDown = false;
	const shutdown = async () => {
		if (shuttingDown) {
			return;
		}
		shuttingDown = true;
		// Container Apps kills the replica 30 seconds after SIGTERM
		setTimeout(() => server.closeAllConnections(), 10_000).unref();
		await new Promise<void>((resolve) => server.close(() => resolve()));
		await shutdownTelemetry();
		await pool.end();
		process.exit(0);
	};
	process.on("SIGTERM", shutdown);
	process.on("SIGINT", shutdown);
}

async function seedUser(): Promise<void> {
	const email = process.env.SEED_USER_EMAIL;
	const password = process.env.SEED_USER_PASSWORD;
	if (email && password && !(await userExists(email))) {
		await createCredentialUser(email, "Test", password);
	}
}

// Mirrors toNodeHandler of better-call, which does not limit the body size
async function handleAuthRequest(
	request: IncomingMessage,
	response: ServerResponse,
): Promise<void> {
	const protocol =
		request.headers["x-forwarded-proto"] ||
		((request.socket as TLSSocket).encrypted ? "https" : "http");
	const base = `${protocol}://${request.headers[":authority"] || request.headers.host}`;
	await setResponse(
		response,
		await auth.handler(
			getRequest({
				base,
				request,
				bodySizeLimit: maximumAuthRequestBodyLength,
			}),
		),
	);
}

async function handleDeleteUser(
	request: IncomingMessage,
	response: ServerResponse,
): Promise<void> {
	if (
		!timingSafeEqual(
			digest(request.headers.authorization ?? ""),
			serviceAuthorizationDigest,
		)
	) {
		response.writeHead(401);
		response.end();
		return;
	}

	const userId = await readUserId(request);
	if (userId === undefined) {
		response.writeHead(400);
		response.end();
		return;
	}

	await deleteUser(userId);
	response.writeHead(204);
	response.end();
}

function digest(value: string): Buffer {
	return createHash("sha256").update(value).digest();
}

async function readUserId(
	request: IncomingMessage,
): Promise<string | undefined> {
	let body = "";
	for await (const chunk of request.setEncoding("utf8")) {
		body += chunk;
		if (body.length > maximumRequestBodyLength) {
			return undefined;
		}
	}
	try {
		const { userId } = JSON.parse(body) ?? {};
		return typeof userId === "string" && userId !== "" ? userId : undefined;
	} catch {
		return undefined;
	}
}
