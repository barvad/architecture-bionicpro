from airflow import DAG
from airflow.providers.postgres.hooks.postgres import PostgresHook
from airflow.operators.python import PythonOperator
from datetime import datetime, timedelta
import pandas as pd
from clickhouse_driver import Client
import json
import uuid 

default_args = {
    'owner': 'prosthetic_team',
    'depends_on_past': False,
    'start_date': datetime(2023, 1, 1),
    'retries': 1,
    'retry_delay': timedelta(minutes=5),
}

dag = DAG(
    'prosthetic_daily_report',
    default_args=default_args,
    description='ETL: Combine CRM and Telemetry data for Daily Reports',
    schedule_interval='0 1 * * *',
    catchup=False
)

def extract_and_transform(**context):
    print("=== START extract_and_transform ===")
    
    crm_hook = PostgresHook(postgres_conn_id='crm_db')
    crm_data = crm_hook.get_pandas_df("SELECT id, full_name, model FROM users")
    crm_data['id'] = crm_data['id'].astype(str).str.lower()
    
    yesterday = (datetime.now() - timedelta(days=1)).strftime('%Y-%m-%d')
    
    telemetry_query = f"""
        SELECT 
            user_id, 
            SUM(steps) as steps_count, 
            AVG(battery) as avg_battery,
            COUNT(CASE WHEN level = 'error' THEN 1 END) as errors_count
        FROM telemetry 
        WHERE event_date = '{yesterday}'
        GROUP BY user_id
    """
    
    telemetry_data = crm_hook.get_pandas_df(telemetry_query)
    telemetry_data['user_id'] = telemetry_data['user_id'].astype(str).str.lower()
    if crm_data.empty:
        print("No CRM data found")
        context['ti'].xcom_push(key='report_data', value=[])
        return
    
    if telemetry_data.empty:
        print("No telemetry data found")
        context['ti'].xcom_push(key='report_data', value=[])
        return
    
    report_df = crm_data.merge(telemetry_data, left_on='id', right_on='user_id')
    report_df['report_date'] = yesterday
    
    records = report_df.to_dict(orient="records")
    
    context['ti'].xcom_push(key='report_data', value=records)
    print(f"Saved {len(records)} records to XCom")
    print("=== END extract_and_transform ===")

def load_to_olap(**context):
    print("=== START load_to_olap ===")
    
    data = context['ti'].xcom_pull(task_ids='extract_transform', key='report_data')
    
    if not data:
        print("No data to load")
        return
    
    print(f"Loaded {len(data)} records from XCom")
    
    # Прямое подключение к ClickHouse
    try:
        client = Client(
            host='clickhouse',
            port=9000,
            user='default',
            password='',
            database='default'
        )
        print("✓ Connected to ClickHouse")
        
        # Создаём таблицу
        client.execute("""
            CREATE TABLE IF NOT EXISTS daily_user_reports (
                user_id UUID,
                report_date Date,
                full_name String,
                prosthetic_model String,
                steps_count UInt32,
                avg_battery_level Float32,
                errors_count UInt8
            ) ENGINE = MergeTree()
            ORDER BY (user_id, report_date)
        """)
        
        # Подготовка данных
        formatted_data = []
        for row in data:
            try:
                formatted_row = (
                    uuid.UUID(row['id']),
                    datetime.strptime(row['report_date'], "%Y-%m-%d").date(),
                    str(row['full_name']),
                    str(row['model']),
                    int(row['steps_count']),
                    float(row['avg_battery']),
                    int(row['errors_count'])
                )
                formatted_data.append(formatted_row)
            except Exception as e:
                print(f"Error formatting row {row}: {e}")
                continue
        
        if formatted_data:
            client.execute(
                "INSERT INTO daily_user_reports VALUES",
                formatted_data
            )
            print(f"✓ Inserted {len(formatted_data)} rows into ClickHouse")
        else:
            print("No valid data to insert")
        
    except Exception as e:
        print(f"✗ Error: {e}")
        import traceback
        traceback.print_exc()
        raise
    
    print("=== END load_to_olap ===")

t1 = PythonOperator(
    task_id='extract_transform',
    python_callable=extract_and_transform,
    provide_context=True,
    dag=dag,
)

t2 = PythonOperator(
    task_id='load_to_olap',
    python_callable=load_to_olap,
    provide_context=True,
    dag=dag,
)

t1 >> t2