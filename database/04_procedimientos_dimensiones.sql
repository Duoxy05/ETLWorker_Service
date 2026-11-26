-- =====================================================================
-- 04_procedimientos_dimensiones.sql   (MySQL 8.0.4 o superior)
-- Procedimientos almacenados que cargan las 5 dimensiones de SistemaOpinionesDW.
--
-- Patrón (el mismo que usa el profesor en SQL Server):
--   * cada procedimiento recibe los datos como PARÁMETRO y devuelve un mensaje
--     (p_result) y un código de retorno (p_codigo) por parámetros de salida;
--   * revisa con COUNT(*) si el registro ya existe: si existe NO se inserta y el
--     mensaje dice "... SE ENCUENTRA REGISTRADA";
--   * MySQL no tiene parámetros de tipo tabla (SqlDbType.Structured de SQL Server):
--     el equivalente es enviar TODAS las filas como un arreglo JSON en un solo
--     parámetro y expandirlas con JSON_TABLE -> una sola llamada por dimensión;
--   * cada procedimiento corre en UNA transacción (todo o nada) con manejador de errores.
--
-- Ejecutar como administrador (root) DESPUÉS de 03_dw_dimensiones.sql.
-- Se ejecuta con "mysql -u root -p < 04_procedimientos_dimensiones.sql" o completo en MySQL Workbench
-- (el script ya incluye DELIMITER).
-- =====================================================================
USE SistemaOpinionesDW;

DELIMITER $$

-- ---------------------------------------------------------------------
-- usp_LoadCliente: carga Dim_Cliente
-- p_filas = arreglo JSON: [{IdCliente, Nombre, Email, Pais, Edad, TipoCliente, EsEspecial}, ...]
-- Códigos de retorno (p_codigo):  0 = se insertó al menos una fila
--                                 1 = SE ENCUENTRA(N) REGISTRADA(S): no se insertó nada
--                                 2 = no se recibieron filas
--                                -1 = error (se hizo ROLLBACK; el motivo va en p_result)
-- ---------------------------------------------------------------------
DROP PROCEDURE IF EXISTS usp_LoadCliente$$
CREATE PROCEDURE usp_LoadCliente(
    IN  p_filas      LONGTEXT CHARACTER SET utf8mb4,
    OUT p_insertadas INT,
    OUT p_existentes INT,
    OUT p_codigo     INT,
    OUT p_result     VARCHAR(500))
    SQL SECURITY DEFINER
