# BionicPRO Reports API с MinIO и Nginx CDN

## Архитектура

API генерирует отчёты и сохраняет их в MinIO (S3-compatible хранилище). Nginx выступает в роли CDN с кешированием, обеспечивая быстрый доступ к отчётам.

### Компоненты:

1. **BionicPRO.Reports API** - ASP.NET Core приложение
   - Получает данные отчётов из ClickHouse
   - Генерирует JSON отчёты
   - Сохраняет в MinIO
   - Возвращает URL на CDN

2. **MinIO** - Object Storage (S3-compatible)
   - Хранилище для отчётов
   - Web консоль на порту 9001
   - API на порту 9000

3. **Nginx** - CDN с кешированием
   - Проксирует запросы к MinIO
   - Кеширует отчёты на 30 минут
   - Порт 8080 для доступа

## Структура файлов отчётов в MinIO

```
reports/
  ??? {userId}/
  ?   ??? 2024/
  ?   ?   ??? 01/
  ?   ?   ?   ??? 01.json
  ?   ?   ?   ??? 02.json
  ?   ?   ?   ??? ...
  ?   ?   ??? 02/
  ?   ?       ??? ...
  ?   ??? ...
```

Где `{userId}` - GUID пользователя в формате строки.

## Запуск

### Docker Compose

```bash
# Запуск MinIO и Nginx
docker-compose -f docker-compose-cdn.yml up -d

# Проверка статуса
docker ps

# Логи
docker-compose -f docker-compose-cdn.yml logs -f
```

### Конфигурация приложения

appsettings.json:
```json
"Minio": {
  "Endpoint": "http://localhost:9000",
  "AccessKey": "minioadmin",
  "SecretKey": "minioadmin123",
  "CdnUrl": "http://localhost:8080"
}
```

## API

### Получить отчёты пользователя

```
GET /reports/{userId}
Authorization: Bearer <token>
```

Ответ:
```json
[
  {
    "reportDate": "2024-01-15T00:00:00Z",
    "reportUrl": "http://localhost:8080/reports/12345678-1234-1234-1234-123456789abc/2024/01/15.json",
    "status": "generated"
  },
  {
    "reportDate": "2024-01-14T00:00:00Z",
    "reportUrl": "http://localhost:8080/reports/12345678-1234-1234-1234-123456789abc/2024/01/14.json",
    "status": "cached"
  }
]
```

## Процесс

1. **Запрос отчётов** ? API получает запрос с userId
2. **Загрузка данных** ? Чтение из ClickHouse
3. **Проверка кеша** ? Проверяется наличие в MinIO
4. **Генерация** ? Если нет, генерируется JSON отчёт
5. **Загрузка** ? Сохраняется в MinIO
6. **Возврат URL** ? Клиент получает ссылку на CDN
7. **Кеширование** ? Nginx кеширует на 30 минут

## Мониторинг

### MinIO Web Console
http://localhost:9001
- Вход: minioadmin / minioadmin123

### Nginx Health Check
http://localhost:8080/health

## Keycloak

Убедитесь, что конфигурация Keycloak правильно установлена:

```json
"Keycloak": {
  "Authority": "https://your-keycloak/realms/your-realm",
  "ClientId": "your-client-id"
}
```

## Логирование

Приложение логирует:
- Инициализацию сервисов
- Загрузку данных из ClickHouse
- Операции с MinIO
- Генерацию отчётов
- Ошибки и исключения

Уровень логирования: **Info** (для отладки и мониторинга)

## Обработка ошибок

- **404** - отчётов не найдено для пользователя
- **401** - не авторизован
- **403** - нет прав доступа
- **500** - ошибка сервера (проверьте логи)
