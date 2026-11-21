-- =====================================================================
-- 02_analitica_staging.sql   (MySQL 8)
-- BD ANALITICA: zona de staging + bitácora del ETL.
-- Regla de oro del staging: SIN claves foráneas ni restricciones de valor y
-- todo como texto. Así NINGUNA fila se rechaza en la extracción (en la tarea
-- anterior se perdían 582 filas por FK). Los problemas se registran en
-- etl_calidad_datos y se resuelven en la etapa Transform.
-- =====================================================================
CREATE DATABASE IF NOT EXISTS SistemaOpinionesDW CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE SistemaOpinionesDW;

-- Staging: encuestas internas (origen CSV)
CREATE TABLE IF NOT EXISTS stg_encuestas (
    Id               BIGINT        NOT NULL AUTO_INCREMENT,
    LoteId           CHAR(36)      NOT NULL,
    Fuente           VARCHAR(50)   NOT NULL,
    NumeroFila       INT           NOT NULL,
    IdOrigen         VARCHAR(100)  NULL,
    IdClienteRaw     VARCHAR(100)  NULL,
    IdProductoRaw    VARCHAR(100)  NULL,
    FechaRaw         VARCHAR(100)  NULL,
    ComentarioRaw    TEXT          NULL,
    PuntajeRaw       VARCHAR(50)   NULL,
    ClasificacionRaw VARCHAR(50)   NULL,
    PlataformaRaw    VARCHAR(100)  NULL,
    FechaExtraccion  DATETIME(3)   NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    PRIMARY KEY (Id),
    KEY ix_stg_encuestas_lote (LoteId)
) ENGINE=InnoDB;

-- Staging: reseñas web (origen BD relacional)
CREATE TABLE IF NOT EXISTS stg_resenas_web (
    Id               BIGINT        NOT NULL AUTO_INCREMENT,
    LoteId           CHAR(36)      NOT NULL,
    Fuente           VARCHAR(50)   NOT NULL,
    NumeroFila       INT           NOT NULL,
    IdOrigen         VARCHAR(100)  NULL,
    IdClienteRaw     VARCHAR(100)  NULL,
    IdProductoRaw    VARCHAR(100)  NULL,
    FechaRaw         VARCHAR(100)  NULL,
    ComentarioRaw    TEXT          NULL,
    PuntajeRaw       VARCHAR(50)   NULL,
    ClasificacionRaw VARCHAR(50)   NULL,
    PlataformaRaw    VARCHAR(100)  NULL,
    FechaExtraccion  DATETIME(3)   NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    PRIMARY KEY (Id),
    KEY ix_stg_resenas_web_lote (LoteId)
) ENGINE=InnoDB;

-- Staging: comentarios de redes sociales (origen API REST)
CREATE TABLE IF NOT EXISTS stg_comentarios_sociales (
    Id               BIGINT        NOT NULL AUTO_INCREMENT,
    LoteId           CHAR(36)      NOT NULL,
    Fuente           VARCHAR(50)   NOT NULL,
    NumeroFila       INT           NOT NULL,
    IdOrigen         VARCHAR(100)  NULL,
    IdClienteRaw     VARCHAR(100)  NULL,
    IdProductoRaw    VARCHAR(100)  NULL,
    FechaRaw         VARCHAR(100)  NULL,
    ComentarioRaw    TEXT          NULL,
    PuntajeRaw       VARCHAR(50)   NULL,
    ClasificacionRaw VARCHAR(50)   NULL,
    PlataformaRaw    VARCHAR(100)  NULL,
    FechaExtraccion  DATETIME(3)   NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    PRIMARY KEY (Id),
    KEY ix_stg_comentarios_sociales_lote (LoteId)
) ENGINE=InnoDB;

-- Hallazgos de calidad detectados al extraer (no bloquean la carga)
CREATE TABLE IF NOT EXISTS etl_calidad_datos (
    Id            BIGINT        NOT NULL AUTO_INCREMENT,
    LoteId        CHAR(36)      NOT NULL,
    Fuente        VARCHAR(50)   NOT NULL,
    IdOrigen      VARCHAR(100)  NULL,
    Campo         VARCHAR(50)   NOT NULL,
    Problema      VARCHAR(200)  NOT NULL,
    Severidad     VARCHAR(15)   NOT NULL,   -- Advertencia | Error
    ValorOriginal VARCHAR(200)  NULL,
    FechaRegistro DATETIME(3)   NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    PRIMARY KEY (Id),
    KEY ix_calidad_lote (LoteId, Fuente)
) ENGINE=InnoDB;

-- Resumen por fuente y por ejecución (trazabilidad y métricas de rendimiento)
CREATE TABLE IF NOT EXISTS etl_ejecucion (
    Id                    BIGINT       NOT NULL AUTO_INCREMENT,
    LoteId                CHAR(36)     NOT NULL,
    Fuente                VARCHAR(50)  NOT NULL,
    TipoFuente            VARCHAR(20)  NOT NULL,
    Estado                VARCHAR(10)  NOT NULL,   -- OK | ERROR
    FilasLeidas           BIGINT       NOT NULL,
    FilasConObservaciones BIGINT       NOT NULL,
    Observaciones         BIGINT       NOT NULL,
    DuracionMs            BIGINT       NOT NULL,
    Mensaje               VARCHAR(1000) NULL,
    FechaRegistro         DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    PRIMARY KEY (Id),
    KEY ix_ejecucion_lote (LoteId)
) ENGINE=InnoDB;

-- Mínimo privilegio: solo puede insertar, leer y limpiar staging/bitácora (no DROP, no DDL).
CREATE USER IF NOT EXISTS 'etl_writer'@'localhost'
IDENTIFIED BY RANDOM PASSWORD;
GRANT INSERT, DELETE ON SistemaOpinionesDW.stg_encuestas
TO 'etl_writer'@'localhost';

GRANT INSERT, DELETE ON SistemaOpinionesDW.stg_resenas_web
TO 'etl_writer'@'localhost';

GRANT INSERT, DELETE ON SistemaOpinionesDW.stg_comentarios_sociales
TO 'etl_writer'@'localhost';

GRANT INSERT ON SistemaOpinionesDW.etl_calidad_datos
TO 'etl_writer'@'localhost';

GRANT INSERT ON SistemaOpinionesDW.etl_ejecucion
TO 'etl_writer'@'localhost';
REVOKE SELECT, INSERT, DELETE ON SistemaOpinionesDW.*
FROM 'etl_writer'@'localhost';

SHOW GRANTS FOR 'etl_writer'@'localhost';
FLUSH PRIVILEGES;
