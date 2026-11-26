-- =====================================================================
-- 03_dw_dimensiones.sql   (MySQL 8)
-- Almacén de datos SistemaOpinionesDW: dimensiones del modelo estrella,
-- bitácora de la carga de dimensiones y usuario del cargador.
--
-- Las dimensiones se crean con IF NOT EXISTS: si ya las tienes de la tarea de Modelado
-- (misma estructura), este script no las toca y los procedimientos almacenados las llenan.
-- Los procedimientos se crean en 04_procedimientos_dimensiones.sql.
-- Ejecutar como administrador (root) DESPUÉS de 02_analitica_staging.sql.
-- =====================================================================
USE SistemaOpinionesDW;

-- Quién opina. IdCliente conserva el ID del catálogo; negativos = miembros especiales.
CREATE TABLE IF NOT EXISTS Dim_Cliente (
    IdCliente   INT          NOT NULL,
    Nombre      VARCHAR(150) NOT NULL,
    Email       VARCHAR(150) NULL,
    Pais        VARCHAR(60)  NULL,
    Edad        TINYINT UNSIGNED NULL,
    TipoCliente VARCHAR(40)  NULL,
    EsEspecial  BOOLEAN      NOT NULL DEFAULT FALSE,
    PRIMARY KEY (IdCliente),
    UNIQUE KEY uq_dim_cliente_email (Email)
) ENGINE=InnoDB;

-- Sobre qué producto. La categoría vive dentro de la dimensión (estrella, sin copo de nieve).
CREATE TABLE IF NOT EXISTS Dim_Producto (
    IdProducto INT          NOT NULL,
    Nombre     VARCHAR(150) NOT NULL,
    Categoria  VARCHAR(100) NULL,
    EsEspecial BOOLEAN      NOT NULL DEFAULT FALSE,
    PRIMARY KEY (IdProducto)
) ENGINE=InnoDB;

-- Por dónde llegó: canal (Encuesta, Web, Red social) + plataforma (Instagram, Facebook, Twitter...).
CREATE TABLE IF NOT EXISTS Dim_Fuente (
    IdFuente   INT         NOT NULL,
    Canal      VARCHAR(20) NOT NULL,
    Plataforma VARCHAR(30) NOT NULL,
    PRIMARY KEY (IdFuente),
    UNIQUE KEY uq_dim_fuente (Canal, Plataforma)
) ENGINE=InnoDB;

-- Cuándo: IdFecha = AAAAMMDD, con año/trimestre/mes ya calculados.
CREATE TABLE IF NOT EXISTS Dim_Fecha (
    IdFecha   INT         NOT NULL,
    Fecha     DATE        NOT NULL,
    Anio      SMALLINT    NOT NULL,
    Trimestre TINYINT     NOT NULL,
    Mes       TINYINT     NOT NULL,
    NombreMes VARCHAR(12) NOT NULL,
    AnioMes   CHAR(7)     NOT NULL,
    Dia       TINYINT     NOT NULL,
    PRIMARY KEY (IdFecha),
    UNIQUE KEY uq_dim_fecha_fecha (Fecha)
) ENGINE=InnoDB;

-- El tono: Positiva (+1), Neutra (0), Negativa (-1).
CREATE TABLE IF NOT EXISTS Dim_Sentimiento (
    IdSentimiento TINYINT     NOT NULL,
    Nombre        VARCHAR(10) NOT NULL,
    Polaridad     TINYINT     NOT NULL,
    PRIMARY KEY (IdSentimiento),
    UNIQUE KEY uq_dim_sentimiento_nombre (Nombre),
    CONSTRAINT ck_dim_sentimiento_polaridad CHECK (Polaridad IN (-1, 0, 1))
) ENGINE=InnoDB;

-- Bitácora: una fila por dimensión y por ejecución (trazabilidad y evidencia).
-- Se recrea (DROP) porque en esta versión cambian sus columnas: guarda el código de retorno y el
-- mensaje que devuelve cada procedimiento almacenado. Solo contiene el historial del Worker.
DROP TABLE IF EXISTS etl_carga_dimensiones;
CREATE TABLE etl_carga_dimensiones (
    Id             BIGINT        NOT NULL AUTO_INCREMENT,
    LoteId         CHAR(36)      NOT NULL,
    Dimension      VARCHAR(30)   NOT NULL,
    Estado         VARCHAR(10)   NOT NULL,   -- OK | ERROR
    FilasLeidas    BIGINT        NOT NULL,
    Rechazadas     BIGINT        NOT NULL,   -- filas inválidas descartadas por el Worker antes de llamar al procedimiento
    Insertadas     BIGINT        NOT NULL,
    YaRegistradas  BIGINT        NOT NULL,   -- filas cuyo registro ya existía: no se insertan ni se actualizan
    CodigoRetorno  INT           NOT NULL,   -- p_codigo del procedimiento: 0 insertó | 1 ya registrada | 2 sin filas | -1 error
    DuracionMs     BIGINT        NOT NULL,
    Mensaje        VARCHAR(1000) NULL,       -- p_result del procedimiento (ej. "... SE ENCUENTRAN REGISTRADAS")
    FechaRegistro  DATETIME(3)   NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    PRIMARY KEY (Id),
    KEY ix_carga_dim_lote (LoteId)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- Usuario del cargador de dimensiones (mínimo privilegio):
--   * EXECUTE sobre los 5 procedimientos (se otorga en 04_procedimientos_dimensiones.sql);
--     los procedimientos escriben con los permisos de su creador (SQL SECURITY DEFINER),
--     así que el usuario NO tiene INSERT/UPDATE/DELETE sobre las dimensiones
--   * SELECT solo sobre los 3 staging (para descubrir plataformas de Dim_Fuente)
--   * INSERT solo sobre su bitácora
-- CAMBIAR_ESTA_CLAVE: ponga una contraseña real (la misma que luego va en user-secrets).
-- Si el usuario ya existía de una ejecución anterior, ALTER USER le deja la clave nueva.
-- ---------------------------------------------------------------------
CREATE USER IF NOT EXISTS 'etl_loader'@'localhost' IDENTIFIED BY 'EtlLoader2026';
ALTER USER 'etl_loader'@'localhost' IDENTIFIED BY 'EtlLoader2026';

GRANT SELECT ON SistemaOpinionesDW.stg_encuestas            TO 'etl_loader'@'localhost';
GRANT SELECT ON SistemaOpinionesDW.stg_resenas_web          TO 'etl_loader'@'localhost';
GRANT SELECT ON SistemaOpinionesDW.stg_comentarios_sociales TO 'etl_loader'@'localhost';

GRANT INSERT ON SistemaOpinionesDW.etl_carga_dimensiones TO 'etl_loader'@'localhost';

FLUSH PRIVILEGES;
SHOW GRANTS FOR 'etl_loader'@'localhost';
