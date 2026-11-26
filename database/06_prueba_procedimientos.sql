-- =====================================================================
-- 06_prueba_procedimientos.sql
-- Prueba manual de los procedimientos almacenados desde MySQL (sin el Worker).
-- Ejecutar como root DESPUÉS de haber corrido el Worker (dimensiones ya cargadas).
-- Cada CALL devuelve: filas insertadas, filas ya registradas, código de retorno y mensaje.
--   Código 0 = insertó | 1 = SE ENCUENTRA REGISTRADA | 2 = sin filas | -1 = error (con ROLLBACK)
-- Ejecute cada bloque (CALL + SELECT) por separado para poder capturar cada resultado.
-- =====================================================================
USE SistemaOpinionesDW;

-- ---------------------------------------------------------------------
-- PRUEBA 1: registro que YA existe -> no se inserta, mensaje "SE ENCUENTRA REGISTRADA" (código 1)
-- ---------------------------------------------------------------------
CALL usp_LoadSentimiento(
    '[{"IdSentimiento":1,"Nombre":"Positiva","Polaridad":1}]',
    @insertadas, @existentes, @codigo, @mensaje);
SELECT @insertadas AS insertadas, @existentes AS ya_registradas, @codigo AS codigo, @mensaje AS mensaje;

-- ---------------------------------------------------------------------
-- PRUEBA 2: registro NUEVO -> se inserta (código 0). Cliente de prueba con ID 999999.
-- ---------------------------------------------------------------------
CALL usp_LoadCliente(
    '[{"IdCliente":999999,"Nombre":"Cliente de prueba","Email":"prueba@dw.local","EsEspecial":0}]',
    @insertadas, @existentes, @codigo, @mensaje);
SELECT @insertadas AS insertadas, @existentes AS ya_registradas, @codigo AS codigo, @mensaje AS mensaje;
SELECT * FROM Dim_Cliente WHERE IdCliente = 999999;

-- ---------------------------------------------------------------------
-- PRUEBA 3: el mismo cliente otra vez -> "SE ENCUENTRA REGISTRADA" (código 1), no se duplica
-- ---------------------------------------------------------------------
CALL usp_LoadCliente(
    '[{"IdCliente":999999,"Nombre":"Cliente de prueba","Email":"prueba@dw.local","EsEspecial":0}]',
    @insertadas, @existentes, @codigo, @mensaje);
SELECT @insertadas AS insertadas, @existentes AS ya_registradas, @codigo AS codigo, @mensaje AS mensaje;

-- ---------------------------------------------------------------------
-- PRUEBA 4: dos filas, una existente y una nueva -> inserta solo la nueva (código 0)
-- ---------------------------------------------------------------------
CALL usp_LoadFuente(
    '[{"Canal":"Red social","Plataforma":"Instagram"},{"Canal":"Red social","Plataforma":"TikTok"}]',
    @insertadas, @existentes, @codigo, @mensaje);
SELECT @insertadas AS insertadas, @existentes AS ya_registradas, @codigo AS codigo, @mensaje AS mensaje;
SELECT * FROM Dim_Fuente ORDER BY IdFuente;

-- ---------------------------------------------------------------------
-- PRUEBA 5: error controlado -> Polaridad 5 viola el CHECK de Dim_Sentimiento.
-- El procedimiento hace ROLLBACK y devuelve código -1 con el motivo (no lanza la excepción).
-- ---------------------------------------------------------------------
CALL usp_LoadSentimiento(
    '[{"IdSentimiento":9,"Nombre":"Prueba","Polaridad":5}]',
    @insertadas, @existentes, @codigo, @mensaje);
SELECT @insertadas AS insertadas, @existentes AS ya_registradas, @codigo AS codigo, @mensaje AS mensaje;
SELECT COUNT(*) AS filas_dim_sentimiento FROM Dim_Sentimiento;   -- sigue en 3

-- ---------------------------------------------------------------------
-- LIMPIEZA: deja las dimensiones como las dejó el Worker (502 clientes, 5 fuentes)
-- ---------------------------------------------------------------------
DELETE FROM Dim_Cliente WHERE IdCliente = 999999;
DELETE FROM Dim_Fuente  WHERE Canal = 'Red social' AND Plataforma = 'TikTok';
SELECT (SELECT COUNT(*) FROM Dim_Cliente) AS dim_cliente, (SELECT COUNT(*) FROM Dim_Fuente) AS dim_fuente;