BEGIN
    DECLARE v_total INT DEFAULT 0;
    DECLARE v_max   INT DEFAULT 0;
    DECLARE v_msg   TEXT;

    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        GET DIAGNOSTICS CONDITION 1 v_msg = MESSAGE_TEXT;
        ROLLBACK;
        SET p_insertadas = 0;
        SET p_existentes = 0;
        SET p_codigo     = -1;
        SET p_result     = CONCAT('Dim_Cliente: ERROR - ', LEFT(v_msg, 400));
    END;

    SET p_insertadas = 0;
    SET p_existentes = 0;
    SET p_codigo     = 0;
    SET p_result     = '';

    IF p_filas IS NULL OR NOT JSON_VALID(p_filas) THEN
        SET p_codigo = 2;
        SET p_result = 'Dim_Cliente: no se recibieron filas para procesar (JSON vacío o inválido).';
    ELSEIF JSON_TYPE(p_filas) <> 'ARRAY' THEN
        SET p_codigo = 2;
        SET p_result = 'Dim_Cliente: el parámetro debe ser un arreglo JSON de filas.';
    ELSE
        -- Filas recibidas
        SELECT COUNT(*) INTO v_total
        FROM JSON_TABLE(p_filas, '$[*]' COLUMNS (IdCliente INT PATH '$.IdCliente')) AS j;

        IF v_total = 0 THEN
            SET p_codigo = 2;
            SET p_result = 'Dim_Cliente: no se recibieron filas para procesar.';
        ELSE
            -- Regla de existencia: ¿cuántas ya están registradas en la dimensión?
            SELECT COUNT(*) INTO p_existentes
            FROM JSON_TABLE(p_filas, '$[*]' COLUMNS (IdCliente INT PATH '$.IdCliente')) AS j
            WHERE EXISTS (SELECT 1 FROM Dim_Cliente d WHERE d.IdCliente = j.IdCliente);

            -- Solo se insertan las que NO existen; las existentes no se tocan.
            START TRANSACTION;
            INSERT INTO Dim_Cliente (IdCliente, Nombre, Email, Pais, Edad, TipoCliente, EsEspecial)
            SELECT j.IdCliente, j.Nombre,
                   -- Email es UNIQUE: si otro cliente ya lo usa, este se guarda sin correo.
                   CASE WHEN j.Email IS NOT NULL
                         AND EXISTS (SELECT 1 FROM Dim_Cliente x WHERE x.Email = (j.Email COLLATE utf8mb4_0900_ai_ci))
                        THEN NULL ELSE j.Email END,
                   j.Pais, j.Edad, j.TipoCliente, COALESCE(j.EsEspecial, 0)
            FROM JSON_TABLE(p_filas, '$[*]' COLUMNS (
            IdCliente   INT                       PATH '$.IdCliente',
            Nombre      VARCHAR(150) CHARACTER SET utf8mb4 PATH '$.Nombre',
            Email       VARCHAR(150) CHARACTER SET utf8mb4 PATH '$.Email',
            Pais        VARCHAR(60)  CHARACTER SET utf8mb4 PATH '$.Pais',
            Edad        TINYINT UNSIGNED          PATH '$.Edad',
            TipoCliente VARCHAR(40)  CHARACTER SET utf8mb4 PATH '$.TipoCliente',
            EsEspecial  TINYINT                   PATH '$.EsEspecial'
            )) AS j
            WHERE NOT EXISTS (SELECT 1 FROM Dim_Cliente d WHERE d.IdCliente = j.IdCliente);
            SET p_insertadas = ROW_COUNT();
            COMMIT;

            IF p_insertadas = 0 THEN
                SET p_codigo = 1;
                IF v_total = 1 THEN
                    SET p_result = 'Dim_Cliente: LA FILA SE ENCUENTRA REGISTRADA. No se insertó nada.';
                ELSE
                    SET p_result = CONCAT('Dim_Cliente: LAS ', v_total, ' FILAS SE ENCUENTRAN REGISTRADAS. No se insertó nada.');
                END IF;
            ELSEIF p_existentes = 0 THEN
                SET p_result = CONCAT('Dim_Cliente: ', p_insertadas, ' fila(s) insertada(s).');
            ELSE
                SET p_result = CONCAT('Dim_Cliente: ', p_insertadas, ' fila(s) insertada(s); ',
                                      p_existentes, ' SE ENCUENTRAN REGISTRADAS.');
            END IF;
        END IF;
    END IF;
END$$

-- ---------------------------------------------------------------------
-- usp_LoadProducto: carga Dim_Producto
-- p_filas = arreglo JSON: [{IdProducto, Nombre, Categoria, EsEspecial}, ...]
-- Códigos de retorno (p_codigo):  0 = se insertó al menos una fila
--                                 1 = SE ENCUENTRA(N) REGISTRADA(S): no se insertó nada
--                                 2 = no se recibieron filas
--                                -1 = error (se hizo ROLLBACK; el motivo va en p_result)
-- ---------------------------------------------------------------------
DROP PROCEDURE IF EXISTS usp_LoadProducto$$
CREATE PROCEDURE usp_LoadProducto(
    IN  p_filas      LONGTEXT CHARACTER SET utf8mb4,
    OUT p_insertadas INT,
    OUT p_existentes INT,
    OUT p_codigo     INT,
    OUT p_result     VARCHAR(500))
    SQL SECURITY DEFINER
