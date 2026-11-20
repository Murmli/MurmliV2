import { betterAuth } from "better-auth";
import { bearer, jwt } from "better-auth/plugins";
import { PostgresDialect } from "kysely";
import { pool } from "../infrastructure/database.ts";
import { requireEnvironment } from "../infrastructure/environment.ts";

export const baseURL = requireEnvironment("BETTER_AUTH_URL");

export const auth = betterAuth({
	baseURL,
	database: {
		dialect: new PostgresDialect({ pool }),
		type: "postgres",
		schemaName: "better-auth",
		// The object form of database disables transactions unless this is set
		transaction: true,
	},
	emailAndPassword: { enabled: true, disableSignUp: true },
	socialProviders: {
		apple: {
			// Native iOS identity tokens carry the bundle id as audience, the ID token flow needs no client secret
			clientId: requireEnvironment("APPLE_BUNDLE_IDENTIFIER"),
			// Better Auth requires an email per user, Murmli stores none from Apple
			mapProfileToUser: (profile) => ({
				email: `${profile.sub}@apple.placeholder.invalid`,
			}),
		},
	},
	plugins: [
		bearer({ requireSignature: true }),
		jwt({
			// .NET cannot validate EdDSA (the default), and stored keys keep their algorithm
			jwks: { keyPairConfig: { alg: "ES256" } },
			jwt: {
				issuer: baseURL,
				audience: requireEnvironment("JWT_AUDIENCE"),
				// An empty payload keeps email and name out of the token
				definePayload: () => ({}),
			},
		}),
	],
});
