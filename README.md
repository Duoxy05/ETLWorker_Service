# ETLWorker_Service — Extracción (E de ETL) del Sistema de Opiniones

Worker Service en **.NET 8** con Clean Architecture que extrae opiniones de tres fuentes
(CSV, BD relacional MySQL y API REST) y las deja en tablas *staging* de la BD analítica,
registrando logs (ILogger + Serilog) y hallazgos de calidad **sin rechazar ninguna fila**.

## Estructura
| Carpeta | Contenido |
|---|---|
| `src/ETLWorker.Domain` | Entidades (`RawOpinion`, `QualityIssue`, `ExtractionSummary`) y enums |
| `src/ETLWorker.Application` | `IExtractor`, orquestador, pipeline, validador, opciones |
| `src/ETLWorker.Infrastructure` | `CsvExtractor`, `DatabaseExtractor`, `ApiExtractor`, repositorio MySQL |
| `src/ETLWorker.Worker` | Host (`Program.cs`, `EtlWorker`), `appsettings.json` |
| `src/ETLWorker.MockSocialApi` | API REST simulada que sirve `social_comments.csv` |
| `database/` | Scripts MySQL: BD origen y BD analítica (staging + bitácora) |
| `data/` | CSV de entrada |
| `docs/` | Diagramas de arquitectura y de flujo |

## Puesta en marcha
1. **MySQL 8**: ejecutar `database/01_origen_resenas_web.sql` y `database/02_analitica_staging.sql`
   (cambie `CAMBIAR_ESTA_CLAVE` por contraseñas reales).
2. **Secretos** (nunca van en el repositorio), desde `src/ETLWorker.Worker`:
   ```
   dotnet user-secrets set "ConnectionStrings:OrigenWeb" "Server=localhost;Port=3306;Database=OpinionesOrigenDB;User ID=etl_reader;Password=TU_CLAVE"
   dotnet user-secrets set "ConnectionStrings:Analitica" "Server=localhost;Port=3306;Database=SistemaOpinionesDW;User ID=etl_writer;Password=TU_CLAVE"
   dotnet user-secrets set "Secrets:SocialApiKey" "dev-local-key-123"
   ```
   En producción use variables de entorno: `ConnectionStrings__Analitica`, `Secrets__SocialApiKey`.
3. **API simulada** (terminal 1): `dotnet run --project src/ETLWorker.MockSocialApi`
4. **Worker** (terminal 2): `dotnet run --project src/ETLWorker.Worker`
5. Verificar: `SELECT * FROM etl_ejecucion;` y `SELECT Fuente, Problema, COUNT(*) FROM etl_calidad_datos GROUP BY 1,2;`

## Agregar una fuente nueva
Agregue un bloque en `Etl:Sources` de `appsettings.json` (tipo, tabla `stg_*` y mapeo de campos).
Si es de un tipo existente no se escribe código; si es un tipo nuevo, cree una clase que implemente
`IExtractor` y regístrela en `InfrastructureServiceRegistration`.
