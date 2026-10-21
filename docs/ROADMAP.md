# Roadmap

Las fases siguientes son propuestas de trabajo y no están implementadas.

## Fase 2 — Clasificación asistida

- Definir categorías, campos estructurados, criterios de confianza y errores recuperables.
- Añadir un caso de uso de clasificación y un contrato de proveedor cuando exista una necesidad concreta.
- Probar con corpus sintético y medir calidad; no automatizar acciones a partir de una respuesta no validada.

## Fase 3 — Revisión humana y borradores

- Incorporar una cola de revisión y un borrador asociado al mensaje.
- Permitir aprobar, editar o descartar propuestas con identidad y auditoría.
- Mantener el envío fuera del flujo hasta acordar permisos y política de aprobación.

## Fase 4 — Integraciones

- Añadir una integración de n8n y conectores de correo con permisos mínimos.
- Establecer verificación de webhooks, reintentos, límites de frecuencia y trazabilidad.
- Evitar persistir tokens de proveedores sin un plan de cifrado y rotación.

## Fase 5 — Operación segura

- Diseñar autenticación y autorización por buzón, retención/borrado, cifrado, auditoría y monitorización.
- Incorporar pruebas de carga, recuperación ante fallos, copias de seguridad y controles de despliegue.
- Revisar el modelo de amenazas y cumplimiento antes de procesar datos reales o exponer servicios públicamente.
