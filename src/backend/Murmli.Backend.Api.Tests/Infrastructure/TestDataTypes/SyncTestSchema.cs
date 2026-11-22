namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal static class SyncTestSchema
{
    public const string Name = "synctest";

    public const string Ddl = """
        CREATE SCHEMA IF NOT EXISTS synctest;
        CREATE TABLE IF NOT EXISTS synctest.notes (
          id               uuid         NOT NULL CONSTRAINT pk_notes PRIMARY KEY,
          scope_id         uuid         NOT NULL CONSTRAINT fk_notes_scopes_scope_id REFERENCES sync.scopes (id) ON DELETE CASCADE,
          version          bigint       NOT NULL,
          title            text         NOT NULL,
          title_changed_at timestamptz  NOT NULL,
          color            text         NULL,
          weight           numeric(4,1) NULL,
          style_changed_at timestamptz  NOT NULL,
          pinned           boolean      NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_notes_scope_id_version ON synctest.notes (scope_id, version);
        """;
}
