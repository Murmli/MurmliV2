import { createInterface } from "node:readline/promises";
import { createCredentialUser, userExists } from "../authentication/users.ts";
import { pool } from "../infrastructure/database.ts";

const [email, name] = process.argv.slice(2);
if (!email || !name) {
	console.error("Usage: npm run create-user -- <email> <name>");
	process.exit(1);
}

const readline = createInterface({
	input: process.stdin,
	output: process.stdout,
});
const password = await readline.question("Password: ");
readline.close();

try {
	if (await userExists(email)) {
		console.error(`A user with the email ${email} already exists.`);
		process.exitCode = 1;
	} else {
		await createCredentialUser(email, name, password);
		console.log(`Created the user ${email}.`);
	}
} catch (error) {
	console.error(error instanceof Error ? error.message : error);
	process.exitCode = 1;
} finally {
	await pool.end();
}
