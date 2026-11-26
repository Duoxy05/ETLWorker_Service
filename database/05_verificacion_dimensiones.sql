-- =====================================================================
-- 05_verificacion_dimensiones.sql
-- Consultas para comprobar (y capturar como evidencia) las dimensiones cargadas.
-- Conteos esperados con los datos del proyecto:
--   Dim_Cliente 502 (500 + 2 especiales) | Dim_Producto 201 (200 + 1) | Dim_Fecha 732 (731 días + 1)
--   Dim_Sentimiento 3 | Dim_Fuente 5 (Encuesta, Web, Instagram, Facebook, Twitter)
-- =====================================================================
USE SistemaOpinionesDW;

-- 1) Resumen: filas por dimensión
SELECT 'Dim_Cliente'     AS dimension, COUNT(*) AS filas FROM Dim_Cliente
UNION ALL SELECT 'Dim_Producto',    COUNT(*) FROM Dim_Producto
UNION ALL SELECT 'Dim_Fuente',      COUNT(*) FROM Dim_Fuente
UNION ALL SELECT 'Dim_Fecha',       COUNT(*) FROM Dim_Fecha
UNION ALL SELECT 'Dim_Sentimiento', COUNT(*) FROM Dim_Sentimiento;

-- 2) Miembros especiales (claves negativas que evitan perder las 582 opiniones con IDs huérfanos)
SELECT IdCliente  AS id, Nombre, Email, EsEspecial FROM Dim_Cliente  WHERE IdCliente  < 0;
SELECT IdProducto AS id, Nombre, Categoria, EsEspecial FROM Dim_Producto WHERE IdProducto < 0;
SELECT * FROM Dim_Fecha WHERE IdFecha < 0;

-- 3) Muestras
SELECT * FROM Dim_Cliente  WHERE IdCliente  > 0 ORDER BY IdCliente  LIMIT 5;
SELECT * FROM Dim_Producto WHERE IdProducto > 0 ORDER BY IdProducto LIMIT 5;
SELECT * FROM Dim_Fuente ORDER BY IdFuente;
SELECT * FROM Dim_Sentimiento ORDER BY IdSentimiento;
SELECT * FROM Dim_Fecha WHERE IdFecha IN (20240101, 20241231, 20250914, 20251231) ORDER BY IdFecha;

-- 4) Productos por categoría
SELECT Categoria, COUNT(*) AS productos FROM Dim_Producto WHERE EsEspecial = FALSE GROUP BY Categoria ORDER BY productos DESC;

-- 5) Calendario completo: días por año (366 en 2024 por ser bisiesto, 365 en 2025)
SELECT Anio, COUNT(*) AS dias, MIN(Fecha) AS desde, MAX(Fecha) AS hasta FROM Dim_Fecha WHERE IdFecha > 0 GROUP BY Anio;

-- 6) Integridad: no debe haber IDs repetidos ni correos repetidos (ambas consultas deben devolver 0 filas)
SELECT Email, COUNT(*) FROM Dim_Cliente WHERE Email IS NOT NULL GROUP BY Email HAVING COUNT(*) > 1;
SELECT Canal, Plataforma, COUNT(*) FROM Dim_Fuente GROUP BY Canal, Plataforma HAVING COUNT(*) > 1;

-- 7) Cobertura frente al staging: cuántas encuestas tienen cliente/producto en la dimensión.
--    Las que no, son las referencias huérfanas que el hecho resolverá con -2 / -1 (no se pierden).
SELECT
    COUNT(*)                                                         AS encuestas_en_staging,
    SUM(c.IdCliente  IS NULL)                                        AS sin_cliente_en_dimension,
    SUM(p.IdProducto IS NULL)                                        AS sin_producto_en_dimension
FROM stg_encuestas s
LEFT JOIN Dim_Cliente  c ON c.IdCliente  = CAST(NULLIF(TRIM(s.IdClienteRaw),  '') AS SIGNED)
LEFT JOIN Dim_Producto p ON p.IdProducto = CAST(NULLIF(TRIM(s.IdProductoRaw), '') AS SIGNED);

-- 8) Bitácora de la carga (última ejecución de cada dimensión)
SELECT Dimension, Estado, FilasLeidas, Rechazadas, Insertadas, YaRegistradas, CodigoRetorno, DuracionMs, Mensaje
FROM etl_carga_dimensiones
WHERE Id IN (SELECT MAX(Id) FROM etl_carga_dimensiones GROUP BY Dimension)
ORDER BY Dimension;
