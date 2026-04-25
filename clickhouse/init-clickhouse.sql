-- =========================
-- KAFKA SOURCES
-- =========================

CREATE TABLE users_kafka
(
    id String,
    full_name String,
    model String,
    __op String,
    __ts_ms UInt64,
    __table String
)
ENGINE = Kafka
SETTINGS
    kafka_broker_list = 'kafka:9092',
    kafka_topic_list = 'crm_pg.public.users',
    kafka_group_name = 'ch_users',
    kafka_format = 'JSONEachRow';


CREATE TABLE telemetry_kafka
(
    id UInt32,
    user_id String,
    event_date UInt32,
    steps Int32,
    battery Float32,
    level String,
    __op String,
    __ts_ms UInt64,
    __table String
)
ENGINE = Kafka
SETTINGS
    kafka_broker_list = 'kafka:9092',
    kafka_topic_list = 'crm_pg.public.telemetry',
    kafka_group_name = 'ch_telemetry',
    kafka_format = 'JSONEachRow';


-- =========================
-- RAW STORAGE (STATE LAYER)
-- =========================

CREATE TABLE users_raw
(
    id String,
    full_name String,
    model String,
    __ts_ms UInt64
)
ENGINE = MergeTree
ORDER BY id;


CREATE TABLE telemetry_raw
(
    user_id String,
    event_date UInt32,
    steps Int32,
    battery Float32,
    level String,
    __ts_ms UInt64
)
ENGINE = MergeTree
ORDER BY (user_id, event_date);


-- =========================
-- STREAM → RAW
-- =========================

CREATE MATERIALIZED VIEW users_mv
TO users_raw
AS
SELECT
    id,
    full_name,
    model,
    __ts_ms
FROM users_kafka
WHERE __op IN ('r','c','u');


CREATE MATERIALIZED VIEW telemetry_mv
TO telemetry_raw
AS
SELECT
    user_id,
    event_date,
    steps,
    battery,
    level,
    __ts_ms
FROM telemetry_kafka
WHERE __op IN ('r','c','u');


-- =========================
-- FINAL TABLE
-- =========================

CREATE TABLE daily_user_reports_final
(
    user_id String,
    report_date Date,
    full_name String,
    model String,
    steps_count UInt64,
    avg_battery_level Float32,
    errors_count UInt32,
    updated_at DateTime
)
ENGINE = ReplacingMergeTree(updated_at)
ORDER BY (user_id, report_date);


-- =========================
-- AGGREGATION + JOIN MV
-- =========================

CREATE MATERIALIZED VIEW reports_mv
TO daily_user_reports_final
AS
SELECT
    t.user_id,
    toDate(t.event_date) AS report_date,

    any(u.full_name) AS full_name,
    any(u.model) AS model,

    sum(t.steps) AS steps_count,
    avg(t.battery) AS avg_battery_level,
    countIf(t.level = 'error') AS errors_count,

    now() AS updated_at
FROM telemetry_raw t
LEFT JOIN users_raw u ON t.user_id = u.id
GROUP BY
    t.user_id,
    report_date;