BEGIN
    DECLARE v_total INT DEFAULT 0;
    DECLARE v_max   INT DEFAULT 0;
    DECLARE v_msg   TEXT;

    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        GET DIAGNOSTICS CONDITION 1 v_msg = MESSAGE_TEXT;
        ROLLBACK;
        SET p_insertadas = 0;
        SET p_existentes = 0;
        SET p_codigo     = -1;
        SET p_result     = CONCAT('Dim_Producto: ERROR - ', LEFT(v_msg, 400));
    END;

    SET p_insertadas = 0;
    SET p_existentes = 0;
    SET p_codigo     = 0;
    SET p_result     = '';

    IF p_filas IS NULL OR NOT JSON_VALID(p_filas) THEN
        SET p_codigo = 2;
        SET p_result = 'Dim_Producto: no se recibieron filas para procesar (JSON vacío o inválido).';
    ELSEIF JSON_TYPE(p_filas) <> 'ARRAY' THEN
        SET p_codigo = 2;
        SET p_result = 'Dim_Producto: el parámetro debe ser un arreglo JSON de filas.';
    ELSE
        -- Filas recibidas
        SELECT COUNT(*) INTO v_total
        FROM JSON_TABLE(p_filas, '$[*]' COLUMNS (IdProducto INT PATH '$.IdProducto')) AS j;

        IF v_total = 0 THEN
            SET p_codigo = 2;
            SET p_result = 'Dim_Producto: no se recibieron filas para procesar.';
        ELSE
            -- Regla de existencia: ¿cuántas ya están registradas en la dimensión?
            SELECT COUNT(*) INTO p_existentes
            FROM JSON_TABLE(p_filas, '$[*]' COLUMNS (IdProducto INT PATH '$.IdProducto')) AS j
            WHERE EXISTS (SELECT 1 FROM Dim_Producto d WHERE d.IdProducto = j.IdProducto);

            -- Solo se insertan las que NO existen; las existentes no se tocan.
            START TRANSACTION;
            INSERT INTO Dim_Producto (IdProducto, Nombre, Categoria, EsEspecial)
            SELECT j.IdProducto, j.Nombre, j.Categoria, COALESCE(j.EsEspecial, 0)
            FROM JSON_TABLE(p_filas, '$[*]' COLUMNS (
            IdProducto INT                       PATH '$.IdProducto',
            Nombre     VARCHAR(150) CHARACTER SET utf8mb4 PATH '$.Nombre',
            Categoria  VARCHAR(100) CHARACTER SET utf8mb4 PATH '$.Categoria',
            EsEspecial TINYINT                   PATH '$.EsEspecial'
            )) AS j
            WHERE NOT EXISTS (SELECT 1 FROM Dim_Producto d WHERE d.IdProducto = j.IdProducto);
            SET p_insertadas = ROW_COUNT();
            COMMIT;

            IF p_insertadas = 0 THEN
                SET p_codigo = 1;
                IF v_total = 1 THEN
                    SET p_result = 'Dim_Producto: LA FILA SE ENCUENTRA REGISTRADA. No se insertó nada.';
                ELSE
                    SET p_result = CONCAT('Dim_Producto: LAS ', v_total, ' FILAS SE ENCUENTRAN REGISTRADAS. No se insertó nada.');
                END IF;
            ELSEIF p_existentes = 0 THEN
                SET p_result = CONCAT('Dim_Producto: ', p_insertadas, ' fila(s) insertada(s).');
            ELSE
                SET p_result = CONCAT('Dim_Producto: ', p_insertadas, ' fila(s) insertada(s); ',
                                      p_existentes, ' SE ENCUENTRAN REGISTRADAS.');
            END IF;
        END IF;
    END IF;
END$$

-- ---------------------------------------------------------------------
-- usp_LoadFuente: carga Dim_Fuente
-- p_filas = arreglo JSON: [{Canal, Plataforma}, ...]  (clave natural Canal + Plataforma; el IdFuente lo asigna el procedimiento)
-- Códigos de retorno (p_codigo):  0 = se insertó al menos una fila
--                                 1 = SE ENCUENTRA(N) REGISTRADA(S): no se insertó nada
--                                 2 = no se recibieron filas
--                                -1 = error (se hizo ROLLBACK; el motivo va en p_result)
-- ---------------------------------------------------------------------
DROP PROCEDURE IF EXISTS usp_LoadFuente$$
CREATE PROCEDURE usp_LoadFuente(
    IN  p_filas      LONGTEXT CHARACTER SET utf8mb4,
    OUT p_insertadas INT,
    OUT p_existentes INT,
    OUT p_codigo     INT,
    OUT p_result     VARCHAR(500))
    SQL SECURITY DEFINER
