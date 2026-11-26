using ETLWorker.Domain.Entities;

namespace ETLWorker.Application.Dimensions;

/// <summary>Construye las filas del calendario (Dim_Fecha) con todos los atributos ya calculados.</summary>
public static class CalendarFactory
{
    private static readonly string[] Meses =
    {
        "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
        "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
    };

    public static DimFechaRow Create(DateTime date) => new(
        IdFecha: date.Year * 10000 + date.Month * 100 + date.Day,   // AAAAMMDD
        Fecha: date.Date,
        Anio: date.Year,
        Trimestre: (date.Month - 1) / 3 + 1,
        Mes: date.Month,
        NombreMes: Meses[date.Month - 1],
        AnioMes: $"{date.Year:D4}-{date.Month:D2}",
        Dia: date.Day);

    /// <summary>
    /// Miembro especial -1 "Fecha desconocida". Usa una fecha centinela (1900-01-01) para no depender de
    /// que las columnas admitan NULL; los análisis lo excluyen con IdFecha &lt;&gt; -1.
    /// </summary>
    public static DimFechaRow Unknown { get; } = new(
        SpecialMembers.FechaDesconocida, new DateTime(1900, 1, 1), 1900, 1, 1, "Desconocida", "1900-01", 1);

    public static IEnumerable<DimFechaRow> Range(int startYear, int endYear)
    {
        var end = new DateTime(endYear, 12, 31);
        for (var d = new DateTime(startYear, 1, 1); d <= end; d = d.AddDays(1))
            yield return Create(d);
    }
}
