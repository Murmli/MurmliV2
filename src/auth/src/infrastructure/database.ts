import { DefaultAzureCredential } from "@azure/identity";
import { entraTokenProvider } from "@azure/postgresql-auth";
import pg from "pg";
import { requireEnvironment } from "./environment.ts";

export const pool = process.env.MURMLI_PASSWORD
	? new pg.Pool({ connectionString: requireEnvironment("MURMLI_URI") })
	: new pg.Pool({
			host: requireEnvironment("MURMLI_HOST"),
			port: Number(requireEnvironment("MURMLI_PORT")),
			database: requireEnvironment("MURMLI_DATABASENAME"),
			user: requireEnvironment("PGUSER"),
			password: entraTokenProvider(new DefaultAzureCredential()),
			ssl: true,
		});

pool.on("error", (error) => {
	console.error("An idle database client failed.", error);
});

// pg-pool only listens on idle clients, an error on a checked-out client without a listener would end the process. The failed query reports it.
pool.on("connect", (client) => {
	client.on("error", () => {});
});