BEGIN
    DECLARE v_total INT DEFAULT 0;
    DECLARE v_max   INT DEFAULT 0;
    DECLARE v_msg   TEXT;

    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        GET DIAGNOSTICS CONDITION 1 v_msg = MESSAGE_TEXT;
        ROLLBACK;
        SET p_insertadas = 0;
        SET p_existentes = 0;
        SET p_codigo     = -1;
        SET p_result     = CONCAT('Dim_Fuente: ERROR - ', LEFT(v_msg, 400));
    END;

    SET p_insertadas = 0;
    SET p_existentes = 0;
    SET p_codigo     = 0;
    SET p_result     = '';

    IF p_filas IS NULL OR NOT JSON_VALID(p_filas) THEN
        SET p_codigo = 2;
        SET p_result = 'Dim_Fuente: no se recibieron filas para procesar (JSON vacío o inválido).';
    ELSEIF JSON_TYPE(p_filas) <> 'ARRAY' THEN
        SET p_codigo = 2;
        SET p_result = 'Dim_Fuente: el parámetro debe ser un arreglo JSON de filas.';
    ELSE
        -- Filas recibidas
        SELECT COUNT(*) INTO v_total
        FROM JSON_TABLE(p_filas, '$[*]' COLUMNS (Canal VARCHAR(20) CHARACTER SET utf8mb4 PATH '$.Canal', Plataforma VARCHAR(30) CHARACTER SET utf8mb4 PATH '$.Plataforma')) AS j;

        IF v_total = 0 THEN
            SET p_codigo = 2;
            SET p_result = 'Dim_Fuente: no se recibieron filas para procesar.';
        ELSE
            -- Regla de existencia: ¿cuántas ya están registradas en la dimensión?
            SELECT COUNT(*) INTO p_existentes
            FROM JSON_TABLE(p_filas, '$[*]' COLUMNS (Canal VARCHAR(20) CHARACTER SET utf8mb4 PATH '$.Canal', Plataforma VARCHAR(30) CHARACTER SET utf8mb4 PATH '$.Plataforma')) AS j
            WHERE EXISTS (SELECT 1 FROM Dim_Fuente d WHERE d.Canal = (j.Canal COLLATE utf8mb4_0900_ai_ci) AND d.Plataforma = (j.Plataforma COLLATE utf8mb4_0900_ai_ci));

            -- Solo se insertan las que NO existen; las existentes no se tocan.
            START TRANSACTION;
            -- El IdFuente se asigna aquí: el siguiente libre (los ya usados por la tabla de hechos no cambian).
            SELECT COALESCE(MAX(IdFuente), 0) INTO v_max FROM Dim_Fuente;

            INSERT INTO Dim_Fuente (IdFuente, Canal, Plataforma)
            SELECT v_max + ROW_NUMBER() OVER (ORDER BY j.ord), j.Canal, j.Plataforma
            FROM JSON_TABLE(p_filas, '$[*]' COLUMNS (
            ord        FOR ORDINALITY,
            Canal      VARCHAR(20) CHARACTER SET utf8mb4 PATH '$.Canal',
            Plataforma VARCHAR(30) CHARACTER SET utf8mb4 PATH '$.Plataforma'
            )) AS j
            WHERE NOT EXISTS (SELECT 1 FROM Dim_Fuente d
                              WHERE d.Canal      = (j.Canal      COLLATE utf8mb4_0900_ai_ci)
                                AND d.Plataforma = (j.Plataforma COLLATE utf8mb4_0900_ai_ci));
            SET p_insertadas = ROW_COUNT();
            COMMIT;

            IF p_insertadas = 0 THEN
                SET p_codigo = 1;
                IF v_total = 1 THEN
                    SET p_result = 'Dim_Fuente: LA FILA SE ENCUENTRA REGISTRADA. No se insertó nada.';
                ELSE
                    SET p_result = CONCAT('Dim_Fuente: LAS ', v_total, ' FILAS SE ENCUENTRAN REGISTRADAS. No se insertó nada.');
                END IF;
            ELSEIF p_existentes = 0 THEN
                SET p_result = CONCAT('Dim_Fuente: ', p_insertadas, ' fila(s) insertada(s).');
            ELSE
                SET p_result = CONCAT('Dim_Fuente: ', p_insertadas, ' fila(s) insertada(s); ',
                                      p_existentes, ' SE ENCUENTRAN REGISTRADAS.');
            END IF;
        END IF;
    END IF;
