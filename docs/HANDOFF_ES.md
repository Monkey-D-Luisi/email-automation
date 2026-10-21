# Handoff — Smart Inbox Automation, fase 1

## Estado

Fase 1 implementada en el repositorio independiente `email-automation`. La solución tiene los ocho proyectos previstos, el agregado `InboundMessage`, CQRS con MediatR 12.5.0 y FluentValidation, persistencia EF Core/Npgsql, API v1 con token de servicio, migración inicial, Docker Compose, CI y documentación.

La recepción, consulta, paginación, deduplicación, conflicto y concurrencia fueron verificadas contra PostgreSQL 17 real. La API local quedó configurada con User Secrets; los valores de desarrollo y el archivo `.env` permanecen fuera del repositorio.

## Verificación ejecutada

- `dotnet restore SmartInbox.sln`: correcto.
- `dotnet build SmartInbox.sln --configuration Release --no-restore`: correcto, 0 warnings y 0 errores.
- `dotnet test SmartInbox.sln --configuration Release --no-build --no-restore`: **24/24 tests** correctos, incluidos tests de API, unitarios y PostgreSQL con contextos independientes.
- `dotnet list SmartInbox.sln package --vulnerable --include-transitive`: ningún paquete vulnerable reportado por las fuentes NuGet consultadas.
- Docker Compose PostgreSQL 17: contenedor healthy en `127.0.0.1:5432`; migración `InitialCreate` aplicada a `smartinbox_dev`. Las pruebas se ejecutaron contra `smartinbox_test`.
- Las pruebas PostgreSQL verificaron el índice único real, la colisión concurrente, reintentos, conflicto sin sobreescritura, autenticación válida/ausente/inválida y límites del cuerpo HTTP.
- Smoke test en Kestrel local: `/health` 200, mensaje sin token 401, POST nuevo 202, repetido 200 con el mismo ID, conflicto 409, GET y listado 200; el contenido original permaneció intacto. Dos POST concurrentes devolvieron 202 y 200 con el mismo ID, y el listado confirmó exactamente una fila persistida para el mensaje.

La CI configurada repite restore, build, todas las pruebas con servicio PostgreSQL y auditoría NuGet. La ejecución de GitHub Actions no se ha lanzado porque este repositorio no está publicado.

## Cómo continuar

1. En PowerShell, crea `.env` desde `.env.example` con una contraseña aleatoria local para `SMARTINBOX_DB_PASSWORD`; Compose prepara `smartinbox_dev` y `smartinbox_test`.
2. Ejecuta `docker compose up -d` y guarda `ConnectionStrings:Database` y `ServiceAuthentication:Token` con `dotnet user-secrets set` para el proyecto API.
3. Ejecuta `dotnet tool restore` y aplica `dotnet ef database update` explícitamente.
4. Inicia `dotnet run --project src/SmartInbox.API/SmartInbox.API.csproj` y prueba el ejemplo `docs/examples/synthetic-email.json`.
5. Ejecuta la suite completa con `SMARTINBOX_TEST_CONNECTION_STRING` apuntando a una base dedicada cuyo nombre termine en `_test`.

Ver [README.md](../README.md) para comandos PowerShell completos.

## Límites y riesgos reales

- El cuerpo de correo se almacena como texto legible en PostgreSQL. No hay cifrado de aplicación, retención ni borrado automatizado.
- MediatR 12.5.0 se mantiene por el requisito de licencia Apache 2.0; su versión principal 12 no ofrece garantías de soporte upstream, así que conviene revisar esa decisión antes de una fase con uso operativo.
- El token compartido no gestiona usuarios, permisos por buzón, rotación, rate limiting ni una auditoría de acceso.
- La demo solo sirve en localhost y exclusivamente con datos sintéticos; no es apta para exposición pública ni correos reales de clientes.
- Los adjuntos solo aportan metadatos normalizados; no hay descarga, lectura ni persistencia de bytes.
- No existen integraciones Gmail/Outlook, n8n, IA, borradores, envío ni frontend.
- El repositorio `document-processor` se pudo revisar localmente y sirvió de referencia para capas e IoC. La URL solicitada de `converso` devolvió 404 y no había copia local accesible, así que no se tomaron decisiones basadas en ese repositorio.
- No se publicaron repositorios ni se lanzó CI hospedada.

## Preparación para fase 2

Definir el caso de uso de clasificación, su política de datos y la retención antes de introducir un proveedor IA. Añadir contrato e implementación solo cuando exista ese caso de uso. Mantener el cuerpo y el prompt fuera de logs, probar con correos sintéticos y conservar la aprobación humana para cualquier acción futura.
