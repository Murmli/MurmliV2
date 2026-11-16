// Better Auth's type definitions import bun:sqlite; without this, tsc fails and skipLibCheck would turn the database option into any
declare module "bun:sqlite" {
	export class Database {
		private readonly bunSqliteDatabaseBrand: never;
	}
}