END$$

-- ---------------------------------------------------------------------
-- usp_LoadFecha: carga Dim_Fecha
-- p_filas = arreglo JSON: [{IdFecha, Fecha, Anio, Trimestre, Mes, NombreMes, AnioMes, Dia}, ...]
-- Códigos de retorno (p_codigo):  0 = se insertó al menos una fila
--                                 1 = SE ENCUENTRA(N) REGISTRADA(S): no se insertó nada
--                                 2 = no se recibieron filas
--                                -1 = error (se hizo ROLLBACK; el motivo va en p_result)
-- ---------------------------------------------------------------------
DROP PROCEDURE IF EXISTS usp_LoadFecha$$
CREATE PROCEDURE usp_LoadFecha(
    IN  p_filas      LONGTEXT CHARACTER SET utf8mb4,
    OUT p_insertadas INT,
    OUT p_existentes INT,
    OUT p_codigo     INT,
    OUT p_result     VARCHAR(500))
    SQL SECURITY DEFINER
BEGIN
    DECLARE v_total INT DEFAULT 0;
    DECLARE v_max   INT DEFAULT 0;
    DECLARE v_msg   TEXT;

    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        GET DIAGNOSTICS CONDITION 1 v_msg = MESSAGE_TEXT;
        ROLLBACK;
        SET p_insertadas = 0;
        SET p_existentes = 0;
        SET p_codigo     = -1;
        SET p_result     = CONCAT('Dim_Fecha: ERROR - ', LEFT(v_msg, 400));
    END;

    SET p_insertadas = 0;
    SET p_existentes = 0;
    SET p_codigo     = 0;
    SET p_result     = '';

    IF p_filas IS NULL OR NOT JSON_VALID(p_filas) THEN
        SET p_codigo = 2;
        SET p_result = 'Dim_Fecha: no se recibieron filas para procesar (JSON vacío o inválido).';
    ELSEIF JSON_TYPE(p_filas) <> 'ARRAY' THEN
        SET p_codigo = 2;
        SET p_result = 'Dim_Fecha: el parámetro debe ser un arreglo JSON de filas.';
    ELSE
        -- Filas recibidas
        SELECT COUNT(*) INTO v_total
        FROM JSON_TABLE(p_filas, '$[*]' COLUMNS (IdFecha INT PATH '$.IdFecha')) AS j;

        IF v_total = 0 THEN
            SET p_codigo = 2;
            SET p_result = 'Dim_Fecha: no se recibieron filas para procesar.';
        ELSE
            -- Regla de existencia: ¿cuántas ya están registradas en la dimensión?
            SELECT COUNT(*) INTO p_existentes
            FROM JSON_TABLE(p_filas, '$[*]' COLUMNS (IdFecha INT PATH '$.IdFecha')) AS j
            WHERE EXISTS (SELECT 1 FROM Dim_Fecha d WHERE d.IdFecha = j.IdFecha);

            -- Solo se insertan las que NO existen; las existentes no se tocan.
            START TRANSACTION;
            INSERT INTO Dim_Fecha (IdFecha, Fecha, Anio, Trimestre, Mes, NombreMes, AnioMes, Dia)
            SELECT j.IdFecha, j.Fecha, j.Anio, j.Trimestre, j.Mes, j.NombreMes, j.AnioMes, j.Dia
            FROM JSON_TABLE(p_filas, '$[*]' COLUMNS (
            IdFecha   INT                      PATH '$.IdFecha',
            Fecha     DATE                     PATH '$.Fecha',
            Anio      SMALLINT                 PATH '$.Anio',
            Trimestre TINYINT                  PATH '$.Trimestre',
            Mes       TINYINT                  PATH '$.Mes',
            NombreMes VARCHAR(12) CHARACTER SET utf8mb4 PATH '$.NombreMes',
            AnioMes   CHAR(7)     CHARACTER SET utf8mb4 PATH '$.AnioMes',
            Dia       TINYINT                  PATH '$.Dia'
            )) AS j
            WHERE NOT EXISTS (SELECT 1 FROM Dim_Fecha d WHERE d.IdFecha = j.IdFecha);
            SET p_insertadas = ROW_COUNT();
            COMMIT;

            IF p_insertadas = 0 THEN
                SET p_codigo = 1;
                IF v_total = 1 THEN
                    SET p_result = 'Dim_Fecha: LA FILA SE ENCUENTRA REGISTRADA. No se insertó nada.';
                ELSE
                    SET p_result = CONCAT('Dim_Fecha: LAS ', v_total, ' FILAS SE ENCUENTRAN REGISTRADAS. No se insertó nada.');
                END IF;
            ELSEIF p_existentes = 0 THEN
                SET p_result = CONCAT('Dim_Fecha: ', p_insertadas, ' fila(s) insertada(s).');
            ELSE
                SET p_result = CONCAT('Dim_Fecha: ', p_insertadas, ' fila(s) insertada(s); ',
                                      p_existentes, ' SE ENCUENTRAN REGISTRADAS.');
            END IF;
        END IF;
    END IF;
