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
        CREATE TABLE IF NOT EXISTS synctest.boards (
          id               uuid         NOT NULL CONSTRAINT pk_boards PRIMARY KEY,
          scope_id         uuid         NOT NULL CONSTRAINT fk_boards_scopes_scope_id REFERENCES sync.scopes (id) ON DELETE CASCADE,
          version          bigint       NOT NULL,
          title            text         NOT NULL,
          title_changed_at timestamptz  NOT NULL,
          CONSTRAINT ck_boards_scope_id CHECK (scope_id = id)
        );
        CREATE INDEX IF NOT EXISTS ix_boards_scope_id_version ON synctest.boards (scope_id, version);
        CREATE TABLE IF NOT EXISTS synctest.cards (
          id                   uuid         NOT NULL CONSTRAINT pk_cards PRIMARY KEY,
          scope_id             uuid         NOT NULL CONSTRAINT fk_cards_scopes_scope_id REFERENCES sync.scopes (id) ON DELETE CASCADE,
          version              bigint       NOT NULL,
          text                 text         NOT NULL,
          text_changed_at      timestamptz  NOT NULL,
          count                numeric(7,3) NULL,
          measure              text         NULL,
          amount_changed_at    timestamptz  NOT NULL,
          done                 boolean      NOT NULL,
          done_changed_at      timestamptz  NOT NULL,
          changed_by_member_id uuid         NULL CONSTRAINT fk_cards_member_entries_changed_by_member_id REFERENCES sync.member_entries (id),
          created_by_member_id uuid         NULL CONSTRAINT fk_cards_member_entries_created_by_member_id REFERENCES sync.member_entries (id),
          done_by_member_id    uuid         NULL CONSTRAINT fk_cards_member_entries_done_by_member_id REFERENCES sync.member_entries (id),
          created_at           timestamptz  NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_cards_scope_id_version ON synctest.cards (scope_id, version);
        CREATE INDEX IF NOT EXISTS ix_cards_changed_by_member_id ON synctest.cards (changed_by_member_id) WHERE changed_by_member_id IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ix_cards_created_by_member_id ON synctest.cards (created_by_member_id) WHERE created_by_member_id IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ix_cards_done_by_member_id ON synctest.cards (done_by_member_id) WHERE done_by_member_id IS NOT NULL;
        """;
}
