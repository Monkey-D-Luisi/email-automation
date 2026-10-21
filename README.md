# Smart Inbox Automation

Smart Inbox Automation es una demo local para recibir, validar, deduplicar y consultar correos normalizados. Esta entrega cubre exclusivamente la fase 1: ingestión y persistencia en PostgreSQL. Solo se deben usar mensajes sintéticos.

## Arquitectura

La solución usa .NET 10, ASP.NET Core Minimal API, CQRS con MediatR 12.5.0, FluentValidation, EF Core y PostgreSQL.

```text
SmartInbox.API -> SmartInbox.Application -> SmartInbox.Domain
       |                    ^
       +-> SmartInbox.Infrastructure -+
                    |
                PostgreSQL
```

`Domain` contiene el agregado `InboundMessage` y sus invariantes. `Application` contiene comandos, consultas, validadores y el contrato del repositorio. `Infrastructure` implementa el repositorio, EF Core y el registro `[IoC]`. `API` compone las capas y define los contratos HTTP versionados. Ninguna integración de correo o IA está implementada.

La solución contiene los ocho proyectos requeridos:

- `SmartInbox.Domain`, `SmartInbox.Application`, `SmartInbox.Infrastructure`, `SmartInbox.API`
- `SmartInbox.Domain.Tests`, `SmartInbox.Application.Tests`, `SmartInbox.Infrastructure.Tests`, `SmartInbox.API.Tests`

Las decisiones y límites están en [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md); el estado y la guía para continuar, en [docs/HANDOFF_ES.md](docs/HANDOFF_ES.md); y las fases posteriores, en [docs/ROADMAP.md](docs/ROADMAP.md).

## Requisitos

- .NET 10 SDK
- Docker Desktop con Docker Compose
- PowerShell 7 para los ejemplos de esta guía

## Arranque local

PostgreSQL se publica únicamente en `127.0.0.1:5432`. Compose crea bases separadas `smartinbox_dev` y `smartinbox_test`; la API local usa la primera y las pruebas de integración la segunda. El `.env.example` no incluye una contraseña funcional. En la primera ejecución, crea un `.env` local con una contraseña aleatoria; no lo subas a Git.

```powershell
if (-not (Test-Path '.env')) {
    $password = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $values = Get-Content '.env.example' | ForEach-Object {
        if ($_ -like 'SMARTINBOX_DB_PASSWORD=*') { "SMARTINBOX_DB_PASSWORD=$password" } else { $_ }
    }
    Set-Content -LiteralPath '.env' -Value $values
}
docker compose up -d
```

Guarda una cadena de conexión y un token de servicio en User Secrets. El proyecto ya incluye un `UserSecretsId`; los valores permanecen fuera del repositorio.

```powershell
$password = ((Get-Content '.env' | Where-Object { $_.StartsWith('SMARTINBOX_DB_PASSWORD=') }) -split '=', 2)[1]
$connection = "Host=127.0.0.1;Port=5432;Database=smartinbox_dev;Username=smartinbox;Password=$password"
$token = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet user-secrets set 'ConnectionStrings:Database' $connection --project 'src/SmartInbox.API/SmartInbox.API.csproj'
dotnet user-secrets set 'ServiceAuthentication:Token' $token --project 'src/SmartInbox.API/SmartInbox.API.csproj'
```

Las migraciones no se ejecutan al arrancar la API. Aplícalas explícitamente y después inicia el servidor:

```powershell
dotnet tool restore
$env:ConnectionStrings__Database = $connection
dotnet ef database update --project 'src/SmartInbox.Infrastructure/SmartInbox.Infrastructure.csproj' --startup-project 'src/SmartInbox.API/SmartInbox.API.csproj'
Remove-Item Env:ConnectionStrings__Database
dotnet run --project 'src/SmartInbox.API/SmartInbox.API.csproj'
```

La API local escucha en `http://127.0.0.1:5168`. Para usar otro entorno, define `ConnectionStrings__Database` y `ServiceAuthentication__Token` mediante variables de entorno o User Secrets. El token debe tener al menos 32 caracteres.

## API v1

Todos los endpoints bajo `/api/v1/messages` requieren `Authorization: Bearer <service-token>`. `/health` es público y comprueba PostgreSQL.

Ejemplo de [correo sintético](docs/examples/synthetic-email.json):

```json
{
  "source": "demo",
  "mailboxId": "synthetic-mailbox-001",
  "externalMessageId": "demo-message-001",
  "externalThreadId": "demo-thread-001",
  "senderEmail": "alex@example.test",
  "senderName": "Alex Example",
  "subject": "Consulta de demostración",
  "bodyText": "Hola, este correo es completamente sintético.",
  "receivedAt": "2026-10-21T09:00:00Z",
  "attachments": [
    {
      "externalAttachmentId": "demo-attachment-001",
      "name": "presupuesto-demo.pdf",
      "contentType": "application/pdf",
      "sizeBytes": 12345
    }
  ]
}
```