END$$

-- ---------------------------------------------------------------------
-- usp_LoadSentimiento: carga Dim_Sentimiento
-- p_filas = arreglo JSON: [{IdSentimiento, Nombre, Polaridad}, ...]
-- Códigos de retorno (p_codigo):  0 = se insertó al menos una fila
--                                 1 = SE ENCUENTRA(N) REGISTRADA(S): no se insertó nada
--                                 2 = no se recibieron filas
--                                -1 = error (se hizo ROLLBACK; el motivo va en p_result)
-- ---------------------------------------------------------------------
DROP PROCEDURE IF EXISTS usp_LoadSentimiento$$
CREATE PROCEDURE usp_LoadSentimiento(
    IN  p_filas      LONGTEXT CHARACTER SET utf8mb4,
    OUT p_insertadas INT,
    OUT p_existentes INT,
    OUT p_codigo     INT,
    OUT p_result     VARCHAR(500))
    SQL SECURITY DEFINER
BEGIN
    DECLARE v_total INT DEFAULT 0;
    DECLARE v_max   INT DEFAULT 0;
    DECLARE v_msg   TEXT;

    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        GET DIAGNOSTICS CONDITION 1 v_msg = MESSAGE_TEXT;
        ROLLBACK;
        SET p_insertadas = 0;
        SET p_existentes = 0;
        SET p_codigo     = -1;
        SET p_result     = CONCAT('Dim_Sentimiento: ERROR - ', LEFT(v_msg, 400));
    END;

    SET p_insertadas = 0;
    SET p_existentes = 0;
    SET p_codigo     = 0;
    SET p_result     = '';

    IF p_filas IS NULL OR NOT JSON_VALID(p_filas) THEN
        SET p_codigo = 2;
        SET p_result = 'Dim_Sentimiento: no se recibieron filas para procesar (JSON vacío o inválido).';
    ELSEIF JSON_TYPE(p_filas) <> 'ARRAY' THEN
        SET p_codigo = 2;
        SET p_result = 'Dim_Sentimiento: el parámetro debe ser un arreglo JSON de filas.';
    ELSE
        -- Filas recibidas
        SELECT COUNT(*) INTO v_total
        FROM JSON_TABLE(p_filas, '$[*]' COLUMNS (IdSentimiento INT PATH '$.IdSentimiento')) AS j;

        IF v_total = 0 THEN
            SET p_codigo = 2;
            SET p_result = 'Dim_Sentimiento: no se recibieron filas para procesar.';
        ELSE
            -- Regla de existencia: ¿cuántas ya están registradas en la dimensión?
            SELECT COUNT(*) INTO p_existentes
            FROM JSON_TABLE(p_filas, '$[*]' COLUMNS (IdSentimiento INT PATH '$.IdSentimiento')) AS j
            WHERE EXISTS (SELECT 1 FROM Dim_Sentimiento d WHERE d.IdSentimiento = j.IdSentimiento);

            -- Solo se insertan las que NO existen; las existentes no se tocan.
            START TRANSACTION;
            INSERT INTO Dim_Sentimiento (IdSentimiento, Nombre, Polaridad)
            SELECT j.IdSentimiento, j.Nombre, j.Polaridad
            FROM JSON_TABLE(p_filas, '$[*]' COLUMNS (
            IdSentimiento INT                      PATH '$.IdSentimiento',
            Nombre        VARCHAR(10) CHARACTER SET utf8mb4 PATH '$.Nombre',
            Polaridad     TINYINT                  PATH '$.Polaridad'
            )) AS j
            WHERE NOT EXISTS (SELECT 1 FROM Dim_Sentimiento d WHERE d.IdSentimiento = j.IdSentimiento);
            SET p_insertadas = ROW_COUNT();
            COMMIT;

            IF p_insertadas = 0 THEN
                SET p_codigo = 1;
                IF v_total = 1 THEN
                    SET p_result = 'Dim_Sentimiento: LA FILA SE ENCUENTRA REGISTRADA. No se insertó nada.';
                ELSE
                    SET p_result = CONCAT('Dim_Sentimiento: LAS ', v_total, ' FILAS SE ENCUENTRAN REGISTRADAS. No se insertó nada.');
                END IF;
            ELSEIF p_existentes = 0 THEN
                SET p_result = CONCAT('Dim_Sentimiento: ', p_insertadas, ' fila(s) insertada(s).');
            ELSE
                SET p_result = CONCAT('Dim_Sentimiento: ', p_insertadas, ' fila(s) insertada(s); ',
                                      p_existentes, ' SE ENCUENTRAN REGISTRADAS.');
            END IF;
        END IF;
    END IF;
