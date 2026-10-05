CREATE TABLE conversation_users (
    chat_id bigint PRIMARY KEY CHECK (chat_id <> 0),
    mode text NOT NULL DEFAULT 'study' CHECK (mode IN ('study', 'translate', 'quiz')),
    temperature numeric(2, 1) NOT NULL DEFAULT 0.3 CHECK (temperature IN (0.0, 0.3, 0.7, 1.0)),
    revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0)
);

CREATE TABLE conversation_turns (
    turn_id uuid PRIMARY KEY,
    chat_id bigint NOT NULL REFERENCES conversation_users(chat_id),
    revision bigint NOT NULL CHECK (revision >= 0),
    sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
    status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'succeeded', 'failed'))
);

CREATE INDEX conversation_turns_by_chat ON conversation_turns(chat_id, sequence);

CREATE TABLE conversation_messages (
    message_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    turn_id uuid NOT NULL REFERENCES conversation_turns(turn_id) ON DELETE CASCADE,
    role text NOT NULL CHECK (role IN ('user', 'assistant')),
    content text NOT NULL CHECK (length(content) > 0),
    UNIQUE (turn_id, role)
);
