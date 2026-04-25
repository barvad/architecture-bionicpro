CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

CREATE TABLE IF NOT EXISTS users (
    id UUID PRIMARY KEY,
    full_name TEXT NOT NULL,
    model TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS telemetry (
    id SERIAL PRIMARY KEY,
    user_id UUID NOT NULL,
    event_date DATE NOT NULL,
    steps INT NOT NULL,
    battery FLOAT NOT NULL,
    level TEXT NOT NULL
);

CREATE PUBLICATION crm_publication FOR TABLE users, telemetry;

SELECT * FROM pg_create_logical_replication_slot(
    'debezium_slot',
    'pgoutput'
);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM users LIMIT 1) THEN
        INSERT INTO users (id, full_name, model) VALUES
        ('ad77c3f6-c7e9-48ff-8c1d-696fc6699f48'::UUID, 'User #1', 'Pro-Hand v1'),
        ('21c6f492-fc55-4730-ae52-65d0adbb7329'::UUID, 'User #2', 'Bionic-Leg v2');
    END IF;
END $$;


DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM telemetry LIMIT 1) THEN
        INSERT INTO telemetry (user_id, event_date, steps, battery, level)
        SELECT 
            u.id as user_id,
            CURRENT_DATE - (d || ' day')::INTERVAL as event_date,
            (random() * 10000)::INT as steps,
            (random() * 100)::FLOAT as battery,
            CASE WHEN random() < 0.05 THEN 'error' ELSE 'info' END as level
        FROM users u
        CROSS JOIN generate_series(0, 30) s(d);
    END IF;
END $$;