END$$

DELIMITER ;

-- ---------------------------------------------------------------------
-- Permisos del usuario del Worker: solo EXECUTE sobre los 5 procedimientos.
-- Los procedimientos corren con SQL SECURITY DEFINER (los permisos de quien los creó),
-- así que etl_loader NO necesita INSERT/UPDATE directo sobre las dimensiones.
-- ---------------------------------------------------------------------
GRANT EXECUTE ON PROCEDURE SistemaOpinionesDW.usp_LoadCliente      TO 'etl_loader'@'localhost';
GRANT EXECUTE ON PROCEDURE SistemaOpinionesDW.usp_LoadProducto     TO 'etl_loader'@'localhost';
GRANT EXECUTE ON PROCEDURE SistemaOpinionesDW.usp_LoadFuente       TO 'etl_loader'@'localhost';
GRANT EXECUTE ON PROCEDURE SistemaOpinionesDW.usp_LoadFecha        TO 'etl_loader'@'localhost';
GRANT EXECUTE ON PROCEDURE SistemaOpinionesDW.usp_LoadSentimiento  TO 'etl_loader'@'localhost';
FLUSH PRIVILEGES;

-- Comprobación: deben aparecer los 5 procedimientos y los permisos EXECUTE.
SELECT ROUTINE_NAME AS procedimiento, CREATED AS creado
FROM information_schema.ROUTINES
WHERE ROUTINE_SCHEMA = 'SistemaOpinionesDW' AND ROUTINE_TYPE = 'PROCEDURE' AND ROUTINE_NAME LIKE 'usp\_Load%'
ORDER BY ROUTINE_NAME;
SHOW GRANTS FOR 'etl_loader'@'localhost';
