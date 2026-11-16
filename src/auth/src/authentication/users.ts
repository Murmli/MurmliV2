import { auth } from "./auth.ts";

export async function userExists(email: string): Promise<boolean> {
	const context = await auth.$context;
	return (await context.internalAdapter.findUserByEmail(email)) !== null;
}

export async function createCredentialUser(
	email: string,
	name: string,
	password: string,
): Promise<void> {
	const context = await auth.$context;
	const { minPasswordLength, maxPasswordLength } = context.password.config;
	if (
		password.length < minPasswordLength ||
		password.length > maxPasswordLength
	) {
		throw new Error(
			`The password must be between ${minPasswordLength} and ${maxPasswordLength} characters long.`,
		);
	}
	const passwordHash = await context.password.hash(password);
	const user = await context.internalAdapter.createUser(
		{ email, name, emailVerified: true },
		{ method: "admin" },
	);
	try {
		await context.internalAdapter.linkAccount({
			userId: user.id,
			providerId: "credential",
			accountId: user.id,
			password: passwordHash,
		});
	} catch (error) {
		await context.internalAdapter.deleteUser(user.id);
		throw error;
	}
}

export async function deleteUser(userId: string): Promise<void> {
	const context = await auth.$context;
	if (await context.internalAdapter.findUserById(userId)) {
		await context.internalAdapter.deleteUser(userId);
	}
}
