# Arquitectura

## Capas y dependencias

- `SmartInbox.Domain`: agregado `InboundMessage`, estado, transiciones, metadatos de adjuntos y límites del dominio. Solo depende de .NET.
- `SmartInbox.Application`: comandos, consultas, validadores FluentValidation, comportamiento de validación, `TimeProvider` y contrato `IInboundMessageRepository`.
- `SmartInbox.Infrastructure`: registros EF Core, `AppDbContext`, migraciones PostgreSQL, repositorio y escaneo validado `[IoC]`.
- `SmartInbox.API`: composición, contratos v1, token Bearer, límites HTTP, `ProblemDetails` y endpoints Minimal API.

Application no depende de Infrastructure. La API es el composition root y referencia Application e Infrastructure. El dominio no conoce HTTP ni EF Core.

## Agregado y huella

`InboundMessage.Receive` crea el estado inicial `Received`, toma `ImportedAt` de `TimeProvider` y convierte ambas fechas a UTC. `Source` se recorta y pasa a minúsculas; `MailboxId`, `ExternalMessageId` y `ExternalThreadId` conservan su capitalización porque son IDs externos opacos. El cuerpo y el asunto se conservan sin reescritura. Los campos opcionales vacíos se convierten en `null`; el tipo MIME se recorta y pasa a minúsculas.

La huella SHA-256 se calcula sobre una representación JSON determinista de `Source`, IDs, remitente, asunto, cuerpo, `ReceivedAt` en UTC y metadatos de adjuntos. No incluye ID interno, estado ni fecha de importación. Así, una repetición del mismo mensaje conserva su huella aunque llegue en otra ejecución.

El estado admite `Received -> Processing -> Processed/Failed` y `Failed -> Processing`. No hay procesador, clasificación, borradores ni agregados de acciones en esta fase.

## Idempotencia y transacciones

PostgreSQL impone el índice único `ux_inbound_messages_source_mailbox_external_message_id` sobre `(Source, MailboxId, ExternalMessageId)`. La aplicación no depende de una consulta previa.

1. El repositorio intenta insertar el mensaje.
2. Si PostgreSQL confirma la escritura, se responde `202 Accepted`.
3. Si PostgreSQL devuelve `23505` para el índice de idempotencia, el repositorio separa la entidad fallida y consulta el registro persistido.
4. Si el registro existe y la huella coincide, se devuelve su ID con `200 OK`.
5. Si existe pero la huella difiere, la aplicación genera `409 Conflict`. No se actualiza la fila.
6. Si la colisión no corresponde al índice esperado o el registro no aparece después de la colisión, el error se propaga al manejador de excepciones.

La prueba concurrente usa dos scopes y `AppDbContext` independientes contra PostgreSQL. Otra prueba inserta directamente dos filas para verificar que el propio índice de PostgreSQL rechaza la segunda.

## Persistencia

La migración inicial crea `inbound_messages`, restricciones de estado y huella, e índices de idempotencia y fecha. Las fechas son `timestamp with time zone`; el repositorio solo escribe `DateTimeOffset` en UTC. Los metadatos normalizados de adjuntos se guardan en `jsonb`; nunca hay una columna para bytes de archivo.

Las migraciones se aplican explícitamente con `dotnet ef database update`. Ningún entorno ejecuta migraciones implícitas durante el arranque de la API.

## API y seguridad

- `POST /api/v1/messages`, `GET /api/v1/messages/{id}` y `GET /api/v1/messages` exigen un token Bearer configurado fuera del repositorio.
- El token requiere al menos 32 caracteres; la comparación se realiza con hashes SHA-256 y comparación de tiempo constante.
- `GET /health` no requiere token y comprueba la conexión a PostgreSQL.
- El cuerpo HTTP se limita a 65 536 bytes. Los campos con límite de caracteres y la paginación se validan en Application.
- Se rechazan propiedades JSON desconocidas para no aceptar silenciosamente campos binarios no soportados.
- Validation, conflicto, ausencia e imprevistos producen respuestas estables. Las excepciones internas y el mensaje de la excepción no se devuelven al cliente.
- Los logs de aplicación registran el tipo de una excepción imprevista, no el payload o el mensaje de la excepción. No se habilita el logging de valores sensibles de EF Core.

El token no sustituye identidad de usuario, autorización por buzón, rotación, auditoría ni controles contra abuso. El sistema sigue siendo una demo privada local.
