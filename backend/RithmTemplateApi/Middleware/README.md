# Middleware

Interceptores HTTP globales para resiliencia y auditoria.

## Componentes

### IdempotencyMiddleware

Maneja la idempotencia de requests usando el header `Idempotency-Key`.

**Funcionamiento:**
1. Intercepta requests POST/PUT/PATCH con header `Idempotency-Key`
2. Verifica si existe una respuesta cacheada en Valkey/Redis
3. Si existe, retorna la respuesta cacheada
4. Si no existe, procesa el request y cachea la respuesta

**Uso en cliente:**
```http
POST /api/orders HTTP/1.1
Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
Content-Type: application/json

{"productId": "123", "quantity": 1}
```

### GlobalExceptionHandler

Implementacion de `IExceptionHandler` (.NET 8) para manejo consistente de errores.

**Mapeo de excepciones:**
- `NotFoundException` -> 404 Not Found
- `DomainException` -> 400 Bad Request
- `ArgumentException` -> 400 Bad Request
- `UnauthorizedAccessException` -> 401 Unauthorized
- `InvalidOperationException` (concurrent) -> 409 Conflict
- `OperationCanceledException` -> 499 Client Closed Request
- `TimeoutException` -> 504 Gateway Timeout
- Otros -> 500 Internal Server Error

**Formato de respuesta (RFC 7807 Problem Details):**
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Resource Not Found",
  "status": 404,
  "detail": "Order with id '123' was not found",
  "traceId": "00-abc123..."
}
```

## Agregar Nuevo Middleware

1. Crear clase que implemente el patron Middleware
2. Registrar en `Program.cs` usando `app.UseMiddleware<T>()`
3. Considerar el orden en el pipeline
