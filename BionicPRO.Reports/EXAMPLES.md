## Примеры использования API

### 1. Получить отчёты пользователя с помощью curl

```bash
# Получить токен от Keycloak
TOKEN=$(curl -X POST http://your-keycloak/realms/your-realm/protocol/openid-connect/token \
  -d "client_id=reports-client" \
  -d "client_secret=your-secret" \
  -d "grant_type=client_credentials" \
  -d "username=user@example.com" \
  -d "password=password" \
  | jq -r '.access_token')

# Получить отчёты
curl -H "Authorization: Bearer $TOKEN" \
  http://localhost:8080/reports/12345678-1234-1234-1234-123456789abc
```

### 2. Использование из C# / .NET

```csharp
using System.Net.Http;
using System.Net.Http.Headers;

var client = new HttpClient();
client.DefaultRequestHeaders.Authorization = 
    new AuthenticationHeaderValue("Bearer", accessToken);

var response = await client.GetAsync(
    "http://localhost:8080/reports/{userId}");

if (response.IsSuccessStatusCode)
{
    var json = await response.Content.ReadAsStringAsync();
    var reports = JsonSerializer.Deserialize<List<ReportResponse>>(json);

    foreach (var report in reports)
    {
        Console.WriteLine($"Report: {report.ReportDate}");
        Console.WriteLine($"URL: {report.ReportUrl}");
        Console.WriteLine($"Status: {report.Status}");
    }
}
```

### 3. Использование из JavaScript / TypeScript

```typescript
const userId = "12345678-1234-1234-1234-123456789abc";
const token = localStorage.getItem('access_token');

const response = await fetch(
    `http://localhost:8080/reports/${userId}`,
    {
        headers: {
            'Authorization': `Bearer ${token}`
        }
    }
);

if (response.ok) {
    const reports = await response.json();

    reports.forEach(report => {
        console.log(`Report: ${report.reportDate}`);
        console.log(`URL: ${report.reportUrl}`);
        console.log(`Status: ${report.status}`);

        // Скачать отчёт
        fetch(report.reportUrl).then(r => r.blob());
    });
}
```

### 4. Использование в Python

```python
import requests
import json

userId = "12345678-1234-1234-1234-123456789abc"
token = "your-access-token"

headers = {
    "Authorization": f"Bearer {token}"
}

response = requests.get(
    f"http://localhost:8080/reports/{userId}",
    headers=headers
)

if response.status_code == 200:
    reports = response.json()

    for report in reports:
        print(f"Report: {report['reportDate']}")
        print(f"URL: {report['reportUrl']}")
        print(f"Status: {report['status']}")

        # Скачать отчёт
        report_response = requests.get(report['reportUrl'])
        if report_response.status_code == 200:
            with open(f"report_{report['reportDate']}.json", 'w') as f:
                f.write(report_response.text)
```

### 5. Скачивание отчётов с помощью curl

```bash
# Загрузить URL из ответа API
REPORT_URL="http://localhost:8080/reports/12345678-1234-1234-1234-123456789abc/2024/01/15.json"

# Скачать отчёт
curl -o report_2024-01-15.json "$REPORT_URL"

# Вывести содержимое
curl "$REPORT_URL" | jq .
```

### 6. Проверка статуса кеша Nginx

```bash
# Получить заголовок X-Cache-Status
curl -I http://localhost:8080/reports/12345678-1234-1234-1234-123456789abc/2024/01/15.json

# Ответ:
# X-Cache-Status: HIT   (кеш использован)
# X-Cache-Status: MISS  (запрос прошёл до MinIO)
```

### 7. Просмотр логов

```bash
# Логи API
docker logs -f reports-api

# Логи MinIO
docker logs -f minio

# Логи Nginx
docker logs -f nginx-cdn
```

### 8. Доступ к MinIO Web Console

```
URL: http://localhost:9001
Вход: minioadmin
Пароль: minioadmin123
```

На консоли можно:
- Просмотреть бакеты и файлы
- Скачать отчёты
- Удалить файлы
- Настроить политики доступа

### 9. Проверка здоровья сервисов

```bash
# Health check API
curl http://localhost:5000/health

# Health check MinIO
curl http://localhost:9000/minio/health/live

# Health check Nginx CDN
curl http://localhost:8080/health
```

## Обработка ошибок

### 404 - Отчёты не найдены
```bash
curl -H "Authorization: Bearer $TOKEN" \
  http://localhost:8080/reports/non-existent-user-id
# Ответ: 404 Not Found
```

### 401 - Не авторизован
```bash
curl http://localhost:8080/reports/12345678-1234-1234-1234-123456789abc
# Ответ: 401 Unauthorized
```

### 403 - Нет прав доступа
```bash
# Если user не имеет роли "user" в Keycloak
curl -H "Authorization: Bearer $TOKEN_WITHOUT_ROLE" \
  http://localhost:8080/reports/12345678-1234-1234-1234-123456789abc
# Ответ: 403 Forbidden
```

## Performance Tips

1. **Кеширование**: Первый запрос может быть медленнее (генерация + загрузка в MinIO)
2. **CDN**: Последующие запросы будут из кеша Nginx (~30ms)
3. **Nginx Cache**: Проверьте `X-Cache-Status` заголовок
4. **MinIO**: Для большого количества файлов используйте S3 client libraries
