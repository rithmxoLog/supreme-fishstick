# Models

Organizacion por funcionalidad, no por capa tecnica (Vertical Slices).

## Estructura

```
Models/
├── Common/                    # Modelos compartidos
│   ├── ErrorResponse.cs       # Respuestas de error estandarizadas
│   └── PaginatedResponse.cs   # Wrapper para paginacion
│
└── Features/                  # Carpetas por Feature/Dominio
    ├── Orders/
    │   ├── CreateOrderRequest.cs
    │   ├── OrderResponse.cs
    │   └── OrderListItem.cs
    │
    ├── Products/
    │   ├── ProductRequest.cs
    │   └── ProductResponse.cs
    │
    └── Users/
        └── UserDto.cs
```

## Principios

### 1. Vertical Slices
Agrupar por funcionalidad/feature, no por tipo tecnico. Esto facilita:
- Encontrar codigo relacionado
- Refactoring de features completas
- Eliminar features sin afectar otras

### 2. DTOs vs Entities
- **Models/**: DTOs para comunicacion HTTP (Request/Response)
- **DAL/Entities/**: Modelos de persistencia (Database)

Nunca exponer Entities directamente en la API.

### 3. Inmutabilidad
Preferir `init` sobre `set` para propiedades de Request/Response:

```csharp
public class OrderResponse
{
    public required string Id { get; init; }
    public required decimal Total { get; init; }
}
```

### 4. Validacion
Usar Data Annotations o FluentValidation para validar Requests:

```csharp
public class CreateOrderRequest
{
    [Required]
    [StringLength(100)]
    public required string ProductId { get; init; }

    [Range(1, 1000)]
    public int Quantity { get; init; }
}
```
