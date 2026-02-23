# Controllers

Controladores Thin. Solo orquestacion HTTP.

## Principios

1. **Thin Controllers**: Los controladores solo deben manejar:
   - Validacion de entrada (Model Binding)
   - Autorizacion (Attributes)
   - Orquestacion de llamadas a servicios
   - Transformacion de respuestas HTTP

2. **No Business Logic**: La logica de negocio debe residir en `RithmTemplate.Helpers`

3. **Herencia**: Todos los controladores deben heredar de `RithmTemplateBaseController`

4. **Claims**: Usar los helpers del BaseController para acceder a claims del JWT:
   - `UserRithmId`: ID del usuario autenticado
   - `OrgRithmId`: ID de la organizacion
   - `UserEmail`: Email del usuario
   - `UserName`: Nombre del usuario

## Ejemplo de Controlador

```csharp
[Authorize]
public class ExampleController : RithmTemplateBaseController
{
    private readonly IExampleService _service;

    public ExampleController(IExampleService service)
    {
        _service = service;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        if (!ValidateRequiredClaims(out var error))
            return error!;

        var result = await _service.GetByIdAsync(id, OrgRithmId!);
        return OkResponse(result);
    }
}
```
