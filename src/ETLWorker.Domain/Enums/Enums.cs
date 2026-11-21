namespace ETLWorker.Domain.Enums;

/// <summary>Tipo de fuente de datos soportada por el proceso de extracción.</summary>
public enum SourceType { Csv, Database, Api }

/// <summary>Advertencia = recuperable en Transform (miembro especial); Error = la fila no es confiable.</summary>
public enum IssueSeverity { Advertencia, Error }

public enum ExtractionStatus { Ok, Error }