Las respuestas de `POST` son `202 Accepted` al guardar por primera vez y `200 OK` al repetir el mismo contenido. Ambas incluyen el mismo ID interno:

```json
{"id":"6a47dbcf-61fb-4a86-b91d-6adef46db57a","status":"Received"}
```

Si se reutiliza la misma combinación `(Source, MailboxId, ExternalMessageId)` con contenido distinto, responde `409 Conflict` sin cambiar el registro original:

```json
{
  "type": "https://smartinbox.local/problems/external-message-conflict",
  "title": "External message ID conflict",
  "status": 409,
  "detail": "The external message ID already exists with different content."
}
```

```powershell
$secretOutput = dotnet user-secrets list --project 'src/SmartInbox.API/SmartInbox.API.csproj' | Out-String
$token = [regex]::Match($secretOutput, 'ServiceAuthentication:Token\s*=\s*(.+)').Groups[1].Value.Trim()
$headers = @{ Authorization = "Bearer $token" }
$created = Invoke-WebRequest -Method Post -Uri 'http://127.0.0.1:5168/api/v1/messages' -Headers $headers -ContentType 'application/json' -InFile 'docs/examples/synthetic-email.json' -SkipHttpErrorCheck
$created.StatusCode
$id = ($created.Content | ConvertFrom-Json).id
Invoke-WebRequest -Uri "http://127.0.0.1:5168/api/v1/messages/$id" -Headers $headers -SkipHttpErrorCheck | Select-Object -ExpandProperty StatusCode
Invoke-WebRequest -Uri 'http://127.0.0.1:5168/api/v1/messages?pageNumber=1&pageSize=20' -Headers $headers -SkipHttpErrorCheck | Select-Object -ExpandProperty StatusCode
```

`GET /api/v1/messages/{id}` devuelve `404` cuando el ID interno no existe. `GET /api/v1/messages` acepta `pageNumber` de 1 a 10 000 y `pageSize` de 1 a 100. Los errores usan `ProblemDetails`; la API no devuelve excepciones internas.

Se aceptan hasta 10 entradas de metadatos de adjuntos (ID externo, nombre, tipo MIME y tamaño). No se reciben ni se guardan bytes de archivos. Los campos JSON desconocidos se rechazan, incluidos posibles campos de contenido binario.

Los límites de ingestión son: cuerpo HTTP de 65 536 bytes, texto del cuerpo de 40 000 caracteres, asunto de 998, IDs externos de 512 y buzón de 256 caracteres. El resto de los límites está en `InboundMessageLimits` y se valida antes de persistir.

## Pruebas, build y auditoría

Las pruebas de integración usan PostgreSQL real en localhost. La cadena debe apuntar a una base dedicada cuyo nombre termine en `_test`; Compose crea `smartinbox_test`. Las pruebas no borran la base ni sus filas.

```powershell
$password = ((Get-Content '.env' | Where-Object { $_.StartsWith('SMARTINBOX_DB_PASSWORD=') }) -split '=', 2)[1]
$env:SMARTINBOX_TEST_CONNECTION_STRING = "Host=127.0.0.1;Port=5432;Database=smartinbox_test;Username=smartinbox;Password=$password"
dotnet restore SmartInbox.sln
dotnet build SmartInbox.sln --configuration Release --no-restore
dotnet test SmartInbox.sln --configuration Release --no-build --no-restore
dotnet list SmartInbox.sln package --vulnerable --include-transitive
Remove-Item Env:SMARTINBOX_TEST_CONNECTION_STRING
```

`dotnet tool restore` instala la versión fijada de `dotnet-ef`. GitHub Actions ejecuta restore, build Release, pruebas unitarias y de integración con PostgreSQL y auditoría NuGet; no usa claves reales ni proveedores externos.

## Seguridad y privacidad

Esta aplicación está limitada a una demo local con datos sintéticos. No la expongas a redes no confiables ni proceses correos reales de clientes. Antes harían falta controles de identidad y autorización, rotación de credenciales, retención y borrado, cifrado y gestión operativa. El cuerpo del correo se guarda como texto en PostgreSQL para cumplir la ingestión de esta fase. Los logs de aplicación no incluyen cuerpos, destinatarios, tokens ni payloads.

El código propio se distribuye bajo MIT; MediatR 12.5.0 conserva su licencia Apache 2.0. Consulta [LICENSE](LICENSE).
