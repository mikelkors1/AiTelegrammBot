-- Миграция 002: структура хранения напоминаний и действий агента.
-- Добавляет пользователей с часовыми поясами, подготовленные действия,
-- подтверждения, напоминания и аудит для команды /why.
-- Ограничения связывают записи с владельцем и защищают от повторного создания;
-- существующие таблицы настроек и истории диалогов сохраняются.

-- Пользователь Telegram учитывается отдельно от чата из первой лабораторной.
-- NULL в timezone означает, что пользователь ещё не выбрал часовой пояс IANA.
CREATE TABLE agent_users (
    owner_id bigint PRIMARY KEY CHECK (owner_id > 0),
    timezone text CHECK (
        timezone IS NULL OR (
            length(timezone) BETWEEN 1 AND 100
            AND timezone = btrim(timezone)
            AND timezone !~ '[[:space:][:cntrl:]]'
        )
    )
);

-- Подготовленные параметры напоминания и состояние согласования.
-- Одному исходному сообщению соответствует одно действие;
-- срок его подтверждения ограничен пятью минутами с момента подготовки.
CREATE TABLE prepared_actions (
    action_id uuid PRIMARY KEY,
    owner_id bigint NOT NULL REFERENCES agent_users(owner_id),
    chat_id bigint NOT NULL CHECK (chat_id = owner_id),
    update_id bigint NOT NULL CHECK (update_id >= 0),
    tool_name text NOT NULL DEFAULT 'add_reminder' CHECK (tool_name = 'add_reminder'),
    reminder_text text NOT NULL CHECK (
        length(reminder_text) BETWEEN 1 AND 500 AND reminder_text ~ '[^[:space:]]'
    ),
    remind_at timestamptz NOT NULL CHECK (isfinite(remind_at)),
    timezone text NOT NULL CHECK (
        length(timezone) BETWEEN 1 AND 100
        AND timezone = btrim(timezone)
        AND timezone !~ '[[:space:][:cntrl:]]'
    ),
    arguments_hash bytea NOT NULL CHECK (octet_length(arguments_hash) = 32),
    status text NOT NULL DEFAULT 'pending' CHECK (
        status IN ('pending', 'confirmed', 'executed', 'cancelled', 'expired')
    ),
    created_at timestamptz NOT NULL DEFAULT now() CHECK (isfinite(created_at)),
    expires_at timestamptz NOT NULL CHECK (isfinite(expires_at)),
    CHECK (remind_at > created_at),
    CHECK (expires_at > created_at AND expires_at <= created_at + interval '5 minutes'),
    UNIQUE (owner_id, chat_id, update_id),
    UNIQUE (action_id, owner_id, chat_id)
);

CREATE INDEX prepared_actions_by_owner ON prepared_actions(owner_id, chat_id, created_at DESC, action_id);
CREATE INDEX prepared_actions_pending_expiry ON prepared_actions(expires_at) WHERE status = 'pending';

-- Подтверждение связано с тем же владельцем и чатом, что и подготовленное действие.
-- Репозиторий должен проверять срок согласия и переход состояния под блокировкой строки.
CREATE TABLE action_confirmations (
    action_id uuid PRIMARY KEY,
    owner_id bigint NOT NULL,
    chat_id bigint NOT NULL,
    confirmation_update_id bigint NOT NULL CHECK (confirmation_update_id >= 0),
    confirmed_at timestamptz NOT NULL DEFAULT now() CHECK (isfinite(confirmed_at)),
    FOREIGN KEY (action_id, owner_id, chat_id) REFERENCES prepared_actions(action_id, owner_id, chat_id),
    UNIQUE (owner_id, chat_id, confirmation_update_id),
    UNIQUE (action_id, owner_id, chat_id)
);

-- Напоминание требует подтверждения действия того же владельца.
-- Репозиторий должен копировать согласованные параметры и переводить действие
-- в состояние executed в одной транзакции. Одно действие создаёт одно напоминание.
CREATE TABLE reminders (
    reminder_id uuid PRIMARY KEY,
    action_id uuid NOT NULL UNIQUE,
    owner_id bigint NOT NULL,
    chat_id bigint NOT NULL,
    reminder_text text NOT NULL CHECK (
        length(reminder_text) BETWEEN 1 AND 500 AND reminder_text ~ '[^[:space:]]'
    ),
    remind_at timestamptz NOT NULL CHECK (isfinite(remind_at)),
    timezone text NOT NULL CHECK (
        length(timezone) BETWEEN 1 AND 100
        AND timezone = btrim(timezone)
        AND timezone !~ '[[:space:][:cntrl:]]'
    ),
    created_at timestamptz NOT NULL DEFAULT now() CHECK (isfinite(created_at)),
    FOREIGN KEY (action_id, owner_id, chat_id) REFERENCES action_confirmations(action_id, owner_id, chat_id)
);

CREATE INDEX reminders_by_owner ON reminders(owner_id, chat_id, remind_at, reminder_id);
CREATE INDEX reminders_by_time ON reminders(remind_at, reminder_id);

-- Аудит решений агента: действие, безопасное резюме аргументов, результат и причина.
-- Сервисы должны исключать из записей полные промпты, метаданные HMAC и тела ответов инструментов.
CREATE TABLE agent_audit_events (
    audit_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    request_id uuid NOT NULL,
    owner_id bigint NOT NULL REFERENCES agent_users(owner_id),
    chat_id bigint NOT NULL CHECK (chat_id <> 0),
    update_id bigint NOT NULL CHECK (update_id >= 0),
    occurred_at timestamptz NOT NULL DEFAULT now() CHECK (isfinite(occurred_at)),
    action text NOT NULL CHECK (action IN ('respond', 'clarify', 'tool_call', 'reject')),
    tool_name text CHECK (tool_name ~ '^[A-Za-z0-9_-]{1,64}$'),
    arguments_summary jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (
        jsonb_typeof(arguments_summary) = 'object'
        AND octet_length(arguments_summary::text) <= 4096
    ),
    status text NOT NULL CHECK (
        status IN ('pending', 'succeeded', 'failed', 'rejected', 'cancelled', 'limit_exceeded')
    ),
    reason text NOT NULL CHECK (length(reason) BETWEEN 1 AND 500 AND reason ~ '[^[:space:]]'),
    duration_ms bigint CHECK (duration_ms >= 0),
    CHECK ((action = 'tool_call' AND tool_name IS NOT NULL) OR action <> 'tool_call')
);

CREATE INDEX agent_audit_by_owner ON agent_audit_events(owner_id, chat_id, audit_id DESC);
CREATE INDEX agent_audit_by_request ON agent_audit_events(request_id, audit_id);
